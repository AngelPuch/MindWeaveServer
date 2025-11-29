using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Data.Entity.Core;
using System.Net.Sockets;
using System.ServiceModel;

namespace MindWeaveServer.Utilities
{
    public class ServiceExceptionHandler : IServiceExceptionHandler
    {
        private readonly Logger logger = LogManager.GetCurrentClassLogger();

        public FaultException<ServiceFaultDto> handleException(Exception exception, string operationContext)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            string safeContext = operationContext ?? "UnknownContext";

            if (exception is EntityException entityEx)
            {
                return handleDatabaseException(entityEx, safeContext);
            }
            else if (exception is TimeoutException timeoutEx)
            {
                return handleTimeoutException(timeoutEx, safeContext);
            }
            else if (exception is SocketException socketEx)
            {
                return handleEmailServiceException(socketEx, safeContext);
            }
            else if (exception is InvalidOperationException invalidOpEx && invalidOpEx.Message == "DuplicateUser")
            {
                return handleDuplicateUserException(safeContext);
            }
            else
            {
                return handleUnknownException(exception, safeContext);
            }
        }

        private FaultException<ServiceFaultDto> handleDatabaseException(EntityException ex, string context)
        {
            logger.Fatal(ex, "Database unavailable. Context: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.DatabaseError,
                Lang.ErrorMsgServerOffline,
                "Database");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
        }

        private FaultException<ServiceFaultDto> handleTimeoutException(TimeoutException ex, string context)
        {
            logger.Error(ex, "Operation timed out. Context: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.DatabaseError,
                Lang.GenericServerError,
                "Timeout");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Service Timeout"));
        }

        private FaultException<ServiceFaultDto> handleEmailServiceException(SocketException ex, string context)
        {
            logger.Error(ex, "Email service communication failed. Context: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.CommunicationError,
                Lang.ErrorEmailServiceUnavailable,
                "EmailService");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Email Service Failed"));
        }

        private FaultException<ServiceFaultDto> handleDuplicateUserException(string context)
        {
            logger.Warn("Duplicate user detected. Context: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.DuplicateRecord,
                Lang.RegistrationUsernameOrEmailExists,
                "Username/Email");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Duplicate Record"));
        }

        private FaultException<ServiceFaultDto> handleUnknownException(Exception ex, string context)
        {
            logger.Fatal(ex, "Unhandled exception. Context: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.Unknown,
                Lang.GenericServerError,
                "Server");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
        }
    }
}