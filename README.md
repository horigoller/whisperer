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
- **Notify API:** a keyed HTTP endpoint (`POST /api/notify`) for non-interactive clients like
  **Home Assistant** to send text, media, or template alerts to a WhatsApp number — see
  [Notify API](#notify-api-machine-clients-eg-home-assistant).

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
  | `headerText` | value for a template's **text header** variable (`{{1}}` in the header) |
  | `snapshots` | base64 image array composed with the Whisperer banner into the template's **image header** (banner on top, snapshots tiled below); `[]` = banner only. Requires a `template` with an image header (e.g. `home_snapshot`). ≤10 images, ≤16 MB each |
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
an approved template and delivers **any time** — the right choice for unattended alerts.

**`home_snapshot` is the default alert template** — a UTILITY template with an **image header** and
body `Home Assistant in {{1}} reported the following events: {{2}}. …`. Pass `params` = `[home,
events]` plus `snapshots` (base64 images). The server composes the **Whisperer banner on top with
the snapshots tiled below** into the single image header, so *every* alert is branded:
`snapshots: []` sends the banner alone (text-only alert), and `snapshots: [img, …]` adds up to 10
camera captures beneath it (`AlertImageComposer`). A text-only `home_event` template (text header,
no banner) is still available if you want an alert without the image header.

> Image-header templates can't be created via the AWS API (Meta needs a resumable-upload handle),
> so create them in **WhatsApp Manager**. `mediaUrl`/`mediaBase64` + `mediaType` still work to put a
> single raw image/video in a template's media header (no banner) — `snapshots` is the branded path.

```bash
# Default branded alert — banner only (text-only), window-proof:
curl -X POST https://whisperer.e-goller.com/api/notify \
  -H "X-Api-Key: $NOTIFY_API_KEY" -H "content-type: application/json" \
  -d '{"to":"+15551234567","template":"home_snapshot","params":["Wayland","Garage door left open"],"snapshots":[]}'

# Branded alert with camera snapshots (banner + tiled snapshots in the header):
curl -X POST https://whisperer.e-goller.com/api/notify \
  -H "X-Api-Key: $NOTIFY_API_KEY" -H "content-type: application/json" \
  -d '{"to":"+15551234567","template":"home_snapshot","params":["Wayland","Motion at front door"],"snapshots":["<base64-jpg>","<base64-jpg>"]}'

# Free-form text (in-window only, no banner):
curl -X POST https://whisperer.e-goller.com/api/notify \
  -H "X-Api-Key: $NOTIFY_API_KEY" -H "content-type: application/json" \
  -d '{"to":"+15551234567","text":"Garage door left open"}'
```

Pass `NotifyApiKey=...` at deploy time (`sam deploy ... --parameter-overrides NotifyApiKey=<secret>`).

### Home Assistant integration

Put the key + recipient in `secrets.yaml`:

```yaml
whisperer_notify_key: "YOUR_NOTIFY_API_KEY"   # the NotifyApiKey the stack was deployed with
whisperer_to: "+15551234567"                  # recipient (must be a saved Whisperer contact)
whisperer_home: "Wayland"                     # this HA instance's name (home_snapshot {{1}}) — set per instance
```

Add one `rest_command` per mode in `configuration.yaml` (the `| to_json` filter quotes/escapes
each value so messages with quotes or newlines can't break the JSON):

```yaml
rest_command:
  # Free-form text — delivers only inside the recipient's 24h window.
  whatsapp_text:
    url: https://whisperer.e-goller.com/api/notify
    method: POST
    content_type: "application/json"
    headers: { X-Api-Key: !secret whisperer_notify_key }
    payload: '{"to": {{ to | to_json }}, "text": {{ message | to_json }}}'

  # Default window-proof alert — branded banner + text via the home_snapshot template.
  #   body {{1}} = home name, {{2}} = the events; "snapshots": [] = banner only (no camera image).
  whatsapp_alert:
    url: https://whisperer.e-goller.com/api/notify
    method: POST
    content_type: "application/json"
    headers: { X-Api-Key: !secret whisperer_notify_key }
    payload: >-
      {"to": {{ to | to_json }}, "template": "home_snapshot",
       "params": [{{ home | to_json }}, {{ message | to_json }}],
       "snapshots": []}
```

A unified `script.whatsapp` picks the right mode automatically — call this everywhere. The default
is the branded `home_snapshot` alert (banner on top, text in the body); to include camera snapshots
beneath the banner, use the base64 helper below (the snapshot files are read and composited
server-side):

```yaml
script:
  whatsapp:
    fields:
      message:      { description: "Alert text / events", required: true }
      home:         { description: "This HA instance's name (home_snapshot {{1}})", default: !secret whisperer_home }
      window_proof: { description: "Use a template so it delivers outside 24h (banner alert)", default: true }
      to:           { description: "Recipient (E.164)", default: !secret whisperer_to }
    sequence:
      - choose:
          - conditions: "{{ window_proof }}"
            sequence:
              # Branded, window-proof: banner on top + text (no camera snapshots here).
              - service: rest_command.whatsapp_alert
                data: { to: "{{ to }}", home: "{{ home }}", message: "{{ message }}" }
        default:
          - service: rest_command.whatsapp_text
            data: { to: "{{ to }}", message: "{{ message }}" }
```

Use it from automations:

```yaml
automation:
  - alias: WhatsApp - garage door left open
    trigger:
      - platform: state
        entity_id: binary_sensor.garage_door
        to: "on"
        for: "00:05:00"
    action:
      - service: script.whatsapp
        data: { message: "Garage door has been open for 5 minutes" }

  - alias: WhatsApp - front door motion snapshot
    trigger:
      - platform: state
        entity_id: binary_sensor.front_door_motion
        to: "on"
    action:
      - service: camera.snapshot
        target: { entity_id: camera.front_door }
        data: { filename: "/config/www/front_door.jpg" }
      - delay: "00:00:02"
      # Branded banner + the camera snapshot composited beneath it (see the base64 helper below).
      - service: shell_command.whatsapp_snapshot
        data:
          to: !secret whisperer_to
          home: !secret whisperer_home
          message: "Motion at the front door"
          files: "/config/www/front_door.jpg"
```

**Camera snapshots under the banner (base64).** Camera files are local, so HA can't fetch them by
URL — the helper reads each file, base64-encodes it, and sends it in `snapshots`; Whisperer
composites the banner + snapshots server-side and sends the branded `home_snapshot`. Store the key
in `/config/.whisperer_notify_key` (chmod 600) and add `/config/scripts/whatsapp.py`:

```python
#!/usr/bin/env python3
import base64, json, os, sys, urllib.request
to      = sys.argv[1]
home    = sys.argv[2]                                 # home/instance name (body {{1}})
msg_b64 = sys.argv[3] if len(sys.argv) > 3 else ""    # message, base64'd by HA (quote-safe)
files   = sys.argv[4] if len(sys.argv) > 4 else ""    # comma-separated snapshot file paths (optional)
message = base64.b64decode(msg_b64).decode() if msg_b64 else ""
key = os.environ.get("WHISPERER_KEY") or open("/config/.whisperer_notify_key").read().strip()
snaps = []
for p in (f.strip() for f in files.split(",") if f.strip()):
    with open(p, "rb") as f:
        snaps.append(base64.b64encode(f.read()).decode())
# Branded, window-proof alert: banner on top + (optional) snapshots tiled below, text in the body.
payload = {"to": to, "template": "home_snapshot", "params": [home, message], "snapshots": snaps}
req = urllib.request.Request("https://whisperer.e-goller.com/api/notify",
    data=json.dumps(payload).encode(), method="POST",
    headers={"Content-Type": "application/json", "X-Api-Key": key})
with urllib.request.urlopen(req, timeout=30) as r:
    print(r.status, r.read().decode())
```

```yaml
shell_command:
  whatsapp_snapshot: >-
    python3 /config/scripts/whatsapp.py
    "{{ to }}" "{{ home }}" "{{ message | base64_encode }}" "{{ files | default('') }}"
```

Call it after `camera.snapshot` (one file) or several captures (comma-separate the paths in
`files`); pass no `files` for a banner-only branded alert. Pass `to` and `home` from your
automation (e.g. `to: !secret whisperer_to`, `home: !secret whisperer_home`). Each snapshot must be
≤16 MB and at most 10 are composited.

**Caveats**
- The recipient must be a **saved contact** in the console (unknown numbers → `404`).
- `text`/media are **in-window only**; use a `template` for unattended/window-proof alerts — and
  the template must be **APPROVED** first (a send to an unapproved template name returns
  `ok: true` but doesn't deliver).
- Media must be ≤16 MB (base64) and `mediaType` is required whenever media is present.

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
