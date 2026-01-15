using System;
using System.Collections.Generic;
using System.ServiceModel;
using MindWeaveServer.AppStart;
using MindWeaveServer.Services;
using NLog;

namespace MindWeaveServer
{
    public class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly List<ServiceHost> serviceHosts = new List<ServiceHost>();

        public static void Main(string[] args)
        {
            Console.Title = "MindWeave Server";

            try
            {
                logger.Info("Initializing MindWeave Server...");
                Bootstrapper.init();

                startAllServices();
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fatal error during server startup.");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FATAL] {ex.Message}");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                stopAllServices();
            }
        }

        private static void startAllServices()
        {
            startService<AuthenticationManagerService>("Authentication");
            startService<ProfileManagerService>("Profile");
            startService<PuzzleManagerService>("Puzzle");
            startService<SocialManagerService>("Social");
            startService<MatchmakingManagerService>("Matchmaking");
            startService<ChatManagerService>("Chat");
        }

        private static void startService<T>(string serviceName) where T : class
        {
            try
            {
                var host = new ServiceHost(typeof(T));
                host.Open();
                serviceHosts.Add(host);

                foreach (var endpoint in host.Description.Endpoints)
                {
                    logger.Info($"{serviceName} service listening at: {endpoint.Address}");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[OK] {serviceName} Service started.");
                Console.ResetColor();
            }
            catch (AddressAlreadyInUseException ex)
            {
                logger.Error(ex, $"Port already in use for {serviceName} service.");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {serviceName} Service - Port already in use.");
                Console.ResetColor();
                throw;
            }
            catch (CommunicationException ex)
            {
                logger.Error(ex, $"Communication error starting {serviceName} service.");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {serviceName} Service - {ex.Message}");
                Console.ResetColor();
                throw;
            }
        }

        private static void stopAllServices()
        {
            Console.WriteLine();
            Console.WriteLine("Stopping services...");

            foreach (var host in serviceHosts)
            {
                try
                {
                    if (host.State == CommunicationState.Opened)
                    {
                        host.Close();
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Error closing service host.");
                    host.Abort();
                }
            }

            serviceHosts.Clear();
            logger.Info("All services stopped.");
            Console.WriteLine("Server shutdown complete.");
        }
    }
}