using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Authentication
{
    [DataContract]
    public class LoginResultDto
    {
        [DataMember]
        public OperationResultDto operationResult { get; set; }

        [DataMember]
        public string username { get; set; }

        [DataMember]
        public string avatarPath { get; set; }
    }
}