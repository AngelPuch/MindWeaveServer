// MindWeaveServer/Utilities/Email/Templates/PasswordRecoveryEmailTemplate.cs
using MindWeaveServer.Resources; // Para acceder a Lang

namespace MindWeaveServer.Utilities.Email.Templates
{
    public class PasswordRecoveryEmailTemplate : IEmailTemplate
    {
        // Usaremos claves de Lang para el asunto y partes del cuerpo
        public string subject => Lang.EmailSubjectPasswordRecovery; // Clave nueva en Lang.resx
        public string htmlBody { get; }

        public PasswordRecoveryEmailTemplate(string username, string recoveryCode)
        {
            // Usaremos claves de Lang para partes del cuerpo
            string greeting = string.Format(Lang.EmailGreeting, username); // "Hello {0},"
            string instruction = Lang.EmailInstructionPasswordRecovery; // "You requested a password reset..."
            string codeInfo = Lang.EmailCodeInfoPasswordRecovery; // "Your recovery code is:"
            string expiryInfo = string.Format(Lang.EmailExpiryInfo, 5); // "This code will expire in {0} minutes." (Asumiendo 5 minutos)
            string ignoreInfo = Lang.EmailIgnoreInfo; // "If you didn't request this..."

            htmlBody = $@"
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