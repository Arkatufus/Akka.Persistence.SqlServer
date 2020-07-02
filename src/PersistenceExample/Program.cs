//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using PersistenceExample.Internal;

namespace PersistenceExample
{
    class Program
    {

        private static Config InitConfig(DockerManager manager)
        {
            //need to make sure db is created before the tests start
            DbUtils.Initialize(manager.ConnectionString);
            var specString = $@"
akka.loglevel = DEBUG
akka.stdout-loglevel = DEBUG
akka.persistence {{
    publish-plugin-commands = on
    journal {{
        plugin = ""akka.persistence.journal.sql-server""
        sql-server {{
            class = ""Akka.Persistence.SqlServer.Journal.BatchingSqlServerJournal, Akka.Persistence.SqlServer""
            recovery-event-timeout = 60s
            schema-name = dbo
            table-name = EventJournal
            auto-initialize = on
            connection-string = ""{DbUtils.ConnectionString}""
        }}
    }}
    snapshot-store {{
        plugin = ""akka.persistence.snapshot - store.sql - server""
        sql-server {{
            class = ""Akka.Persistence.SqlServer.Snapshot.SqlServerSnapshotStore, Akka.Persistence.SqlServer""
            serializer = protobuf
            schema-name = dbo
            table-name = SnapshotStore
            auto-initialize = on
            connection-string = ""{DbUtils.ConnectionString}""
        }}
    }}
}}";

            return ConfigurationFactory.ParseString(specString);
        }

        static async Task Main(string[] args)
        {
            var docker = new DockerManager();

            Console.WriteLine("----- Initializing MSSQL docker client.");
            await docker.Start();
            await Task.Delay(10000);

            Console.WriteLine("----- Starting ActorSystem");
            var config = InitConfig(docker);
            var system = ActorSystem.Create("example", config);
            var pref = system.ActorOf(Props.Create<SnapshotedExampleActor>(), "snapshoted-actor");

            //SqlServerPersistence.Init(system);

            //BasicUsage(system);

            //FailingActorExample(system);

            SnapshotedActor(pref);

            //ViewExample(system);

            // AtLeastOnceDelivery(system);
            var container = true;
            var akka = true;
            var running = true;
            while(running)
            {
                Console.WriteLine("----- Commands:");
                Console.WriteLine($"      ( 1 ) Start/stop database docker. Current: {(container?"running":"stopped")}");
                Console.WriteLine($"      ( 2 ) Start/stop akka ActorSystem. Current: {(akka?"running":"stopped")}");
                Console.WriteLine("      ( 3 ) Kill actor.");
                Console.WriteLine("      ( 4 ) Quit.");
                var input = Console.ReadKey();
                switch (input.Key)
                {
                    case ConsoleKey.D1:
                        if (container)
                        {
                            await docker.StopContainer();
                        }
                        else
                        {
                            await docker.StartContainer();
                        }
                        container = !container;
                        break;
                    case ConsoleKey.D2:
                        if(akka)
                        {
                            await system.Terminate();
                        } else
                        {
                            system = ActorSystem.Create("example", config);
                            pref = system.ActorOf(Props.Create<SnapshotedExampleActor>(), "snapshoted-actor");
                        }
                        akka = !akka;
                        break;
                    case ConsoleKey.D3:
                        system.Stop(pref);
                        break;
                    case ConsoleKey.D4:
                        running = false;
                        break;
                }
            }

/*
            Console.WriteLine("----- Ready, press any key to simulate a database failure.");
            Console.ReadLine();

            await docker.StopContainer();
            await system.Terminate();
            
            system = ActorSystem.Create("example", config);
            pref = system.ActorOf(Props.Create<SnapshotedExampleActor>(), "snapshoted-actor");

            Console.WriteLine("----- Docker container stopped. Press any key to start container again.");
            Console.ReadLine();

            await docker.StartContainer();
            SnapshotedActor(pref);

            Console.WriteLine("----- Ready.");
            Console.ReadLine();

            await docker.Stop();
            Console.WriteLine("----- DONE");
*/
        }

