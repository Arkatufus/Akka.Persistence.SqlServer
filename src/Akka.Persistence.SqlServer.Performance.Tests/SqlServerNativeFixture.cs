// -----------------------------------------------------------------------
// <copyright file="SqlServerFixture.cs" company="Akka.NET Project">
//      Copyright (C) 2013 - 2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Util;
using Docker.DotNet;
using Docker.DotNet.Models;
using Xunit;
using Xunit.Sdk;

namespace Akka.Persistence.SqlServer.Performance.Tests
{
    [CollectionDefinition(nameof(SqlServerSpecsNativeFixture))]
    public sealed class SqlServerSpecsNativeFixture : ICollectionFixture<SqlServerNativeFixture>
    {
    }

    /// <summary>
    ///     Fixture used to run SQL Server
    /// </summary>
    public class SqlServerNativeFixture
    {
        public SqlServerNativeFixture()
        {
            var connectionString = new DbConnectionStringBuilder
            {
                ["Server"] = "localhost",
                ["Database"] = "akka_persistence_tests",
                ["User Id"] = "sa",
                ["Password"] = "l0l!Th1sIsOpenSource",
                ["TrustServerCertificate"] = "true",
            };

            ConnectionString = connectionString.ToString();
            Console.WriteLine($"Connection string: [{ConnectionString}]");
        }

        public string ConnectionString { get; }
    }
}