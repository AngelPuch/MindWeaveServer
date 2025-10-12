using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Authentication
{
    [DataContract]
    public class LoginDto
    {
        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string Password { get; set; }
    }
}