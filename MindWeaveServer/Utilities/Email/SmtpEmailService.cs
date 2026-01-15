using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NLog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MindWeaveServer.Utilities.Email
{
    public class SmtpEmailService : IEmailService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly string host;
        private readonly int port;
        private readonly string user;
        private readonly string pass;
        private readonly string senderName;

        public SmtpEmailService()
        {
            logger.Info("SmtpEmailService (MailKit) instance created.");

            host = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_HOST")?.Trim();
            string portStr = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_PORT")?.Trim();
            user = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_USER")?.Trim();
            pass = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_PASS")?.Trim();
            senderName = "Mind Weave Team";

            try
            {
                if (!string.IsNullOrWhiteSpace(portStr))
                {
                    port = Convert.ToInt32(portStr);
                }
            }
            catch (FormatException formatEx)
            {
                logger.Fatal(formatEx, "SMTP Port is not a valid number.");
                port = 0;
            }
            catch (OverflowException overflowEx)
            {
                logger.Fatal(overflowEx, "SMTP Port number is too large.");
                port = 0;
            }

            if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            {
                logger.Fatal("CRITICAL: SMTP (MailKit) configuration is missing or invalid. Email service will fail.");
            }
        }

        public async Task sendEmailAsync(string recipientEmail, string recipientName, IEmailTemplate template)
        {
            string subject = template.Subject;
            string htmlBody = template.HtmlBody;

            logger.Info("Attempting to send email (MailKit). Template: {TemplateType}, Name: {RecipientName}, Subject: '{Subject}'",
               template.GetType().Name, recipientEmail ?? "NULL", recipientName ?? "NULL");

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                logger.Warn("Failed to send email (MailKit): Recipient email or template is null/whitespace.");
                return;
            }
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(htmlBody))
            {
                logger.Warn("Failed to send email (MailKit) to {RecipientEmail}: Template subject or body is null/whitespace.", recipientEmail);
                return;
            }

            try
            {
                logger.Debug("Creating MimeMessage for recipient");
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(senderName, user));
                message.To.Add(new MailboxAddress(recipientName, recipientEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
                message.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                    await client.AuthenticateAsync(user, pass);
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }

                logger.Info("Email sent successfully to {RecipientEmail}", recipientEmail);
            }
            catch (AuthenticationException authEx)
            {
                logger.Error(authEx, "SMTP Authentication failed for user {SmtpUser}.", user);
                throw new InvalidOperationException($"Failed to authenticate with SMTP server using user '{user}'. Please verify credentials.", authEx);
            }
            catch (SmtpCommandException smtpCmdEx)
            {
                logger.Error(smtpCmdEx, "SMTP Command error. Status: {StatusCode}", smtpCmdEx.StatusCode);
                throw new InvalidOperationException($"SMTP server returned error status {smtpCmdEx.StatusCode} while sending email to {recipientEmail}.", smtpCmdEx);
            }
            catch (SmtpProtocolException smtpProtoEx)
            {
                logger.Error(smtpProtoEx, "SMTP Protocol error.");
                throw new InvalidOperationException($"SMTP protocol error occurred while sending email to {recipientEmail}.", smtpProtoEx);
            }
            catch (IOException ioEx)
            {
                logger.Error(ioEx, "Network error while sending email to {RecipientEmail}.", recipientEmail);
                throw new InvalidOperationException($"Network I/O error occurred while sending email to {recipientEmail}. Please check network connectivity.", ioEx);
            }
            catch (System.Net.Sockets.SocketException sockEx)
            {
                logger.Error(sockEx, "Socket connection failed to SMTP host {Host}:{Port}.", host, port);
                throw new InvalidOperationException($"Failed to connect to SMTP server at {host}:{port}. Please verify server address and firewall settings.", sockEx);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unexpected error sending email to {RecipientEmail}.", recipientEmail);
                throw new InvalidOperationException($"Unexpected error occurred while sending email to {recipientEmail}. See inner exception for details.", ex);
            }
        }
    }
}
