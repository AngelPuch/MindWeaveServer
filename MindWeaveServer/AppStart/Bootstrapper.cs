using Autofac;
using FluentValidation;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Profile;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Utilities;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Validators;
using NLog;
using System;
using MindWeaveServer.BusinessLogic.Manager;
using MindWeaveServer.BusinessLogic.Services;

namespace MindWeaveServer.AppStart
{
    public static class Bootstrapper
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public static IContainer Container { get; private set; }

        private static bool isInitialized;
        private static readonly object @lock = new object();

        public static void init()
        {
            if (isInitialized) return;

            lock (@lock)
            {
                if (isInitialized) return;

                try
                {
                    var builder = new ContainerBuilder();

                    registerDataAccess(builder);
                    registerUtilities(builder);
                    registerValidators(builder);
                    registerGameComponents(builder);
                    registerManagers(builder);
                    registerBusinessLogic(builder);

                    Container = builder.Build();
                    isInitialized = true;

                    logger.Info("Bootstrapper initialized successfully.");
                }
                catch (Exception ex)
                {
                    logger.Fatal(ex, "Failed to initialize Bootstrapper.");
                    throw;
                }
            }
        }

        private static void registerDataAccess(ContainerBuilder builder)
        {
            builder.RegisterType<MindWeaveDBEntities1>()
                .AsSelf()
                .InstancePerLifetimeScope();

            builder.RegisterType<PlayerRepository>()
                .As<IPlayerRepository>();

            builder.RegisterType<MatchmakingRepository>()
                .As<IMatchmakingRepository>();

            builder.RegisterType<PuzzleRepository>()
                .As<IPuzzleRepository>();

            builder.RegisterType<StatsRepository>()
                .As<IStatsRepository>();

            builder.RegisterType<GuestInvitationRepository>()
                .As<IGuestInvitationRepository>();

            builder.RegisterType<FriendshipRepository>()
                .As<IFriendshipRepository>();

            builder.RegisterType<GenderRepository>()
                .As<IGenderRepository>();
        }

        private static void registerUtilities(ContainerBuilder builder)
        {
            builder.RegisterType<SmtpEmailService>()
                .As<IEmailService>();

            builder.RegisterType<PasswordService>()
                .As<IPasswordService>();

            builder.RegisterType<VerificationCodeService>()
                .As<IVerificationCodeService>();

            builder.RegisterType<PasswordPolicyValidator>()
                .As<IPasswordPolicyValidator>();

            builder.RegisterType<PuzzleGenerator>()
                .AsSelf();

            builder.RegisterType<ServiceExceptionHandler>()
                .As<IServiceExceptionHandler>()
                .SingleInstance();
        }

        private static void registerValidators(ContainerBuilder builder)
        {
            builder.RegisterType<UserProfileDtoValidator>()
                .As<IValidator<UserProfileDto>>();

            builder.RegisterType<LoginDtoValidator>()
                .As<IValidator<LoginDto>>();

            builder.RegisterType<UserProfileForEditDtoValidator>()
                .As<IValidator<UserProfileForEditDto>>();
        }

        private static void registerGameComponents(ContainerBuilder builder)
        {
            builder.RegisterType<PuzzleGenerator>()
                .AsSelf();

            builder.RegisterType<ScoreCalculator>()
                .As<IScoreCalculator>()
                .SingleInstance();
        }

        private static void registerManagers(ContainerBuilder builder)
        {
            builder.RegisterType<GameSessionManager>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<GameStateManager>()
                .As<IGameStateManager>()
                .SingleInstance();

            builder.RegisterType<LobbyModerationManager>()
                .SingleInstance();
        }

        private static void registerBusinessLogic(ContainerBuilder builder)
        {
            builder.RegisterType<PlayerExpulsionService>()
                .As<IPlayerExpulsionService>();

            builder.RegisterType<ChatLogic>()
                .AsSelf();

            builder.RegisterType<AuthenticationLogic>()
                .AsSelf();

            builder.RegisterType<ProfileLogic>()
                .AsSelf();

            builder.RegisterType<PuzzleLogic>()
                .AsSelf();

            builder.RegisterType<SocialLogic>()
                .AsSelf();

            builder.RegisterType<StatsLogic>()
                .AsSelf();

            builder.RegisterType<PlayerExpulsionService>().As<IPlayerExpulsionService>();

            builder.RegisterType<NotificationService>()
                .As<INotificationService>();

            builder.RegisterType<LobbyValidationService>()
                .As<ILobbyValidationService>();

            builder.RegisterType<LobbyInteractionService>()
                .As<ILobbyInteractionService>();

            builder.RegisterType<LobbyLifecycleService>()
                .As<ILobbyLifecycleService>();

            builder.RegisterType<MatchmakingLogic>().AsSelf();
        }
    }
}