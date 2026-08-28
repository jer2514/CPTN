using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace RSDSystem.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _log;
        private readonly IHttpClientFactory _httpFactory;

        public EmailService(
            IConfiguration config,
            ILogger<EmailService> log,
            IHttpClientFactory httpFactory)
        {
            _config = config;
            _log = log;
            _httpFactory = httpFactory;
        }

        public bool IsApiConfigured =>
            !string.IsNullOrWhiteSpace(_config["Email:ApiKey"]);

        public bool IsSmtpConfigured
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

        public bool IsConfigured => IsApiConfigured || IsSmtpConfigured;

        public async Task<(bool Sent, string? Error)> SendSetPasswordLinkAsync(
            string toEmail, string fullName, string username, string setPasswordUrl,
            bool isInvite, CancellationToken cancellationToken = default)
        {
            var heading = isInvite ? "Create your password" : "Reset your password";
            var subject = isInvite
                ? "RSD Payroll: create your password"
                : "RSD Payroll: reset your password";
            var intro = isInvite
                ? "Admin created your RSD Payroll account. Click the button below to choose your password. You do not need a code."
                : "Use the button below to choose a new password. You do not need a code.";
            var expiry = isInvite ? "48 hours" : "2 hours";
            var encodedUrl = WebUtility.HtmlEncode(setPasswordUrl);
            var encodedName = WebUtility.HtmlEncode(fullName);
            var encodedUser = WebUtility.HtmlEncode(username);

            var text = $"Hi {fullName},\n\n"
                + $"{intro}\n\n"
                + $"Username: {username}\n"
                + $"Create password: {setPasswordUrl}\n\n"
                + $"This link expires in {expiry}. If you did not expect this email, ignore it.\n\n"
                + "RSD Payroll System";
            var html = WrapHtml(
                heading,
                $"<p>Hi {encodedName},</p>"
                + $"<p>{WebUtility.HtmlEncode(intro)}</p>"
                + $"<p>Username: <strong>{encodedUser}</strong></p>"
                + "<p style=\"margin:28px 0\">"
                + $"<a href=\"{encodedUrl}\" style=\"display:inline-block;background:#163F8B;color:#ffffff;padding:12px 22px;"
                + "border-radius:8px;text-decoration:none;font-weight:700\">Create your password</a></p>"
                + $"<p style=\"font-size:13px;color:#6b7a99\">This link expires in {expiry}. "
                + "If the button does not work, paste this address into your browser:</p>"
                + $"<p style=\"font-size:12px;word-break:break-all;color:#163F8B\">{encodedUrl}</p>");
            return await SendAsync(toEmail, subject, text, html, cancellationToken);
        }

        public async Task SendPasswordChangedAsync(
            string toEmail, string fullName, CancellationToken cancellationToken = default)
        {
            var subject = "RSD Payroll: your password was changed";
            var text = $"Hi {fullName},\n\n"
                + "Your RSD Payroll password was changed. If this was you, no action is needed.\n"
                + "If you did not change it, use Forgot password on the login page and tell Admin.\n\n"
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

            string? apiError = null;
            if (IsApiConfigured)
            {
                var api = await SendViaApiAsync(toEmail, subject, textBody, htmlBody, cancellationToken);
                if (api.Sent)
                    return api;

                apiError = api.Error;
                _log.LogWarning("Email API send failed for {To}: {Error}", toEmail, api.Error);
            }

            if (IsSmtpConfigured)
            {
                var smtp = await SendViaSmtpAsync(toEmail, subject, textBody, htmlBody, cancellationToken);
                if (smtp.Sent)
                    return smtp;

                return smtp;
            }

            if (apiError != null)
                return (false, apiError);

            return (false, "Mail is not set up yet. Put a Resend API key in Email:ApiKey, "
                + "or a Gmail App Password in Smtp:User and Smtp:Password, then restart the app.");
        }

        private async Task<(bool Sent, string? Error)> SendViaApiAsync(
            string toEmail, string subject, string textBody, string htmlBody,
            CancellationToken cancellationToken)
        {
            try
            {
                var provider = (_config["Email:Provider"] ?? "Resend").Trim();
                var fromEmail = FromEmail();
                if (string.Equals(provider, "Resend", StringComparison.OrdinalIgnoreCase)
                    && LooksLikePublicMailbox(fromEmail))
                {
                    return (false, "Resend cannot send From a Gmail address. Keep Email:FromEmail as "
                        + "beth.t@example.com, or send with a Gmail App Password in Smtp instead.");
                }

                var client = _httpFactory.CreateClient("EmailApi");
                using var request = BuildApiRequest(provider, toEmail, subject, textBody, htmlBody);
                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                    return (true, null);

                _log.LogError("Email API {Provider} returned {Status}: {Body}",
                    provider, (int)response.StatusCode, body);
                return (false, DescribeApiFailure(response.StatusCode, body));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Email API failed for {To}", toEmail);
                return (false, "Could not send the email. Try again in a few minutes.");
            }
        }

        private HttpRequestMessage BuildApiRequest(
            string provider, string toEmail, string subject, string textBody, string htmlBody)
        {
            var apiKey = _config["Email:ApiKey"]!.Trim();
            var fromEmail = FromEmail();
            var fromName = FromName();

            if (string.Equals(provider, "Brevo", StringComparison.OrdinalIgnoreCase))
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.TryAddWithoutValidation("api-key", apiKey);
                request.Content = JsonContent(new
                {
                    sender = new { name = fromName, email = fromEmail },
                    to = new[] { new { email = toEmail } },
                    subject,
                    htmlContent = htmlBody,
                    textContent = textBody
                });
                return request;
            }

            var resend = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            resend.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            resend.Content = JsonContent(new
            {
                from = $"{fromName} <{fromEmail}>",
                to = new[] { toEmail },
                subject,
                html = htmlBody,
                text = textBody
            });
            return resend;
        }

        private async Task<(bool Sent, string? Error)> SendViaSmtpAsync(
            string toEmail, string subject, string textBody, string htmlBody,
            CancellationToken cancellationToken)
        {
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
                _log.LogError(ex, "Failed to send SMTP email to {To}", toEmail);
                var detail = ex.Message;
                if (detail.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                    || detail.Contains("5.7.", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Gmail rejected the login. Use an App Password in Smtp:Password, not your normal Gmail password.");
                }
                return (false, "Could not send the email. Try again in a few minutes.");
            }
        }

        private string FromEmail()
        {
            var email = _config["Email:FromEmail"];
            if (!string.IsNullOrWhiteSpace(email))
                return email.Trim();

            var smtpFrom = _config["Smtp:FromEmail"];
            if (!string.IsNullOrWhiteSpace(smtpFrom))
                return smtpFrom.Trim();

            return "beth.t@example.com";
        }

        private string FromName()
        {
            var name = _config["Email:FromName"];
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            var smtpName = _config["Smtp:FromName"];
            return string.IsNullOrWhiteSpace(smtpName) ? "RSD Payroll System" : smtpName.Trim();
        }

        private static StringContent JsonContent(object payload)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private static bool LooksLikePublicMailbox(string email)
        {
            var at = email.LastIndexOf('@');
            if (at < 0 || at == email.Length - 1)
                return false;
            var domain = email[(at + 1)..];
            return domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("googlemail.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("yahoo.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("outlook.com", StringComparison.OrdinalIgnoreCase)
                || domain.Equals("hotmail.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeApiFailure(HttpStatusCode status, string body)
        {
            var apiMessage = TryReadApiMessage(body);

            if (!string.IsNullOrWhiteSpace(apiMessage)
                && apiMessage.Contains("only send testing emails", StringComparison.OrdinalIgnoreCase))
            {
                return "Resend test mode can only mail the Gmail used to create the Resend account. "
                    + "Add the staff user with that same Gmail, or put a Gmail App Password in Smtp.";
            }

            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
                return string.IsNullOrWhiteSpace(apiMessage)
                    ? "The email API key was rejected. Check Email:ApiKey in appsettings.json."
                    : apiMessage;

            if (status == HttpStatusCode.UnprocessableEntity || (int)status == 422)
                return "The email API rejected the From address. For Resend keep FromEmail as beth.t@example.com.";

            return string.IsNullOrWhiteSpace(apiMessage)
                ? "Could not send the email. Try again in a few minutes."
                : apiMessage;
        }

        private static string? TryReadApiMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                    return message.GetString();
            }
            catch (JsonException)
            {
            }
            return null;
        }

        private static string WrapHtml(string heading, string inner) =>
            "<div style=\"font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;color:#111\">"
            + $"<h2 style=\"color:#163F8B;margin:0 0 12px\">{WebUtility.HtmlEncode(heading)}</h2>"
            + inner
            + "<p style=\"color:#6b7a99;font-size:12px;margin-top:28px\">RSD Payroll System · RSD Construction Services</p>"
            + "</div>";
    }
}
