using MindWeaveServer.BusinessLogic.Models;
using MindWeaveServer.Contracts.ServiceContracts;
using System;
using System.Collections.Generic;

namespace MindWeaveServer.BusinessLogic.Abstractions
{
    public interface IHeartbeatMonitor : IDisposable
    {
        bool registerClient(string username, IHeartbeatCallback callback);

        bool recordHeartbeat(string username, long sequenceNumber);

        bool unregisterClient(string username);

        bool isClientRegistered(string username);

        HeartbeatClientInfo getClientInfo(string username);

        IReadOnlyCollection<string> getRegisteredClients();

        void start();

        void stop();
    }
}