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
        ValidationFailed = 3,  

        [EnumMember]
        NotFound = 4,           

        [EnumMember]
        OperationFailed = 5,    

        [EnumMember]
        CommunicationError = 6,
        [EnumMember]
        LobbyFull = 7,     

        [EnumMember]
        GameInProgress = 8,    

        [EnumMember]
        LobbyNotFound = 9,    

        [EnumMember]
        PlayerBanned = 10,

        [EnumMember]
        ValidationError = 11,

        [EnumMember]
        SecurityError = 12
    }
}