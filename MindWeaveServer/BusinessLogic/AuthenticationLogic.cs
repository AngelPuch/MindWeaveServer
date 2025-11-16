using FluentValidation;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Email.Templates;
using System;
using System.Linq;
using System.Threading.Tasks;
using MindWeaveServer.Contracts.DataContracts.Shared;
using NLog;

namespace MindWeaveServer.BusinessLogic
{
    public class AuthenticationLogic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const string RESULT_CODE_ACCOUNT_NOT_VERIFIED = "ACCOUNT_NOT_VERIFIED";
        private readonly IPlayerRepository playerRepository;
        private readonly IEmailService emailService;
        private readonly IPasswordService passwordService;
        private readonly IPasswordPolicyValidator passwordPolicyValidator;
        private readonly IVerificationCodeService verificationCodeService;
        private readonly IValidator<UserProfileDto> profileValidator;
        private readonly IValidator<LoginDto> loginValidator;

        public AuthenticationLogic(
            IPlayerRepository playerRepository,
            IEmailService emailService,
            IPasswordService passwordService,
            IPasswordPolicyValidator passwordPolicyValidator,
            IVerificationCodeService verificationCodeService,
            IValidator<UserProfileDto> profileValidator,
            IValidator<LoginDto> loginValidator)
        {
            this.playerRepository = playerRepository;
            this.emailService = emailService;
            this.passwordService = passwordService;
            this.passwordPolicyValidator = passwordPolicyValidator;
            this.verificationCodeService = verificationCodeService;
            this.profileValidator = profileValidator;
            this.loginValidator = loginValidator;
            logger.Info("AuthenticationLogic instance created.");
        }

