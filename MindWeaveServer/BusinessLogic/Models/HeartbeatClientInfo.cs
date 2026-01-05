using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.ServiceModel;

namespace MindWeaveServer.BusinessLogic.Models
{
    public class HeartbeatClientInfo
    {
        public string Username { get; set; }
        public DateTime LastHeartbeatReceived { get; set; }
        public DateTime RegisteredAt { get; set; }
        public long LastSequenceNumber { get; set; }
        public IHeartbeatCallback Callback { get; set; }
        public ICommunicationObject CommunicationObject { get; set; }
        public volatile bool IsBeingDisconnected;
        public int MissedHeartbeats { get; set; }

        public HeartbeatClientInfo(string username, IHeartbeatCallback callback)
        {
            Username = username;
            Callback = callback;
            CommunicationObject = callback as ICommunicationObject;
            RegisteredAt = DateTime.UtcNow;
            LastHeartbeatReceived = DateTime.UtcNow;
            LastSequenceNumber = 0;
            IsBeingDisconnected = false;
            MissedHeartbeats = 0;
        }

        public bool isChannelHealthy()
        {
            if (CommunicationObject == null)
            {
                return false;
            }

            var state = CommunicationObject.State;
            return state == CommunicationState.Opened;
        }

        public TimeSpan getTimeSinceLastHeartbeat()
        {
            return DateTime.UtcNow - LastHeartbeatReceived;
        }
    }
}