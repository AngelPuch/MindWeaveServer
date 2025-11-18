using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    [DataContract]
    public class AvatarUpdateResultDto
    {
        [DataMember]
        public bool Success { get; set; }

        [DataMember]
        public string Message { get; set; }

        [DataMember]
        public string NewAvatarPath { get; set; }
    }
}
  