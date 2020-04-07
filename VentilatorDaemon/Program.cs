using System;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting daemon");
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            WebSocketThread webSocketThread = new WebSocketThread("ws://localhost:3001");
            SerialThread serialThread = new SerialThread();

            serialThread.SetPortName();

            var webSocketTask = webSocketThread.Start(cancellationToken);
            var serialTask = serialThread.Start(cancellationToken);

            Task.WaitAll(webSocketTask, serialTask);

            Console.WriteLine("Daemon finished");
        }
    }
}
