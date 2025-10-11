using System.Threading.Tasks;

namespace MindWeaveServer.Utilities.Email
{
    public interface IEmailService
    {
        Task sendEmailAsync(string recipientEmail, string recipientName, IEmailTemplate template);

    }
}
