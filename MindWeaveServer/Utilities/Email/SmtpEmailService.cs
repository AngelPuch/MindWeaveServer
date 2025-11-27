using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Configuration;
using System.Threading.Tasks;
using NLog;

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
            try
            {
                host = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_HOST");
                port = Convert.ToInt32(Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_PORT"));
                user = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_USER");
                pass = Environment.GetEnvironmentVariable("MINDWEAVE_SMTP_PASS");
                senderName = "Mind Weave Team";

                if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(senderName))
                {
                    logger.Fatal("CRITICAL: SMTP (MailKit) configuration is missing or invalid in App.config. Email service will likely fail.");
                }
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "CRITICAL: Failed to load or parse SMTP (MailKit) configuration from App.config. Email service might be non-functional."); 
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
                logger.Error(authEx, "MailKit AuthenticationException sending email to {RecipientEmail}. Check SMTP credentials (User: {SmtpUser}).", recipientEmail, user); 
            }
            catch (SmtpCommandException smtpCmdEx)
            {
                logger.Error(smtpCmdEx, "MailKit SmtpCommandException sending email to {RecipientEmail}. StatusCode: {StatusCode}, Mailbox: {Mailbox}", recipientEmail, smtpCmdEx.StatusCode, smtpCmdEx.Mailbox?.Address ?? "N/A");
            }
            catch (System.Net.Sockets.SocketException sockEx)
            {
                logger.Error(sockEx, "SocketException during MailKit operation (likely ConnectAsync) for {RecipientEmail}. Check Host/Port ({SmtpHost}:{SmtpPort}) and network.", recipientEmail, host, port);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "General exception occurred while sending email (MailKit) to {RecipientEmail}", recipientEmail);
            }
        }
    }
}