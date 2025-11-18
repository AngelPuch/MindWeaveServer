using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Stats
{
    [DataContract]
    public class AchievementDto
    {
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public string IconPath { get; set; }
    }
}