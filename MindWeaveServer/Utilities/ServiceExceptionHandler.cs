using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Data.Entity.Core;
using System.IO;
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

            string safeContext = operationContext ?? "UnknownOperation";

            if (exception is EntityException entityEx)
            {
                return handleDatabaseException(entityEx, safeContext);
            }

            if (exception is FileNotFoundException fileNotFoundEx)
            {
                return handleFileNotFoundException(fileNotFoundEx, safeContext);
            }
            
            if (exception is IOException ioEx)
            {
                return handleIOException(ioEx, safeContext);
            }

            if (exception is TimeoutException timeoutEx)
            {
                return handleTimeoutException(timeoutEx, safeContext);
            }

            if (exception is SocketException socketEx)
            {
                return handleEmailServiceException(socketEx, safeContext);
            }

            if (exception is CommunicationException commEx)
            {
                return handleCommunicationException(commEx, safeContext);
            }

            if (exception is ObjectDisposedException disposedEx)
            {
                return handleChannelDisposedException(disposedEx, safeContext);
            }

            if (exception is InvalidOperationException invalidOpEx)
            {
                return handleInvalidOperationException(invalidOpEx, safeContext);
            }

            return handleUnknownException(exception, safeContext);
        }

        private FaultException<ServiceFaultDto> handleDatabaseException(EntityException ex, string context)
        {
            logger.Fatal(ex, "Database unavailable. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.DatabaseError,
                Lang.ErrorMsgServerOffline,
                "Database");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Database Unavailable"));
        }

        private FaultException<ServiceFaultDto> handleFileNotFoundException(FileNotFoundException ex, string context)
        {
            logger.Error(ex, "Resource not found. Context: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.NotFound,
                Lang.ErrorPuzzleFileNotFound,
                "FileSystem");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Resource Missing"));
        }

        private FaultException<ServiceFaultDto> handleIOException(IOException ex, string context)
        {
            logger.Error(ex, "File system/IO error. Context: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.Unknown,
                Lang.ErrorReadingPuzzleFile,
                "FileSystem");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Storage Error"));
        }

        private FaultException<ServiceFaultDto> handleTimeoutException(TimeoutException ex, string context)
        {
            logger.Error(ex, "Operation timed out. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.DatabaseError,
                Lang.GenericServerError,
                "Timeout");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Service Timeout"));
        }

        private FaultException<ServiceFaultDto> handleEmailServiceException(SocketException ex, string context)
        {
            logger.Error(ex, "Email service communication failed. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.CommunicationError,
                Lang.ErrorEmailServiceUnavailable,
                "EmailService");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Email Service Failed"));
        }

        private FaultException<ServiceFaultDto> handleCommunicationException(CommunicationException ex, string context)
        {
            logger.Error(ex, "WCF communication error. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.CommunicationError,
                Lang.ErrorCommunicationChannelFailed,
                "Communication");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Communication Error"));
        }

        private FaultException<ServiceFaultDto> handleChannelDisposedException(ObjectDisposedException ex, string context)
        {
            logger.Warn(ex, "Channel already disposed. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.CommunicationError,
                Lang.ErrorServiceConnectionClosing,
                "Channel");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Channel Disposed"));
        }

        private FaultException<ServiceFaultDto> handleInvalidOperationException(InvalidOperationException ex, string context)
        {
            if (ex.Message == "DuplicateUser")
            {
                return handleDuplicateUserException(context);
            }

            logger.Error(ex, "Invalid operation. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.ValidationError,
                Lang.GenericServerError,
                "InvalidOperation");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Invalid Operation"));
        }

        private FaultException<ServiceFaultDto> handleDuplicateUserException(string context)
        {
            logger.Warn("Duplicate user detected. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.DuplicateRecord,
                Lang.RegistrationUsernameOrEmailExists,
                "Username/Email");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Duplicate Record"));
        }

        private FaultException<ServiceFaultDto> handleUnknownException(Exception ex, string context)
        {
            logger.Fatal(ex, "Unhandled exception. Operation: {Context}", context);

            var fault = new ServiceFaultDto(
                ServiceErrorType.Unknown,
                Lang.GenericServerError,
                "Server");

            return new FaultException<ServiceFaultDto>(fault, new FaultReason("Internal Server Error"));
        }
    }
}