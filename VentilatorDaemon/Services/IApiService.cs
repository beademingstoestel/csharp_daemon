using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using VentilatorDaemon.Models.Api;

namespace VentilatorDaemon.Services
{
    public interface IApiService
    {
        Task SendAlarmToServerAsync(AlarmEvent alarmEvent);
        Task SendSettingToServerAsync(string key, float value);
        Task SendCalculatedValuesToServerAsync(CalculatedValues calculatedValues);
    }
}
