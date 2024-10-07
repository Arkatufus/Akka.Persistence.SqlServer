// -----------------------------------------------------------------------
// <copyright file="BatchingSqlServerJournalPerfSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013 - 2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.TestKit.Performance;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.SqlServer.Performance.Tests
{
    [Collection(nameof(SqlServerSpecsNativeFixture))]
    public class BatchingSqlServerJournalPerfSpec : SqlJournalPerfSpec
    {
        public BatchingSqlServerJournalPerfSpec(ITestOutputHelper output, SqlServerNativeFixture fixture)
            : base(InitConfig(fixture), "BatchingSqlServerJournalPerfSpec", output)
        {
            EventsCount = 10000;
            ExpectDuration = TimeSpan.FromMinutes(10);
            MeasurementIterations = 100;
        }

        private static Config InitConfig(SqlServerNativeFixture fixture)
        {
            //need to make sure db is created before the tests start
            DbUtils.Initialize(fixture.ConnectionString);

            var specString = $@"
                akka.persistence {{
                    publish-plugin-commands = on
                    journal {{
                        plugin = ""akka.persistence.journal.sql-server""
                        sql-server {{
                            class = ""Akka.Persistence.SqlServer.Journal.BatchingSqlServerJournal, Akka.Persistence.SqlServer""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            table-name = EventJournal
                            schema-name = dbo
                            auto-initialize = on
                            connection-string = ""{DbUtils.ConnectionString}""
                        }}
                    }}
                }}";

            return ConfigurationFactory.ParseString(specString);
        }

        protected override void AfterAll()
        {
            base.AfterAll();
            DbUtils.Clean();
        }
    }
}