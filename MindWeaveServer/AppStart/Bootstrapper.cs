using Autofac;
using FluentValidation;
using MindWeaveServer.BusinessLogic;
using MindWeaveServer.DataAccess;
using MindWeaveServer.DataAccess.Abstractions;
using MindWeaveServer.DataAccess.Repositories;
using MindWeaveServer.Utilities;
using MindWeaveServer.Utilities.Abstractions;
using MindWeaveServer.Utilities.Email;
using MindWeaveServer.Utilities.Validators;
using MindWeaveServer.Contracts.DataContracts.Authentication;
using MindWeaveServer.Contracts.DataContracts.Profile;
using NLog;

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

                    builder.RegisterType<MindWeaveDBEntities1>().AsSelf().InstancePerLifetimeScope();
                    builder.RegisterType<PlayerRepository>().As<IPlayerRepository>();
                    builder.RegisterType<MatchmakingRepository>().As<IMatchmakingRepository>();
                    builder.RegisterType<PuzzleRepository>().As<IPuzzleRepository>();
                    builder.RegisterType<StatsRepository>().As<IStatsRepository>();
                    builder.RegisterType<GuestInvitationRepository>().As<IGuestInvitationRepository>();
                    builder.RegisterType<FriendshipRepository>().As<IFriendshipRepository>();
                    builder.RegisterType<GenderRepository>().As<IGenderRepository>();

                    builder.RegisterType<SmtpEmailService>().As<IEmailService>();
                    builder.RegisterType<PasswordService>().As<IPasswordService>();
                    builder.RegisterType<VerificationCodeService>().As<IVerificationCodeService>();
                    builder.RegisterType<PasswordPolicyValidator>().As<IPasswordPolicyValidator>();
                    builder.RegisterType<PuzzleGenerator>().AsSelf();

                    builder.RegisterType<UserProfileDtoValidator>().As<IValidator<UserProfileDto>>();
                    builder.RegisterType<LoginDtoValidator>().As<IValidator<LoginDto>>();
                    builder.RegisterType<UserProfileForEditDtoValidator>().As<IValidator<UserProfileForEditDto>>();

                    builder.RegisterType<GameSessionManager>().AsSelf().SingleInstance();
                    builder.RegisterType<GameStateManager>().As<IGameStateManager>().SingleInstance();
                    builder.RegisterType<LobbyModerationManager>().SingleInstance();

                    builder.RegisterType<AuthenticationLogic>().AsSelf();
                    builder.RegisterType<ChatLogic>().AsSelf();
                    builder.RegisterType<MatchmakingLogic>().AsSelf();
                    builder.RegisterType<ProfileLogic>().AsSelf();
                    builder.RegisterType<PuzzleLogic>().AsSelf();
                    builder.RegisterType<SocialLogic>().AsSelf();
                    builder.RegisterType<StatsLogic>().AsSelf();

                    Container = builder.Build();
                    isInitialized = true;
                }
                catch (System.Exception ex)
                {
                    logger.Fatal(ex, "Failed to initialize Bootstrapper.");
                    throw;
                }
            }
        }
    }
}