using Flurl.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using VentilatorDaemon.Models;
using VentilatorDaemon.Models.Api;

namespace VentilatorDaemon.Services.Implementations
{
    public class ApiService: IApiService
    {
        private readonly FlurlClient flurlClient;
        private readonly ILogger<ApiService> logger;

        public ApiService(ProgramSettings programSettings,
            ILoggerFactory loggerFactory)
        {
            this.flurlClient = new FlurlClient($"http://{programSettings.WebServerHost}:{programSettings.WebServerPort}");

            this.logger = loggerFactory.CreateLogger<ApiService>();
        }

        public async Task SendAlarmToServerAsync(AlarmEvent alarmEvent)
        {
            try
            {
                await flurlClient.Request("/api/alarms")
                    .PutJsonAsync(alarmEvent);
            }
            catch (Exception e)
            {
                logger.LogError("Error while sending alarm to server: {0}", e.Message);
                throw e;
            }
        }

        public async Task SendCalculatedValuesToServerAsync(CalculatedValues calculatedValues)
        {
            try
            {
                await flurlClient.Request("/api/calculated_values")
                            .PutJsonAsync(calculatedValues);
            }
            catch (Exception e)
            {
                logger.LogError("Error while sending calculated values to server: {0}", e.Message);
                throw e;
            }
        }

        public async Task SendSettingToServerAsync(string key, object value)
        {
            try
            {
                Dictionary<string, object> dict = new Dictionary<string, object>();
                dict.Add(key, value);

                await flurlClient.Request("/api/settings")
                    .PutJsonAsync(dict);
            }
            catch (Exception e)
            {
                logger.LogError("Error while sending setting to server: {0}", e.Message);
                throw e;
            }
        }
    }
}
