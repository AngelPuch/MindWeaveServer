// MindWeaveServer/Contracts/DataContracts/Stats/PlayerProfileViewDto.cs

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class PlayerProfileViewDto
    {
        [DataMember]
        public string username { get; set; }

        [DataMember]
        public string avatarPath { get; set; }

        [DataMember]
        public string firstName { get; set; }

        [DataMember]
        public string lastName { get; set; }

        [DataMember]
        public System.DateTime? dateOfBirth { get; set; }

        [DataMember]
        public string gender { get; set; }

        [DataMember]
        public PlayerStatsDto stats { get; set; }

        [DataMember]
        public List<AchievementDto> achievements { get; set; }
    }
}