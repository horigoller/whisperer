# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

A C#/.NET 8 solution that **sends and receives** WhatsApp Business Platform messages from AWS
using **AWS End User Messaging Social** (`AWSSDK.SocialMessaging`), plus a **React management
console ("Whisperer")** to run conversations from a browser. AWS manages the connection to the
WhatsApp Business Account (WABA) — the JSON message bodies follow the Meta WhatsApp Cloud API
"messages" schema and are passed through verbatim by AWS's `SendWhatsAppMessage` API.

**Receiving:** you do *not* host your own HTTPS webhook. AWS owns the Meta webhook
subscription for your WABA and publishes every inbound message and status update to an
**Amazon SNS topic** you nominate as the WABA's *event destination*. This solution buffers
that topic through SQS and processes events in `WhatsAppClient.ReceiveLambda`, then fans them
out on EventBridge. The whole stack (six Lambdas, SNS/SQS/DLQ, two DynamoDB tables, two S3
buckets, EventBridge, an HTTP API + custom domain, a WebSocket API, and a Secrets Manager
secret) is defined in `template.yaml`.

**Console:** the management app is an ASP.NET Core minimal API (`WhatsAppClient.Api`) running
on Lambda that also serves a React SPA (`app/web`) from S3. It offers passwordless login
(one-time code over WhatsApp), an inbox, threaded conversations, contact + system-user
management, and **real-time updates over an API Gateway WebSocket API** (no polling).

The .NET SDK is installed at `~/.dotnet` (not via Homebrew cask, due to sandbox sudo
restrictions). Use `export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"` before running
`dotnet`/`sam`/`dotnet-lambda` if they're not already on PATH.

## Commands

```bash
dotnet build                 # build the whole solution
dotnet test                  # run all tests (Core + App + four Lambdas), 104 tests
dotnet test --filter "FullyQualifiedName~WhatsAppMessageServiceTests"   # run one test class
dotnet test --filter "FullyQualifiedName~SendTextMessageAsync_WithEmptyBody_ThrowsArgumentException"  # single test

npm --prefix app/web ci && npm --prefix app/web run build   # build the React console
```

Deploy the whole stack with SAM (parameters persist in the CloudFormation stack, so on
re-deploys you only need to re-supply them if they change):
```bash
sam build
sam deploy --guided   # first time; supply OriginationPhoneNumberId, MetaApiVersion, WabaId,
                      # BootstrapAdminUsername/Phone, and (optional) WhispererDomainName/CertArn
```

Ship the React console to the stack's WebBucket (build + sync index.html no-cache, assets
immutable):
```bash
app/deploy-web.sh [stack-name] [region] [aws-profile]   # defaults: whatsapp-messaging us-east-1
```

After the first deploy, set the `EventsTopicArn` stack output as your WABA's **event
destination** in the AWS End User Messaging Social console (WhatsApp business accounts → your
WABA → Event destinations). SAM cannot do this for an already-linked WABA.

The send function can still be deployed standalone (from `src/WhatsAppClient.Lambda`):
```bash
dotnet lambda deploy-function send-whatsapp-message --region <your-region>
```

## Operating (runbook)

Find the WABA id and origination phone-number id:
```bash
aws socialmessaging list-linked-whatsapp-business-accounts \
  --query 'linkedAccounts[].{id:id,arn:arn}'
aws socialmessaging get-linked-whatsapp-business-account --id <waba-id> \
  --query 'account.phoneNumbers[].phoneNumberId'
```

Send a media message (upload from S3 → handle → send):
```bash
aws s3 cp ./img.png s3://<MediaBucket>/outgoing/img.png
MEDIA_ID=$(aws socialmessaging post-whatsapp-message-media \
  --origination-phone-number-id <id> \
  --source-s3-file "bucketName=<MediaBucket>,key=outgoing/img.png" --query mediaId --output text)
aws lambda invoke --function-name <SendFunction> --cli-binary-format raw-in-base64-out \
  --payload "{\"To\":\"+1...\",\"MessageType\":\"image\",\"MediaId\":\"$MEDIA_ID\"}" /dev/stdout
```

Create a message template (Meta approves it asynchronously — poll `list-whatsapp-message-templates`
for `APPROVED` before sending it via the `template` message type):
```bash
aws socialmessaging create-whatsapp-message-template --id <waba-id> \
  --cli-binary-format raw-in-base64-out --template-definition fileb://template.json
```

