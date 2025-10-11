using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class VerificationEmailTemplate : IEmailTemplate
    {
        public string subject => "Your Mind Weave Verification Code";
        public string htmlBody { get; }

        public VerificationEmailTemplate(string username, string verificationCode)
        {
            htmlBody = $@"
                <div style='font-family: Arial, sans-serif; text-align: center; color: #333;'>
                    <div style='max-width: 600px; margin: auto; border: 1px solid #ddd; padding: 20px;'>
                        <h2>Welcome to Mind Weave, {username}!</h2>
                        <p>Thank you for registering. Please use the following code to activate your account:</p>
                        <div style='background-color: #f2f2f2; border-radius: 8px; padding: 10px 20px; margin: 20px auto; display: inline-block;'>
                            <h1 style='font-size: 32px; letter-spacing: 4px; margin: 0;'>{verificationCode}</h1>
                        </div>
                        <p>This code will expire in 24 hours.</p>
                        <hr style='border: none; border-top: 1px solid #eee;' />
                        <p style='font-size: 12px; color: #888;'>If you did not create this account, you can safely ignore this email.</p>
                    </div>
                </div>";
        }
    }
}
