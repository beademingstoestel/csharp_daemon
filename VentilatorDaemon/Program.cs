using Flurl.Http;
using Flurl.Http.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
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

        static async Task Main(string[] args)
        {
            // get environment variables
            var mongoHost = Environment.GetEnvironmentVariable("MONGO_HOST") ?? "localhost";
            var interfaceHost = Environment.GetEnvironmentVariable("INTERFACE_HOST") ?? "localhost";
            var serialPort = Environment.GetEnvironmentVariable("SERIAL_PORT");

            var serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConsole().AddFilter(level => level >= LogLevel.Debug))
                .AddSingleton<ProgramSettings>(new ProgramSettings()
                {
                    DatabaseHost = mongoHost,
                    SerialPort = serialPort,
                    WebServerHost = interfaceHost,
                })
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
            await CheckMongoAvailibility(mongoHost);
            await CheckWebServerAvailibility(interfaceHost);

            GeneralConfiguration();

            logger.LogInformation("Starting daemon");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;


            AlarmThread alarmThread = serviceProvider.GetService<AlarmThread>();
            SerialThread serialThread = serviceProvider.GetService<SerialThread>();
            WebSocketThread webSocketThread = serviceProvider.GetService<WebSocketThread>();
            ProcessingThread processingThread = serviceProvider.GetService<ProcessingThread>();

            if (string.IsNullOrEmpty(serialPort))
            {
                serialThread.SetPortName();
            }
            else
            {
                serialThread.SetPortName(serialPort);
            }

            var alarmTask = alarmThread.Start(cancellationToken);
            var webSocketTask = webSocketThread.Start(cancellationToken);
            var serialTask = serialThread.Start(cancellationToken);
            var processingTask = processingThread.Start(cancellationToken);

            Task.WaitAll(webSocketTask, serialTask, processingTask, alarmTask);
            
            // as there is currently no way to cancel the tokens, we should never get here
            logger.LogInformation("Daemon finished");
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

        static async Task CheckMongoAvailibility(string databaseHost)
        {
            bool foundMongo = false;

            while (!foundMongo)
            {
                try
                {
                    var client = new MongoClient($"mongodb://{databaseHost}:27017/?connect=direct;replicaSet=rs0;readPreference=primaryPreferred");
                    var database = client.GetDatabase("beademing");

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

        static async Task CheckWebServerAvailibility(string webServerHost)
        {
            bool foundServer = false;
            FlurlClient flurlClient = new FlurlClient($"http://{webServerHost}:3001");
            
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
