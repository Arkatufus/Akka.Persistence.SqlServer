// -----------------------------------------------------------------------
// <copyright file="DbUtils.cs" company="Akka.NET Project">
//      Copyright (C) 2013 - 2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.Data.SqlClient;

namespace Akka.Persistence.SqlServer.Performance.Tests
{
    public static class DbUtils
    {
        private static SqlConnectionStringBuilder _builder;
        public static string ConnectionString => _builder.ToString();

        public static void Initialize(string connectionString)
        {
            _builder = new SqlConnectionStringBuilder(connectionString);
            var databaseName = _builder.InitialCatalog;
            if(string.IsNullOrWhiteSpace(databaseName))
            {
                _builder.InitialCatalog = databaseName = $"akka_persistence_tests_{Guid.NewGuid()}";
            }
            
            var connectionBuilder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master"
            };

            using (var conn = new SqlConnection(connectionBuilder.ToString()))
            {
                conn.Open();

                using (var cmd = new SqlCommand())
                {
                    cmd.CommandText = 
                        $"""
                        IF db_id('{databaseName}') IS NOT NULL
                        BEGIN
                           ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                           DROP DATABASE [{databaseName}];
                        END
                        """;
                    cmd.Connection = conn;
                    cmd.ExecuteScalar();
                }

                using (var cmd = new SqlCommand())
                {
                    cmd.CommandText = $"CREATE DATABASE [{databaseName}];";
                    cmd.Connection = conn;
                    cmd.ExecuteScalar();
                }
            }

            // Delete local snapshot flat file database
            const string path = "./snapshots";
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }

        public static void Clean()
        {
            var databaseName = _builder.InitialCatalog;
            if(string.IsNullOrWhiteSpace(databaseName))
            {
                _builder.InitialCatalog = databaseName = $"akka_persistence_tests_{Guid.NewGuid()}";
            }
            
            var connectionBuilder = new SqlConnectionStringBuilder(ConnectionString)
            {
                InitialCatalog = "master"
            };

            using (var conn = new SqlConnection(connectionBuilder.ToString()))
            {
                conn.Open();

                using (var cmd = new SqlCommand())
                {
                    cmd.CommandText = 
                        $"""
                         IF db_id('{databaseName}') IS NOT NULL
                         BEGIN
                            ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                            DROP DATABASE [{databaseName}];
                         END
                         """;
                    cmd.Connection = conn;
                    cmd.ExecuteScalar();
                }

                using (var cmd = new SqlCommand())
                {
                    cmd.CommandText = $"CREATE DATABASE [{databaseName}];";
                    cmd.Connection = conn;
                    cmd.ExecuteScalar();
                }
            }

            // Delete local snapshot flat file database
            const string path = "./snapshots";
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
    }
}