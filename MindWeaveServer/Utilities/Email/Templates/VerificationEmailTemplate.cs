using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class VerificationEmailTemplate : IEmailTemplate
    {
        public string subject => Lang.EmailSubjectVerification;
        public string htmlBody { get; }

        public VerificationEmailTemplate(string username, string verificationCode)
        {
            string greeting = string.Format(Lang.EmailWelcome, username);
            string instruction = Lang.EmailInstructionVerify; 
            string expiryInfo = string.Format(Lang.EmailExpiryInfo, 5);
            string ignoreInfo = Lang.EmailIgnoreInfo;

            htmlBody = $@"
                <div style='font-family: Arial, sans-serif; text-align: center; color: #333;'>
                    <div style='max-width: 600px; margin: auto; border: 1px solid #ddd; padding: 20px;'>
                        <h2>{greeting}</h2>
                        <p>{instruction}</p>
                        <div style='background-color: #f2f2f2; border-radius: 8px; padding: 10px 20px; margin: 20px auto; display: inline-block;'>
                            <h1 style='font-size: 32px; letter-spacing: 4px; margin: 0;'>{verificationCode}</h1>
                        </div>
                        <p>{expiryInfo}</p>
                        <hr style='border: none; border-top: 1px solid #eee;' />
                        <p style='font-size: 12px; color: #888;'>{ignoreInfo}</p>
                    </div>
                </div>";
        }
    }
}
