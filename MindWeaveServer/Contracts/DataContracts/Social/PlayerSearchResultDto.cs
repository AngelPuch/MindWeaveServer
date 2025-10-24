using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Social
{
    [DataContract]
    public class PlayerSearchResultDto
    {
        [DataMember]
        public string username { get; set; }

        [DataMember]
        public string avatarPath { get; set; }
    }
}
