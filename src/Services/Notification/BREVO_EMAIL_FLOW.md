# Brevo Email Flow

This document summarizes the Brevo email integration in the Notification service. It is meant to help future development, provider replacement, and flow debugging stay aligned with Clean Architecture.

## Purpose

`EmailVerificationConsumer` receives OTP messages from RabbitMQ. Instead of only saving a test record into `NotificationProfiles`, the service now sends the OTP email through Brevo Transactional Email API and stores the consumed message in `ProcessedMessages` to avoid duplicate processing.

## Current Flow

```text
RabbitMQ
  -> Infrastructure/BackgroundServices/EmailVerificationConsumer
  -> Application/EventHandlers/SendEmailVerificationOtpEventHandler
  -> Application/Services/Interfaces/IEmailSender
  -> Infrastructure/EmailProvider/BrevoEmailSender
  -> Brevo API: POST /v3/smtp/email
```

## Clean Architecture Boundaries

- `Application`
  - Owns the use case and abstraction: `IEmailSender`, `SendEmailVerificationOtpEventHandler`.
  - Does not know about Brevo, HTTP payloads, API keys, or any provider-specific details.
- `Infrastructure`
  - Owns provider-specific implementation: `BrevoEmailSender`, `BrevoSettings`.
  - Sends the HTTP request to Brevo through `HttpClient`.
- `API`
  - Binds configuration and registers dependencies in `Program.cs`.

## Main Files

- `StealDeal.Services.Notification.Application/Services/Interfaces/IEmailSender.cs`
  - Defines the email sending contract.
- `StealDeal.Services.Notification.Application/EventHandlers/SendEmailVerificationOtpEventHandler.cs`
  - Checks `ProcessedMessages`.
  - Calls `IEmailSender.SendOtpAsync(...)`.
  - Saves `ProcessedMessage` after the email is sent successfully.
- `StealDeal.Services.Notification.Infrastructure/EmailProvider/BrevoEmailSender.cs`
  - Builds the Brevo request payload.
  - Calls `/v3/smtp/email`.
  - Throws an exception when Brevo returns a non-success response.
- `StealDeal.Services.Notification.Infrastructure/Configuration/BrevoSettings.cs`
  - Holds `BaseUrl`, `ApiKey`, `FromEmail`, and `FromName`.
- `StealDeal.Services.Notification.API/Program.cs`
  - Registers `BrevoSettings`.
  - Registers `HttpClient<IEmailSender, BrevoEmailSender>`.

## Configuration

Non-sensitive values are stored in `StealDeal.Services.Notification.API/appsettings.json`:

```json
"Brevo": {
  "BaseUrl": "https://api.brevo.com",
  "FromEmail": "your-verified-sender@example.com",
  "FromName": "Steal Deal Dev"
}
```

The API key must be stored with user-secrets or environment variables, not committed to Git:

```powershell
dotnet user-secrets set "Brevo:ApiKey" "xkeysib-your-api-key"
```

If the command is run from the `StealDeal.Services.Notification.API` folder, the `--project` option is not required.

## Brevo Sender Notes

- `FromEmail` must be a verified sender in Brevo.
- Check sender status in:

```text
Brevo Dashboard -> Settings -> Senders, Domains & IPs -> Senders
```

`FromName` should match the Brevo sender name for easier maintenance.

## Reliability Notes

- Keep `ProcessedMessages` in the flow to avoid sending the same OTP again when RabbitMQ redelivers an already processed message.
- Current processing order:

```text
send email through Brevo
  -> save ProcessedMessage
  -> acknowledge RabbitMQ message
```

- Edge case: if Brevo accepts the email but the database save fails, RabbitMQ can retry the message and send a duplicate email. Before production, consider adding a retry policy, a dead-letter queue, or stronger idempotency handling.

## Future Extension

To replace Brevo later, keep `IEmailSender` in the Application layer and add a new adapter in Infrastructure:

```text
BrevoEmailSender
ResendEmailSender
SmtpEmailSender
```

Then switch the DI registration in `Program.cs`.

For additional email types beyond OTP, consider introducing a request object or a higher-level email service instead of adding provider-specific fields to the Application layer.

## Quick Verification

```powershell
dotnet user-secrets list
dotnet build ..\StealDeal.Services.Notification.slnx
dotnet run
```

After triggering the RabbitMQ OTP flow, verify:

- the email arrives in the recipient inbox
- Brevo dashboard shows a new transactional email event
- `ProcessedMessages` contains the consumed message id
- redelivering the same message id does not send another email
