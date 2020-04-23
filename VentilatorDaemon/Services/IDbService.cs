using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using VentilatorDaemon.Models.Db;

namespace VentilatorDaemon.Services
{
    public interface IDbService
    {
        Task SendMeasurementValuesToMongoAsync(DateTime timeStamp,
            long arduinoTime,
            double volume,
            double pressure,
            double targetPressure,
            int trigger,
            double flow,
            double breathsPerMinute);

        List<ValueEntry> GetDocuments(string collection, int number);
        List<ValueEntry> GetDocuments(string collection, DateTime since);
    }
}
