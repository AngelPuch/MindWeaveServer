using Autofac;
using MindWeaveServer.AppStart;
using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Services;
using MindWeaveServer.Contracts.DataContracts.Heartbeat;
using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Contracts.ServiceContracts;
using NLog;
using System;
using System.ServiceModel;

namespace MindWeaveServer.Services
{
    [ServiceBehavior(
        InstanceContextMode = InstanceContextMode.PerSession,
        ConcurrencyMode = ConcurrencyMode.Multiple,
        IncludeExceptionDetailInFaults = false)]
    public class HeartbeatManagerService : IHeartbeatManager
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly IHeartbeatMonitor heartbeatMonitor;

        private string currentUsername;
        private IHeartbeatCallback currentCallback;
        private volatile bool isRegistered;
        private volatile bool isDisconnecting;

        public HeartbeatManagerService()
        {
            Bootstrapper.init();
            this.heartbeatMonitor = Bootstrapper.Container.Resolve<IHeartbeatMonitor>();

            subscribeToChannelEvents();
        }

        public HeartbeatManagerService(IHeartbeatMonitor heartbeatMonitor)
        {
            this.heartbeatMonitor = heartbeatMonitor ?? throw new ArgumentNullException(nameof(heartbeatMonitor));

            subscribeToChannelEvents();
        }

        public HeartbeatRegistrationResult registerForHeartbeat(string username)
        {
            logger.Info("HeartbeatService: Registration request from {0}.", username ?? "NULL");

            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("HeartbeatService: Registration rejected - empty username.");
                return new HeartbeatRegistrationResult
                {
                    Success = false,
                    MessageCode = MessageCodes.VALIDATION_USERNAME_REQUIRED
                };
            }

            if (isRegistered)
            {
                logger.Warn("HeartbeatService: Session already registered for {0}.", currentUsername);

                if (!currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
                {
                    return new HeartbeatRegistrationResult
                    {
                        Success = false,
                        MessageCode = MessageCodes.ERROR_SESSION_MISMATCH
                    };
                }

              
                return createSuccessResult();
            }

            try
            {
                currentCallback = OperationContext.Current?.GetCallbackChannel<IHeartbeatCallback>();

                if (currentCallback == null)
                {
                    logger.Error("HeartbeatService: Could not get callback channel for {0}.", username);
                    return new HeartbeatRegistrationResult
                    {
                        Success = false,
                        MessageCode = MessageCodes.ERROR_COMMUNICATION_CHANNEL
                    };
                }

                bool registered = heartbeatMonitor.registerClient(username, currentCallback);

                if (!registered)
                {
                    logger.Error("HeartbeatService: Monitor rejected registration for {0}.", username);
                    return new HeartbeatRegistrationResult
                    {
                        Success = false,
                        MessageCode = MessageCodes.ERROR_SERVER_GENERIC
                    };
                }

                currentUsername = username;
                isRegistered = true;

                logger.Info("HeartbeatService: {0} registered successfully.", username);

                return createSuccessResult();
            }
            catch (CommunicationException ex)
            {
                logger.Error(ex, "HeartbeatService: Communication error during registration for {0}.", username);
                return new HeartbeatRegistrationResult
                {
                    Success = false,
                    MessageCode = MessageCodes.ERROR_COMMUNICATION_CHANNEL
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "HeartbeatService: Unexpected error during registration for {0}.", username);
                return new HeartbeatRegistrationResult
                {
                    Success = false,
                    MessageCode = MessageCodes.ERROR_SERVER_GENERIC
                };
            }
        }

        public void sendHeartbeat(string username, long sequenceNumber, long clientTimestamp)
        {
            if (!validateHeartbeatRequest(username))
            {
                return;
            }

            bool recorded = heartbeatMonitor.recordHeartbeat(username, sequenceNumber);

            if (recorded && currentCallback != null)
            {
                try
                {
                    long serverTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    currentCallback.heartbeatAck(sequenceNumber, serverTimestamp);
                }
                catch (CommunicationException)
                {
                    logger.Warn("HeartbeatService: Failed to send ACK to {0} - channel issue.", username);
                }
                catch (TimeoutException)
                {
                    logger.Warn("HeartbeatService: Timeout sending ACK to {0}.", username);
                }
                catch (ObjectDisposedException)
                {
                    logger.Warn("HeartbeatService: Channel disposed for {0}.", username);
                }
            }
        }

        public void unregisterHeartbeat(string username)
        {
            logger.Info("HeartbeatService: Unregister request from {0}.", username ?? "NULL");

            if (!validateUnregisterRequest(username))
            {
                return;
            }

            performCleanUnregister();
        }

        private bool validateHeartbeatRequest(string username)
        {
            if (!isRegistered || isDisconnecting)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            if (!currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("HeartbeatService: Username mismatch. Expected: {0}, Got: {1}",
                    currentUsername, username);
                return false;
            }

            return true;
        }

        private bool validateUnregisterRequest(string username)
        {
            if (!isRegistered)
            {
                logger.Warn("HeartbeatService: Unregister called but not registered.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(username) ||
                !currentUsername.Equals(username, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("HeartbeatService: Unregister username mismatch.");
                return false;
            }

            return true;
        }

        private void performCleanUnregister()
        {
            if (isDisconnecting)
            {
                return;
            }

            isDisconnecting = true;

            string usernameToUnregister = currentUsername;

            try
            {
                if (!string.IsNullOrWhiteSpace(usernameToUnregister))
                {
                    heartbeatMonitor.unregisterClient(usernameToUnregister);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "HeartbeatService: Error during unregister for {0}.", usernameToUnregister);
            }
            finally
            {
                currentUsername = null;
                currentCallback = null;
                isRegistered = false;
            }
        }

        private HeartbeatRegistrationResult createSuccessResult()
        {
            return new HeartbeatRegistrationResult
            {
                Success = true,
                MessageCode = MessageCodes.SUCCESS,
                HeartbeatIntervalMs = HeartbeatMonitor.HEARTBEAT_INTERVAL_MS,
                TimeoutMs = HeartbeatMonitor.HEARTBEAT_TIMEOUT_MS
            };
        }

        private void subscribeToChannelEvents()
        {
            if (OperationContext.Current?.Channel == null)
            {
                return;
            }

            OperationContext.Current.Channel.Faulted += onChannelFaultedOrClosed;
            OperationContext.Current.Channel.Closed += onChannelFaultedOrClosed;
        }

        private void onChannelFaultedOrClosed(object sender, EventArgs e)
        {
            if (!isRegistered || isDisconnecting)
            {
                return;
            }

            logger.Warn("HeartbeatService: Channel faulted/closed for {0}. Cleaning up.", currentUsername);

            performCleanUnregister();
        }
    }
}