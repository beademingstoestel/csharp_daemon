﻿using Flurl.Http;
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
            this.flurlClient = new FlurlClient($"http://{programSettings.WebServerHost}:3001");

            this.logger = loggerFactory.CreateLogger<ApiService>();
        }

        public async Task SendAlarmToServerAsync(uint value)
        {
            try
            {
                Dictionary<string, uint> dict = new Dictionary<string, uint>();
                dict.Add("value", value);

                await flurlClient.Request("/api/alarms")
                    .PutJsonAsync(dict);
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

        public async Task SendSettingToServerAsync(string key, float value)
        {
            try
            {
                Dictionary<string, float> dict = new Dictionary<string, float>();
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
