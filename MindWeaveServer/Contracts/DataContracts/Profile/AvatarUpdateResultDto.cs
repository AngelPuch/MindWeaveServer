using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    [DataContract]
    public class AvatarUpdateResultDto
    {
        [DataMember]
        public bool success { get; set; }

        [DataMember]
        public string message { get; set; }

        [DataMember]
        public string newAvatarPath { get; set; }
    }
}
  