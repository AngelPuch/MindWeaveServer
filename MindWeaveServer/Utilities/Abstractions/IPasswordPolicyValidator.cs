using MindWeaveServer.Contracts.DataContracts;

namespace MindWeaveServer.Utilities.Abstractions
{
    public interface IPasswordPolicyValidator
    {
        OperationResultDto validate(string password);
    }
}