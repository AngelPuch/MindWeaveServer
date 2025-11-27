using System;
using System.Data.Entity.Core.EntityClient;
using System.Data.SqlClient;

namespace MindWeaveServer.Utilities
{
    public static class SecureConnection
    {
        public static string getConnectionString()
        {
            string dbUser = Environment.GetEnvironmentVariable("MINDWEAVE_DB_USER");
            string dbPass = Environment.GetEnvironmentVariable("MINDWEAVE_DB_PASS");
            
            if (string.IsNullOrEmpty(dbUser) || string.IsNullOrEmpty(dbPass))
            {
                throw new Exception("FATAL SECURITY ERROR: Database credentials not found in Environment Variables.");
            }

            SqlConnectionStringBuilder sqlBuilder = new SqlConnectionStringBuilder
            {
                DataSource = @".\SQLEXPRESS",
                InitialCatalog = "MindWeaveDB",
                UserID = dbUser,
                Password = dbPass,
                MultipleActiveResultSets = true,
                PersistSecurityInfo = true,
                ApplicationName = "EntityFramework",
                Encrypt = false,
                TrustServerCertificate = true
            };

            EntityConnectionStringBuilder entityBuilder = new EntityConnectionStringBuilder
            {
                Provider = "System.Data.SqlClient",
                ProviderConnectionString = sqlBuilder.ToString(),
                Metadata = "res://*/DataAccess.MindWeaveDB.csdl|res://*/DataAccess.MindWeaveDB.ssdl|res://*/DataAccess.MindWeaveDB.msl"
            };

            return entityBuilder.ToString();
        }
    }
}