### Login template (console auth)

The console delivers login codes via the approved **AUTHENTICATION** template named by
`App.LoginTemplateName` (currently `verify_code_1`), which works even outside the recipient's
24h window. Delivery is **approval-gated**: `AuthService` only uses the template when
`ITemplateService.IsApprovedAsync` confirms it's `APPROVED`, otherwise it falls back to a
free-form text code (which only delivers in-window). So a not-yet-approved template never
breaks login, and the moment it's approved cold logins upgrade automatically — no redeploy.
Creating AUTHENTICATION templates can require Meta business verification; the create API may
return `AccessDeniedByMetaException` until that's done (create it in WhatsApp Manager instead).

### Branding / icons

The app icon master is `app/web/public/icon.svg`; the PNG/favicon/apple-touch sizes and
`manifest.webmanifest` are generated alongside it and wired into `app/web/index.html`.
Regenerate the PNGs from the SVG with `@resvg/resvg-js` (render at 1024/512/192/180/32/16).
Because `deploy-web.sh` marks non-`index.html` assets `immutable`, a revised icon needs a
cache invalidation or a versioned filename to show.

## Architecture

```
src/WhatsAppClient.Core/            Reusable library — message models, validation, send/media/parse services
src/WhatsAppClient.Lambda/          Send Lambda (text/template/image/document/video/audio)
src/WhatsAppClient.ReceiveLambda/   Receive Lambda (SQSEvent from the event-destination topic)
src/WhatsAppClient.AutoReplyLambda/ Auto-reply Lambda (EventBridge MessageReceived -> ack reply)
src/WhatsAppClient.App/             Console domain library — repo, auth, realtime, conversation/contact/user services
src/WhatsAppClient.Api/             ASP.NET Core minimal API on Lambda; REST + serves the SPA from S3
src/WhatsAppClient.AppIngestLambda/ Ingest Lambda (EventBridge -> AppTable, publishes realtime events)
src/WhatsAppClient.WebSocketLambda/ WebSocket API authorizer + $connect/$disconnect handler
app/web/                            React + TypeScript + Vite SPA (the console)
app/deploy-web.sh                   Build + sync the SPA to the WebBucket
template.yaml                       SAM stack (see Overview for the resource list)
tests/*.Tests/                      Six test projects — Core, App, and the Send/Receive/AutoReply/WebSocket Lambdas (104 tests)
```

Inbound flow: `Customer → Meta → AWS End User Messaging Social → SNS (event destination) →
SQS (+ DLQ) → ReceiveLambda → {download media to S3, persist, mark read, PutEvents to
EventBridge} → {AutoReplyLambda, AppIngestLambda}`. AppIngestLambda persists to AppTable and
pushes a realtime event to connected console clients.

Console request flow: `Browser → (custom domain) HTTP API → ApiFunction (REST + SPA)`; and
`Browser ──wss──▶ WebSocket API ($connect JWT authorizer) → connection stored in AppTable`,
with `ApiFunction`/`AppIngestFunction` pushing events back via `@connections PostToConnection`.

### WhatsAppClient.Core

- `Models/` — `WhatsAppMessage` is an abstract base (`WhatsAppTextMessage`,
  `WhatsAppTemplateMessage`, plus read-receipt/reaction shapes) mirroring the Meta Cloud API
  JSON shape (`messaging_product`, `recipient_type`, `to`, `type`, plus `text`/`template`).
  **Important**: because `Type` is an `abstract`/`override` property, serialization must
  use the runtime type (`JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), ...)`),
  not the declared `WhatsAppMessage` type — otherwise derived-only properties are dropped.
- `Services/WhatsAppMessageService` — validates recipient (E.164 via
  `Validation/PhoneNumberValidator`) and body constraints, serializes the message to JSON
  bytes, wraps them in a `MemoryStream`, and calls
  `IAmazonSocialMessaging.SendWhatsAppMessageAsync` with `OriginationPhoneNumberId` and
  `MetaApiVersion` from `Configuration/WhatsAppOptions`. AWS SDK exceptions
  (`AmazonSocialMessagingException`) are caught and rethrown as
  `Exceptions/WhatsAppMessageException`. `MarkMessageReadAsync` (sends a `WhatsAppReadReceipt`,
  a shape *without* `to`/`type`) and `SendReactionAsync` (`WhatsAppReactionMessage`) reuse the
  same private `SendRawAsync` send path. Outbound sends set `biz_opaque_callback_data` so the
  later status update can be correlated back to the message.
