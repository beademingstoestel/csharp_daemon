using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using VentilatorDaemon.Models;
using VentilatorDaemon.Models.Db;

namespace VentilatorDaemon.Services.Implementations
{
    public class DbService: IDbService
    {
        private readonly MongoClient client;
        private readonly IMongoDatabase database;
        private readonly ILogger<DbService> logger;

        public DbService(ProgramSettings programSettings,
            ILoggerFactory loggerFactory)
        {
            client = new MongoClient($"mongodb://{programSettings.DatabaseHost}:27017/?connect=direct;replicaSet=rs0;readPreference=primaryPreferred");
            database = client.GetDatabase(programSettings.GetDatabaseName());

            this.logger = loggerFactory.CreateLogger<DbService>();
        }

        public List<ValueEntry> GetDocuments(string collection, int number)
        {
            var builder = Builders<ValueEntry>.Filter;

            return database.GetCollection<ValueEntry>(collection)
                .Find<ValueEntry>(builder.Empty)
                .SortByDescending(entry => entry.LoggedAt)
                .Limit(number)
                .ToList();

        }

        public List<ValueEntry> GetDocuments(string collection, DateTime since)
        {
            var builder = Builders<ValueEntry>.Filter;

            return database.GetCollection<ValueEntry>(collection)
                .Find<ValueEntry>(builder.Gt(e => e.LoggedAt, since))
                .SortByDescending(entry => entry.LoggedAt)
                .ToList();

        }

        public async Task SendMeasurementValuesToMongoAsync(DateTime timeStamp,
            long arduinoTime,
            double volume,
            double pressure,
            double targetPressure,
            int trigger,
            double flow,
            double fio2,
            double fio2i,
            double fio2e,
            double breathsPerMinute)
        {
            await database.GetCollection<ValueEntry>("measured_values").InsertOneAsync(new ValueEntry()
            {
                Value = new MeasuredValues
                {
                    Volume = volume,
                    Pressure = pressure,
                    TargetPressure = targetPressure,
                    Trigger = trigger,
                    Flow = flow,
                    BreathsPerMinute = breathsPerMinute,
                    FiO2 = fio2,
                    FiO2Inhale = fio2i,
                    FiO2Exhale = fio2e,
                    ArduinoTime = arduinoTime,
                },
                LoggedAt = timeStamp,
            });
        }
    }
}
