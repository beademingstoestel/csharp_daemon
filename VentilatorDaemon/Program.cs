using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Mono.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using VentilatorDaemon.Models;
using VentilatorDaemon.Services;
using VentilatorDaemon.Services.Implementations;

namespace VentilatorDaemon
{
    class Program
    {
        private static ILogger logger;

        static async Task<int> Main(string[] args)
        {
            // get environment variables
            var mongoHost = Environment.GetEnvironmentVariable("MONGO_HOST") ?? "localhost";
            var interfaceHost = Environment.GetEnvironmentVariable("INTERFACE_HOST") ?? "localhost";
            var serialPort = Environment.GetEnvironmentVariable("SERIAL_PORT");
            var logDirectory = Environment.GetEnvironmentVariable("LOG_DIRECTORY");
            var dbCollectionPrefix = Environment.GetEnvironmentVariable("DB_COLLECTION_PREFIX") ?? "";
            var interfacePort = Environment.GetEnvironmentVariable("INTERFACE_PORT") ?? "3001";

            // get console line arguments
            var options = new OptionSet {
                { "p|port=", "The name of the serialport", p => serialPort = p ?? serialPort },
                { "i|interface=", "The host running the interface server.", i => interfaceHost = i ?? interfaceHost },
                { "m|mongo=", "The hostname for connecting to the mongo servers", m => mongoHost = m ?? mongoHost },
                { "d|logdirectory=", "The directory to where the daemon writes the log files, if provided", l => logDirectory = l ?? logDirectory },
                { "c|collectionPrefix=", "The prefix to be used in front of the mongo collection name", l => dbCollectionPrefix = l ?? dbCollectionPrefix },
                { "ip|interfacePort=", "The port on which the interface listens", l => interfacePort = l ?? interfacePort },
            };

            try
            {
                // parse the command line
                options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.WriteLine("Error while trying to parse the options: " + e.Message);
                return -1;
            }

            int interfacePortInt = 3001;
            int.TryParse(interfacePort, out interfacePortInt);

            var programSettings = new ProgramSettings()
            {
                DatabaseHost = mongoHost,
                SerialPort = serialPort,
                WebServerHost = interfaceHost,
                LogDirectory = logDirectory,
                WebServerPort = interfacePortInt,
                DbCollectionPrefix = dbCollectionPrefix,
            };

            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole().AddFilter(level => level >= LogLevel.Debug))
                .AddSingleton(programSettings)
                .AddTransient<IApiService, ApiService>()
                .AddTransient<IDbService, DbService>()
                .AddSingleton<AlarmThread>()
                .AddSingleton<SerialThread>()
                .AddSingleton<WebSocketThread>()
                .AddSingleton<ProcessingThread>()
                .BuildServiceProvider();
            logger = serviceProvider.GetService<ILoggerFactory>().CreateLogger<Program>();

            logger.LogInformation("Starting daemon {0} with version: {1}", 
                Assembly.GetEntryAssembly().GetName().Name, 
                Assembly.GetEntryAssembly().GetName().Version);

            //wait for mongo to be available
            await CheckMongoAvailibility(programSettings);
            await CheckWebServerAvailibility(programSettings);

            GeneralConfiguration();

            logger.LogInformation("Starting daemon");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            Console.CancelKeyPress += delegate {
                // call methods to clean up
                cancellationTokenSource.Cancel();
            };

            AlarmThread alarmThread = serviceProvider.GetService<AlarmThread>();
            SerialThread serialThread = serviceProvider.GetService<SerialThread>();
            WebSocketThread webSocketThread = serviceProvider.GetService<WebSocketThread>();
            ProcessingThread processingThread = serviceProvider.GetService<ProcessingThread>();

            if (string.IsNullOrEmpty(serialPort))
            {
                serialThread.SetPortName(cancellationToken);
            }
            else
            {
                serialThread.SetPortName(serialPort);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                var alarmTask = alarmThread.Start(cancellationToken);
                var webSocketTask = webSocketThread.Start(cancellationToken);
                var serialTask = serialThread.Start(cancellationToken);
                var processingTask = processingThread.Start(cancellationToken);

                Task.WaitAll(webSocketTask, serialTask, processingTask, alarmTask);
            }
            
            // as there is currently no way to cancel the tokens, we should never get here
            logger.LogInformation("Daemon finished");
            return 0;
        }

        static void GeneralConfiguration()
        {
            FlurlHttp.Configure(settings => {
                var jsonSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                settings.JsonSerializer = new NewtonsoftJsonSerializer(jsonSettings);
            });

            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
        }

        static async Task CheckMongoAvailibility(ProgramSettings programSettings)
        {
            bool foundMongo = false;

            while (!foundMongo)
            {
                try
                {
                    var client = new MongoClient($"mongodb://{programSettings.DatabaseHost}:27017/?connect=direct;replicaSet=rs0;readPreference=primaryPreferred");
                    var database = client.GetDatabase(programSettings.GetDatabaseName());

                    await database.ListCollectionsAsync();

                    foundMongo = true;
                }
                catch(Exception e)
                {
                    logger.LogInformation("Waiting for the mongo server to become available");
                    await Task.Delay(1000);
                }
            } 
        }

        static async Task CheckWebServerAvailibility(ProgramSettings programSettings)
        {
            bool foundServer = false;
            FlurlClient flurlClient = new FlurlClient($"http://{programSettings.WebServerHost}:{programSettings.WebServerPort}");
            
            while (!foundServer)
            {
                try
                {
                    var version = Assembly.GetEntryAssembly().GetName().Version;

                    Dictionary<string, string> dict = new Dictionary<string, string>();
                    dict.Add("DAEMON_VERSION", version.ToString());

                    await flurlClient.Request("/api/settings")
                        .PutJsonAsync(dict);

                    foundServer = true;
                }
                catch (Exception e)
                {
                    logger.LogInformation("Waiting for the webserver to become available");
                    await Task.Delay(1000);
                }
            }
        }
    }
}