        public async Task<OperationResultDto> registerPlayerAsync(UserProfileDto userProfile, string password)
        {
            string usernameForContext = userProfile?.username ?? "NULL";
            string emailForContext = userProfile?.email ?? "NULL";
            logger.Info("registerPlayerAsync called for User: {Username}, Email: {Email}", usernameForContext, emailForContext);

            if (userProfile == null)
            {
                logger.Warn("Registration failed for User: {Username}: UserProfileDto is null.", usernameForContext);
                return new OperationResultDto { success = false, message = Lang.ValidationProfileOrPasswordRequired };
            }

            var profileValidationResult = await this.profileValidator.ValidateAsync(userProfile);
            if (!profileValidationResult.IsValid)
            {
                string firstError = profileValidationResult.Errors[0].ErrorMessage;
                logger.Warn("Registration failed for User: {Username}: Profile validation failed. Reason: {Reason}", usernameForContext, firstError);
                return new OperationResultDto { success = false, message = firstError };
            }
            logger.Debug("Profile validation successful for User: {Username}", usernameForContext);

            var passwordValidationResult = this.passwordPolicyValidator.validate(password);
            if (!passwordValidationResult.success)
            {
                logger.Warn("Registration failed for User: {Username}: Password validation failed. Reason: {Reason}", usernameForContext, passwordValidationResult.message);
                return passwordValidationResult;
            }
            logger.Debug("Password validation successful for User: {Username}", usernameForContext);

            try
            {
                logger.Debug("Checking for existing player by username or email for User: {Username}", usernameForContext);
                var existingPlayer = await this.playerRepository.getPlayerByUsernameAsync(userProfile.username)
                                     ?? await this.playerRepository.getPlayerByEmailAsync(userProfile.email);

                if (existingPlayer != null)
                {
                    logger.Info("Existing player found for registration attempt: {ExistingUsername} (ID: {PlayerId}). Handling existing player logic.", existingPlayer.username, existingPlayer.idPlayer);
                    return await handleExistingPlayerRegistrationAsync(existingPlayer, userProfile, password);
                }
                else
                {
                    logger.Info("No existing player found for User: {Username}. Handling new player registration.", usernameForContext);
                    return await handleNewPlayerRegistrationAsync(userProfile, password);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during registerPlayerAsync for User: {Username}", usernameForContext);
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        private async Task<OperationResultDto> handleExistingPlayerRegistrationAsync(Player existingPlayer, UserProfileDto userProfile, string password)
        {
            logger.Debug("Handling existing player registration for User: {Username} (ID: {PlayerId})", existingPlayer.username, existingPlayer.idPlayer);
            if (existingPlayer.is_verified)
            {
                logger.Warn("Registration update failed for User: {Username}: Account is already verified.", existingPlayer.username);
                return new OperationResultDto { success = false, message = Lang.RegistrationUsernameOrEmailExists };
            }

            try
            {
                string newVerificationCode = this.verificationCodeService.generateVerificationCode();
                logger.Debug("Generated new verification code for unverified User: {Username}", existingPlayer.username);

                existingPlayer.password_hash = this.passwordService.hashPassword(password);
                existingPlayer.first_name = userProfile.firstName.Trim();
                existingPlayer.last_name = userProfile.lastName?.Trim();
                existingPlayer.date_of_birth = userProfile.dateOfBirth;
                existingPlayer.gender_id = userProfile.genderId;
                existingPlayer.verification_code = newVerificationCode;
                existingPlayer.code_expiry_date = this.verificationCodeService.getVerificationExpiryTime();
                existingPlayer.email = userProfile.email.Trim();

                await this.playerRepository.saveChangesAsync();
                logger.Info("Updated existing unverified player data for User: {Username}", existingPlayer.username);

                await sendVerificationEmailAsync(existingPlayer.email, existingPlayer.username, newVerificationCode);

                return new OperationResultDto { success = true, message = Lang.RegistrationSuccessful };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during handleExistingPlayerRegistrationAsync for User: {Username}", existingPlayer.username);
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        private async Task<OperationResultDto> handleNewPlayerRegistrationAsync(UserProfileDto userProfile, string password)
        {
            logger.Debug("Handling new player registration for User: {Username}", userProfile.username);
            try
            {
                string verificationCode = this.verificationCodeService.generateVerificationCode();
                logger.Debug("Generated verification code for new User: {Username}", userProfile.username);

                var newPlayer = new Player
                {
                    username = userProfile.username.Trim(),
                    email = userProfile.email.Trim(),
                    password_hash = this.passwordService.hashPassword(password),
                    first_name = userProfile.firstName.Trim(),
                    last_name = userProfile.lastName?.Trim(),
                    date_of_birth = userProfile.dateOfBirth,
                    gender_id = userProfile.genderId,
                    is_verified = false,
                    verification_code = verificationCode,
                    code_expiry_date = this.verificationCodeService.getVerificationExpiryTime(),
                    avatar_path = "/Resources/Images/Avatar/default_avatar.png"
                };

                this.playerRepository.addPlayer(newPlayer);
                await this.playerRepository.saveChangesAsync();
                logger.Info("New player record created for User: {Username} (ID: {PlayerId})", newPlayer.username, newPlayer.idPlayer);

                //TODO: Initialize player stats
                logger.Debug("TODO: Initialize player stats for new User: {Username}", newPlayer.username);

                await sendVerificationEmailAsync(newPlayer.email, newPlayer.username, verificationCode);

                return new OperationResultDto { success = true, message = Lang.RegistrationSuccessful };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during handleNewPlayerRegistrationAsync for User: {Username}", userProfile.username);
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }


        public async Task<OperationResultDto> verifyAccountAsync(string email, string code)
        {
            logger.Info("verifyAccountAsync called for Email: {Email}", email ?? "NULL");

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                logger.Warn("Verification failed: Email or code is null/whitespace.");
                return new OperationResultDto { success = false, message = Lang.VerificationEmailAndCodeRequired };
            }

            if (code.Length != 6 || !code.All(char.IsDigit))
            {
                logger.Warn("Verification failed for Email: {Email}: Code format is invalid.", email);
                return new OperationResultDto { success = false, message = Lang.VerificationCodeInvalidFormat };
            }

            try
            {
                logger.Debug("Fetching player by email for verification: {Email}", email);
                var playerToVerify = await this.playerRepository.getPlayerByEmailAsync(email);

                if (playerToVerify == null)
                {
                    logger.Warn("Verification failed: Player not found for Email: {Email}", email);
                    return new OperationResultDto { success = false, message = Lang.VerificationEmailNotFound };
                }

                if (playerToVerify.is_verified)
                {
                    logger.Warn("Verification failed for Email: {Email}: Account already verified.", email);
                    return new OperationResultDto { success = false, message = Lang.VerificationAccountAlreadyVerified };
                }

                bool isCodeValid = playerToVerify.verification_code == code;
                bool isCodeExpired = playerToVerify.code_expiry_date < DateTime.UtcNow;

                if (!isCodeValid || isCodeExpired)
                {
                    logger.Warn("Verification failed for Email: {Email}: Code is invalid ({IsCodeValid}) or expired ({IsCodeExpired}).", email, isCodeValid, isCodeExpired);
                    return new OperationResultDto { success = false, message = Lang.VerificationInvalidOrExpiredCode };
                }

                logger.Debug("Verification code is valid and not expired for Email: {Email}", email);

                playerToVerify.is_verified = true;
                playerToVerify.verification_code = null;
                playerToVerify.code_expiry_date = null;

                await this.playerRepository.saveChangesAsync();
                logger.Info("Account successfully verified for Email: {Email}, User: {Username}", email, playerToVerify.username);

                return new OperationResultDto { success = true, message = Lang.VerificationSuccessful };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during verifyAccountAsync for Email: {Email}", email);
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<LoginResultDto> loginAsync(LoginDto loginData)
        {
            string emailForContext = loginData?.email ?? "NULL";
            logger.Info("loginAsync called for Email: {Email}", emailForContext);

            if (loginData == null)
            {
                logger.Warn("Login failed: LoginDto is null.");
                return new LoginResultDto { operationResult = new OperationResultDto { success = false, message = Lang.ErrorAllFieldsRequired } };
            }

            var validationResult = await this.loginValidator.ValidateAsync(loginData);
            if (!validationResult.IsValid)
            {
                string firstError = validationResult.Errors[0].ErrorMessage;
                logger.Warn("Login failed for Email: {Email}: Input validation failed. Reason: {Reason}", emailForContext, firstError);
                return new LoginResultDto { operationResult = new OperationResultDto { success = false, message = firstError } };
            }
            logger.Debug("Login input validation successful for Email: {Email}", emailForContext);

            try
            {
                logger.Debug("Fetching player by email for login: {Email}", emailForContext);
                var player = await this.playerRepository.getPlayerByEmailAsync(loginData.email);

                bool passwordVerified = false;
                if (player != null)
                {
                    logger.Debug("Player found for Email: {Email}. Verifying password.", emailForContext);
                    passwordVerified = this.passwordService.verifyPassword(loginData.password, player.password_hash);
                    logger.Debug("Password verification result for Email {Email}: {Result}", emailForContext, passwordVerified);
                }
                else
                {
                    logger.Warn("Login failed: Player not found for Email: {Email}", emailForContext);
                }

                if (player == null || !passwordVerified)
                {
                    logger.Warn("Login failed for Email: {Email}: Invalid credentials (player not found or password mismatch).", emailForContext);
                    return new LoginResultDto { operationResult = new OperationResultDto { success = false, message = Lang.LoginPasswordNotEmpty } };
                }

                if (!player.is_verified)
                {
                    logger.Warn("Login failed for Email: {Email}, User: {Username}: Account is not verified.", emailForContext, player.username);
                    return new LoginResultDto
                    {
                        operationResult = new OperationResultDto { success = false, message = Lang.LoginAccountNotVerified },
                        resultCode = RESULT_CODE_ACCOUNT_NOT_VERIFIED
                    };
                }

                logger.Info("Login successful for Email: {Email}, User: {Username}", emailForContext, player.username);
                return new LoginResultDto
                {
                    operationResult = new OperationResultDto { success = true, message = Lang.LoginSuccessful },
                    username = player.username,
                    avatarPath = player.avatar_path,
                    playerId = player.idPlayer
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during loginAsync for Email: {Email}", emailForContext);
                return new LoginResultDto { operationResult = new OperationResultDto { success = false, message = Lang.GenericServerError } };
            }
        }

        public async Task<OperationResultDto> resendVerificationCodeAsync(string email)
        {
            logger.Info("resendVerificationCodeAsync called for Email: {Email}", email ?? "NULL");

            if (string.IsNullOrWhiteSpace(email))
            {
                logger.Warn("Resend code failed: Email is null or whitespace.");
                return new OperationResultDto { success = false, message = Lang.ValidationEmailRequired };
            }

            try
            {
                logger.Debug("Fetching player by email for resend code: {Email}", email);
                var playerToVerify = await this.playerRepository.getPlayerByEmailAsync(email);

                if (playerToVerify == null)
                {
                    logger.Warn("Resend code failed: Player not found for Email: {Email}", email);
                    return new OperationResultDto { success = false, message = Lang.VerificationEmailNotFound };
                }

                if (playerToVerify.is_verified)
                {
                    logger.Warn("Resend code failed for Email: {Email}: Account already verified.", email);
                    return new OperationResultDto { success = false, message = Lang.VerificationAccountAlreadyVerified };
                }

                string newVerificationCode = this.verificationCodeService.generateVerificationCode();
                playerToVerify.verification_code = newVerificationCode;
                playerToVerify.code_expiry_date = this.verificationCodeService.getVerificationExpiryTime();
                logger.Debug("Generated new verification code for Email: {Email}", email);

                await playerRepository.saveChangesAsync();
                logger.Info("Updated verification code in DB for Email: {Email}", email);

                await sendVerificationEmailAsync(playerToVerify.email, playerToVerify.username, newVerificationCode);

                return new OperationResultDto { success = true, message = Lang.RegistrationSuccessful };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during resendVerificationCodeAsync for Email: {Email}", email);
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> sendPasswordRecoveryCodeAsync(string email)
        {
            logger.Info("sendPasswordRecoveryCodeAsync called for Email: {Email}", email ?? "NULL");

            if (string.IsNullOrWhiteSpace(email))
            {
                logger.Warn("Send recovery code failed: Email is null or whitespace.");
                return new OperationResultDto { success = false, message = Lang.ValidationEmailRequired };
            }

            try
            {
                logger.Debug("Fetching player by email for password recovery: {Email}", email);
                var player = await playerRepository.getPlayerByEmailAsync(email);

                if (player == null)
                {
                    logger.Warn("Send recovery code failed: Account not found for Email: {Email}", email);
                    return new OperationResultDto { success = false, message = Lang.ErrorAccountNotFound };
                }

                string recoveryCode = verificationCodeService.generateVerificationCode();
                DateTime expiryTime = verificationCodeService.getVerificationExpiryTime();
                logger.Debug("Generated password recovery code for Email: {Email}", email);

                player.verification_code = recoveryCode;
                player.code_expiry_date = expiryTime;

                await playerRepository.saveChangesAsync();
                logger.Info("Updated recovery code in DB for Email: {Email}", email);

                var emailTemplate = new PasswordRecoveryEmailTemplate(player.username, recoveryCode);
                await emailService.sendEmailAsync(player.email, player.username, emailTemplate);

                return new OperationResultDto { success = true, message = Lang.InfoRecoveryCodeSent };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during sendPasswordRecoveryCodeAsync for Email: {Email}. Could be DB or Email sending failure.", email);
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        public async Task<OperationResultDto> resetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            logger.Info("resetPasswordWithCodeAsync called for Email: {Email}", email ?? "NULL");

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(newPassword))
            {
                logger.Warn("Password reset failed: Email, code, or new password is null/whitespace.");
                return new OperationResultDto { success = false, message = Lang.ErrorAllFieldsRequired };
            }

            if (code.Length != 6 || !code.All(char.IsDigit))
            {
                logger.Warn("Password reset failed for Email: {Email}: Code format is invalid.", email);
                return new OperationResultDto { success = false, message = Lang.VerificationCodeInvalidFormat };
            }

            var passwordValidation = passwordPolicyValidator.validate(newPassword);
            if (!passwordValidation.success)
            {
                logger.Warn("Password reset failed for Email: {Email}: New password validation failed. Reason: {Reason}", email, passwordValidation.message);
                return passwordValidation;
            }
            logger.Debug("New password validation successful for Email: {Email}", email);

            try
            {
                logger.Debug("Fetching player by email for password reset: {Email}", email);
                var player = await playerRepository.getPlayerByEmailAsync(email);

                if (player == null)
                {
                    logger.Warn("Password reset failed: Account not found for Email: {Email}", email);
                    return new OperationResultDto { success = false, message = Lang.ErrorAccountNotFound };
                }

                bool isCodeValid = player.verification_code == code;
                bool isCodeExpired = player.code_expiry_date < DateTime.UtcNow;

                if (!isCodeValid || isCodeExpired)
                {
                    logger.Warn("Password reset failed for Email: {Email}: Recovery code is invalid ({IsCodeValid}) or expired ({IsCodeExpired}).", email, isCodeValid, isCodeExpired);
                    return new OperationResultDto { success = false, message = Lang.VerificationInvalidOrExpiredCode };
                }

                logger.Debug("Recovery code is valid and not expired for Email: {Email}", email);

                player.password_hash = passwordService.hashPassword(newPassword);
                player.verification_code = null;
                player.code_expiry_date = null;
                if (!player.is_verified)
                {
                    player.is_verified = true;
                    logger.Info("Account for Email: {Email} was also marked as verified during password reset.", email);
                }

                await playerRepository.saveChangesAsync();
                logger.Info("Password successfully reset and saved for Email: {Email}, User: {Username}", email, player.username);

                return new OperationResultDto { success = true, message = Lang.InfoPasswordResetSuccess };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Exception during resetPasswordWithCodeAsync for Email: {Email}", email);
                return new OperationResultDto { success = false, message = Lang.GenericServerError };
            }
        }

        private async Task sendVerificationEmailAsync(string email, string username, string code)
        {
            logger.Info("Attempting to send verification email to {Email} for user {Username}", email, username);
            try
            {
                var emailTemplate = new VerificationEmailTemplate(username, code);
                await this.emailService.sendEmailAsync(email, username, emailTemplate);
            }
            catch (Exception ex)
            {
              
                logger.Error(ex, "Failed to send verification email to {Email} (User: {Username}) from AuthenticationLogic.", email, username);
            }
        }
    }
}