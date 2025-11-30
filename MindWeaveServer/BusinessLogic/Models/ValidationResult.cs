namespace MindWeaveServer.BusinessLogic.Models
{
    public class ValidationResult
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }

        private ValidationResult(bool success, string message)
        {
            IsSuccess = success;
            ErrorMessage = message;
        }

        public static ValidationResult success()
        {
            return new ValidationResult(true, string.Empty);
        }

        public static ValidationResult failure(string message)
        {
            return new ValidationResult(false, message);
        }
    }
}