using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using WhatsAppClient.App.Configuration;
using WhatsAppClient.App.Models;

namespace WhatsAppClient.App.Persistence;

/// <summary>
/// Single-table DynamoDB store. Keys:
/// USER#&lt;u&gt;/PROFILE, CONTACT#&lt;waId&gt;/PROFILE, CONTACT#&lt;waId&gt;/CONV,
/// CONTACT#&lt;waId&gt;/MSG#&lt;ts&gt;#&lt;id&gt;, AUTH#&lt;id&gt;/CHALLENGE. GSI1 doubles as a type index
/// (USERS, CONTACTS, CONV partitions) and resolves a sent message by WAMSG#&lt;waMessageId&gt;.
/// </summary>
public sealed class DynamoAppRepository : IAppRepository
{
    public const string Gsi1 = "GSI1";
    private const string GsiUsers = "USERS";
    private const string GsiContacts = "CONTACTS";
    private const string GsiConv = "CONV";

    private readonly IAmazonDynamoDB _db;
    private readonly string _table;

    public DynamoAppRepository(IAmazonDynamoDB db, IOptions<AppOptions> options)
    {
        _db = db;
        _table = options.Value.TableName;
    }

    // ---- Users --------------------------------------------------------------
    public async Task<SystemUser?> GetUserAsync(string username, CancellationToken ct = default)
    {
        var item = await GetAsync(UserPk(username), "PROFILE", ct);
        return item is null ? null : ReadUser(item);
    }

