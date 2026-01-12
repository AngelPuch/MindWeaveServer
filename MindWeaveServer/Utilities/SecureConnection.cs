using System;
using System.Data.Entity.Core.EntityClient;
using System.Data.SqlClient;

namespace MindWeaveServer.Utilities
{
    public static class SecureConnection
    {
        private const string VAR_DB_SOURCE = "MINDWEAVE_DB_SOURCE";
        private const string VAR_DB_CATALOG = "MINDWEAVE_DB_CATALOG";
        private const string VAR_DB_USER = "MINDWEAVE_DB_USER";
        private const string VAR_DB_PASS = "MINDWEAVE_DB_PASS";
        private const string VAR_EF_METADATA = "MINDWEAVE_EF_METADATA";
        private const string PROVIDER_NAME = "System.Data.SqlClient";
        private const string APP_NAME = "EntityFramework";

        public static string getConnectionString()
        {
            string dbSource = Environment.GetEnvironmentVariable(VAR_DB_SOURCE);
            string dbCatalog = Environment.GetEnvironmentVariable(VAR_DB_CATALOG);
            string dbUser = Environment.GetEnvironmentVariable(VAR_DB_USER);
            string dbPass = Environment.GetEnvironmentVariable(VAR_DB_PASS);
            string efMetadata = Environment.GetEnvironmentVariable(VAR_EF_METADATA);

            if (string.IsNullOrEmpty(dbSource) ||
                string.IsNullOrEmpty(dbCatalog) ||
                string.IsNullOrEmpty(dbUser) ||
                string.IsNullOrEmpty(dbPass) ||
                string.IsNullOrEmpty(efMetadata))
            {
                throw new InvalidOperationException("FATAL SECURITY ERROR: One or more database configuration variables are missing in the Environment.");
            }
        

            SqlConnectionStringBuilder sqlBuilder = new SqlConnectionStringBuilder
            {
                DataSource = dbSource,
                InitialCatalog = dbCatalog,
                UserID = dbUser,
                Password = dbPass,
                MultipleActiveResultSets = true,
                PersistSecurityInfo = true,
                ApplicationName = APP_NAME,
                Encrypt = false,
                TrustServerCertificate = true
            };

            EntityConnectionStringBuilder entityBuilder = new EntityConnectionStringBuilder
            {
                Provider = PROVIDER_NAME,
                ProviderConnectionString = sqlBuilder.ToString(),
                Metadata = efMetadata
            };

            return entityBuilder.ToString();
        }
    }
}