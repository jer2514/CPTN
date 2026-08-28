using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace RSDSystem.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _log;

        public EmailService(IConfiguration config, ILogger<EmailService> log)
        {
            _config = config;
            _log = log;
        }

        public bool IsConfigured
        {
            get
            {
                var host = _config["Smtp:Host"];
                var user = _config["Smtp:User"];
                var password = _config["Smtp:Password"];
                return !string.IsNullOrWhiteSpace(host)
                    && !string.IsNullOrWhiteSpace(user)
                    && !string.IsNullOrWhiteSpace(password);
            }
        }

        public async Task<(bool Sent, string? Error)> SendVerificationCodeAsync(
            string toEmail, string fullName, string code, CancellationToken cancellationToken = default)
        {
            var subject = "RSD Payroll: confirm it's you";
            var text = $"Hi {fullName},\n\n"
                + "Use this code to confirm your identity and create a new password:\n\n"
                + $"    {code}\n\n"
                + "This code expires in 10 minutes. If you did not ask to change your password, ignore this email.\n\n"
                + "RSD Payroll System";
            var html = WrapHtml(
                "Confirm it's you",
                $"<p>Hi {WebUtility.HtmlEncode(fullName)},</p>"
                + "<p>Use this code to confirm your identity and create a new password:</p>"
                + $"<p style=\"font-size:28px;letter-spacing:6px;font-weight:700;color:#163F8B;margin:24px 0\">{WebUtility.HtmlEncode(code)}</p>"
                + "<p>This code expires in 10 minutes. If you did not ask to change your password, ignore this email.</p>");
            return await SendAsync(toEmail, subject, text, html, cancellationToken);
        }

        public async Task SendPasswordChangedAsync(
            string toEmail, string fullName, CancellationToken cancellationToken = default)
        {
            var subject = "RSD Payroll: your password was changed";
            var text = $"Hi {fullName},\n\n"
                + "Your RSD Payroll password was changed. If this was you, no action is needed.\n"
                + "If you did not change it, sign in with Forgot password and set a new one, then tell Admin.\n\n"
                + "RSD Payroll System";
            var html = WrapHtml(
                "Your password was changed",
                $"<p>Hi {WebUtility.HtmlEncode(fullName)},</p>"
                + "<p>Your RSD Payroll password was changed. If this was you, no action is needed.</p>"
                + "<p>If you did not change it, use <strong>Forgot password</strong> on the login page and tell Admin.</p>");
            var result = await SendAsync(toEmail, subject, text, html, cancellationToken);
            if (!result.Sent)
                _log.LogWarning("Password-changed email was not sent to {Email}: {Error}", toEmail, result.Error);
        }

        public async Task<(bool Sent, string? Error)> SendAsync(
            string toEmail, string subject, string textBody, string htmlBody,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                return (false, "This account has no email address.");

            if (!IsConfigured)
                return (false, "Email is not configured. Ask Admin to set SMTP in appsettings.json.");

            try
            {
                var host = _config["Smtp:Host"]!;
                var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
                var user = _config["Smtp:User"]!;
                var password = _config["Smtp:Password"]!;
                var fromEmail = string.IsNullOrWhiteSpace(_config["Smtp:FromEmail"])
                    ? user
                    : _config["Smtp:FromEmail"]!;
                var fromName = string.IsNullOrWhiteSpace(_config["Smtp:FromName"])
                    ? "RSD Payroll System"
                    : _config["Smtp:FromName"]!;

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, fromEmail));
                message.To.Add(MailboxAddress.Parse(toEmail));
                message.Subject = subject;
                message.Body = new BodyBuilder { TextBody = textBody, HtmlBody = htmlBody }.ToMessageBody();

                using var client = new SmtpClient();
                var secure = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                await client.ConnectAsync(host, port, secure, cancellationToken);
                await client.AuthenticateAsync(user, password, cancellationToken);
                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
                return (true, null);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send email to {To}", toEmail);
                return (false, "Could not send the email. Try again in a few minutes.");
            }
        }

        private static string WrapHtml(string heading, string inner) =>
            "<div style=\"font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;color:#111\">"
            + $"<h2 style=\"color:#163F8B;margin:0 0 12px\">{WebUtility.HtmlEncode(heading)}</h2>"
            + inner
            + "<p style=\"color:#6b7a99;font-size:12px;margin-top:28px\">RSD Payroll System · RSD Construction Services</p>"
            + "</div>";
    }
}
