using Flurl.Http;
using Flurl.Http.Configuration;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // get environment variables
            var mongoHost = Environment.GetEnvironmentVariable("MONGO_HOST") ?? "localhost";
            var interfaceHost = Environment.GetEnvironmentVariable("INTERFACE_HOST") ?? "localhost";
            var serialPort = Environment.GetEnvironmentVariable("SERIAL_PORT");

            //wait for mongo to be available
            await CheckMongoAvailibility(mongoHost);
            await CheckWebServerAvailibility(interfaceHost);

            GeneralConfiguration();

            Console.WriteLine("Starting daemon");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            AlarmThread alarmThread = new AlarmThread();
            SerialThread serialThread = new SerialThread(mongoHost, interfaceHost, alarmThread);
            WebSocketThread webSocketThread = new WebSocketThread($"ws://{interfaceHost}:3001", serialThread, alarmThread);
            ProcessingThread processingThread = new ProcessingThread(serialThread, webSocketThread, alarmThread, mongoHost, interfaceHost);

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

            Console.WriteLine("Daemon finished");
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
                    Console.WriteLine("Got an error waiting for mongo");
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
                    Console.WriteLine("Got an error waiting for webserver");
                    await Task.Delay(1000);
                }
            }
        }
    }
}
