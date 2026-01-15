// Templates/GuestInviteEmailTemplate.cs
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class GuestInviteEmailTemplate : BaseEmailTemplate
    {
        private readonly string inviterUsername;
        private readonly string lobbyCode;

        public override string Subject => Lang.EmailSubjectGuestInvite;
        protected override string greeting => Lang.EmailGreetingGuest;
        protected override string code => lobbyCode;
        protected override string instruction => string.Format(Lang.EmailInstructionGuestInvite, inviterUsername);
        protected override string codeInfo => Lang.EmailCodeInfoGuestInvite;
        protected override string footerText => Lang.EmailHowToJoinGuest;

        public GuestInviteEmailTemplate(string inviterUsername, string lobbyCode)
        {
            this.inviterUsername = inviterUsername;
            this.lobbyCode = lobbyCode;
        }
    }
}