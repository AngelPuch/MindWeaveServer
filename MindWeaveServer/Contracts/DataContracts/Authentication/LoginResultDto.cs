using System.Runtime.Serialization;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Contracts.DataContracts.Authentication
{
    [DataContract]
    public class LoginResultDto
    {
        [DataMember]
        public OperationResultDto operationResult { get; set; }

        [DataMember]
        public int playerId { get; set; }

        [DataMember]
        public string username { get; set; }

        [DataMember]
        public string avatarPath { get; set; }

        [DataMember]
        public string resultCode { get; set; }
    }
}