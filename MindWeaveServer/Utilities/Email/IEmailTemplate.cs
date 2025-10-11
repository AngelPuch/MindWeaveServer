namespace MindWeaveServer.Utilities.Email
{
    public interface IEmailTemplate
    {
        string subject { get; }
        string htmlBody { get; }
    }
}