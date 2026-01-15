namespace MindWeaveServer.Utilities.Email.Templates
{
    public abstract class BaseEmailTemplate : IEmailTemplate
    {
        public abstract string Subject { get; }

        private string htmlBody;
        public string HtmlBody => htmlBody ?? buildHtml();

        protected abstract string greeting { get; }
        protected abstract string code { get; }

        protected virtual string instruction => null;
        protected virtual string codeInfo => null;
        protected virtual string expiryInfo => null;
        protected virtual string footerText => "Mind Weave Team";

        protected BaseEmailTemplate() { }

        private string buildHtml()
        {
            var instructionHtml = !string.IsNullOrEmpty(instruction)
                ? $"<p>{instruction}</p>" : "";

            var codeInfoHtml = !string.IsNullOrEmpty(codeInfo)
                ? $"<p>{codeInfo}</p>" : "";

            var expiryHtml = !string.IsNullOrEmpty(expiryInfo)
                ? $"<p>{expiryInfo}</p>" : "";

            return $@"
                <div style='font-family: Arial, sans-serif; text-align: center; color: #333;'>
                    <div style='max-width: 600px; margin: auto; border: 1px solid #ddd; padding: 20px;'>
                        <h2>{greeting}</h2>
                        {instructionHtml}
                        {codeInfoHtml}
                        <div style='background-color: #f2f2f2; border-radius: 8px; padding: 10px 20px; margin: 20px auto; display: inline-block;'>
                            <h1 style='font-size: 32px; letter-spacing: 4px; margin: 0;'>{code}</h1>
                        </div>
                        {expiryHtml}
                        <hr style='border: none; border-top: 1px solid #eee;' />
                        <p style='font-size: 12px; color: #888;'>{footerText}</p>
                    </div>
                </div>";
        }
    }
}