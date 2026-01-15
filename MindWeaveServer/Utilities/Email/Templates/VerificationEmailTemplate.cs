using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class VerificationEmailTemplate : BaseEmailTemplate
    {
        private readonly string username;
        private readonly string verificationCode;

        public override string Subject => Lang.EmailSubjectVerification;
        protected override string greeting => string.Format(Lang.EmailWelcome, username);
        protected override string code => verificationCode;
        protected override string instruction => Lang.EmailInstructionVerify;
        protected override string expiryInfo => string.Format(Lang.EmailExpiryInfo, 5);
        protected override string footerText => Lang.EmailIgnoreInfo;

        public VerificationEmailTemplate(string username, string verificationCode)
        {
            this.username = username;
            this.verificationCode = verificationCode;
        }
    }
}