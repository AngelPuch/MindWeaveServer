using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    
    [DataContract]
    public class UserProfileForEditDto
    {
        [DataMember]
        public string FirstName { get; set; }

        [DataMember]
        public string LastName { get; set; }

        [DataMember]
        public DateTime? DateOfBirth { get; set; }

        [DataMember]
        public int IdGender { get; set; }

        [DataMember]
        public List<GenderDto> AvailableGenders { get; set; }

        [DataMember]
        public List<PlayerSocialMediaDto> SocialMedia { get; set; }
    }

   
}