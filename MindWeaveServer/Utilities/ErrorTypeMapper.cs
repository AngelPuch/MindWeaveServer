using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Resources;

namespace MindWeaveServer.Utilities
{

    public static class ErrorTypeMapper
    {
        public static ServiceErrorType GetErrorTypeFromMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return ServiceErrorType.Unknown;

            if (message == Lang.ErrorCommunicationChannelFailed)
                return ServiceErrorType.CommunicationError;

            if (message == Lang.ErrorJoiningLobbyData)
                return ServiceErrorType.JoiningLobbyFailed;

            if (message == Lang.GenericServerError)
                return ServiceErrorType.Unknown;

            if (message == Lang.DatabaseErrorStartingMatch)
                return ServiceErrorType.StartingMatchFailed;

            if (message == Lang.ErrorPuzzleFileNotFound)
                return ServiceErrorType.PuzzleFileNotFound;

            if (message == Lang.ErrorSavingDifficultyChange)
                return ServiceErrorType.DifficultyChangeFailed;

            if (message == Lang.ErrorSendingGuestInvitation)
                return ServiceErrorType.GuestInvitationFailed;

            if (message == Lang.ErrorEmailServiceUnavailable)
                return ServiceErrorType.EmailServiceUnavailable;

            if (message == Lang.ErrorServiceConnectionClosing)
                return ServiceErrorType.ServiceConnectionClosing;

            return ServiceErrorType.Unknown;
        }

        public static OperationResultDto CreateErrorResult(ServiceErrorType errorType, string target = null)
        {
            return new OperationResultDto
            {
                Success = false,
                ErrorType = errorType,
                Message = string.Empty,
                Target = target
            };
        }
    }
}