using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    
    [DataContract]
    public class UserProfileForEditDto
    {
        [DataMember]
        public string firstName { get; set; }

        [DataMember]
        public string lastName { get; set; }

        [DataMember]
        public DateTime? dateOfBirth { get; set; }

        [DataMember]
        public int idGender { get; set; }

        [DataMember]
        public List<GenderDto> availableGenders { get; set; }
    }

   
}