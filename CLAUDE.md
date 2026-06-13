# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

A C#/.NET 8 solution that **sends and receives** WhatsApp Business Platform messages from AWS
using **AWS End User Messaging Social** (`AWSSDK.SocialMessaging`). AWS manages the connection
to the WhatsApp Business Account (WABA) — the JSON message bodies follow the Meta WhatsApp
Cloud API "messages" schema and are passed through verbatim by AWS's `SendWhatsAppMessage`
API.

**Receiving:** you do *not* host your own HTTPS webhook. AWS owns the Meta webhook
subscription for your WABA and publishes every inbound message and status update to an
**Amazon SNS topic** you nominate as the WABA's *event destination*. This solution buffers
that topic through SQS and processes events in `WhatsAppClient.ReceiveLambda`. The whole
stack (both Lambdas, SNS/SQS/DLQ, DynamoDB, S3, EventBridge) is defined in `template.yaml`.

The .NET SDK is installed at `~/.dotnet` (not via Homebrew cask, due to sandbox sudo
restrictions). Use `export PATH="$HOME/.dotnet:$PATH"` before running `dotnet` if it's not
already on PATH.

## Commands

```bash
dotnet build                 # build the whole solution
dotnet test                  # run all tests (Core + three Lambdas), 66 tests
dotnet test --filter "FullyQualifiedName~WhatsAppMessageServiceTests"   # run one test class
dotnet test --filter "FullyQualifiedName~SendTextMessageAsync_WithEmptyBody_ThrowsArgumentException"  # single test
```

Deploy the whole stack (both Lambdas + SNS/SQS/DLQ + DynamoDB + S3 + EventBridge) with SAM:
```bash
sam build
sam deploy --guided   # supply OriginationPhoneNumberId and MetaApiVersion
```
After deploy, set the `EventsTopicArn` stack output as your WABA's **event destination** in the
AWS End User Messaging Social console (WhatsApp business accounts → your WABA → Event
destinations). SAM cannot do this for an already-linked WABA.

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

## Architecture

```
src/WhatsAppClient.Core/           Reusable library — message models, validation, send/media/parse services
src/WhatsAppClient.Lambda/         Send Lambda (text/template/image/document/video/audio)
src/WhatsAppClient.ReceiveLambda/  Receive Lambda (SQSEvent from the event-destination topic)
src/WhatsAppClient.AutoReplyLambda/ Auto-reply Lambda (EventBridge MessageReceived -> ack reply)
template.yaml                      SAM stack: 3 Lambdas + SNS/SQS/DLQ/DynamoDB/S3/EventBridge (+ rule)
tests/WhatsAppClient.Core.Tests/         Model/parser/service tests (Moq)
tests/WhatsAppClient.Lambda.Tests/       Send handler tests
tests/WhatsAppClient.ReceiveLambda.Tests/ Receive handler + processor tests
tests/WhatsAppClient.AutoReplyLambda.Tests/ Auto-reply handler tests
```

Inbound flow: `Customer → Meta → AWS End User Messaging Social → SNS (event destination) →
SQS (+ DLQ) → ReceiveLambda → {download media to S3, persist to DynamoDB, mark read, PutEvents
to EventBridge}`.

### WhatsAppClient.Core

- `Models/` — `WhatsAppMessage` is an abstract base (`WhatsAppTextMessage`,
  `WhatsAppTemplateMessage`) mirroring the Meta Cloud API JSON shape
  (`messaging_product`, `recipient_type`, `to`, `type`, plus `text`/`template`).
  **Important**: because `Type` is an `abstract`/`override` property, serialization must
  use the runtime type (`JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), ...)`),
  not the declared `WhatsAppMessage` type — otherwise derived-only properties are dropped.
- `Services/WhatsAppMessageService` — validates recipient (E.164 via
  `Validation/PhoneNumberValidator`) and body constraints, serializes the message to JSON
  bytes, wraps them in a `MemoryStream`, and calls
  `IAmazonSocialMessaging.SendWhatsAppMessageAsync` with `OriginationPhoneNumberId` and
  `MetaApiVersion` from `Configuration/WhatsAppOptions`. AWS SDK exceptions
  (`AmazonSocialMessagingException`) are caught and rethrown as
  `Exceptions/WhatsAppMessageException`.
  `Exceptions/WhatsAppMessageException`. `MarkMessageReadAsync` (sends a `WhatsAppReadReceipt`,
  a shape *without* `to`/`type`) and `SendReactionAsync` (`WhatsAppReactionMessage`) reuse the
  same private `SendRawAsync` send path.
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
  `WhatsAppOptions` from the `"WhatsApp"` config section (`OriginationPhoneNumberId`,
  `MetaApiVersion`), registers `IAmazonSocialMessaging` via
  `AWSSDK.Extensions.NETCore.Setup`, registers `IWhatsAppMessageService` / `IWhatsAppMediaService`
  / `IWhatsAppEventParser`, and adds a `NullLogger<>` fallback so a minimal host (the send Lambda)
  resolves the services without calling `AddLogging` (a host that does call `AddLogging` wins).

