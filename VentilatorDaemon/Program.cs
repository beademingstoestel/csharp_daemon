using Flurl.Http;
using Flurl.Http.Configuration;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    class Program
    {
        static async Task Main(string[] args)
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
            Console.WriteLine("Starting daemon");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            SerialThread serialThread = new SerialThread();
            //await serialThread.SendSettingToServer("DAEMON_VERSION", 1.0f);

            WebSocketThread webSocketThread = new WebSocketThread("ws://localhost:3001", serialThread);
            ProcessingThread processingThread = new ProcessingThread(serialThread, webSocketThread);

            serialThread.SetPortName();

            var webSocketTask = webSocketThread.Start(cancellationToken);
            var serialTask = serialThread.Start(cancellationToken);
            var processingTask = processingThread.Start(cancellationToken);

            Task.WaitAll(webSocketTask, serialTask, processingTask);

            Console.WriteLine("Daemon finished");
        }
    }
}
