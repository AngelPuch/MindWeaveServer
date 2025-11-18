using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Social
{
    [DataContract]
    public class PlayerSearchResultDto
    {
        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string AvatarPath { get; set; }
    }
}
