using System;
using System.ServiceModel;
using MindWeaveServer.Contracts.DataContracts.Shared;

namespace MindWeaveServer.Utilities.Abstractions
{
    public interface IServiceExceptionHandler
    {
        FaultException<ServiceFaultDto> handleException(Exception exception, string operationContext);
    }
}
