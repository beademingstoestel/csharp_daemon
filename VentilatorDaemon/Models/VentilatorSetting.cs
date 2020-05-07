using System;
using System.Collections.Generic;
using System.Text;
using VentilatorDaemon.Helpers.Serial;

namespace VentilatorDaemon.Models
{
    public interface IVentilatorSetting
    {
        string SettingKey { get; set; }
        Type SettingType { get; }
        public byte[] SerialStart { get; }
        bool CausesAlarmInactivity { get; set; }
        public bool SendToArduino { get; set; }
        byte[] ToBytes(object value);
    }

    public static class VentilatorSettingsFactory
    {
        public static List<IVentilatorSetting> GetVentilatorSettings()
        {
            return new List<IVentilatorSetting>()
            {
                new VentilatorSetting<int>("RR", true, true),   // Respiratory rate
                new VentilatorSetting<int>("RA", false, true),   // Reset alarm
                new VentilatorSetting<int>("VT", true, true),   // Tidal Volume
                new VentilatorSetting<int>("PK", true, true),   // Peak Pressure
                new VentilatorSetting<double>("TS", true, true),   // Breath Trigger Threshold
                new VentilatorSetting<double>("IE", true, true),   // Inspiration/Expiration (N for 1/N)
                new VentilatorSetting<int>("PP", true, true),   // PEEP (positive end expiratory pressure)
                new VentilatorSetting<int>("ADPK", true, true), // Allowed deviation Peak Pressure/not used anymore, only kept for backward compability
                new VentilatorSetting<int>("HPK", true, true), // Highest allowed pressure
                new VentilatorSetting<int>("LPK", true, true), // Lowest allowed pressure
                new VentilatorSetting<int>("ADVT", true, true), // Allowed deviation Tidal Volume
                new VentilatorSetting<int>("ADPP", true, true), // Allowed deviation PEEP
                new VentilatorSetting<int>("MODE", true, true),  // Machine Mode (Volume Control / Pressure Control)
                new VentilatorSetting<int>("ACTIVE", true, true),  // Machine on / off
                new VentilatorSetting<int>("PS", false, true), // support pressure
                new VentilatorSetting<double>("RP", true, true), // ramp time
                new VentilatorSetting<double>("TP", true, true), // trigger pressure
                new VentilatorSetting<int>("MT", false, true), // mute
                new VentilatorSetting<double>("FW", false, false), // firmware version
                new VentilatorSetting<double>("FIO2", true, true), // oxygen level, 0.20 -> 1.0
                new VentilatorSetting<double>("ADFIO2", true, true), // oxygen level, 0.20 -> 1.0
                new VentilatorSetting<int>("HRR", true, true), // detect hyperventilating, absolute value
                new VentilatorSetting<int>("LRR", true, true), // detect low bpm, absolute value
                new VentilatorSetting<int>("RVOL", true, true), // detect residual volume, absolute value
            };
        }
    }

    public class VentilatorSetting<T>: IVentilatorSetting
    {
        public VentilatorSetting(string settingKey, bool causesAlarmInactivity, bool sendToArduino)
        {
            SettingKey = settingKey;
            CausesAlarmInactivity = causesAlarmInactivity;
            SendToArduino = sendToArduino;
        }

        public string SettingKey { get; set; }
        public bool CausesAlarmInactivity { get; set; }

        public bool SendToArduino { get; set; }

        public byte[] SerialStart 
        { 
            get => string.Format("{0}=", SettingKey).ToASCIIBytes(); 
        }

        public Type SettingType 
        { 
            get => typeof(T); 
        }

        public byte[] ToBytes(object value)
        {
            if (typeof(T) == typeof(float))
            {
                return ASCIIEncoding.ASCII.GetBytes(string.Format("{0}={1}", SettingKey, ((float)value).ToString("0.00")));
            }
            else if (typeof(T) == typeof(int))
            {
                return ASCIIEncoding.ASCII.GetBytes(string.Format("{0}={1}", SettingKey, ((int)value).ToString("0")));
            }

            return ASCIIEncoding.ASCII.GetBytes(string.Format("{0}={1}", SettingKey, value.ToString()));
        }
    }
}
