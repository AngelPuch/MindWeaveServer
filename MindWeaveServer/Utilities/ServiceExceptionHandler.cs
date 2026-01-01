using MindWeaveServer.Contracts.DataContracts.Shared;
using MindWeaveServer.Resources;
using MindWeaveServer.Utilities.Abstractions;
using NLog;
using System;
using System.Data.Entity.Core;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
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
                return handleEntityException(entityEx, safeContext);
            }

            if (exception is DbUpdateException dbUpdateEx)
            {
                return handleDbUpdateException(dbUpdateEx, safeContext);
            }

            if (exception is SqlException sqlEx)
            {
                return handleSqlException(sqlEx, safeContext);
            }

            if (exception is FileNotFoundException fileNotFoundEx)
            {
                return handleFileNotFoundException(fileNotFoundEx, safeContext);
            }

            if (exception is IOException ioEx)
            {
                return handleIoException(ioEx, safeContext);
            }

            if (exception is TimeoutException timeoutEx)
            {
                return handleTimeoutException(timeoutEx, safeContext);
            }

            if (exception is SocketException socketEx)
            {
                return handleSocketException(socketEx, safeContext);
            }

            if (exception is CommunicationException commEx)
            {
                return handleCommunicationException(commEx, safeContext);
            }

            if (exception is ObjectDisposedException disposedEx)
            {
                return handleObjectDisposedException(disposedEx, safeContext);
            }

            if (exception is InvalidOperationException invalidOpEx)
            {
                return handleInvalidOperationException(invalidOpEx, safeContext);
            }

            if (exception is ArgumentNullException argNullEx)
            {
                return handleArgumentNullException(argNullEx, safeContext);
            }

            if (exception is ArgumentException argEx)
            {
                return handleArgumentException(argEx, safeContext);
            }

            if (exception is UnauthorizedAccessException unauthEx)
            {
                return handleUnauthorizedAccessException(unauthEx, safeContext);
            }

            if (exception is OutOfMemoryException oomEx)
            {
                return handleOutOfMemoryException(oomEx, safeContext);
            }


            logger.Fatal(exception, "UNEXPECTED exception type [{0}] in operation: {1}. Add specific handler!",
                exception.GetType().Name, safeContext);

            return createFault(
                ServiceErrorType.Unknown,
                Lang.GenericServerError,
                "Server",
                "Internal Server Error");
        }

        private FaultException<ServiceFaultDto> handleEntityException(EntityException ex, string context)
        {
            var baseException = ex.GetBaseException();
            if (baseException is SqlException sqlEx)
            {
                return handleSqlException(sqlEx, context);
            }

            logger.Fatal(ex, "Database unavailable (EntityException). Operation: {0}", context);

            return createFault(
                ServiceErrorType.DatabaseError,
                Lang.GenericServerError,
                "Database",
                "Database Error");
        }

        private FaultException<ServiceFaultDto> handleDbUpdateException(DbUpdateException ex, string context)
        {
            var innerSql = ex.InnerException?.InnerException as SqlException;
            if (innerSql != null)
            {
                if (innerSql.Number == 2627 || innerSql.Number == 2601)
                {
                    logger.Warn(ex, "Duplicate record constraint violation. Operation: {0}", context);
                    return createFault(
                        ServiceErrorType.DuplicateRecord,
                        Lang.RegistrationUsernameOrEmailExists,
                        "Constraint",
                        "Duplicate Record");
                }

                if (innerSql.Number == 547)
                {
                    logger.Error(ex, "Foreign key constraint violation. Operation: {0}", context);
                    return createFault(
                        ServiceErrorType.ValidationError,
                        Lang.GenericServerError,
                        "Constraint",
                        "Reference Integrity Error");
                }
            }

            logger.Error(ex, "Database update failed. Operation: {0}", context);
            return createFault(
                ServiceErrorType.DatabaseError,
                Lang.GenericServerError,
                "Database",
                "Database Update Failed");
        }

        private FaultException<ServiceFaultDto> handleSqlException(SqlException ex, string context)
        {
            if (ex.Number == -2 || ex.Number == 53 || ex.Number == -1 || ex.Number == 2 || ex.Number == 0)
            {
                logger.Fatal(ex, "SQL Server connection failed. Operation: {0}", context);
                return createFault(
                    ServiceErrorType.DatabaseError,
                    Lang.GenericServerError,
                    "Database",
                    "Database Connection Failed");
            }

            if (ex.Number == -2)
            {
                logger.Error(ex, "SQL query timeout. Operation: {0}", context);
                return createFault(
                    ServiceErrorType.DatabaseError,
                    Lang.GenericServerError,
                    "Database",
                    "Query Timeout");
            }

            logger.Error(ex, "SQL error (Number: {0}). Operation: {1}", ex.Number, context);
            return createFault(
                ServiceErrorType.DatabaseError,
                Lang.GenericServerError,
                "Database",
                "Database Error");
        }

        private FaultException<ServiceFaultDto> handleFileNotFoundException(FileNotFoundException ex, string context)
        {
            logger.Error(ex, "File not found: {0}. Operation: {1}", ex.FileName, context);

            return createFault(
                ServiceErrorType.NotFound,
                Lang.ErrorPuzzleFileNotFound,
                "FileSystem",
                "Resource Missing");
        }

        private FaultException<ServiceFaultDto> handleIoException(IOException ex, string context)
        {
            logger.Error(ex, "IO error. Operation: {0}", context);

            return createFault(
                ServiceErrorType.Unknown,
                Lang.ErrorReadingPuzzleFile,
                "FileSystem",
                "Storage Error");
        }

        private FaultException<ServiceFaultDto> handleTimeoutException(TimeoutException ex, string context)
        {
            logger.Error(ex, "Operation timed out. Operation: {0}", context);

            return createFault(
                ServiceErrorType.DatabaseError,
                Lang.GenericServerError,
                "Timeout",
                "Service Timeout");
        }

        private FaultException<ServiceFaultDto> handleSocketException(SocketException ex, string context)
        {
            logger.Error(ex, "Socket error (Code: {0}). Operation: {1}", ex.SocketErrorCode, context);

            return createFault(
                ServiceErrorType.CommunicationError,
                Lang.ErrorEmailServiceUnavailable,
                "Network",
                "Network Error");
        }

        private FaultException<ServiceFaultDto> handleCommunicationException(CommunicationException ex, string context)
        {
            logger.Error(ex, "WCF communication error. Operation: {0}", context);

            return createFault(
                ServiceErrorType.CommunicationError,
                Lang.ErrorCommunicationChannelFailed,
                "Communication",
                "Communication Error");
        }

        private FaultException<ServiceFaultDto> handleObjectDisposedException(ObjectDisposedException ex, string context)
        {
            logger.Warn(ex, "Object disposed: {0}. Operation: {1}", ex.ObjectName, context);

            return createFault(
                ServiceErrorType.CommunicationError,
                Lang.ErrorServiceConnectionClosing,
                "Channel",
                "Channel Disposed");
        }

        private FaultException<ServiceFaultDto> handleInvalidOperationException(InvalidOperationException ex, string context)
        {
            if (ex.Message == "DuplicateUser")
            {
                logger.Warn("Duplicate user detected. Operation: {0}", context);
                return createFault(
                    ServiceErrorType.DuplicateRecord,
                    Lang.RegistrationUsernameOrEmailExists,
                    "Username/Email",
                    "Duplicate Record");
            }

            logger.Error(ex, "Invalid operation. Operation: {0}", context);
            return createFault(
                ServiceErrorType.ValidationError,
                Lang.GenericServerError,
                "InvalidOperation",
                "Invalid Operation");
        }

        private FaultException<ServiceFaultDto> handleArgumentNullException(ArgumentNullException ex, string context)
        {
            logger.Error(ex, "Null argument: {0}. Operation: {1}", ex.ParamName, context);

            return createFault(
                ServiceErrorType.ValidationError,
                Lang.ErrorAllFieldsRequired,
                ex.ParamName ?? "Parameter",
                "Missing Required Field");
        }

        private FaultException<ServiceFaultDto> handleArgumentException(ArgumentException ex, string context)
        {
            logger.Error(ex, "Invalid argument: {0}. Operation: {1}", ex.ParamName, context);

            return createFault(
                ServiceErrorType.ValidationError,
                Lang.GenericServerError,
                ex.ParamName ?? "Parameter",
                "Invalid Argument");
        }

        private FaultException<ServiceFaultDto> handleUnauthorizedAccessException(UnauthorizedAccessException ex, string context)
        {
            logger.Error(ex, "Access denied to file/directory. Operation: {0}", context);

            return createFault(
                ServiceErrorType.Unknown,
                Lang.GenericServerError,
                "FileSystem",
                "Access Denied");
        }

        private FaultException<ServiceFaultDto> handleOutOfMemoryException(OutOfMemoryException ex, string context)
        {
            logger.Fatal(ex, "Out of memory processing request. Operation: {0}", context);

            return createFault(
                ServiceErrorType.Unknown,
                "The file is too large to process. Please try with a smaller file.",
                "Memory",
                "Out of Memory");
        }

        private static FaultException<ServiceFaultDto> createFault(
            ServiceErrorType errorType,
            string userMessage,
            string source,
            string faultReason)
        {
            var fault = new ServiceFaultDto(errorType, userMessage, source);
            return new FaultException<ServiceFaultDto>(fault, new FaultReason(faultReason));
        }
    }
}