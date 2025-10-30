using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Configuration;
using System.Threading.Tasks;
using NLog; // Using para NLog
using MindWeaveServer.Utilities.Email.Templates; // Asegúrate que IEmailTemplate esté aquí o en un namespace superior

namespace MindWeaveServer.Utilities.Email
{
    public class SmtpEmailService : IEmailService
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger(); // Logger NLog

        private readonly string host;
        private readonly int port;
        private readonly string user;
        private readonly string pass;
        private readonly string senderName;

        public SmtpEmailService()
        {
            logger.Info("SmtpEmailService (MailKit) instance created."); // Log añadido
            try // Mantener try-catch original para la configuración
            {
                host = ConfigurationManager.AppSettings["SmtpHost"];
                port = Convert.ToInt32(ConfigurationManager.AppSettings["SmtpPort"]);
                user = ConfigurationManager.AppSettings["SmtpUser"]; // Clave de tu App.config
                pass = ConfigurationManager.AppSettings["SmtpPass"]; // Clave de tu App.config
                senderName = ConfigurationManager.AppSettings["SenderName"]; // Clave de tu App.config

                // Loguear configuración (sin contraseña)
                logger.Debug("SMTP (MailKit) Configuration loaded: Host={SmtpHost}, Port={SmtpPort}, User={SmtpUser}, SenderName={SenderName}", host, port, user, senderName); // Log añadido

                // Validar que las claves existan (básico)
                if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass) || string.IsNullOrWhiteSpace(senderName))
                {
                    logger.Fatal("CRITICAL: SMTP (MailKit) configuration is missing or invalid in App.config. Email service will likely fail."); // Log añadido
                }
            }
            catch (Exception ex)
            {
                logger.Fatal(ex, "CRITICAL: Failed to load or parse SMTP (MailKit) configuration from App.config. Email service might be non-functional."); // Log añadido
                                                                                                                                                            // Considerar relanzar si es crítico para el arranque
                                                                                                                                                            // throw;
            }
        }

        public async Task sendEmailAsync(string recipientEmail, string recipientName, IEmailTemplate template)
        {
            // Usar propiedades de IEmailTemplate como en tu código original
            string subject = template.subject; // Propiedad directa
            string htmlBody = template.htmlBody; // Propiedad directa

            logger.Info("Attempting to send email (MailKit). Template: {TemplateType}, To: {RecipientEmail}, Name: {RecipientName}, Subject: '{Subject}'",
               template?.GetType().Name ?? "Unknown", recipientEmail ?? "NULL", recipientName ?? "NULL", subject ?? "NULL"); // Log añadido

            // Validar parámetros (mejorado ligeramente para loguear)
            if (string.IsNullOrWhiteSpace(recipientEmail) || template == null)
            {
                logger.Warn("Failed to send email (MailKit): Recipient email or template is null/whitespace."); // Log añadido
                return; // Salir temprano
            }
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(htmlBody))
            {
                logger.Warn("Failed to send email (MailKit) to {RecipientEmail}: Template subject or body is null/whitespace.", recipientEmail); // Log añadido
                return; // Salir si la plantilla no tiene contenido
            }

            try // Añadir try-catch alrededor de las operaciones de MailKit
            {
                logger.Debug("Creating MimeMessage for recipient {RecipientEmail}", recipientEmail); // Log añadido
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(senderName, user));
                message.To.Add(new MailboxAddress(recipientName, recipientEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
                message.Body = bodyBuilder.ToMessageBody();
                logger.Debug("MimeMessage created successfully."); // Log añadido

                using (var client = new SmtpClient())
                {
                    logger.Debug("Connecting to SMTP server: {SmtpHost}:{SmtpPort} using StartTls...", host, port); // Log añadido
                    await client.ConnectAsync(host, port, SecureSocketOptions.StartTls); // Como en tu código
                    logger.Debug("Connected. Authenticating with user: {SmtpUser}...", user); // Log añadido
                    await client.AuthenticateAsync(user, pass);
                    logger.Debug("Authenticated. Sending message to {RecipientEmail}...", recipientEmail); // Log añadido
                    await client.SendAsync(message);
                    logger.Info("Email sent successfully via MailKit to {RecipientEmail}", recipientEmail); // Log añadido
                    logger.Debug("Disconnecting from SMTP server..."); // Log añadido
                    await client.DisconnectAsync(true);
                    logger.Debug("Disconnected."); // Log añadido
                }
            }
            // Capturar excepciones específicas de MailKit (las más comunes)
            catch (AuthenticationException authEx)
            {
                logger.Error(authEx, "MailKit AuthenticationException sending email to {RecipientEmail}. Check SMTP credentials (User: {SmtpUser}).", recipientEmail, user); // Log añadido
            }
            catch (SmtpCommandException smtpCmdEx)
            {
                logger.Error(smtpCmdEx, "MailKit SmtpCommandException sending email to {RecipientEmail}. StatusCode: {StatusCode}, Mailbox: {Mailbox}", recipientEmail, smtpCmdEx.StatusCode, smtpCmdEx.Mailbox?.Address ?? "N/A"); // Log añadido
            }
            catch (System.Net.Sockets.SocketException sockEx) // Excepción común de conexión
            {
                logger.Error(sockEx, "SocketException during MailKit operation (likely ConnectAsync) for {RecipientEmail}. Check Host/Port ({SmtpHost}:{SmtpPort}) and network.", recipientEmail, host, port); // Log añadido
            }
            // Captura general
            catch (Exception ex)
            {
                logger.Error(ex, "General exception occurred while sending email (MailKit) to {RecipientEmail}", recipientEmail); // Log añadido
            }
        }
    }
}