- `Services/WhatsAppMediaService` — `DownloadToS3Async` calls `GetWhatsAppMessageMedia` with a
  `DestinationS3File` so AWS writes inbound media straight to S3 (bytes never pass through the
  caller); `UploadFromS3Async` calls `PostWhatsAppMessageMedia`. Neither media API takes
  `MetaApiVersion`.
- `Services/WhatsAppEventParser` — deserializes an AWS event (the SNS `Message` string) into a
  `WhatsAppEventEnvelope`, then deserializes the nested `whatsAppWebhookEntry` *string* (Meta's
  `entry` object) and flattens it into a `Models/Inbound/WhatsAppInboundEvent` (messages +
  statuses + contact lookup). Uses `PropertyNameCaseInsensitive` so the PascalCase AWS header and
  snake_case Meta fields both bind.
- `ServiceCollectionExtensions.AddWhatsAppMessaging(configuration)` — binds
  `WhatsAppOptions` from the `"WhatsApp"` config section, registers `IAmazonSocialMessaging` via
  `AWSSDK.Extensions.NETCore.Setup`, registers `IWhatsAppMessageService` / `IWhatsAppMediaService`
  / `IWhatsAppEventParser`, and adds a `NullLogger<>` fallback so a minimal host resolves the
  services without calling `AddLogging` (a host that does call `AddLogging` wins).

### WhatsAppClient.Lambda

- `Function.FunctionHandler(SendMessageInput, ILambdaContext) -> SendMessageOutput` —
  never throws; catches `ArgumentException`/`WhatsAppMessageException` and returns
  `{ Success = false, ErrorMessage }` instead, so callers (Step Functions, SQS, API
  Gateway) can branch on the result.
- `SendMessageInput.MessageType` is `"text"`, `"template"`, or a media type
  (`"image"|"document"|"video"|"audio"`), case-insensitive. Media sends take `MediaId` or
  `MediaLink` (public HTTPS) plus optional `Caption`/`Filename`. Template components only support
  `text`-type parameters in the Lambda DTO; for currency/date_time/media parameters, call
  `IWhatsAppMessageService` directly from Core's richer `WhatsAppTemplateParameter` model.
- Built as an **executable deployment package** (`OutputType=Exe`, `Program.cs` uses
  `LambdaBootstrapBuilder` + `Amazon.Lambda.RuntimeSupport`) with a source-generated
  `System.Text.Json` serializer for fast cold starts. `aws-lambda-tools-defaults.json` targets
  `arm64`/`dotnet8`.
- The default `Function()` constructor builds its own DI container; the internal
  `Function(IWhatsAppMessageService)` constructor is for tests (`InternalsVisibleTo`).

### WhatsAppClient.ReceiveLambda

- `Function.FunctionHandler(SQSEvent, ILambdaContext) -> SQSBatchResponse` — for each record,
  `ExtractEventJson` unwraps the SNS notification (handles both raw and non-raw delivery), the
  parser builds a `WhatsAppInboundEvent`, and `InboundMessageProcessor` runs the side effects.
  Per-record failures are returned as `BatchItemFailures` (partial-batch responses), so only the
  failed records are redelivered and eventually land in the DLQ.
- `Processing/InboundMessageProcessor` — per message: download media to S3 (when present), persist
  via `IInboundMessageStore`, mark read via `IWhatsAppMessageService`, publish via
  `IInboundEventPublisher`; per status: persist + publish. Each step is gated by a `ReceiveOptions`
  toggle (`DownloadMedia`/`MarkAsRead`/`PublishEvents`).
- `Persistence/DynamoDbInboundMessageStore` — single-table design, `PK = "WA#{waId}"`,
  `SK = "MSG#{ts}#{id}"` (or `"STATUS#..."`), with a `MessageId-index` GSI for correlating
  statuses.
- `Events/EventBridgeInboundEventPublisher` — `PutEvents` with `Source = "whatsapp.inbound"`,
  `DetailType = "MessageReceived" | "StatusUpdated"`.
