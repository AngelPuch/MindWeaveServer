using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Profile
{
    /// <summary>
    /// DTO que encapsula el resultado de la operación de actualización de avatar.
    /// Contiene el estado de la operación y la nueva ruta de la imagen si fue exitosa.
    /// </summary>
    [DataContract]
    public class AvatarUpdateResultDto
    {
        [DataMember]
        public bool success { get; set; }

        [DataMember]
        public string message { get; set; }

        [DataMember]
        public string newAvatarPath { get; set; }
    }
}
  