using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Utilities;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Email.Templates;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic
{
    public class AuthenticationLogic
    {
        private readonly IEmailService emailService;
        private static readonly Random random = new Random();

        public AuthenticationLogic(IEmailService emailService)
        {
            this.emailService = emailService;
        }

        public async Task<OperationResultDto> registerPlayerAsync(UserProfileDto userProfile, string password)
        {
            // Aquí puedes añadir validaciones más robustas (longitud de contraseña, formato de email, etc.)
            if (userProfile == null || string.IsNullOrWhiteSpace(password))
            {
                return new OperationResultDto { success = false, message = "User profile and password are required." };
            }

            using (var context = new MindWeaveDBEntities())
            {
                if (context.Player.Any(p => p.username == userProfile.username || p.email == userProfile.email))
                {
                    return new OperationResultDto { success = false, message = "Username or email is already taken." };
                }

                string verificationCode = random.Next(100000, 999999).ToString("D6");

                var newPlayer = new Player
                {
                    username = userProfile.username,
                    email = userProfile.email,
                    password_hash = PasswordHasher.hashPassword(password),
                    first_name = userProfile.firstName,
                    last_name = userProfile.lastName,
                    date_of_birth = userProfile.dateOfBirth,
                    gender_id = userProfile.genderId,
                    is_verified = false,
                    verification_code = verificationCode,
                    code_expiry_date = DateTime.UtcNow.AddMinutes(15)
                };

                context.Player.Add(newPlayer);
                await context.SaveChangesAsync();

                var emailTemplate = new VerificationEmailTemplate(newPlayer.username, verificationCode);
                await emailService.sendEmailAsync(newPlayer.email, newPlayer.username, emailTemplate);

                return new OperationResultDto { success = true, message = "Registration successful. A verification code has been sent to your email." };
            }
        }

        public OperationResultDto verifyAccount(string email, string code)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                return new OperationResultDto { success = false, message = "Email and code are required." };
            }

            using (var context = new MindWeaveDBEntities())
            {
                var playerToVerify = context.Player.FirstOrDefault(p => p.email == email);

                if (playerToVerify == null)
                {
                    return new OperationResultDto { success = false, message = "No pending verification found for this email." };
                }

                if (playerToVerify.is_verified)
                {
                    return new OperationResultDto { success = false, message = "This account is already verified." };
                }

                if (playerToVerify.verification_code != code || playerToVerify.code_expiry_date < DateTime.UtcNow)
                {
                    return new OperationResultDto { success = false, message = "Invalid or expired verification code." };
                }

                playerToVerify.is_verified = true;
                playerToVerify.verification_code = null;
                playerToVerify.code_expiry_date = null;

                context.SaveChanges();

                return new OperationResultDto { success = true, message = "Account verified successfully. You can now log in." };
            }
        }
    }
}