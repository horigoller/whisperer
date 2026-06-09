# aws-whatsapp-client

A C#/.NET 8 solution for sending WhatsApp Business Platform messages from AWS using
[AWS End User Messaging Social](https://docs.aws.amazon.com/social-messaging/latest/userguide/what-is-social-messaging.html)
(`AWSSDK.SocialMessaging`). AWS End User Messaging Social manages the connection to your
WhatsApp Business Account (WABA) — you don't need to store or rotate a Meta access token
yourself.

## Solution layout

```
WhatsAppClient.sln
src/
  WhatsAppClient.Core/      Reusable library: message models, validation, IWhatsAppMessageService
  WhatsAppClient.Lambda/    AWS Lambda function that sends a message via the Core library
tests/
  WhatsAppClient.Core.Tests/    Unit tests for models and the message service
  WhatsAppClient.Lambda.Tests/  Unit tests for the Lambda function handler
```

### WhatsAppClient.Core

- `Models/` — C# DTOs for the WhatsApp Cloud API "messages" JSON payload (text and
  template messages), serialized with `System.Text.Json`. AWS End User Messaging Social's
  `SendWhatsAppMessage` API passes this JSON through to Meta verbatim.
- `Services/IWhatsAppMessageService` / `WhatsAppMessageService` — validates input,
  builds the message JSON, and calls `IAmazonSocialMessaging.SendWhatsAppMessageAsync`.
- `Configuration/WhatsAppOptions` — `OriginationPhoneNumberId` and `MetaApiVersion`,
  bound from configuration.
- `Validation/PhoneNumberValidator` — validates recipient numbers are E.164.
- `Exceptions/WhatsAppMessageException` — wraps AWS SDK exceptions raised while sending.
- `ServiceCollectionExtensions.AddWhatsAppMessaging(configuration)` — registers
  `IAmazonSocialMessaging` and `IWhatsAppMessageService` for DI.

### WhatsAppClient.Lambda

A minimal Lambda function (`Function.FunctionHandler`) that accepts a `SendMessageInput`
(recipient, message type, text/template details), sends it via `IWhatsAppMessageService`,
and returns a `SendMessageOutput` (`Success`, `MessageId`, `ErrorMessage`) without throwing —
so callers (Step Functions, SQS consumers, API Gateway) can branch on the result.

It's built as an **executable deployment package** (the AWS-recommended .NET Lambda model
since .NET 7+) using `Amazon.Lambda.RuntimeSupport` and a source-generated
`System.Text.Json` serializer for fast cold starts, targeting the `arm64`/Graviton2
architecture for better price-performance.

## Prerequisites

1. A WhatsApp Business Account (WABA) and phone number, linked to AWS End User Messaging
   Social. See
   [Getting started with WhatsApp](https://docs.aws.amazon.com/social-messaging/latest/userguide/getting-started-whatsapp.html).
2. The phone number identifier from
   [`GetLinkedWhatsAppBusinessAccount`](https://docs.aws.amazon.com/social-messaging/latest/APIReference/API_GetLinkedWhatsAppBusinessAccount.html),
   formatted as `phone-number-id-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`.
3. .NET 8 SDK and the Amazon.Lambda.Tools global tool:
   ```bash
   dotnet tool install -g Amazon.Lambda.Tools
   ```

## Configuration

Set in `src/WhatsAppClient.Lambda/appsettings.json` (or override via Lambda environment
variables `WhatsApp__OriginationPhoneNumberId` / `WhatsApp__MetaApiVersion`):

```json
{
  "WhatsApp": {
    "OriginationPhoneNumberId": "phone-number-id-00000000000000000000000000000000",
    "MetaApiVersion": "v21.0"
  }
}
```

Check [End User Messaging Social service endpoints](https://docs.aws.amazon.com/general/latest/gr/end-user-messaging.html)
for the Meta API versions supported in your Region.

## Building and testing

```bash
dotnet build
dotnet test
```

## Required IAM permissions

The Lambda execution role needs permission to call `SendWhatsAppMessage` (and, if you send
media, `PostWhatsAppMessageMedia`) on the linked phone number resource:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "social-messaging:SendWhatsAppMessage",
        "social-messaging:PostWhatsAppMessageMedia"
      ],
      "Resource": "arn:aws:social-messaging:REGION:ACCOUNT_ID:phone-number-id/*"
    }
  ]
}
```

## Deploying the Lambda function

```bash
cd src/WhatsAppClient.Lambda
dotnet lambda deploy-function send-whatsapp-message --region <your-region>
```

## Invoking

```bash
aws lambda invoke \
  --function-name send-whatsapp-message \
  --cli-binary-format raw-in-base64-out \
  --payload '{"to":"+15551234567","messageType":"text","text":"Hello from AWS!"}' \
  output.json
```

Template message example:

```json
{
  "to": "+15551234567",
  "messageType": "template",
  "templateName": "order_confirmation",
  "languageCode": "en_US",
  "components": [
    {
      "type": "body",
      "parameters": [
        { "type": "text", "text": "Jane Doe" },
        { "type": "text", "text": "12345" }
      ]
    }
  ]
}
```

## Using the library directly

```csharp
var services = new ServiceCollection();
services.AddWhatsAppMessaging(configuration); // reads the "WhatsApp" config section
var provider = services.BuildServiceProvider();

var whatsApp = provider.GetRequiredService<IWhatsAppMessageService>();
var result = await whatsApp.SendTextMessageAsync("+15551234567", "Hello from AWS!");
Console.WriteLine($"Sent message {result.MessageId}");
```