    public Task PutUserAsync(SystemUser user, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(UserPk(user.Username)),
            ["SK"] = S("PROFILE"),
            ["entity"] = S("user"),
            ["GSI1PK"] = S(GsiUsers),
            ["GSI1SK"] = S(user.Username.ToLowerInvariant()),
            ["Username"] = S(user.Username),
            ["DisplayName"] = S(user.DisplayName),
            ["PhoneE164"] = S(user.PhoneE164),
            ["Role"] = S(user.Role.ToString()),
            ["Status"] = S(user.Status),
            ["CreatedAt"] = S(user.CreatedAt),
        };
        return _db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item }, ct);
    }

    public async Task<IReadOnlyList<SystemUser>> ListUsersAsync(CancellationToken ct = default)
    {
        var items = await QueryGsiAsync(GsiUsers, ct: ct);
        return items.Select(ReadUser).ToList();
    }

    public async Task<int> CountUsersAsync(CancellationToken ct = default)
    {
        var r = await _db.QueryAsync(new QueryRequest
        {
            TableName = _table,
            IndexName = Gsi1,
            KeyConditionExpression = "GSI1PK = :p",
            ExpressionAttributeValues = new() { [":p"] = S(GsiUsers) },
            Select = Select.COUNT,
        }, ct);
        return r.Count ?? 0;
    }

    public Task DeleteUserAsync(string username, CancellationToken ct = default) =>
        _db.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(UserPk(username)), ["SK"] = S("PROFILE") },
        }, ct);

    // ---- Contacts -----------------------------------------------------------
    public async Task<Contact?> GetContactAsync(string waId, CancellationToken ct = default)
    {
        var item = await GetAsync(ContactPk(waId), "PROFILE", ct);
        return item is null ? null : ReadContact(item);
    }

    public Task PutContactAsync(Contact contact, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(ContactPk(contact.WaId)),
            ["SK"] = S("PROFILE"),
            ["entity"] = S("contact"),
            ["GSI1PK"] = S(GsiContacts),
            ["GSI1SK"] = S(contact.WaId),
            ["WaId"] = S(contact.WaId),
            ["PhoneE164"] = S(contact.PhoneE164),
            ["Source"] = S(contact.Source),
            ["CreatedAt"] = S(contact.CreatedAt),
        };
        if (!string.IsNullOrEmpty(contact.Name)) item["Name"] = S(contact.Name);
        return _db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item }, ct);
    }

    public async Task<IReadOnlyList<Contact>> ListContactsAsync(CancellationToken ct = default)
    {
        var items = await QueryGsiAsync(GsiContacts, ct: ct);
        return items.Select(ReadContact).ToList();
    }

    // ---- Conversations ------------------------------------------------------
    public async Task<Conversation?> GetConversationAsync(string waId, CancellationToken ct = default)
    {
        var item = await GetAsync(ContactPk(waId), "CONV", ct);
        return item is null ? null : ReadConversation(item);
    }

    public Task PutConversationAsync(Conversation conv, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(ContactPk(conv.WaId)),
            ["SK"] = S("CONV"),
            ["entity"] = S("conversation"),
            ["GSI1PK"] = S(GsiConv),
            ["GSI1SK"] = S(conv.LastActivityAt),
            ["WaId"] = S(conv.WaId),
            ["LastActivityAt"] = S(conv.LastActivityAt),
            ["Unread"] = N(conv.Unread),
        };
        if (!string.IsNullOrEmpty(conv.Name)) item["Name"] = S(conv.Name);
        if (!string.IsNullOrEmpty(conv.LastPreview)) item["LastPreview"] = S(conv.LastPreview);
        if (!string.IsNullOrEmpty(conv.WindowExpiresAt)) item["WindowExpiresAt"] = S(conv.WindowExpiresAt);
        return _db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item }, ct);
    }

    public async Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken ct = default)
    {
        var items = await QueryGsiAsync(GsiConv, scanForward: false, ct: ct);
        return items.Select(ReadConversation).ToList();
    }

    public Task ResetConversationUnreadAsync(string waId, CancellationToken ct = default) =>
        _db.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(ContactPk(waId)), ["SK"] = S("CONV") },
            UpdateExpression = "SET Unread = :zero",
            ExpressionAttributeValues = new() { [":zero"] = N(0) },
        }, ct);

    // ---- Messages -----------------------------------------------------------
    public Task PutMessageAsync(ChatMessage m, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(ContactPk(m.WaId)),
            ["SK"] = S($"MSG#{m.CreatedAt}#{m.Id}"),
            ["entity"] = S("message"),
            ["WaId"] = S(m.WaId),
            ["Id"] = S(m.Id),
            ["Direction"] = S(m.Direction),
            ["Type"] = S(m.Type),
            ["Status"] = S(m.Status),
            ["CreatedAt"] = S(m.CreatedAt),
        };
        if (!string.IsNullOrEmpty(m.Text)) item["Text"] = S(m.Text);
        if (!string.IsNullOrEmpty(m.MediaId)) item["MediaId"] = S(m.MediaId);
        if (!string.IsNullOrEmpty(m.MediaS3Key)) item["MediaS3Key"] = S(m.MediaS3Key);
        if (!string.IsNullOrEmpty(m.SentBy)) item["SentBy"] = S(m.SentBy);
        if (!string.IsNullOrEmpty(m.TemplateName)) item["TemplateName"] = S(m.TemplateName);
        if (!string.IsNullOrEmpty(m.WaMessageId))
        {
            item["WaMessageId"] = S(m.WaMessageId);
            item["GSI1PK"] = S($"WAMSG#{m.WaMessageId}");
            item["GSI1SK"] = S(m.WaId);
        }
        return _db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item }, ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(string waId, CancellationToken ct = default)
    {
        var items = await PaginateAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "PK = :p AND begins_with(SK, :m)",
            ExpressionAttributeValues = new() { [":p"] = S(ContactPk(waId)), [":m"] = S("MSG#") },
        }, ct);
        return items.Select(ReadMessage).ToList();
    }

    public async Task<bool> PatchMessageStatusByWaMessageIdAsync(string waMessageId, string status, CancellationToken ct = default)
    {
        var r = await _db.QueryAsync(new QueryRequest
        {
            TableName = _table,
            IndexName = Gsi1,
            KeyConditionExpression = "GSI1PK = :p",
            ExpressionAttributeValues = new() { [":p"] = S($"WAMSG#{waMessageId}") },
            Limit = 1,
        }, ct);
        var item = r.Items.FirstOrDefault();
        if (item is null) return false;
        await _db.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = item["PK"], ["SK"] = item["SK"] },
            UpdateExpression = "SET #s = :s",
            ExpressionAttributeNames = new() { ["#s"] = "Status" },
            ExpressionAttributeValues = new() { [":s"] = S(status) },
        }, ct);
        return true;
    }

    // ---- Auth challenges ----------------------------------------------------
    public Task PutAuthChallengeAsync(AuthChallenge c, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = S(AuthPk(c.ChallengeId)),
            ["SK"] = S("CHALLENGE"),
            ["entity"] = S("auth"),
            ["ChallengeId"] = S(c.ChallengeId),
            ["Username"] = S(c.Username),
            ["CodeHash"] = S(c.CodeHash),
            ["Attempts"] = N(c.Attempts),
            ["ttl"] = N(c.Ttl),
        };
        return _db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item }, ct);
    }

    public async Task<AuthChallenge?> GetAuthChallengeAsync(string challengeId, CancellationToken ct = default)
    {
        var item = await GetAsync(AuthPk(challengeId), "CHALLENGE", ct);
        if (item is null) return null;
        return new AuthChallenge
        {
            ChallengeId = GetS(item, "ChallengeId"),
            Username = GetS(item, "Username"),
            CodeHash = GetS(item, "CodeHash"),
            Attempts = (int)GetN(item, "Attempts"),
            Ttl = GetN(item, "ttl"),
        };
    }

    public Task IncrementAuthAttemptsAsync(string challengeId, CancellationToken ct = default) =>
        _db.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(AuthPk(challengeId)), ["SK"] = S("CHALLENGE") },
            UpdateExpression = "SET Attempts = Attempts + :one",
            ExpressionAttributeValues = new() { [":one"] = N(1) },
        }, ct);

    public Task DeleteAuthChallengeAsync(string challengeId, CancellationToken ct = default) =>
        _db.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(AuthPk(challengeId)), ["SK"] = S("CHALLENGE") },
        }, ct);

    // ---- helpers ------------------------------------------------------------
    private static string UserPk(string u) => $"USER#{u.ToLowerInvariant()}";
    private static string ContactPk(string waId) => $"CONTACT#{waId}";
    private static string AuthPk(string id) => $"AUTH#{id}";

    private static AttributeValue S(string v) => new() { S = v };
    private static AttributeValue N(long v) => new() { N = v.ToString() };

    private static string GetS(IReadOnlyDictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var v) ? v.S ?? string.Empty : string.Empty;

    private static string? GetSOrNull(IReadOnlyDictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var v) ? v.S : null;

    private static long GetN(IReadOnlyDictionary<string, AttributeValue> item, string key) =>
        item.TryGetValue(key, out var v) && long.TryParse(v.N, out var n) ? n : 0;

    private async Task<Dictionary<string, AttributeValue>?> GetAsync(string pk, string sk, CancellationToken ct)
    {
        var r = await _db.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = new() { ["PK"] = S(pk), ["SK"] = S(sk) },
        }, ct);
        return r.IsItemSet ? r.Item : null;
    }

    private Task<List<Dictionary<string, AttributeValue>>> QueryGsiAsync(
        string gsi1Pk, bool scanForward = true, CancellationToken ct = default) =>
        PaginateAsync(new QueryRequest
        {
            TableName = _table,
            IndexName = Gsi1,
            KeyConditionExpression = "GSI1PK = :p",
            ExpressionAttributeValues = new() { [":p"] = S(gsi1Pk) },
            ScanIndexForward = scanForward,
        }, ct);

    /// <summary>Runs a query to completion, following LastEvaluatedKey so results aren't capped at 1 MB.</summary>
    private async Task<List<Dictionary<string, AttributeValue>>> PaginateAsync(QueryRequest request, CancellationToken ct)
    {
        var items = new List<Dictionary<string, AttributeValue>>();
        do
        {
            var r = await _db.QueryAsync(request, ct);
            items.AddRange(r.Items);
            request.ExclusiveStartKey = r.LastEvaluatedKey;
        } while (request.ExclusiveStartKey is { Count: > 0 });
        return items;
    }

    private static SystemUser ReadUser(IReadOnlyDictionary<string, AttributeValue> i) => new()
    {
        Username = GetS(i, "Username"),
        DisplayName = GetS(i, "DisplayName"),
        PhoneE164 = GetS(i, "PhoneE164"),
        Role = Enum.TryParse<UserRole>(GetS(i, "Role"), out var r) ? r : UserRole.Agent,
        Status = GetS(i, "Status"),
        CreatedAt = GetS(i, "CreatedAt"),
    };

    private static Contact ReadContact(IReadOnlyDictionary<string, AttributeValue> i) => new()
    {
        WaId = GetS(i, "WaId"),
        PhoneE164 = GetS(i, "PhoneE164"),
        Name = GetSOrNull(i, "Name"),
        Source = GetS(i, "Source"),
        CreatedAt = GetS(i, "CreatedAt"),
    };

    private static Conversation ReadConversation(IReadOnlyDictionary<string, AttributeValue> i) => new()
    {
        WaId = GetS(i, "WaId"),
        Name = GetSOrNull(i, "Name"),
        LastPreview = GetSOrNull(i, "LastPreview"),
        LastActivityAt = GetS(i, "LastActivityAt"),
        WindowExpiresAt = GetSOrNull(i, "WindowExpiresAt"),
        Unread = (int)GetN(i, "Unread"),
    };

    private static ChatMessage ReadMessage(IReadOnlyDictionary<string, AttributeValue> i) => new()
    {
        WaId = GetS(i, "WaId"),
        Id = GetS(i, "Id"),
        Direction = GetS(i, "Direction"),
        Type = GetS(i, "Type"),
        Text = GetSOrNull(i, "Text"),
        MediaId = GetSOrNull(i, "MediaId"),
        MediaS3Key = GetSOrNull(i, "MediaS3Key"),
        Status = GetS(i, "Status"),
        WaMessageId = GetSOrNull(i, "WaMessageId"),
        SentBy = GetSOrNull(i, "SentBy"),
        TemplateName = GetSOrNull(i, "TemplateName"),
        CreatedAt = GetS(i, "CreatedAt"),
    };
}
