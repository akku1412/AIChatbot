using ChatGPTClone.Api.Configuration;
using ChatGPTClone.Api.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ChatGPTClone.Api.Services;

public sealed class EmailDeliveryException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>SMTP sender (Gmail with an App Password by default). Credentials come from configuration only.</summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, string textBody,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

        // Never fall back to an unencrypted connection.
        var socketOptions = settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        using var client = new SmtpClient { Timeout = 20_000 };
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log the failure type/status only: no credentials, no message body (it contains the code).
            logger.LogError("Email delivery via {Host}:{Port} failed: {ErrorType} {Status}",
                settings.Host, settings.Port, ex.GetType().Name,
                (ex as SmtpCommandException)?.StatusCode.ToString() ?? (ex as AuthenticationException)?.Message);
            throw new EmailDeliveryException("Email delivery failed.", ex);
        }
    }
}
