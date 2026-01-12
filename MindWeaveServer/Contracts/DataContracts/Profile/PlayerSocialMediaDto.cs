using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    [DataContract]
    public class PlayerSocialMediaDto
    {
        [DataMember]
        public int IdSocialMediaPlatform { get; set; }

        [DataMember]
        public string PlatformName { get; set; }

        [DataMember]
        public string Username { get; set; }
    }
}