namespace MindWeaveServer.Utilities.Email
{
    public interface IEmailTemplate
    {
        string Subject { get; }
        string HtmlBody { get; }
    }
}