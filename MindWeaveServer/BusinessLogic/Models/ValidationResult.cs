namespace MindWeaveServer.BusinessLogic.Models
{
    public class ValidationResult
    {
        public bool IsSuccess { get; private set; }
        public string MessageCode { get; private set; } 
        public string[] MessageParams { get; private set; }

        private ValidationResult(bool isSuccess, string messageCode, string[] parameters)
        {
            IsSuccess = isSuccess;
            MessageCode = messageCode;
            MessageParams = parameters;
        }

        public static ValidationResult success()
        {
            return new ValidationResult(true, null, null);
        }

        public static ValidationResult failure(string messageCode, params string[] parameters)
        {
            return new ValidationResult(false, messageCode, parameters);
        }
    }
}