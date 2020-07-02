using Akka.Util;
using Docker.DotNet;
using Docker.DotNet.Models;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PersistenceExample.Internal
{
    [Flags]
    public enum ContainerStatus
    {
        None,

        Attach,
        Commit,
        Copy,
        Create,
        Destroy,
        Detach,
        Die,

        ExecCreate,
        ExecDetach,
        ExecStart,

        Export,
        HealthStatus,
        Kill,
        OOM,
        Pause,
        Rename,
        Resize,
        Restart,
        Start,
        Stop,
        Top,
        Unpause,
        Update
    }

    public enum ImageStatus
    {
        Delete,
        Import,
        Load,
        Pull,
        Push,
        Save,
        Tag,
        Untag
    }

    public enum PluginStatus
    {
        Enable,
        Disable,
        Install,
        Remove
    }

    public enum VolumeStatus
    {
        Create,
        Destroy,
        Mount,
        Unmount
    }

    public enum NetworkStatus
    {
        None,

        Create,
        Connect,
        Destroy,
        Disconnect,
        Remove
    }

    public delegate void DockerStatusDelegate(ContainerStatus status);
    public delegate void NetworkStatusDelegate(NetworkStatus status);

    internal class DockerManager
    {
        private static readonly string _sqlContainerName = $"sqlserver-{Guid.NewGuid():N}";
        private static int _sqlServerHostPort;
        private static DockerClient _client;

        public string ConnectionString { get; private set; }
        public ContainerStatus ContainerStatus { get; private set; }
        public DockerStatusDelegate OnContainerStatusChanged { get; set; }
        public NetworkStatusDelegate OnNetworkStatusChanged { get; set; }

        private static string SqlServerImageName
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return "microsoft/mssql-server-windows-express";
                return "mcr.microsoft.com/mssql/server";
            }
        }

        public DockerManager()
        {
            Console.WriteLine("----- Creating DockerClient");
            DockerClientConfiguration config;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                config = new DockerClientConfiguration(new Uri("unix://var/run/docker.sock"));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                config = new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine"));
            else
                throw new NotSupportedException($"Unsupported OS [{RuntimeInformation.OSDescription}]");

            _client = config.CreateClient();

        }

        private CancellationTokenSource _monitoringCts;
        private CancellationToken _token;
        private Thread _monitoringThread;
        private Stream _stream;
        private bool _monitoring;

        private void InvokeContainerEvent(ContainerStatus status)
            => OnContainerStatusChanged?.Invoke(status);

        private void InvokeNetworkEvent(NetworkStatus status)
            => OnNetworkStatusChanged?.Invoke(status);

        public async Task Start()
        {
            _monitoringCts = new CancellationTokenSource();
            _token = _monitoringCts.Token;

            _stream = await _client.System.MonitorEventsAsync( new ContainerEventsParameters(), default);

            _monitoring = true;
            _monitoringThread = new Thread(() =>
            {
                while(_monitoring)
                {
                    MonitorStreamForMessagesAsync(_stream, _progress, _token);
                }
            });
            _monitoringThread.Start();

            var images = await _client.Images.ListImagesAsync(new ImagesListParameters { MatchName = SqlServerImageName });
            if (images.Count == 0)
                await _client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = SqlServerImageName, Tag = "latest" }, null,
                    new Progress<JSONMessage>(message =>
                    {
                        Console.WriteLine(!string.IsNullOrEmpty(message.ErrorMessage)
                            ? message.ErrorMessage
                            : $"{message.ID} {message.Status} {message.ProgressMessage}");
                    }));

            _sqlServerHostPort = ThreadLocalRandom.Current.Next(9000, 10000);

            // create the container
            try
            {
                await _client.Containers.CreateContainerAsync(new CreateContainerParameters
                {
                    Image = SqlServerImageName,
                    Name = _sqlContainerName,
                    Tty = true,
                    ExposedPorts = new Dictionary<string, EmptyStruct>
                    {
                        {"1433/tcp", new EmptyStruct()}
                    },
                    HostConfig = new HostConfig
                    {
                        PortBindings = new Dictionary<string, IList<PortBinding>>
                        {
                            {
                                "1433/tcp",
                                new List<PortBinding>
                                {
                                    new PortBinding
                                    {
                                        HostPort = $"{_sqlServerHostPort}"
                                    }
                                }
                            }
                        }
                    },
                    Env = new[] { "ACCEPT_EULA=Y", "SA_PASSWORD=l0lTh1sIsOpenSource" }
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }

            var connectionString = new DbConnectionStringBuilder
            {
                ConnectionString =
                    "data source=.;database=akka_persistence_tests;user id=sa;password=l0lTh1sIsOpenSource"
            };
            connectionString["Data Source"] = $"localhost,{_sqlServerHostPort}";
            ConnectionString = connectionString.ToString();

            Console.WriteLine($"Connection string: {ConnectionString}");

            await StartContainer();

            await UntilContainerStatusIs(ContainerStatus.ExecStart);
            // Provide a 10 second startup delay
            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        public async Task UntilContainerStatusIs(ContainerStatus status)
        {
            var triggered = (status & ContainerStatus) != 0;

            DockerStatusDelegate statusListener = s =>
            {
                if (!triggered && (status & s) != 0)
                    triggered = true;
            };

            OnContainerStatusChanged += statusListener;
            while (true)
            {
                if (triggered)
                    break;
                await Task.Delay(100);
            }
            OnContainerStatusChanged -= statusListener;
        }

        public async Task Stop()
        {
            Console.WriteLine("----- Stopping docker containers.");
            await StopContainer();
            await UntilContainerStatusIs(ContainerStatus.Stop);

            await _client.Containers.RemoveContainerAsync(_sqlContainerName,
                new ContainerRemoveParameters { Force = true });
            await UntilContainerStatusIs(ContainerStatus.Destroy);

            Console.WriteLine("----- Stopping docker event monitoring.");
            _monitoringCts.Cancel();
            _monitoring = false;
            _monitoringThread.Join(1000);
            _client.Dispose();
        }

        public async Task<bool> StopContainer()
        {
            return await _client.Containers.StopContainerAsync(_sqlContainerName, new ContainerStopParameters());
            /*
            if (!result) return result;
            await UntilContainerStatusIs(ContainerStatus.Stop);
            return result;
            */
        }

        public async Task<bool> StartContainer()
        {
            // start the container
            return await _client.Containers.StartContainerAsync(_sqlContainerName, new ContainerStartParameters());
            /*
            if (!result) return result;
            await UntilContainerStatusIs(ContainerStatus.Start | ContainerStatus.ExecStart);
            return result;
            */
        }

        private IProgress<StatusEventBase> _progress => new Progress<StatusEventBase>(msg => {
            string output = null;
            string enumName;
            switch(msg)
            {
                case ContainerStatusEvent e:
                    output = $"[Docker][{msg.Time}][Container][{e.From}][{e.Action}] Status:{e.Status}";
                    switch (e.Status)
                    {
                        case string s when s.StartsWith("exec_create"):
                            ContainerStatus = ContainerStatus.ExecCreate;
                            InvokeContainerEvent(ContainerStatus);
                            break;
                        case string s when s.StartsWith("exec_detach"):
                            ContainerStatus = ContainerStatus.ExecDetach;
                            InvokeContainerEvent(ContainerStatus);
                            break;
                        case string s when s.StartsWith("exec_start"):
                            ContainerStatus = ContainerStatus.ExecStart;
                            InvokeContainerEvent(ContainerStatus);
                            break;
                        default:
                            enumName = e.Action.Substring(0, 1).ToUpper() + e.Action.Substring(1);
                            Enum.TryParse<ContainerStatus>(enumName, out var containerEnum);
                            ContainerStatus = containerEnum;
                            InvokeContainerEvent(containerEnum);
                            break;
                    }
                    Console.WriteLine(output);
                    break;
                case NetworkStatusEvent e:
                    output = $"[Docker][{msg.Time}][Network][{e.Action}]";
                    enumName = e.Action.Substring(0, 1).ToUpper() + e.Action.Substring(1);
                    Enum.TryParse<NetworkStatus>(enumName, out var netEnum);
                    InvokeNetworkEvent(netEnum);
                    Console.WriteLine(output);
                    break;
            }
            /*
            if (string.IsNullOrEmpty(msg.ErrorMessage))
                output =
                    $"[DEBUG][{msg.Time}][Docker][From:{msg.From}] Status:{msg.Status}";
            else
                output =
                    $"[ERROR][{msg.Time}][Docker][{msg.From}] {msg.ErrorMessage}{Environment.NewLine}Cause: {msg.Error}";
            */

        });

        private JsonSerializer _serializer = new JsonSerializer();
        private int _startObjectCount = 0;
        private readonly StringBuilder _buffer = new StringBuilder();
        internal void MonitorStreamForMessagesAsync(
            Stream stream,
            IProgress<StatusEventBase> progress,
            CancellationToken token)
        {
            if (!stream.CanRead) return;

            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            var ch = new char[1];
            using (token.Register(() => { 
                reader.Dispose();
                stream.Dispose();
            }))
            {
                try
                {
                    while (reader.ReadAsync(ch, 0, 1).ConfigureAwait(false).GetAwaiter().GetResult() == 1)
                    {
                        _buffer.Append(ch[0]);
                        switch (ch[0])
                        {
                            case '}':
                                {
                                    _startObjectCount--;
                                    if (_startObjectCount == 0)
                                    {
                                        var str = _buffer.ToString();
                                        _buffer.Clear();

                                        var json = JObject.Parse(str);
                                        var type = json.GetValue("Type").ToString();

                                        StatusEventBase prog = null;
                                        switch (type)
                                        {
                                            case "container":
                                                prog = _serializer.DeserializeObject<ContainerStatusEvent>(str);
                                                break;
                                            case "network":
                                                prog = _serializer.DeserializeObject<NetworkStatusEvent>(str);
                                                break;
                                        }
                                        prog.Raw = str;

                                        //Console.WriteLine(str);
                                        //if (prog == null) continue;
                                        progress.Report(prog);
                                    }
                                    break;
                                }
                            case '{':
                                _startObjectCount++;
                                break;
                        }
                    }
                } catch
                {

                }
            }
        }
    }

    public abstract class StatusEventBase
    {
        [DataMember(Name = "scope", EmitDefaultValue = false)]
        public string Scope { get; set; }
        [DataMember(Name = "time", EmitDefaultValue = false)]
        public DateTime Time { get; set; }
        [DataMember(Name = "timeNano", EmitDefaultValue = false)]
        public long TimeNano { get; set; }
        [DataMember(Name = "Actor", EmitDefaultValue = false)]
        public Actor Actor { get; set; }
        public string Raw { get; set; }
    }

    public class ContainerStatusEvent : StatusEventBase
    {
        [DataMember(Name = "status", EmitDefaultValue = false)]
        public string Status { get; set; }

        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        [DataMember(Name = "Action", EmitDefaultValue = false)]
        public string Action { get; set; }

        [DataMember(Name = "from", EmitDefaultValue = false)]
        public string From { get; set; }
    }

    public class NetworkStatusEvent : StatusEventBase
    {
        [DataMember(Name = "Action", EmitDefaultValue = false)]
        public string Action { get; set; }

    }

    public class Actor
    {
        [DataMember(Name = "ID", EmitDefaultValue = false)]
        public string Id { get; set; }

        [DataMember(Name = "Attributes", EmitDefaultValue = false)]
        public Dictionary<string, string> Attributes { get; set; }
    }
}
