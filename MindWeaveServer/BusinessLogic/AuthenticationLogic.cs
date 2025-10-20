using FluentValidation;
using MindWeaveServer.Contracts.DataContracts;
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

namespace MindWeaveServer.BusinessLogic
{
    public class AuthenticationLogic
    {
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
        }

        public async Task<OperationResultDto> registerPlayerAsync(UserProfileDto userProfile, string password)
        {
            var profileValidationResult = await this.profileValidator.ValidateAsync(userProfile);
            if (!profileValidationResult.IsValid)
            {
                return new OperationResultDto { success = false, message = profileValidationResult.Errors.First().ErrorMessage };
            }

            var passwordValidationResult = this.passwordPolicyValidator.validate(password);
            if (!passwordValidationResult.success)
            {
                return passwordValidationResult;
            }

            var existingPlayer = await this.playerRepository.getPlayerByUsernameOrEmailAsync(userProfile.username, userProfile.email);

            if (existingPlayer != null)
            {
                return await handleExistingPlayerRegistrationAsync(existingPlayer, userProfile, password);
            }
            else
            {
                return await handleNewPlayerRegistrationAsync(userProfile, password);
            }
        }

        private async Task<OperationResultDto> handleExistingPlayerRegistrationAsync(Player existingPlayer, UserProfileDto userProfile, string password)
        {
            if (existingPlayer.is_verified)
            {
                return new OperationResultDto { success = false, message = Lang.RegistrationUsernameOrEmailExists };
            }

            string newVerificationCode = this.verificationCodeService.generateVerificationCode();

            existingPlayer.password_hash = this.passwordService.hashPassword(password);
            existingPlayer.first_name = userProfile.firstName.Trim();
            existingPlayer.last_name = userProfile.lastName?.Trim();
            existingPlayer.date_of_birth = userProfile.dateOfBirth;
            existingPlayer.gender_id = userProfile.genderId;
            existingPlayer.verification_code = newVerificationCode;
            existingPlayer.code_expiry_date = this.verificationCodeService.getVerificationExpiryTime();

            await this.playerRepository.saveChangesAsync();

            await sendVerificationEmailAsync(existingPlayer.email, existingPlayer.username, newVerificationCode);

            return new OperationResultDto { success = true, message = Lang.RegistrationSuccessful };
        }

        private async Task<OperationResultDto> handleNewPlayerRegistrationAsync(UserProfileDto userProfile, string password)
        {
            string verificationCode = this.verificationCodeService.generateVerificationCode();

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
                code_expiry_date = this.verificationCodeService.getVerificationExpiryTime()
            };

            this.playerRepository.addPlayer(newPlayer);
            await this.playerRepository.saveChangesAsync();

            await sendVerificationEmailAsync(newPlayer.email, newPlayer.username, verificationCode);

            return new OperationResultDto { success = true, message = Lang.RegistrationSuccessful };
        }



        public async Task<OperationResultDto> verifyAccountAsync(string email, string code)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                return new OperationResultDto { success = false, message = Lang.VerificationEmailAndCodeRequired };
            }

            if (code.Length != 6 || !code.All(char.IsDigit))
            {
                return new OperationResultDto { success = false, message = Lang.VerificationCodeInvalidFormat };
            }

            var playerToVerify = await this.playerRepository.getPlayerByEmailAsync(email);

            if (playerToVerify == null)
            {
                return new OperationResultDto { success = false, message = Lang.VerificationEmailNotFound };
            }

            if (playerToVerify.is_verified)
            {
                return new OperationResultDto { success = false, message = Lang.VerificationAccountAlreadyVerified };
            }

            if (playerToVerify.verification_code != code || playerToVerify.code_expiry_date < DateTime.UtcNow)
            {
                return new OperationResultDto { success = false, message = Lang.VerificationInvalidOrExpiredCode };
            }

            playerToVerify.is_verified = true;
            playerToVerify.verification_code = null;
            playerToVerify.code_expiry_date = null;

            await this.playerRepository.saveChangesAsync();

            return new OperationResultDto { success = true, message = Lang.VerificationSuccessful };
        }

        public async Task<LoginResultDto> loginAsync(LoginDto loginData)
        {
            var validationResult = await this.loginValidator.ValidateAsync(loginData);
            if (!validationResult.IsValid)
            {
                return new LoginResultDto
                {
                    operationResult = new OperationResultDto { success = false, message = validationResult.Errors.First().ErrorMessage }
                };
            }

            var player = await this.playerRepository.getPlayerByEmailAsync(loginData.email);

            if (player == null || !this.passwordService.verifyPassword(loginData.password, player.password_hash))
            {
                return new LoginResultDto
                {
                    operationResult = new OperationResultDto { success = false, message = Lang.LoginPasswordNotEmpty }
                };
            }

            if (!player.is_verified)
            {
                return new LoginResultDto
                {
                    operationResult = new OperationResultDto { success = false, message = Lang.LoginAccountNotVerified },
                    resultCode = RESULT_CODE_ACCOUNT_NOT_VERIFIED
                };
            }

            return new LoginResultDto
            {
                operationResult = new OperationResultDto { success = true, message = Lang.LoginSuccessful },
                username = player.username,
                avatarPath = player.avatar_path
            };
        }

        public async Task<OperationResultDto> resendVerificationCodeAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new OperationResultDto { success = false, message = Lang.ValidationEmailRequired };
            }

            var playerToVerify = await this.playerRepository.getPlayerByEmailAsync(email);

            if (playerToVerify == null)
            {
                return new OperationResultDto { success = false, message = Lang.VerificationEmailNotFound };
            }

            if (playerToVerify.is_verified)
            {
                return new OperationResultDto { success = false, message = Lang.VerificationAccountAlreadyVerified };
            }

            string newVerificationCode = this.verificationCodeService.generateVerificationCode();
            playerToVerify.verification_code = newVerificationCode;
            playerToVerify.code_expiry_date = this.verificationCodeService.getVerificationExpiryTime();

            await playerRepository.saveChangesAsync();

            var emailTemplate = new VerificationEmailTemplate(playerToVerify.username, newVerificationCode);
            await emailService.sendEmailAsync(playerToVerify.email, playerToVerify.username, emailTemplate);

            return new OperationResultDto { success = true, message = Lang.RegistrationSuccessful };

        }

        private async Task sendVerificationEmailAsync(string email, string username, string code)
        {
            var emailTemplate = new VerificationEmailTemplate(username, code);
            await this.emailService.sendEmailAsync(email, username, emailTemplate);
        }
    }



}