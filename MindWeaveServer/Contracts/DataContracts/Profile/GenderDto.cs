
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    [DataContract]
    public class GenderDto
    {
        [DataMember]
        public int IdGender { get; set; }

        [DataMember]
        public string Name { get; set; }
    }
}