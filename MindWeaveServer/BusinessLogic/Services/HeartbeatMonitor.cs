using MindWeaveServer.BusinessLogic.Abstractions;
using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.ServiceContracts;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

namespace MindWeaveServer.BusinessLogic.Services
{
    public class HeartbeatMonitor : IHeartbeatMonitor
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ConcurrentDictionary<string, HeartbeatClientInfo> registeredClients;
        private readonly IDisconnectionHandler disconnectionHandler;

        private Timer monitorTimer;
        private volatile bool isRunning;
        private volatile bool isDisposed;
        private readonly object startStopLock = new object();

        public const int HEARTBEAT_INTERVAL_MS = 500;
        public const int HEARTBEAT_TIMEOUT_MS = 2500;
        public const int MAX_MISSED_HEARTBEATS = 5;
        public const int MONITOR_CHECK_INTERVAL_MS = 500;

        private const string DISCONNECT_REASON_HEARTBEAT_TIMEOUT = "HeartbeatTimeout";
        private const string DISCONNECT_REASON_CHANNEL_FAULTED = "ChannelFaulted";

        public HeartbeatMonitor(IDisconnectionHandler disconnectionHandler)
        {
            this.disconnectionHandler = disconnectionHandler ?? throw new ArgumentNullException(nameof(disconnectionHandler));
            this.registeredClients = new ConcurrentDictionary<string, HeartbeatClientInfo>(StringComparer.OrdinalIgnoreCase);

            logger.Info("HeartbeatMonitor initialized. Interval: {0}ms, Timeout: {1}ms, MaxMissed: {2}",
                HEARTBEAT_INTERVAL_MS, HEARTBEAT_TIMEOUT_MS, MAX_MISSED_HEARTBEATS);
        }

        public void start()
        {
            lock (startStopLock)
            {
                if (isRunning || isDisposed)
                {
                    logger.Warn("HeartbeatMonitor: Attempted to start while already running or disposed.");
                    return;
                }

                isRunning = true;
                monitorTimer = new Timer(checkAllClients, null, MONITOR_CHECK_INTERVAL_MS, MONITOR_CHECK_INTERVAL_MS);

                logger.Info("HeartbeatMonitor started. Checking clients every {0}ms.", MONITOR_CHECK_INTERVAL_MS);
            }
        }

        public void stop()
        {
            lock (startStopLock)
            {
                if (!isRunning)
                {
                    return;
                }

                isRunning = false;
                monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                monitorTimer?.Dispose();
                monitorTimer = null;

                logger.Info("HeartbeatMonitor stopped.");
            }
        }

