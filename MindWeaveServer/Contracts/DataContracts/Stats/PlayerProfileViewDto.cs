using MindWeaveServer.Contracts.DataContracts.Profile;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class PlayerProfileViewDto
    {
        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public string AvatarPath { get; set; }

        [DataMember]
        public string FirstName { get; set; }

        [DataMember]
        public string LastName { get; set; }

        [DataMember]
        public System.DateTime? DateOfBirth { get; set; }

        [DataMember]
        public string Gender { get; set; }

        [DataMember]
        public PlayerStatsDto Stats { get; set; }

        [DataMember]
        public List<AchievementDto> Achievements { get; set; }

        [DataMember]
        public List<PlayerSocialMediaDto> SocialMedia { get; set; }
    }
}