- `ReceiveServiceCollectionExtensions.AddWhatsAppReceiver` binds the `"Receive"` config section.

### WhatsAppClient.AutoReplyLambda

- `Function.FunctionHandler(AutoReplyEvent, ILambdaContext) -> AutoReplyResult` — triggered by an
  EventBridge rule (`source: whatsapp.inbound`, `detail-type: MessageReceived`). Sends the
  configured acknowledgement via `IWhatsAppMessageService.SendTextMessageAsync`. Never throws.
  **Loop-safe**: the reply only produces `StatusUpdated` events, never `MessageReceived`, so the
  rule cannot re-trigger. Ack text is configurable via `AutoReply__Message`.

### WhatsAppClient.App (console domain library)

- `Configuration/AppOptions` — binds the `"App"` section: `TableName`, `WebBucketName`,
  `SessionSecretArn`, `WabaId`, `BootstrapAdminUsername`/`Phone`, `LoginTemplateName`,
  `RealtimeEndpoint` (https `@connections` management URL), `RealtimeWsUrl` (wss URL for the
  client), `NotifyApiKey` (machine notify API key), `MediaBucketName` (S3 bucket for staging
  outbound media).
- `Persistence/DynamoAppRepository` (`IAppRepository`) — single-table `AppTable` design
  (PK/SK + a `GSI1` type-index + TTL). Holds users, contacts, conversations + windows, messages,
  auth challenges, login rate-limits (`RATE#`), and WebSocket connections (`CONN#`). Notable:
  `PatchMessageStatusByRefAsync` correlates a status to its message via the `biz_opaque`
  `MSGREF#` key; `PatchAuthDeliveryErrorAsync` records a login-code delivery failure;
  `TryStartLoginAsync` enforces the send-code cooldown; `PaginateAsync` follows
  `LastEvaluatedKey`.
- `Auth/` — `LoginCodes` (generate/hash/verify, TTL, attempts, cooldown), `SessionTokenService`
  (HS256 JWT, `MapInboundClaims=false`, ≥32-byte secret), `SessionSecretProvider` (reads the
  Secrets Manager secret).
- `Services/AuthService` — passwordless login: resolves/seeds the bootstrap admin, sends a code
  (template-first, approval-gated; free-form fallback), verifies it, issues a session.
  `ConversationService` (`ReplyAsync` with 24h-window guard, `SendTemplateAsync`; both stamp
  `biz_opaque`, persist the outbound message, and publish a realtime event), `ContactService`,
  `UserService` (`AddAsync`/`UpdateAsync`/`DeleteAsync`; username is the identity and stays
  fixed on update), `TemplateService` (`ListApprovedAsync`/`IsApprovedAsync`, ~60s cache),
  `NotifyService` (machine-API send: text/image/video to a phone, auto-creates the contact,
  persists + publishes; `IOutboundMediaStore`/`S3OutboundMediaStore` stage base64 media to the
  MediaBucket for `UploadFromS3Async`).
- `Realtime/RealtimePublisher` (`IRealtimePublisher`) — broadcasts a `RealtimeEvent`
  (`message`/`status`) to every stored connection via `IAmazonApiGatewayManagementApi`
  `PostToConnection`, pruning `GoneException` connections. **No-op when `RealtimeEndpoint` is
  empty** (local/tests).
- `Ingest/AppIngestProcessor` — handles EventBridge `MessageReceived` (self-register contact,
  append message, set window from the message timestamp, publish `message`) and `StatusUpdated`
  (correlate by `biz_opaque` and publish `status`, or record an auth delivery error).
- `AppServiceCollectionExtensions.AddWhatsAppApp` wires it all up (`ITemplateService`,
  `IRealtimePublisher`, repo, token/secret services, and the domain services).

### WhatsAppClient.Api (console REST + SPA host)

- ASP.NET Core minimal API hosted on Lambda via `Amazon.Lambda.AspNetCoreServer.Hosting`
  (`AddAWSLambdaHosting(LambdaEventSource.HttpApi)`), fronted by an HTTP API + custom domain.
