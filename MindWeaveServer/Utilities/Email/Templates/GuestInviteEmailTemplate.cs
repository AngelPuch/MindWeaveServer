using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class GuestInviteEmailTemplate : IEmailTemplate
    {
        public string subject => Lang.EmailSubjectGuestInvite;
        public string htmlBody { get; }

        public GuestInviteEmailTemplate(string inviterUsername, string lobbyCode)
        {
            // Assumes Lang keys exist
            string greeting = Lang.EmailGreetingGuest;
            string instruction = string.Format(Lang.EmailInstructionGuestInvite, inviterUsername);
            string codeInfo = Lang.EmailCodeInfoGuestInvite;
            string howToJoin = Lang.EmailHowToJoinGuest;

            htmlBody = $@"
                <div style='font-family: Arial, sans-serif; text-align: center; color: #333;'>
                    <div style='max-width: 600px; margin: auto; border: 1px solid #ddd; padding: 20px;'>
                        <h2>{greeting}</h2>
                        <p>{instruction}</p>
                        <p>{codeInfo}</p>
                        <div style='background-color: #f2f2f2; border-radius: 8px; padding: 10px 20px; margin: 20px auto; display: inline-block;'>
                            <h1 style='font-size: 32px; letter-spacing: 4px; margin: 0;'>{lobbyCode}</h1>
                        </div>
                        <p>{howToJoin}</p>
                        <hr style='border: none; border-top: 1px solid #eee;' />
                        <p style='font-size: 12px; color: #888;'>Mind Weave Team</p>
                    </div>
                </div>";
        }
    }
}