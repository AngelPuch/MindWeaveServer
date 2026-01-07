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

        void start();

        void stop();
    }
}