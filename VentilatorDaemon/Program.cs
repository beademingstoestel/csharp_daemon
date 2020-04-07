using System;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
            Console.WriteLine("Starting daemon");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            SerialThread serialThread = new SerialThread();
            WebSocketThread webSocketThread = new WebSocketThread("ws://localhost:3001", serialThread);

            serialThread.SetPortName();

            var webSocketTask = webSocketThread.Start(cancellationToken);
            var serialTask = serialThread.Start(cancellationToken);

            Task.WaitAll(webSocketTask, serialTask);

            Console.WriteLine("Daemon finished");
        }
    }
}
