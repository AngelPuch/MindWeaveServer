
using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    [DataContract]
    public class GenderDto
    {
        [DataMember]
        public int idGender { get; set; }

        [DataMember]
        public string name { get; set; }
    }
}