### WhatsAppClient.Lambda

- `Function.FunctionHandler(SendMessageInput, ILambdaContext) -> SendMessageOutput` —
  never throws; catches `ArgumentException`/`WhatsAppMessageException` and returns
  `{ Success = false, ErrorMessage }` instead, so callers (Step Functions, SQS, API
  Gateway) can branch on the result.
- `SendMessageInput.MessageType` is `"text"`, `"template"`, or a media type
  (`"image"|"document"|"video"|"audio"`), case-insensitive. Media sends take `MediaId` (a handle
  from `PostWhatsAppMessageMedia`) or `MediaLink` (public HTTPS), plus optional `Caption`/`Filename`,
  and call `IWhatsAppMessageService.SendMediaMessageAsync`. Template components only support
  `text`-type parameters in the Lambda DTO (`TemplateParameterInput`); for
  currency/date_time/media parameters, call `IWhatsAppMessageService` directly from Core's richer
  `WhatsAppTemplateParameter` model.
- Built as an **executable deployment package** (`OutputType=Exe`, `Program.cs` uses
  `LambdaBootstrapBuilder` + `Amazon.Lambda.RuntimeSupport`) with a source-generated
  `System.Text.Json` serializer (`LambdaJsonSerializerContext`) for fast cold starts.
  `aws-lambda-tools-defaults.json` targets `arm64`/`dotnet8`.
- The default `Function()` constructor builds its own DI container from
  `appsettings.json` + environment variables (`WhatsApp__OriginationPhoneNumberId`, etc.)
  and the default AWS credential chain. The internal `Function(IWhatsAppMessageService)`
  constructor is for tests — `WhatsAppClient.Lambda.Tests` is granted access via
  `InternalsVisibleTo` in `AssemblyInfo.cs`.

### WhatsAppClient.ReceiveLambda

- `Function.FunctionHandler(SQSEvent, ILambdaContext) -> SQSBatchResponse` — for each record,
  `ExtractEventJson` unwraps the SNS notification (handles both raw and non-raw delivery), the
  parser builds a `WhatsAppInboundEvent`, and `InboundMessageProcessor` runs the side effects.
  Per-record failures are returned as `BatchItemFailures` (partial-batch responses), so only the
  failed records are redelivered and eventually land in the DLQ. Same dual-constructor +
  `InternalsVisibleTo` test pattern as the send Lambda; the internal ctor injects the parser and
  `IInboundMessageProcessor`.
- `Processing/InboundMessageProcessor` — per message: download media to S3 (when present), persist
  via `IInboundMessageStore`, mark read via `IWhatsAppMessageService`, publish via
  `IInboundEventPublisher`; per status: persist + publish. Each step is gated by a `ReceiveOptions`
  toggle (`DownloadMedia`/`MarkAsRead`/`PublishEvents`).
- `Persistence/DynamoDbInboundMessageStore` — single-table design, `PK = "WA#{waId}"`,
  `SK = "MSG#{ts}#{id}"` (or `"STATUS#..."`), with a `MessageId-index` GSI for correlating
  statuses once outbound messages are also stored.
- `Events/EventBridgeInboundEventPublisher` — `PutEvents` with `Source = "whatsapp.inbound"`,
  `DetailType = "MessageReceived" | "StatusUpdated"`.
- `ReceiveServiceCollectionExtensions.AddWhatsAppReceiver` calls Core's `AddWhatsAppMessaging`
  then registers the DynamoDB/EventBridge clients, store, publisher, and processor. Config binds
  from the `"Receive"` section (env `Receive__MessagesTableName`, `Receive__MediaBucketName`,
  `Receive__EventBusName`, ...).

### WhatsAppClient.AutoReplyLambda

- `Function.FunctionHandler(AutoReplyEvent, ILambdaContext) -> AutoReplyResult` — triggered by an
  EventBridge rule (`source: whatsapp.inbound`, `detail-type: MessageReceived`) on the inbound bus.
  Reads `detail.Message.from` (a `wa_id`, normalized to E.164 by prefixing `+`) and sends the
  configured acknowledgement via `IWhatsAppMessageService.SendTextMessageAsync`. Never throws.
  **Loop-safe**: the reply only produces `StatusUpdated` events, never `MessageReceived`, so the
  rule cannot re-trigger. Ack text is configurable via `AutoReply__Message`. `AutoReplyEvent.Detail.Message`
  reuses Core's `WhatsAppInboundMessage`; the source-gen context is case-insensitive so the
  PascalCase EventBridge `detail` keys bind.

## Testing conventions

- Mock `IAmazonSocialMessaging` with Moq; capture the request's `Message` stream via
  `request.Message.ToArray()` **inside the `Callback`** — the service disposes the stream
  after the call returns, so capturing the `SendWhatsAppMessageRequest` reference alone and
  reading it later throws `ObjectDisposedException`.
- Model serialization tests parse the JSON with `JsonDocument` and assert against the Meta
  Cloud API field names (snake_case via `[JsonPropertyName]`).
