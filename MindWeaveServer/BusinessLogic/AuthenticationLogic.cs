using FluentValidation;
using FluentValidation.Results;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Email.Templates;
using NLog;
using System;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;


namespace MindWeaveServer.BusinessLogic
{
    public class AuthenticationLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const int VERIFICATION_CODE_LENGTH = 6;
        private const string DEFAULT_AVATAR_PATH = "/Resources/Images/Avatar/default_avatar.png";

        private readonly IPlayerRepository playerRepository;
        private readonly IEmailService emailService;
        private readonly IPasswordService passwordService;
        private readonly IPasswordPolicyValidator passwordPolicyValidator;
        private readonly IVerificationCodeService verificationCodeService;
        private readonly IUserSessionManager userSessionManager;
        private readonly IValidator<UserProfileDto> profileValidator;
        private readonly IValidator<LoginDto> loginValidator;
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Major Code Smell",
            "S107:Methods should not have too many parameters",
            Justification = "Dependencies are injected via DI container - this is standard practice for service classes")]
        public AuthenticationLogic(
            IPlayerRepository playerRepository,
            IEmailService emailService,
            IPasswordService passwordService,
            IPasswordPolicyValidator passwordPolicyValidator,
            IVerificationCodeService verificationCodeService,
            IUserSessionManager userSessionManager,
            IValidator<UserProfileDto> profileValidator,
            IValidator<LoginDto> loginValidator)
        {
            this.playerRepository = playerRepository;
            this.emailService = emailService;
            this.passwordService = passwordService;
            this.passwordPolicyValidator = passwordPolicyValidator;
            this.verificationCodeService = verificationCodeService;
            this.userSessionManager = userSessionManager;
            this.profileValidator = profileValidator;
            this.loginValidator = loginValidator;
        }

        public async Task<OperationResultDto> registerPlayerAsync(UserProfileDto userProfile, string password)
        {
            var validationResult = await validateRegistrationInputAsync(userProfile, password);
            if (!validationResult.Success)
            {
                return validationResult;
            }

            var existingPlayer = await playerRepository.getPlayerByEmailAsync(userProfile.Email);

            if (existingPlayer != null)
            {
                
                if (!existingPlayer.is_verified)
                {
                    return new OperationResultDto
                    {
                        Success = false,
                        MessageCode = MessageCodes.AUTH_ACCOUNT_NOT_VERIFIED
                    };
                }

                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_EMAIL_ALREADY_REGISTERED
                };
            }

            try
            {
                return await processPlayerRegistrationAsync(userProfile, password);
            }
            catch (DbUpdateException dbEx) when (isDuplicateKeyException(dbEx))
            {
                logger.Warn(dbEx, "Duplicate user registration attempt.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_USER_ALREADY_EXISTS
                };
            }
        }

        public async Task<LoginResultDto> loginAsync(LoginDto loginData)
        {
            if (loginData == null)
            {
                return new LoginResultDto
                {
                    OperationResult = new OperationResultDto
                    {
                        Success = false,
                        MessageCode = MessageCodes.VALIDATION_FIELDS_REQUIRED
                    }
                };
            }

            var validationResult = await loginValidator.ValidateAsync(loginData);
            if (!validationResult.IsValid)
            {
                logger.Warn("Login validation failed.");
                return new LoginResultDto
                {
                    OperationResult = new OperationResultDto
                    {
                        Success = false,
                        MessageCode = MessageCodes.VALIDATION_GENERAL_ERROR
                    }
                };
            }

            var player = await playerRepository.getPlayerByEmailAsync(loginData.Email);

            if (player == null || !passwordService.verifyPassword(loginData.Password, player.password_hash))
            {
                logger.Warn("Invalid login credentials provided.");
                return new LoginResultDto
                {
                    OperationResult = new OperationResultDto
                    {
                        Success = false,
                        MessageCode = MessageCodes.AUTH_INVALID_CREDENTIALS
                    }
                };
            }

            if (userSessionManager.isUserLoggedIn(player.username))
            {
                logger.Warn("Duplicate login attempt blocked for user: {Username}", player.username);
                return new LoginResultDto
                {
                    OperationResult = new OperationResultDto
                    {
                        Success = false,
                        MessageCode = MessageCodes.AUTH_USER_ALREADY_LOGGED_IN
                    },
                    ResultCode = MessageCodes.AUTH_USER_ALREADY_LOGGED_IN
                };
            }

            if (!player.is_verified)
            {
                logger.Warn("Login attempt on unverified account. PlayerId: {Id}", player.idPlayer);
                return new LoginResultDto
                {
                    OperationResult = new OperationResultDto
                    {
                        Success = false,
                        MessageCode = MessageCodes.AUTH_ACCOUNT_NOT_VERIFIED
                    },
                    ResultCode = MessageCodes.AUTH_ACCOUNT_NOT_VERIFIED
                };
            }

            userSessionManager.addSession(player.username);

            logger.Info("Login successful. PlayerId: {Id}", player.idPlayer);
            return new LoginResultDto
            {
                OperationResult = new OperationResultDto
                {
                    Success = true,
                    MessageCode = MessageCodes.AUTH_LOGIN_SUCCESS
                },
                Username = player.username,
                AvatarPath = player.avatar_path,
                PlayerId = player.idPlayer
            };
        }

        public async Task<OperationResultDto> verifyAccountAsync(string email, string code)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_EMAIL_CODE_REQUIRED
                };
            }

            if (!isVerificationCodeValidFormat(code))
            {
                logger.Warn("Verification code format invalid.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_CODE_INVALID_FORMAT
                };
            }

            var player = await playerRepository.getPlayerByEmailAsync(email);
            if (player == null)
            {
                logger.Warn("Verification failed: Player not found.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_USER_NOT_FOUND
                };
            }

            if (player.is_verified)
            {
                logger.Warn("Account already verified. PlayerId: {Id}", player.idPlayer);
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_ACCOUNT_ALREADY_VERIFIED
                };
            }

            if (!checkCodeValidity(player, code))
            {
                logger.Warn("Invalid or expired verification code. PlayerId: {Id}", player.idPlayer);
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_CODE_INVALID_OR_EXPIRED
                };
            }

            markPlayerAsVerified(player);
            await playerRepository.updatePlayerAsync(player);

            logger.Info("Account verified successfully. PlayerId: {Id}", player.idPlayer);
            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.AUTH_VERIFICATION_SUCCESS
            };
        }

        public async Task<OperationResultDto> resendVerificationCodeAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_EMAIL_REQUIRED
                };
            }

            var player = await playerRepository.getPlayerByEmailAsync(email);
            if (player == null)
            {
                logger.Warn("Resend code failed: Player not found.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_USER_NOT_FOUND
                };
            }

            if (player.is_verified)
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_ACCOUNT_ALREADY_VERIFIED
                };
            }

            await generateAndSaveNewCodeAsync(player);
            await sendVerificationEmailSafeAsync(player.email, player.username, player.verification_code, player.idPlayer);

            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.AUTH_VERIFICATION_CODE_RESENT
            };
        }

        public async Task<OperationResultDto> sendPasswordRecoveryCodeAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_EMAIL_REQUIRED
                };
            }

            var player = await playerRepository.getPlayerByEmailAsync(email);
            if (player == null)
            {
                logger.Warn("Recovery code failed: Player not found.");
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_USER_NOT_FOUND
                };
            }

            if (!player.is_verified)
            {
                logger.Warn("Recovery attempt on unverified account. PlayerId: {Id}", player.idPlayer);
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_ACCOUNT_NOT_VERIFIED
                };
            }

            await generateAndSaveNewCodeAsync(player);

            var emailTemplate = new PasswordRecoveryEmailTemplate(player.username, player.verification_code);
            await emailService.sendEmailAsync(player.email, player.username, emailTemplate);

            logger.Info("Recovery code sent. PlayerId: {Id}", player.idPlayer);

            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.AUTH_RECOVERY_CODE_SENT
            };
        }

        public async Task<OperationResultDto> resetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(newPassword))
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_FIELDS_REQUIRED
                };
            }

            if (!isVerificationCodeValidFormat(code))
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_CODE_INVALID_FORMAT
                };
            }

            var passwordValidation = passwordPolicyValidator.validate(newPassword);
            if (!passwordValidation.Success)
            {
                if (string.IsNullOrEmpty(passwordValidation.MessageCode))
                {
                    passwordValidation.MessageCode = MessageCodes.VALIDATION_GENERAL_ERROR;
                }
                return passwordValidation;
            }

            var player = await playerRepository.getPlayerByEmailAsync(email);
            if (player == null)
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_USER_NOT_FOUND
                };
            }

            if (!checkCodeValidity(player, code))
            {
                logger.Warn("Reset password failed: Invalid code. PlayerId: {Id}", player.idPlayer);
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_CODE_INVALID_OR_EXPIRED
                };
            }

            player.password_hash = passwordService.hashPassword(newPassword);

            player.verification_code = null;
            player.code_expiry_date = null;

            await playerRepository.updatePlayerAsync(player);

            logger.Info("Password reset successful. PlayerId: {Id}", player.idPlayer);

            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.AUTH_PASSWORD_RESET_SUCCESS
            };
        }

        public void logout(string username)
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                userSessionManager.removeSession(username);
            }
        }

        private async Task<OperationResultDto> validateRegistrationInputAsync(UserProfileDto userProfile, string password)
        {
            if (userProfile == null)
            {
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_PROFILE_PASSWORD_REQUIRED
                };
            }

            ValidationResult profileResult = await profileValidator.ValidateAsync(userProfile);
            if (!profileResult.IsValid)
            {
                var firstError = profileResult.Errors[0];
                logger.Warn("Profile validation failed. ErrorCode: {Error}", firstError.ErrorCode);

                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = firstError.ErrorCode
                };
            }

            var passwordResult = passwordPolicyValidator.validate(password);
            if (!passwordResult.Success)
            {
                logger.Warn("Password validation failed. Code: {Code}", passwordResult.MessageCode);
                return passwordResult;
            }

            return new OperationResultDto { Success = true };
        }

        private async Task<OperationResultDto> processPlayerRegistrationAsync(UserProfileDto userProfile, string password)
        {
            var existingPlayer = await playerRepository.getPlayerByUsernameAsync(userProfile.Username)
                             ?? await playerRepository.getPlayerByEmailAsync(userProfile.Email);

            if (existingPlayer != null)
            {
                return await handleExistingPlayerRegistrationAsync(existingPlayer, userProfile, password);
            }

            return await handleNewPlayerRegistrationAsync(userProfile, password);
        }

        private async Task<OperationResultDto> handleExistingPlayerRegistrationAsync(Player existingPlayer, UserProfileDto userProfile, string password)
        {
            if (existingPlayer.is_verified)
            {
                logger.Warn("Registration attempt on already verified account. PlayerId: {Id}", existingPlayer.idPlayer);
                return new OperationResultDto
                {
                    Success = false,
                    MessageCode = MessageCodes.AUTH_USER_ALREADY_EXISTS
                };
            }

            updatePlayerEntity(existingPlayer, userProfile, password);

            string newCode = verificationCodeService.generateVerificationCode();
            existingPlayer.verification_code = newCode;
            existingPlayer.code_expiry_date = verificationCodeService.getVerificationExpiryTime();

            await playerRepository.updatePlayerAsync(existingPlayer);
            await sendVerificationEmailSafeAsync(existingPlayer.email, existingPlayer.username, newCode, existingPlayer.idPlayer);

            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.AUTH_REGISTRATION_SUCCESS
            };
        }

        private async Task<OperationResultDto> handleNewPlayerRegistrationAsync(UserProfileDto userProfile, string password)
        {
            var newPlayer = createNewPlayerEntity(userProfile, password);

            var code = verificationCodeService.generateVerificationCode();
            newPlayer.verification_code = code;
            newPlayer.code_expiry_date = verificationCodeService.getVerificationExpiryTime();

            playerRepository.addPlayer(newPlayer);

            await sendVerificationEmailSafeAsync(newPlayer.email, newPlayer.username, code, newPlayer.idPlayer);

            logger.Info("New player registered successfully. PlayerId: {Id}", newPlayer.idPlayer);
            return new OperationResultDto
            {
                Success = true,
                MessageCode = MessageCodes.AUTH_REGISTRATION_SUCCESS
            };
        }

        private static bool isVerificationCodeValidFormat(string code)
        {
            return code != null && code.Length == VERIFICATION_CODE_LENGTH && code.All(char.IsDigit);
        }

        private static bool checkCodeValidity(Player player, string inputCode)
        {
            bool isMatch = player.verification_code == inputCode;
            bool isNotExpired = player.code_expiry_date.HasValue && player.code_expiry_date.Value >= DateTime.UtcNow;
            return isMatch && isNotExpired;
        }

        private static void markPlayerAsVerified(Player player)
        {
            player.is_verified = true;
            player.verification_code = null;
            player.code_expiry_date = null;
        }

        private async Task generateAndSaveNewCodeAsync(Player player)
        {
            player.verification_code = verificationCodeService.generateVerificationCode();
            player.code_expiry_date = verificationCodeService.getVerificationExpiryTime();
            await playerRepository.updatePlayerAsync(player);
        }

        private Player createNewPlayerEntity(UserProfileDto dto, string password)
        {
            return new Player
            {
                username = dto.Username.Trim(),
                email = dto.Email.Trim(),
                password_hash = passwordService.hashPassword(password),
                first_name = dto.FirstName.Trim(),
                last_name = dto.LastName?.Trim(),
                date_of_birth = dto.DateOfBirth,
                gender_id = dto.GenderId,
                is_verified = false,
                avatar_path = DEFAULT_AVATAR_PATH
            };
        }

        private void updatePlayerEntity(Player player, UserProfileDto dto, string password)
        {
            player.password_hash = passwordService.hashPassword(password);
            player.first_name = dto.FirstName.Trim();
            player.last_name = dto.LastName?.Trim();
            player.date_of_birth = dto.DateOfBirth;
            player.gender_id = dto.GenderId;
            player.email = dto.Email.Trim();
        }

        private async Task sendVerificationEmailSafeAsync(string email, string username, string code, int playerId)
        {
            try
            {
                var emailTemplate = new VerificationEmailTemplate(username, code);
                await emailService.sendEmailAsync(email, username, emailTemplate);
            }
            catch (SmtpException smtpEx)
            {
                logger.Error(smtpEx, "SMTP failure sending verification email to {Email}. PlayerId: {Id}", email, playerId);
            }
            catch (TimeoutException timeoutEx)
            {
                logger.Error(timeoutEx, "Timeout sending verification email to {Email}. PlayerId: {Id}", email, playerId);
            }
        }

        private static bool isDuplicateKeyException(DbUpdateException ex)
        {
            return ex.InnerException?.InnerException is SqlException sqlEx &&
                   (sqlEx.Number == 2627 || sqlEx.Number == 2601);
        }
    }
}