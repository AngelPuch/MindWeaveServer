using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Shared
{
    [DataContract]
    public class ServiceFaultDto
    {
        [DataMember]
        public ServiceErrorType ErrorType { get; set; }

        [DataMember]
        public string MessageCode { get; set; }

        [DataMember]
        public string[] MessageParams { get; set; }

        [DataMember]
        public string Target { get; set; }

        public ServiceFaultDto(ServiceErrorType errorType, string messageCode, string target = null)
        {
            ErrorType = errorType;
            MessageCode = messageCode;
            Target = target;
        }

        public ServiceFaultDto(ServiceErrorType errorType, string messageCode, string[] messageParams, string target = null)
        {
            ErrorType = errorType;
            MessageCode = messageCode;
            MessageParams = messageParams;
            Target = target;
        }

    }
}