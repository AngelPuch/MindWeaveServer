using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Game
{
    [DataContract]
    public class PlayerResultDto
    {
        [DataMember]
        public int PlayerId { get; set; }

        [DataMember]
        public string Username { get; set; }

        [DataMember]
        public int Score { get; set; }

        [DataMember]
        public int Rank { get; set; }

        [DataMember]
        public int PiecesPlaced { get; set; }

        [DataMember]
        public bool IsWinner { get; set; }
    }
}