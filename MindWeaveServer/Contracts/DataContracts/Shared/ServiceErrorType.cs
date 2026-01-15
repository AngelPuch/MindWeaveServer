using System.Runtime.Serialization;

namespace MindWeaveServer.Contracts.DataContracts.Shared
{
    [DataContract(Name = "ServiceErrorType")]
    public enum ServiceErrorType
    {
        [EnumMember]
        Unknown = 0,           

        [EnumMember]
        DatabaseError = 1,     

        [EnumMember]
        DuplicateRecord = 2,    

        [EnumMember]
        NotFound = 4,           

        [EnumMember]
        OperationFailed = 5,    

        [EnumMember]
        CommunicationError = 6,

        [EnumMember]
        ValidationError = 11,

        [EnumMember]
        SecurityError = 12
    }
}