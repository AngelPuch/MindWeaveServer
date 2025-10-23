using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts // O MindWeaveServer.Contracts.DataContracts.Puzzle
{
    [DataContract]
    public class UploadResultDto : OperationResultDto // Hereda de OperationResultDto
    {
        [DataMember]
        public int newPuzzleId { get; set; } // ID del puzzle recién creado
    }
}