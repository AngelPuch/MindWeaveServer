using System;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Authentication
{
    [DataContract]
    public class UserProfileDto
    {
        [DataMember]
        public string username { get; set; }

        [DataMember]
        public string firstName { get; set; }

        [DataMember]
        public string lastName { get; set; }

        [DataMember]
        public string email { get; set; }

        [DataMember]
        public DateTime dateOfBirth { get; set; }

        [DataMember]
        public int genderId { get; set; }

        [DataMember]
        public byte[] avatar { get; set; }
    }
}
