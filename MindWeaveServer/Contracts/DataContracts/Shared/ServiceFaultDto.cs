using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Shared
{
    [DataContract]
    public class ServiceFaultDto
    {
        [DataMember]
        public ServiceErrorType ErrorType { get; set; }

        [DataMember]
        public string Message { get; set; }

        [DataMember]
        public string Target { get; set; } 
        public ServiceFaultDto(ServiceErrorType type, string message, string target = null)
        {
            ErrorType = type;
            Message = message;
            Target = target;
        }
    }
}