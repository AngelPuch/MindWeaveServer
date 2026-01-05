using System.ServiceModel;
using MindWeaveServer.Contracts.DataContracts.Heartbeat;

namespace MindWeaveServer.Contracts.ServiceContracts
{
    [ServiceContract(CallbackContract = typeof(IHeartbeatCallback), SessionMode = SessionMode.Required)]
    public interface IHeartbeatManager
    {
        [OperationContract]
        HeartbeatRegistrationResult registerForHeartbeat(string username);

        [OperationContract(IsOneWay = true)]
        void sendHeartbeat(string username, long sequenceNumber, long clientTimestamp);

        [OperationContract(IsOneWay = true)]
        void unregisterHeartbeat(string username);
    }

    [ServiceContract]
    public interface IHeartbeatCallback
    {
        [OperationContract(IsOneWay = true)]
        void heartbeatAck(long sequenceNumber, long serverTimestamp);

        [OperationContract(IsOneWay = true)]
        void connectionTerminating(string reason);
    }
}