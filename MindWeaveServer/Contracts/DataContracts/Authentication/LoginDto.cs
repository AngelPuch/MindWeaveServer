using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Authentication
{
    [DataContract]
    public class LoginDto
    {
        [DataMember]
        public string email { get; set; }

        [DataMember]
        public string password { get; set; }
    }
}