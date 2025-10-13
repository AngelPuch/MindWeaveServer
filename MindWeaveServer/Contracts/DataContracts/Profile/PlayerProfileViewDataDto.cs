using MindWeaveServer.DataAccess;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    [DataContract]
    public class PlayerProfileViewDataDto
    {
        [DataMember]
        public string username { get; set; }

        [DataMember]
        public string avatarPath { get; set; }

        [DataMember]
        public PlayerStats stats { get; set; }

        [DataMember]
        public List<AchievementData> achievements { get; set; }
    }

    [DataContract]
    public class AchievementData
    {
        [DataMember]
        public string name { get; set; }

        [DataMember]
        public string description { get; set; }

        [DataMember]
        public string iconPath { get; set; }
    }
}