- Routes under `/api`: `auth/{start,verify,delivery,me,logout}` (start is rate-limited; verify
  sets a session cookie and returns `{ token, user, wsUrl }`), `conversations` (list, thread,
  incremental `?after=`, `reply`, `template`), `contacts`, `users` (list/add/**update**/delete,
  admin-gated; responses are projected to a stable shape with `role` as a lowercase string),
  `templates`, `health`, and `notify` (machine API, below). `Security.cs` provides the
  `AuthFilter`/`AdminFilter`/`ApiKeyFilter` endpoint filters.
- `POST /api/notify` — machine-to-machine send for non-interactive clients (e.g. Home Assistant).
  Gated by `ApiKeyFilter` (`X-Api-Key` vs `App.NotifyApiKey`, constant-time compare; 503 when the
  key is unset). Body `{ to, text?, mediaUrl?|mediaBase64?, mediaType?(image|video), caption?,
  filename? }`. Delegates to `NotifyService` (in App): normalizes the phone, auto-creates the
  contact, sends text or an image/video (URL → `link`; base64 → stage to the MediaBucket via
  `IOutboundMediaStore` then `IWhatsAppMediaService.UploadFromS3Async` → handle), stamps
  `biz_opaque` for status correlation, persists the outbound message and publishes a realtime
  event so it shows in the console. Free-form, so it only delivers inside the recipient's 24h
  window. `NotifyApiKey` and `MediaBucketName` come from the `App` config section.
- `StaticSite` serves the built SPA from the WebBucket (content-typed, including
  `.webmanifest`); a `MapFallback("/{**path}")` serves files and falls back to `index.html` for
  client-side routes (the parameterless `MapFallback` `{*path:nonfile}` would 404 file paths).

### WhatsAppClient.AppIngestLambda

- Thin EventBridge handler that builds the App DI container and delegates to
  `AppIngestProcessor`. Granted `execute-api:ManageConnections` and the `App__RealtimeEndpoint`
  env so it can push realtime events. Source-gen JSON serializer, case-insensitive for the
  PascalCase EventBridge `detail` keys.

### WhatsAppClient.WebSocketLambda (class-handler style; two SAM handlers, one artifact)

- `Function.Authorize(APIGatewayCustomAuthorizerRequest) → APIGatewayCustomAuthorizerResponse`
  — validates the session JWT from `?token=` (browsers can't set WS handshake headers) and
  returns an Allow/Deny policy with `principalId = username`.
- `Function.Handle(APIGatewayProxyRequest) → APIGatewayProxyResponse` — by route key: `$connect`
  → `PutConnectionAsync(connectionId, username)`, `$disconnect` → `DeleteConnectionAsync`,
  `$default` → 200. `[assembly: LambdaSerializer(DefaultLambdaJsonSerializer)]`.

### app/web (React console)

- Vite + React + TypeScript. `api.ts` is the typed client (Bearer token in localStorage);
  `auth.tsx` holds the session + `wsUrl`; `realtime.ts` opens a reconnecting `wss://…?token=`
  and dispatches events.
- Pages: `Login` (username → code, polls `/auth/delivery` for failures), `Inbox`, `Conversation`
  (realtime append/patch + 30s fallback poll, template dialog), `Contacts`, `Users`
  (add/edit/delete via modal). `App.tsx` is the shell with a **collapsible slim icon-rail
  sidebar** (remembers the choice, auto-collapses on mobile to an overlay drawer). The logo
  (`/icon.svg`) shows on the login page and in the sidebar brand.

## Testing conventions

- Mock `IAmazonSocialMessaging` with Moq; capture the request's `Message` stream via
  `request.Message.ToArray()` **inside the `Callback`** — the service disposes the stream
  after the call returns, so capturing the `SendWhatsAppMessageRequest` reference alone and
  reading it later throws `ObjectDisposedException`.
- Model serialization tests parse the JSON with `JsonDocument` and assert against the Meta
  Cloud API field names (snake_case via `[JsonPropertyName]`).
- App tests use an in-memory `IAppRepository` (`tests/WhatsAppClient.App.Tests/InMemoryAppRepository.cs`)
  — implement any new repository method there too. `RealtimePublisher` is tested with a mocked
  `IAmazonApiGatewayManagementApi` and as a no-op when the endpoint is empty.
- `WhatsAppClient.Api` and `WhatsAppClient.AppIngestLambda` have no test project; their logic
  lives in `WhatsAppClient.App` (which is tested) — keep handlers thin.
