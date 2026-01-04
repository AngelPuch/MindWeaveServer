namespace MindWeaveServer.BusinessLogic.Models
{
    public class ValidationResult
    {
        public bool IsSuccess { get; private set; }

        public string MessageCode { get; private set; }

        public string[] MessageParams { get; private set; }

        private ValidationResult() { }

        public static ValidationResult success()
        {
            return new ValidationResult { IsSuccess = true };
        }

        public static ValidationResult failure(string messageCode, params string[] messageParams)
        {
            return new ValidationResult
            {
                IsSuccess = false,
                MessageCode = messageCode,
                MessageParams = messageParams?.Length > 0 ? messageParams : null
            };
        }
    }
}