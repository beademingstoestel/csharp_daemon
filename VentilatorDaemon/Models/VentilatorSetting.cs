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
        byte[] ToBytes(object value);
    }

    public static class VentilatorSettingsFactory
    {
        public static List<IVentilatorSetting> GetVentilatorSettings()
        {
            return new List<IVentilatorSetting>()
            {
                new VentilatorSetting<int>("RR", true),   // Respiratory rate
                new VentilatorSetting<int>("RA", true),   // Respiratory rate
                new VentilatorSetting<int>("VT", true),   // Tidal Volume
                new VentilatorSetting<int>("PK", true),   // Peak Pressure
                new VentilatorSetting<float>("TS", true),   // Breath Trigger Threshold
                new VentilatorSetting<float>("IE", true),   // Inspiration/Expiration (N for 1/N)
                new VentilatorSetting<int>("PP", true),   // PEEP (positive end expiratory pressure)
                new VentilatorSetting<int>("ADPK", true), // Allowed deviation Peak Pressure
                new VentilatorSetting<int>("ADVT", true), // Allowed deviation Tidal Volume
                new VentilatorSetting<int>("ADPP", true), // Allowed deviation PEEP
                new VentilatorSetting<int>("MODE", true),  // Machine Mode (Volume Control / Pressure Control)
                new VentilatorSetting<int>("ACTIVE", true),  // Machine on / off
                new VentilatorSetting<int>("PS", false), // support pressure
                new VentilatorSetting<float>("RP", true), // ramp time
                new VentilatorSetting<float>("TP", true), // trigger pressure
                new VentilatorSetting<int>("MT", false), // mute
                new VentilatorSetting<float>("FW", false), // firmware version
                new VentilatorSetting<float>("FIO2", true), // oxygen level
            };
        }
    }

    public class VentilatorSetting<T>: IVentilatorSetting
    {
        public VentilatorSetting(string settingKey, bool causesAlarmInactivity)
        {
            SettingKey = settingKey;
            CausesAlarmInactivity = causesAlarmInactivity;
        }

        public string SettingKey { get; set; }
        public bool CausesAlarmInactivity { get; set; }

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
