using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts // O MindWeaveServer.Contracts.DataContracts.Puzzle
{
    [DataContract]
    public class PuzzleInfoDto
    {
        [DataMember]
        public int puzzleId { get; set; }

        [DataMember]
        public string name { get; set; }

        [DataMember]
        public string imagePath { get; set; } // Ruta relativa o nombre de archivo para el cliente
    }
}