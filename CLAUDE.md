# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

A C#/.NET 8 solution that sends WhatsApp Business Platform messages from AWS using
**AWS End User Messaging Social** (`AWSSDK.SocialMessaging`). AWS manages the connection
to the WhatsApp Business Account (WABA) — the JSON message bodies follow the Meta WhatsApp
Cloud API "messages" schema and are passed through verbatim by AWS's `SendWhatsAppMessage`
API.

The .NET SDK is installed at `~/.dotnet` (not via Homebrew cask, due to sandbox sudo
restrictions). Use `export PATH="$HOME/.dotnet:$PATH"` before running `dotnet` if it's not
already on PATH.

## Commands

```bash
dotnet build                 # build the whole solution
dotnet test                  # run all tests (Core + Lambda), ~40 tests
dotnet test --filter "FullyQualifiedName~WhatsAppMessageServiceTests"   # run one test class
dotnet test --filter "FullyQualifiedName~SendTextMessageAsync_WithEmptyBody_ThrowsArgumentException"  # single test
```

Lambda packaging/deploy (from `src/WhatsAppClient.Lambda`):
```bash
dotnet lambda deploy-function send-whatsapp-message --region <your-region>
```

## Architecture

```
src/WhatsAppClient.Core/      Reusable library — message models, validation, send service
src/WhatsAppClient.Lambda/    Lambda entry point that calls into Core
tests/WhatsAppClient.Core.Tests/    Model serialization + service tests (Moq)
tests/WhatsAppClient.Lambda.Tests/  Lambda handler tests (Moq + Amazon.Lambda.TestUtilities)
```

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
- `ServiceCollectionExtensions.AddWhatsAppMessaging(configuration)` — binds
  `WhatsAppOptions` from the `"WhatsApp"` config section (`OriginationPhoneNumberId`,
  `MetaApiVersion`), registers `IAmazonSocialMessaging` via
  `AWSSDK.Extensions.NETCore.Setup`, and registers `IWhatsAppMessageService`.

### WhatsAppClient.Lambda

- `Function.FunctionHandler(SendMessageInput, ILambdaContext) -> SendMessageOutput` —
  never throws; catches `ArgumentException`/`WhatsAppMessageException` and returns
  `{ Success = false, ErrorMessage }` instead, so callers (Step Functions, SQS, API
  Gateway) can branch on the result.
- `SendMessageInput.MessageType` is `"text"` or `"template"` (case-insensitive). Template
  components only support `text`-type parameters in the Lambda DTO (`TemplateParameterInput`);
  for currency/date_time/media parameters, call `IWhatsAppMessageService` directly from
  Core's richer `WhatsAppTemplateParameter` model.
- Built as an **executable deployment package** (`OutputType=Exe`, `Program.cs` uses
  `LambdaBootstrapBuilder` + `Amazon.Lambda.RuntimeSupport`) with a source-generated
  `System.Text.Json` serializer (`LambdaJsonSerializerContext`) for fast cold starts.
  `aws-lambda-tools-defaults.json` targets `arm64`/`dotnet8`.
- The default `Function()` constructor builds its own DI container from
  `appsettings.json` + environment variables (`WhatsApp__OriginationPhoneNumberId`, etc.)
  and the default AWS credential chain. The internal `Function(IWhatsAppMessageService)`
  constructor is for tests — `WhatsAppClient.Lambda.Tests` is granted access via
  `InternalsVisibleTo` in `AssemblyInfo.cs`.

## Testing conventions

- Mock `IAmazonSocialMessaging` with Moq; capture the request's `Message` stream via
  `request.Message.ToArray()` **inside the `Callback`** — the service disposes the stream
  after the call returns, so capturing the `SendWhatsAppMessageRequest` reference alone and
  reading it later throws `ObjectDisposedException`.
- Model serialization tests parse the JSON with `JsonDocument` and assert against the Meta
  Cloud API field names (snake_case via `[JsonPropertyName]`).
