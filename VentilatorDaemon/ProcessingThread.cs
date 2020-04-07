using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    public class ProcessingThread
    {
        private readonly MongoClient client;
        private readonly IMongoDatabase database;
        private readonly SerialThread serialThread;

        public ProcessingThread(SerialThread serialThread)
        {
            client = new MongoClient("mongodb://localhost/beademing");
            database = client.GetDatabase("beademing");
            this.serialThread = serialThread;
        }

        public Task Start(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500);
                }
            });
        }
    }
}
