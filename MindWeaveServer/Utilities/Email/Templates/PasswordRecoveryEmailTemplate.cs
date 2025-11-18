using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class PasswordRecoveryEmailTemplate : IEmailTemplate
    {
        public string Subject => Lang.EmailSubjectPasswordRecovery; 
        public string HtmlBody { get; }

        public PasswordRecoveryEmailTemplate(string username, string recoveryCode)
        {
            string greeting = string.Format(Lang.EmailGreeting, username);
            string instruction = Lang.EmailInstructionPasswordRecovery;
            string codeInfo = Lang.EmailCodeInfoPasswordRecovery;
            string expiryInfo = string.Format(Lang.EmailExpiryInfo, 5);
            string ignoreInfo = Lang.EmailIgnoreInfo;

            HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; text-align: center; color: #333;'>
                    <div style='max-width: 600px; margin: auto; border: 1px solid #ddd; padding: 20px;'>
                        <h2>{greeting}</h2>
                        <p>{instruction}</p>
                        <p>{codeInfo}</p>
                        <div style='background-color: #f2f2f2; border-radius: 8px; padding: 10px 20px; margin: 20px auto; display: inline-block;'>
                            <h1 style='font-size: 32px; letter-spacing: 4px; margin: 0;'>{recoveryCode}</h1>
                        </div>
                        <p>{expiryInfo}</p>
                        <hr style='border: none; border-top: 1px solid #eee;' />
                        <p style='font-size: 12px; color: #888;'>{ignoreInfo}</p>
                    </div>
                </div>";
        }
    }
}