        public bool registerClient(string username, IHeartbeatCallback callback)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                logger.Warn("HeartbeatMonitor: Cannot register client with null/empty username.");
                return false;
            }

            if (callback == null)
            {
                logger.Warn("HeartbeatMonitor: Cannot register client {0} with null callback.", username);
                return false;
            }

            var clientInfo = new HeartbeatClientInfo(username, callback);

            var result = registeredClients.AddOrUpdate(
            username,
            clientInfo,
            (key, existingClient) =>
            {
                cleanupClientCallbackEvents(existingClient);

                logger.Info("HeartbeatMonitor: Updating existing registration for {0}.", key);
                return clientInfo;
            });

            setupClientCallbackEvents(result);

            logger.Info("HeartbeatMonitor: Client {0} registered successfully. Total clients: {1}",
                username, registeredClients.Count);

            return true;
        }

        public bool recordHeartbeat(string username, long sequenceNumber)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            if (!registeredClients.TryGetValue(username, out var clientInfo))
            {
                logger.Warn("HeartbeatMonitor: Received heartbeat from unregistered client {0}.", username);
                return false;
            }

            if (clientInfo.IsBeingDisconnected)
            {
                logger.Warn("HeartbeatMonitor: Ignoring heartbeat from {0} - client is being disconnected.", username);
                return false;
            }

            if (sequenceNumber < clientInfo.LastSequenceNumber - 10)
            {
                logger.Warn("HeartbeatMonitor: Sequence number regression for {0}. Expected >= {1}, got {2}.",
                    username, clientInfo.LastSequenceNumber, sequenceNumber);
            }

            clientInfo.LastHeartbeatReceived = DateTime.UtcNow;
            clientInfo.LastSequenceNumber = sequenceNumber;
            clientInfo.MissedHeartbeats = 0;

            logger.Trace("HeartbeatMonitor: Heartbeat recorded for {0}. Seq: {1}", username, sequenceNumber);

            return true;
        }

        public bool unregisterClient(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            if (registeredClients.TryRemove(username, out var removedClient))
            {
                cleanupClientCallbackEvents(removedClient);

                logger.Info("HeartbeatMonitor: Client {0} unregistered. Total clients: {1}",
                    username, registeredClients.Count);

                return true;
            }

            logger.Warn("HeartbeatMonitor: Attempted to unregister unknown client {0}.", username);
            return false;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (isDisposed)
            {
                return;
            }

            if (disposing)
            {
                stop();

                foreach (var kvp in registeredClients)
                {
                    cleanupClientCallbackEvents(kvp.Value);
                }

                registeredClients.Clear();
            }

            isDisposed = true;
            logger.Info("HeartbeatMonitor disposed.");
        }

        private void checkAllClients(object state)
        {
            if (!isRunning || isDisposed)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var clientsToCheck = registeredClients.ToArray();

            foreach (var kvp in clientsToCheck)
            {
                var username = kvp.Key;
                var clientInfo = kvp.Value;

                try
                {
                    checkSingleClient(username, clientInfo);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "HeartbeatMonitor: Error checking client {0}.", username);
                }
            }
        }

        private void checkSingleClient(string username, HeartbeatClientInfo clientInfo)
        {
            if (clientInfo.IsBeingDisconnected)
            {
                return;
            }

            if (!clientInfo.isChannelHealthy())
            {
                logger.Warn("HeartbeatMonitor: Channel unhealthy for {0}. Initiating disconnect.", username);
                initiateClientDisconnection(username, clientInfo, DISCONNECT_REASON_CHANNEL_FAULTED);
                return;
            }

            var timeSinceLastHeartbeat = clientInfo.getTimeSinceLastHeartbeat();

            if (timeSinceLastHeartbeat.TotalMilliseconds > HEARTBEAT_TIMEOUT_MS)
            {
                clientInfo.MissedHeartbeats++;

                logger.Warn("HeartbeatMonitor: Client {0} missed heartbeat #{1}. Last seen: {2:F1}s ago.",
                    username, clientInfo.MissedHeartbeats, timeSinceLastHeartbeat.TotalSeconds);

                if (clientInfo.MissedHeartbeats >= MAX_MISSED_HEARTBEATS)
                {
                    logger.Error("HeartbeatMonitor: Client {0} exceeded max missed heartbeats ({1}). Disconnecting.",
                        username, MAX_MISSED_HEARTBEATS);

                    initiateClientDisconnection(username, clientInfo, DISCONNECT_REASON_HEARTBEAT_TIMEOUT);
                }
            }
        }

        private void initiateClientDisconnection(string username, HeartbeatClientInfo clientInfo, string reason)
        {
            if (clientInfo.IsBeingDisconnected)
            {
                return;
            }

            clientInfo.IsBeingDisconnected = true;

            tryNotifyClientOfTermination(clientInfo, reason);

            Task.Run(async () =>
            {
                try
                {
                    logger.Info("HeartbeatMonitor: Executing full disconnection for {0}. Reason: {1}", username, reason);

                    unregisterClient(username);

                    await disconnectionHandler.handleFullDisconnectionAsync(username, reason);

                    logger.Info("HeartbeatMonitor: Full disconnection completed for {0}.", username);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "HeartbeatMonitor: Error during disconnection handling for {0}.", username);
                }
            });
        }

        private static void tryNotifyClientOfTermination(HeartbeatClientInfo clientInfo, string reason)
        {
            if (clientInfo.Callback == null || !clientInfo.isChannelHealthy())
            {
                return;
            }

            try
            {
                clientInfo.Callback.connectionTerminating(reason);
            }
            catch (CommunicationException)
            {
                // Esperado si el canal ya está muerto
            }
            catch (TimeoutException)
            {
                // Esperado si el cliente no responde
            }
            catch (ObjectDisposedException)
            {
                // Canal ya cerrado
            }
        }

        private void setupClientCallbackEvents(HeartbeatClientInfo clientInfo)
        {
            var commObject = clientInfo.CommunicationObject;

            if (commObject == null)
            {
                return;
            }

            commObject.Faulted += (sender, args) => onClientChannelFaulted(clientInfo.Username);
            commObject.Closed += (sender, args) => onClientChannelClosed(clientInfo.Username);
        }

        private static void cleanupClientCallbackEvents(HeartbeatClientInfo clientInfo)
        {
            clientInfo.Callback = null;
            clientInfo.CommunicationObject = null;
        }

        private void onClientChannelFaulted(string username)
        {
            logger.Warn("HeartbeatMonitor: Channel faulted for client {0}.", username);

            if (registeredClients.TryGetValue(username, out var clientInfo))
            {
                initiateClientDisconnection(username, clientInfo, DISCONNECT_REASON_CHANNEL_FAULTED);
            }
        }

        private void onClientChannelClosed(string username)
        {
            logger.Info("HeartbeatMonitor: Channel closed for client {0}.", username);

            if (registeredClients.TryGetValue(username, out var clientInfo) && !clientInfo.IsBeingDisconnected)
            {
                initiateClientDisconnection(username, clientInfo, DISCONNECT_REASON_CHANNEL_FAULTED);
            }
        }
    }
}