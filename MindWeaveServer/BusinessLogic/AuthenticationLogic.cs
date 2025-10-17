using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.DataAccess;
using MindWeaveServer.Utilities;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Email.Templates;
using MindWeaveServer.Utilities.Validators;
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
            var profileValidator = new UserProfileDtoValidator();
            var profileValidationResult = await profileValidator.ValidateAsync(userProfile);

            if (!profileValidationResult.IsValid)
            {
                return new OperationResultDto { success = false, message = profileValidationResult.Errors.First().ErrorMessage };
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            {
                return new OperationResultDto { success = false, message = Resources.Lang.ValidationPasswordLength };
            }
            if (password.Any(char.IsWhiteSpace)) 
            {
                return new OperationResultDto { success = false, message = Resources.Lang.ValidationPasswordNoSpaces };
            }
            if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit)) 
            {
                return new OperationResultDto { success = false, message = Resources.Lang.ValidationPasswordComplexity };
            }
            

            using (var context = new MindWeaveDBEntities1())
            {
                var existingPlayer = await context.Player.FirstOrDefaultAsync(p => p.username == userProfile.username || p.email == userProfile.email);
                if (existingPlayer != null)
                {
                    if (existingPlayer.is_verified)
                    {
                        return new OperationResultDto { success = false, message = Resources.Lang.RegistrationUsernameOrEmailExists };
                    }

                    // Si el jugador existe pero no está verificado, actualizamos sus datos y reenviamos el código.
                    string newVerificationCode = random.Next(100000, 999999).ToString("D6");

                    existingPlayer.password_hash = PasswordHasher.hashPassword(password);
                    existingPlayer.first_name = userProfile.firstName.Trim();
                    existingPlayer.last_name = userProfile.lastName?.Trim();
                    existingPlayer.date_of_birth = userProfile.dateOfBirth;
                    existingPlayer.gender_id = userProfile.genderId;
                    existingPlayer.verification_code = newVerificationCode;
                    existingPlayer.code_expiry_date = DateTime.UtcNow.AddMinutes(5);

                    await context.SaveChangesAsync();

                    var emailTemplateRe = new VerificationEmailTemplate(existingPlayer.username, newVerificationCode);
                    await emailService.sendEmailAsync(existingPlayer.email, existingPlayer.username, emailTemplateRe);

                    return new OperationResultDto { success = true, message = Resources.Lang.RegistrationSuccessful };
                }

                string verificationCode = random.Next(100000, 999999).ToString("D6");

                // Sanitizamos los inputs antes de guardarlos
                var newPlayer = new Player
                {
                    username = userProfile.username.Trim(), // Usamos Trim() por si acaso
                    email = userProfile.email.Trim(),
                    password_hash = PasswordHasher.hashPassword(password),
                    first_name = userProfile.firstName.Trim(),
                    last_name = userProfile.lastName?.Trim(), // El '?' es por si el apellido es opcional
                    date_of_birth = userProfile.dateOfBirth,
                    gender_id = userProfile.genderId,
                    is_verified = false,
                    verification_code = verificationCode,
                    code_expiry_date = DateTime.UtcNow.AddMinutes(5)
                };

                context.Player.Add(newPlayer);
                await context.SaveChangesAsync();

                var emailTemplate = new VerificationEmailTemplate(newPlayer.username, verificationCode);
                await emailService.sendEmailAsync(newPlayer.email, newPlayer.username, emailTemplate);

                return new OperationResultDto { success = true, message = Resources.Lang.RegistrationSuccessful };
            }
        }



        public OperationResultDto verifyAccount(string email, string code)
        {
            // 1. Validación de Nulos (ya la tenías)
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                return new OperationResultDto { success = false, message = Resources.Lang.VerificationEmailAndCodeRequired };
            }

            // --- NUEVA VALIDACIÓN ---
            // 2. Validación de formato y longitud en el servidor
            if (code.Length != 6 || !code.All(char.IsDigit))
            {
                return new OperationResultDto { success = false, message = Resources.Lang.VerificationCodeInvalidFormat };
            }
            // --- FIN DE LA NUEVA VALIDACIÓN ---

            // 3. Lógica de base de datos (si las validaciones pasan)
            using (var context = new MindWeaveDBEntities1())
            {
                var playerToVerify = context.Player.FirstOrDefault(p => p.email == email);

                if (playerToVerify == null)
                {
                    return new OperationResultDto { success = false, message = Resources.Lang.VerificationEmailNotFound };
                }

                if (playerToVerify.is_verified)
                {
                    return new OperationResultDto { success = false, message = Resources.Lang.VerificationAccountAlreadyVerified };
                }

                // Ahora esta comparación es más segura porque ya sabemos que el código es válido.
                if (playerToVerify.verification_code != code || playerToVerify.code_expiry_date < DateTime.UtcNow)
                {
                    return new OperationResultDto { success = false, message = Resources.Lang.VerificationInvalidOrExpiredCode };
                }

                playerToVerify.is_verified = true;
                playerToVerify.verification_code = null;
                playerToVerify.code_expiry_date = null;

                context.SaveChanges();

                return new OperationResultDto { success = true, message = Resources.Lang.VerificationSuccessful };
            }
        }

        public async Task<LoginResultDto> loginAsync(LoginDto loginData)
        {
            var validator = new LoginDtoValidator();
            var validationResult = await validator.ValidateAsync(loginData);

            if (!validationResult.IsValid)
            {
                return new LoginResultDto
                {
                    operationResult = new OperationResultDto { success = false, message = validationResult.Errors.First().ErrorMessage }
                };
            }

            using (var context = new MindWeaveDBEntities1())
            {
                var player = await context.Player.FirstOrDefaultAsync(p => p.email.Equals(loginData.email, StringComparison.OrdinalIgnoreCase));

                if (player == null || !PasswordHasher.verifyPassword(loginData.password, player.password_hash))
                {
                    return new LoginResultDto
                    {
                        operationResult = new OperationResultDto { success = false, message = Resources.Lang.LoginPasswordNotEmpty }
                    };
                }

                if (!player.is_verified)
                {
                    return new LoginResultDto
                    {
                        operationResult = new OperationResultDto { success = false, message = Resources.Lang.LoginAccountNotVerified }
                    };
                }

                return new LoginResultDto
                {
                    operationResult = new OperationResultDto { success = true, message = Resources.Lang.LoginSuccessful },
                    username = player.username,
                    avatarPath = player.avatar_path 
                };
            }
        }

        // ... al final de la clase AuthenticationLogic

        public async Task<OperationResultDto> resendVerificationCodeAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new OperationResultDto { success = false, message = Resources.Lang.ValidationEmailRequired };
            }

            using (var context = new MindWeaveDBEntities1())
            {
                var playerToVerify = await context.Player.FirstOrDefaultAsync(p => p.email == email);

                if (playerToVerify == null)
                {
                    // Por seguridad, no revelamos si el email existe o no.
                    return new OperationResultDto { success = false, message = Resources.Lang.VerificationEmailNotFound };
                }

                if (playerToVerify.is_verified)
                {
                    return new OperationResultDto { success = false, message = Resources.Lang.VerificationAccountAlreadyVerified };
                }

                // Generar y asignar nuevo código y expiración
                string newVerificationCode = random.Next(100000, 999999).ToString("D6");
                playerToVerify.verification_code = newVerificationCode;
                playerToVerify.code_expiry_date = DateTime.UtcNow.AddMinutes(5);

                await context.SaveChangesAsync();

                // Enviar el nuevo correo
                var emailTemplate = new VerificationEmailTemplate(playerToVerify.username, newVerificationCode);
                await emailService.sendEmailAsync(playerToVerify.email, playerToVerify.username, emailTemplate);

                return new OperationResultDto { success = true, message = Resources.Lang.RegistrationSuccessful }; // Puedes crear una clave de recurso nueva si lo prefieres.
            }
        }

    }
}