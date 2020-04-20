using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace VentilatorDaemon.Models.Api
{
    public class CalculatedValues
    {
        [JsonProperty("IE")]
        public double IE { get; set; }
        [JsonProperty("tidalVolume")]
        public double TidalVolume { get; set; }
        [JsonProperty("volumePerMinute")]
        public double VolumePerMinute { get; set; }
        [JsonProperty("respatoryRate")]
        public double RespatoryRate { get; set; }
        [JsonProperty("pressurePlateau")]
        public double PressurePlateau { get; set; }

        public void ResetValues()
        {
            IE = 0.0;
            VolumePerMinute = 0.0;
            RespatoryRate = 0.0;
            PressurePlateau = 0.0;
        }
    }
}
