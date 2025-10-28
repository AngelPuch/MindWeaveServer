using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Utilities.Abstractions
{
    public interface IPasswordPolicyValidator
    {
        OperationResultDto validate(string password);
    }
}