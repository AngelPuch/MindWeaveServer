// Templates/PasswordRecoveryEmailTemplate.cs
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class PasswordRecoveryEmailTemplate : BaseEmailTemplate
    {
        private readonly string username;
        private readonly string recoveryCode;

        public override string Subject => Lang.EmailSubjectPasswordRecovery;
        protected override string greeting => string.Format(Lang.EmailGreeting, username);
        protected override string code => recoveryCode;
        protected override string instruction => Lang.EmailInstructionPasswordRecovery;
        protected override string codeInfo => Lang.EmailCodeInfoPasswordRecovery;
        protected override string expiryInfo => string.Format(Lang.EmailExpiryInfo, 5);
        protected override string footerText => Lang.EmailIgnoreInfo;

        public PasswordRecoveryEmailTemplate(string username, string recoveryCode)
        {
            this.username = username;
            this.recoveryCode = recoveryCode;
        }
    }
}