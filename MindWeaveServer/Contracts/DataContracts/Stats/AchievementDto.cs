using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class AchievementDto
    {
        [DataMember]
        public string name { get; set; }

        [DataMember]
        public string description { get; set; }

        [DataMember]
        public string iconPath { get; set; }
    }
}