        private static void AtLeastOnceDelivery(ActorSystem system)
        {
            Console.WriteLine("\n--- AT LEAST ONCE DELIVERY EXAMPLE ---\n");
            var delivery = system.ActorOf(Props.Create(()=> new DeliveryActor()),"delivery");

            var deliverer = system.ActorOf(Props.Create(() => new AtLeastOnceDeliveryExampleActor(delivery.Path)));
            delivery.Tell("start");
            deliverer.Tell(new Message("foo"));
            

            System.Threading.Thread.Sleep(1000); //making sure delivery stops before send other commands
            delivery.Tell("stop");

            deliverer.Tell(new Message("bar"));

            Console.WriteLine("\nSYSTEM: Throwing exception in Deliverer\n");
            deliverer.Tell("boom");
            System.Threading.Thread.Sleep(1000);

            deliverer.Tell(new Message("bar1"));
            Console.WriteLine("\nSYSTEM: Enabling confirmations in 3 seconds\n");

            System.Threading.Thread.Sleep(3000);
            Console.WriteLine("\nSYSTEM: Enabled confirmations\n");
            delivery.Tell("start");
            
        }

        private static void ViewExample(ActorSystem system)
        {
            Console.WriteLine("\n--- PERSISTENT VIEW EXAMPLE ---\n");
            var pref = system.ActorOf(Props.Create<ViewExampleActor>());
            var view = system.ActorOf(Props.Create<ExampleView>());

            system.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromSeconds(2), pref, "scheduled", ActorRefs.NoSender);
            system.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, TimeSpan.FromSeconds(5), view, "snap", ActorRefs.NoSender);
        }

        private static void SnapshotedActor(IActorRef pref)
        {
            // send two messages (a, b) and persist them
            pref.Tell("a");
            pref.Tell("b");

            // make a snapshot: a, b will be stored in durable memory
            pref.Tell("snap");

            // send next two messages - those will be cleared, since MemoryJournal is not "persistent"
            pref.Tell("c");
            pref.Tell("d");

            // print internal actor's state
            pref.Tell("print");

            // result after first run should be like:

            // Current actor's state: d, c, b, a

            // after second run:

            // Offered state (from snapshot): b, a      - taken from the snapshot
            // Current actor's state: d, c, b, a, b, a  - 2 last messages loaded from the snapshot, rest send in this run

            // after third run:

            // Offered state (from snapshot): b, a, b, a        - taken from the snapshot
            // Current actor's state: d, c, b, a, b, a, b, a    - 4 last messages loaded from the snapshot, rest send in this run

            // etc...
        }

        private static void FailingActorExample(ActorSystem system)
        {
            Console.WriteLine("\n--- FAILING ACTOR EXAMPLE ---\n");
            var pref = system.ActorOf(Props.Create<ExamplePersistentFailingActor>(), "failing-actor");

            pref.Tell("a");
            pref.Tell("print");
            // restart and recovery
            pref.Tell("boom");
            pref.Tell("print");
            pref.Tell("b");
            pref.Tell("print");
            pref.Tell("c");
            pref.Tell("print");

            // Will print in a first run (i.e. with empty journal):

            // Received: a
            // Received: a, b
            // Received: a, b, c
        }

        private static void BasicUsage(ActorSystem system)
        {
            Console.WriteLine("\n--- BASIC EXAMPLE ---\n");

            // create a persistent actor, using LocalSnapshotStore and MemoryJournal
            var aref = system.ActorOf(Props.Create<ExamplePersistentActor>(), "basic-actor");

            // all commands are stacked in internal actor's state as a list
            aref.Tell(new Command("foo"));
            aref.Tell(new Command("baz"));
            aref.Tell(new Command("bar"));

            // save current actor state using LocalSnapshotStore (it will be serialized and stored inside file on example bin/snapshots folder)
            aref.Tell("snap");

            // add one more message, this one is not snapshoted and won't be persisted (because of MemoryJournal characteristics)
            aref.Tell(new Command("buzz"));

            // print current actor state
            aref.Tell("print");

            // on first run displayed state should be: 
            
            // buzz-3, bar-2, baz-1, foo-0 
            // (numbers denotes current actor's sequence numbers for each stored event)

            // on the second run: 

            // buzz-6, bar-5, baz-4, foo-3, bar-2, baz-1, foo-0
            // (sequence numbers are continuously increasing taken from last snapshot, 
            // also buzz-3 event isn't present since it's has been called after snapshot request,
            // and MemoryJournal will destroy stored events on program stop)

            // on next run's etc...
        }

    }
}

