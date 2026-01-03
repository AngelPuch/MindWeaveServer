namespace MindWeaveServer.Contracts.DataContracts.Shared
{
    public static class MessageCodes
    {
        public const string AUTH_LOGIN_SUCCESS = "AUTH_LOGIN_SUCCESS";
        public const string AUTH_REGISTRATION_SUCCESS = "AUTH_REGISTRATION_SUCCESS";
        public const string AUTH_VERIFICATION_SUCCESS = "AUTH_VERIFICATION_SUCCESS";
        public const string AUTH_VERIFICATION_CODE_RESENT = "AUTH_VERIFICATION_CODE_RESENT";
        public const string AUTH_RECOVERY_CODE_SENT = "AUTH_RECOVERY_CODE_SENT";
        public const string AUTH_PASSWORD_RESET_SUCCESS = "AUTH_PASSWORD_RESET_SUCCESS";

        public const string AUTH_INVALID_CREDENTIALS = "AUTH_INVALID_CREDENTIALS";
        public const string AUTH_USER_ALREADY_LOGGED_IN = "AUTH_USER_ALREADY_LOGGED_IN";
        public const string AUTH_ACCOUNT_NOT_VERIFIED = "AUTH_ACCOUNT_NOT_VERIFIED";
        public const string AUTH_USER_ALREADY_EXISTS = "AUTH_USER_ALREADY_EXISTS";
        public const string AUTH_USER_NOT_FOUND = "AUTH_USER_NOT_FOUND";
        public const string AUTH_ACCOUNT_ALREADY_VERIFIED = "AUTH_ACCOUNT_ALREADY_VERIFIED";
        public const string AUTH_CODE_INVALID_OR_EXPIRED = "AUTH_CODE_INVALID_OR_EXPIRED";

        public const string VALIDATION_GENERAL_ERROR = "VALIDATION_GENERAL_ERROR";
        public const string VALIDATION_FIELDS_REQUIRED = "VALIDATION_FIELDS_REQUIRED";
        public const string VALIDATION_EMAIL_REQUIRED = "VALIDATION_EMAIL_REQUIRED";
        public const string VALIDATION_EMAIL_CODE_REQUIRED = "VALIDATION_EMAIL_CODE_REQUIRED";
        public const string VALIDATION_CODE_INVALID_FORMAT = "VALIDATION_CODE_INVALID_FORMAT";
        public const string VALIDATION_PROFILE_PASSWORD_REQUIRED = "VALIDATION_PROFILE_PASSWORD_REQUIRED";

        public const string ERROR_SERVER_GENERIC = "ERROR_SERVER_GENERIC";
        public const string ERROR_DATABASE = "ERROR_DATABASE";


        public const string MATCH_USERNAME_REQUIRED = "MATCH_USERNAME_REQUIRED";
        public const string MATCH_USER_ALREADY_BUSY = "MATCH_USER_ALREADY_BUSY";
        public const string MATCH_LOBBY_NOT_FOUND = "MATCH_LOBBY_NOT_FOUND";
        public const string MATCH_USER_BANNED = "MATCH_USER_BANNED";
        public const string MATCH_LOBBY_FULL = "MATCH_LOBBY_FULL";
        public const string MATCH_PLAYER_ALREADY_IN_LOBBY = "MATCH_PLAYER_ALREADY_IN_LOBBY";
        public const string MATCH_USER_NOT_ONLINE = "MATCH_USER_NOT_ONLINE";
        public const string MATCH_NOT_HOST = "MATCH_NOT_HOST";
        public const string MATCH_NOT_ENOUGH_PLAYERS = "MATCH_NOT_ENOUGH_PLAYERS";
        public const string MATCH_HOST_CANNOT_KICK_SELF = "MATCH_HOST_CANNOT_KICK_SELF";
        public const string MATCH_PLAYER_NOT_FOUND = "MATCH_PLAYER_NOT_FOUND";

        public const string MATCH_LOBBY_CREATION_FAILED = "MATCH_LOBBY_CREATION_FAILED";
        public const string MATCH_LOBBY_CREATED = "MATCH_LOBBY_CREATED";
        public const string MATCH_JOIN_ERROR_DATA = "MATCH_JOIN_ERROR_DATA";
        public const string MATCH_GUEST_NAME_GENERATION_FAILED = "MATCH_GUEST_NAME_GENERATION_FAILED";
        public const string MATCH_GUEST_JOIN_SUCCESS = "MATCH_GUEST_JOIN_SUCCESS";
        public const string MATCH_GUEST_INVITE_INVALID = "MATCH_GUEST_INVITE_INVALID";
        public const string MATCH_GUEST_INVITE_SENT = "MATCH_GUEST_INVITE_SENT";

        public const string MATCH_START_DB_ERROR = "MATCH_START_DB_ERROR";
        public const string MATCH_PUZZLE_FILE_NOT_FOUND = "MATCH_PUZZLE_FILE_NOT_FOUND";
        public const string MATCH_DIFFICULTY_CHANGE_ERROR = "MATCH_DIFFICULTY_CHANGE_ERROR";
        public const string MATCH_GUEST_INVITE_SEND_ERROR = "MATCH_GUEST_INVITE_SEND_ERROR";
        public const string MATCH_COMMUNICATION_ERROR = "MATCH_COMMUNICATION_ERROR";
        public const string MATCH_SERVICE_CLOSING = "MATCH_SERVICE_CLOSING";

        public const string NOTIFY_KICKED_BY_HOST = "NOTIFY_KICKED_BY_HOST";
        public const string NOTIFY_KICKED_PROFANITY = "NOTIFY_KICKED_PROFANITY";
        public const string NOTIFY_HOST_LEFT = "NOTIFY_HOST_LEFT";
    }
}