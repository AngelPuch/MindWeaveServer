using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Configuration;
using System.Threading.Tasks;

namespace MindWeaveServer.Utilities.Email
{
    public class SmtpEmailService : IEmailService
    {
        private readonly string host;
        private readonly int port;
        private readonly string user;
        private readonly string pass;
        private readonly string senderName;

        public SmtpEmailService()
        {
            host = ConfigurationManager.AppSettings["SmtpHost"];
            port = Convert.ToInt32(ConfigurationManager.AppSettings["SmtpPort"]);
            user = ConfigurationManager.AppSettings["SmtpUser"];
            pass = ConfigurationManager.AppSettings["SmtpPass"];
            senderName = ConfigurationManager.AppSettings["SenderName"];
        }

        public async Task sendEmailAsync(string recipientEmail, string recipientName, IEmailTemplate template)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, user));
            message.To.Add(new MailboxAddress(recipientName, recipientEmail));
            message.Subject = template.subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = template.htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(user, pass);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }
    }
}