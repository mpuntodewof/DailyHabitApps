using System.Net;
using System.Net.Mail;
using AtomicHabits.Config;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace AtomicHabits.Service
{
    public class EmailService : IEmailSender
    {
        private readonly EmailOptions _options;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailOptions> options, ILogger<EmailService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
            {
                _logger.LogError("SMTP credentials are not configured. Set Smtp:Username (config) and SMTP_PASSWORD (env var).");
                throw new InvalidOperationException("SMTP credentials are not configured.");
            }

            // For Gmail SMTP the From address must match the authenticated account, otherwise
            // the server rejects with 5.7.0. Default to Username when FromAddress is blank.
            var fromAddress = string.IsNullOrWhiteSpace(_options.FromAddress)
                ? _options.Username
                : _options.FromAddress;

            using var client = new SmtpClient
            {
                Host = _options.Host,
                Port = _options.Port,
                EnableSsl = _options.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                Timeout = 8000  // ms — keep the HTTP request short; surface the failure fast.
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromAddress, _options.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(email);

            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Sent email to {Email} (subject: {Subject})", email, subject);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex,
                    "SMTP failure sending to {Email}. StatusCode={StatusCode}, Host={Host}, From={From}",
                    email, ex.StatusCode, _options.Host, fromAddress);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure sending email to {Email}", email);
                throw;
            }
        }
    }
}
