using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Authentication
{
    [DataContract]
    public class UserProfileDto
    {
        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string FirstName { get; set; }

        [DataMember]
        public string LastName { get; set; }

        [DataMember]
        public string Email { get; set; }

        [DataMember]
        public DateTime DateOfBirth { get; set; }

        [DataMember]
        public int GenderId { get; set; }

        [DataMember]
        public byte[] Avatar { get; set; }
    }
}
