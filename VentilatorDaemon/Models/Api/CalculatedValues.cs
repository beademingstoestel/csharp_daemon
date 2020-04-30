using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace VentilatorDaemon.Models.Api
{
    public struct CalculatedValues
    {
        [JsonProperty("IE")]
        public double IE { get; set; }
        [JsonProperty("tidalVolume")]
        public double TidalVolume { get; set; }
        [JsonProperty("residualVolume")]
        public double ResidualVolume { get; set; }
        [JsonProperty("volumePerMinute")]
        public double VolumePerMinute { get; set; }
        [JsonProperty("respatoryRate")]
        public double RespatoryRate { get; set; }
        [JsonProperty("pressurePlateau")]
        public double PressurePlateau { get; set; }
        [JsonProperty("peakPressure")]
        public double PeakPressure { get; set; }
        [JsonProperty("fiO2")]
        public double FiO2 { get; set; }
        [JsonProperty("lungCompliance")]
        public double LungCompliance { get; set; }
        [JsonProperty("lungResistance")]
        public double LungResistance { get; set; }
    }
}
