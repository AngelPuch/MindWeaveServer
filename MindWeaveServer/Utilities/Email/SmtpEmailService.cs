using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using NLog;
using System;
using System.Configuration;
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

            host = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_HOST");
            string portStr = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_PORT");
            user = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_USER");
            pass = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_PASS");
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

            logger.Info("Attempting to send email (MailKit). Template: {TemplateType}, To: {RecipientEmail}, Name: {RecipientName}, Subject: '{Subject}'",
               template.GetType().Name, recipientEmail ?? "NULL", recipientName ?? "NULL", subject ?? "NULL");

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
                logger.Debug("Creating MimeMessage for recipient {RecipientEmail}", recipientEmail);
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
            }
            catch (AuthenticationException authEx)
            {
                logger.Error(authEx, "SMTP Authentication failed for user {SmtpUser}.", user);
                throw;
            }
            catch (SmtpCommandException smtpCmdEx)
            {
                logger.Error(smtpCmdEx, "SMTP Command error. Status: {StatusCode}", smtpCmdEx.StatusCode);
                throw;
            }
            catch (SmtpProtocolException smtpProtoEx)
            {
                logger.Error(smtpProtoEx, "SMTP Protocol error.");
                throw;
            }
            catch (IOException ioEx)
            {
                logger.Error(ioEx, "Network error while sending email to {RecipientEmail}.", recipientEmail);
                throw;
            }
            catch (System.Net.Sockets.SocketException sockEx)
            {
                logger.Error(sockEx, "Socket connection failed to SMTP host {Host}:{Port}.", host, port);
                throw;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unexpected error sending email to {RecipientEmail}.", recipientEmail);
                throw;
            }
        }
    }
}