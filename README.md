# Whisperer — AWS WhatsApp messaging

A C#/.NET 8 solution that **sends and receives** WhatsApp Business Platform messages from AWS
using [AWS End User Messaging Social](https://docs.aws.amazon.com/social-messaging/latest/userguide/what-is-social-messaging.html)
(`AWSSDK.SocialMessaging`), plus **Whisperer**, a React management console for running
conversations from the browser. AWS manages the connection to your WhatsApp Business Account
(WABA) — you don't store or rotate a Meta access token yourself, and you don't host your own
webhook.

## What's in the box

- **Send** text, template, and media (image/document/video/audio) messages.
- **Receive** inbound messages and delivery/read statuses — AWS publishes them to an SNS topic
  (the WABA *event destination*), buffered through SQS and processed by a Lambda.
- **Auto-reply** to first inbound messages via an EventBridge rule.
- **Console (Whisperer):** passwordless login (one-time code over WhatsApp), inbox, threaded
  conversations, contact + system-user management, and **real-time updates over a WebSocket
  API** (no polling). Served as a React SPA from S3 by an ASP.NET Core Lambda, optionally on a
  custom domain.

## Architecture

```
Customer ─▶ Meta ─▶ AWS End User Messaging Social
                         │ (event destination)
                         ▼
   SNS ─▶ SQS (+DLQ) ─▶ ReceiveLambda ─▶ S3 (media) + DynamoDB + EventBridge
                                                          │
                              ┌───────────────────────────┴───────────────┐
                              ▼                                            ▼
                       AutoReplyLambda                            AppIngestLambda ─▶ AppTable
                                                                         │ realtime push
   Browser ─ HTTP API (custom domain) ─▶ ApiFunction (REST + serves SPA from S3)
   Browser ─ wss ─▶ WebSocket API ($connect JWT authorizer) ─▶ connections in AppTable
```

Outbound sends carry `biz_opaque_callback_data` so the later status update is correlated back
to the originating message.

## Solution layout

```
WhatsAppClient.sln
src/
  WhatsAppClient.Core/             Reusable library: message models, validation, send/media/parse services
  WhatsAppClient.Lambda/           Send Lambda (text/template/media)
  WhatsAppClient.ReceiveLambda/    Receive Lambda (SQS from the event-destination topic)
  WhatsAppClient.AutoReplyLambda/  Auto-reply Lambda (EventBridge MessageReceived -> ack)
  WhatsAppClient.App/              Console domain library: repo, auth, realtime, conversation/contact/user services
  WhatsAppClient.Api/              ASP.NET Core minimal API on Lambda; REST + serves the SPA
  WhatsAppClient.AppIngestLambda/  Ingest Lambda (EventBridge -> AppTable, realtime push)
  WhatsAppClient.WebSocketLambda/  WebSocket authorizer + $connect/$disconnect handler
app/
  web/                             React + TypeScript + Vite SPA (the console)
  deploy-web.sh                    Build + sync the SPA to the WebBucket
template.yaml                      SAM stack (all of the above + SNS/SQS/DLQ/DynamoDB/S3/EventBridge/APIs)
tests/                            Six test projects: Core, App, and the Send/Receive/AutoReply/WebSocket Lambdas (109 tests)
```

See [CLAUDE.md](CLAUDE.md) for a per-project deep dive.

## Prerequisites

1. A WhatsApp Business Account (WABA) and phone number, linked to AWS End User Messaging Social.
   See [Getting started with WhatsApp](https://docs.aws.amazon.com/social-messaging/latest/userguide/getting-started-whatsapp.html).
2. The WABA id and the origination phone-number id (`phone-number-id-xxxxxxxx…`):
   ```bash
   aws socialmessaging list-linked-whatsapp-business-accounts --query 'linkedAccounts[].{id:id,arn:arn}'
   aws socialmessaging get-linked-whatsapp-business-account --id <waba-id> \
     --query 'account.phoneNumbers[].phoneNumberId'
   ```
3. .NET 8 SDK, the AWS SAM CLI, Node.js 18+, and the Amazon.Lambda.Tools global tool:
   ```bash
   dotnet tool install -g Amazon.Lambda.Tools
   ```

## Build & test

```bash
dotnet build
dotnet test                              # 109 tests
npm --prefix app/web ci && npm --prefix app/web run build   # build the console
```

## Deploy

Deploy the full stack with SAM (parameters persist in the stack on re-deploys):

```bash
sam build
sam deploy --guided
```

Parameters:

| Parameter | Notes |
|---|---|
| `OriginationPhoneNumberId` | `phone-number-id-…` |
| `MetaApiVersion` | e.g. `v21.0` |
| `WabaId` | `waba-…` (lets the console list approved templates) |
| `BootstrapAdminUsername` / `BootstrapAdminPhone` | seeded as the first admin on first login |
| `WhispererDomainName` / `WhispererCertArn` | optional custom domain + ACM cert (us-east-1) |

Then ship the console:

```bash
app/deploy-web.sh whatsapp-messaging us-east-1 <aws-profile>
```

Finally, set the **`EventsTopicArn`** stack output as your WABA's **event destination** in the
AWS End User Messaging Social console (WhatsApp business accounts → your WABA → Event
destinations). SAM cannot do this for an already-linked WABA.

Useful stack outputs: `AppUrl` (Lambda URL), `ConsoleApiEndpoint`, `WhispererDomainTarget`
(CNAME target for the custom domain), `WebSocketUrl`, `WebBucketName`, `MediaBucketName`.

## Using the console

1. Open `AppUrl` (or your custom domain) and log in with the bootstrap admin username — a
   one-time code is sent to that admin's WhatsApp.
2. Inbox lists conversations; open one to reply (inside the 24h window) or start a template
   exchange (outside it). Updates arrive in real time over the WebSocket connection.
3. Manage contacts and system users (admins can add/edit/delete users) from the sidebar.

Login codes are delivered via the approved **AUTHENTICATION** template named by
`App.LoginTemplateName` (`verify_code_1`), which works outside the 24h window. Delivery is
approval-gated: until that template is `APPROVED`, the console falls back to a free-form code
(in-window only) automatically, with no redeploy needed once it clears review.

## Notify API (machine clients, e.g. Home Assistant)

A keyed endpoint lets a non-interactive client (like Home Assistant) send a WhatsApp message to
a phone number — handy for home-automation alerts with text and a camera snapshot or short clip.

- **Endpoint:** `POST /api/notify`
- **Auth:** `X-Api-Key: <key>` header, matched against the `NotifyApiKey` stack parameter. Empty
  key disables the endpoint (503).
- **Body** (JSON; `to` plus one of: `template`, `text`, or media):

  | field | notes |
  |---|---|
  | `to` | recipient phone, E.164 (e.g. `+15551234567`) — required |
  | `template` | approved template name → **delivers outside the 24h window** |
  | `params` | string array filling the template's body variables (`{{1}}`, `{{2}}`, …) |
  | `languageCode` | template language (default `en_US`) |
  | `text` | free-form message text (used as the caption when media is present) |
  | `mediaUrl` | public HTTPS URL to an image/video (Meta fetches it) |
  | `mediaBase64` | base64 media bytes (staged to S3, uploaded to WhatsApp; ≤16 MB decoded) — alternative to `mediaUrl` |
  | `mediaType` | `image` or `video` (required whenever media is present) |
  | `caption` | explicit caption (overrides `text` for the caption) |
  | `filename` | optional, mostly for choosing the staged file extension |

The recipient **must already be a contact** — add the number in the console first; unknown
numbers return `404`. The send is persisted and shows up in that contact's console thread.

**Free-form vs template:** `text`/media are free-form and only deliver while the recipient's 24h
window is open (for self-alerts, message your WABA number once to open it). A `template` send uses
an approved template and delivers **any time** — the right choice for unattended alerts. A
ready-made `home_event` UTILITY template exists (body `… recorded a new event: {{1}} …`).

**Media in a template (window-proof images):** add `mediaUrl`/`mediaBase64` + `mediaType` to a
`template` send and it fills the template's **media header**. The template must be created *with*
a matching image/video header — image-header templates can't be created via the AWS API (Meta
needs a resumable-upload handle), so create them in **WhatsApp Manager** (e.g. a `home_snapshot`
with an image header).

```bash
# Free-form (in-window):
curl -X POST https://whisperer.e-goller.com/api/notify \
  -H "X-Api-Key: $NOTIFY_API_KEY" -H "content-type: application/json" \
  -d '{"to":"+15551234567","text":"Garage door left open"}'

# Template (window-proof text):
curl -X POST https://whisperer.e-goller.com/api/notify \
  -H "X-Api-Key: $NOTIFY_API_KEY" -H "content-type: application/json" \
  -d '{"to":"+15551234567","template":"home_event","params":["Garage door left open"]}'

# Template with an image header (window-proof image alert):
curl -X POST https://whisperer.e-goller.com/api/notify \
  -H "X-Api-Key: $NOTIFY_API_KEY" -H "content-type: application/json" \
  -d '{"to":"+15551234567","template":"home_snapshot","params":["Garage door open"],"mediaUrl":"https://…/snap.jpg","mediaType":"image"}'
```

Home Assistant `rest_command` (text + an optional base64 camera snapshot):

```yaml
rest_command:
  whatsapp_notify:
    url: https://whisperer.e-goller.com/api/notify
    method: POST
    headers:
      X-Api-Key: !secret whisperer_notify_key
      content-type: application/json
    payload: >-
      {"to":"+15551234567","text":"{{ message }}"
      {% if image_b64 is defined %}, "mediaBase64":"{{ image_b64 }}", "mediaType":"image"{% endif %} }
```

Pass `NotifyApiKey=...` at deploy time (`sam deploy ... --parameter-overrides NotifyApiKey=<secret>`).

## Configuration reference

- `WhatsApp` section (all Lambdas): `OriginationPhoneNumberId`, `MetaApiVersion`.
- `Receive` section (ReceiveLambda): `MessagesTableName`, `MediaBucketName`, `EventBusName`,
  and the `DownloadMedia`/`MarkAsRead`/`PublishEvents` toggles.
- `App` section (Api/AppIngest): `TableName`, `WebBucketName`, `SessionSecretArn`, `WabaId`,
  `BootstrapAdminUsername`/`Phone`, `LoginTemplateName`, `RealtimeEndpoint`, `RealtimeWsUrl`,
  `NotifyApiKey`, `MediaBucketName`.

`template.yaml` sets these from stack parameters/resources; for local runs override via
environment variables (e.g. `WhatsApp__OriginationPhoneNumberId`, `App__TableName`).

## Required IAM permissions (send-only standalone use)

The Lambda execution role needs to call `SendWhatsAppMessage` (and, for media,
`PostWhatsAppMessageMedia`) on the linked phone-number resource:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["social-messaging:SendWhatsAppMessage", "social-messaging:PostWhatsAppMessageMedia"],
      "Resource": "arn:aws:social-messaging:REGION:ACCOUNT_ID:phone-number-id/*"
    }
  ]
}
```

The SAM template grants the full set of permissions each function needs.

## Using the library directly

```csharp
var services = new ServiceCollection();
services.AddWhatsAppMessaging(configuration); // reads the "WhatsApp" config section
var provider = services.BuildServiceProvider();

var whatsApp = provider.GetRequiredService<IWhatsAppMessageService>();
var result = await whatsApp.SendTextMessageAsync("+15551234567", "Hello from AWS!");
Console.WriteLine($"Sent message {result.MessageId}");
```

## Standalone send Lambda

The send function can be deployed on its own:

```bash
cd src/WhatsAppClient.Lambda
dotnet lambda deploy-function send-whatsapp-message --region <your-region>

aws lambda invoke --function-name send-whatsapp-message --cli-binary-format raw-in-base64-out \
  --payload '{"to":"+15551234567","messageType":"text","text":"Hello from AWS!"}' output.json
```

## Branding

The Whisperer app icon master is [`app/web/public/icon.svg`](app/web/public/icon.svg); the
PNG/favicon/apple-touch sizes and `manifest.webmanifest` are generated from it and wired into
`app/web/index.html`. Regenerate the PNGs from the SVG (e.g. with `@resvg/resvg-js` at
1024/512/192/180/32/16). Note that `deploy-web.sh` marks non-`index.html` assets `immutable`,
so a revised icon needs a cache invalidation or a versioned filename to show.
