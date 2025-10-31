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
                host = ConfigurationManager.AppSettings["SmtpHost"];
                port = Convert.ToInt32(ConfigurationManager.AppSettings["SmtpPort"]);
                user = ConfigurationManager.AppSettings["SmtpUser"];
                pass = ConfigurationManager.AppSettings["SmtpPass"];
                senderName = ConfigurationManager.AppSettings["SenderName"];

                logger.Debug("SMTP (MailKit) Configuration loaded: Host={SmtpHost}, Port={SmtpPort}, User={SmtpUser}, SenderName={SenderName}", host, port, user, senderName); 

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
            string subject = template.subject;
            string htmlBody = template.htmlBody;

            logger.Info("Attempting to send email (MailKit). Template: {TemplateType}, To: {RecipientEmail}, Name: {RecipientName}, Subject: '{Subject}'",
               template?.GetType().Name, recipientEmail ?? "NULL", recipientName ?? "NULL", subject ?? "NULL");

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
                logger.Debug("MimeMessage created successfully.");

                using (var client = new SmtpClient())
                {
                    logger.Debug("Connecting to SMTP server: {SmtpHost}:{SmtpPort} using StartTls...", host, port);
                    await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                    logger.Debug("Connected. Authenticating with user: {SmtpUser}...", user);
                    await client.AuthenticateAsync(user, pass);
                    logger.Debug("Authenticated. Sending message to {RecipientEmail}...", recipientEmail);
                    await client.SendAsync(message);
                    logger.Info("Email sent successfully via MailKit to {RecipientEmail}", recipientEmail);
                    logger.Debug("Disconnecting from SMTP server...");
                    await client.DisconnectAsync(true);
                    logger.Debug("Disconnected.");
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