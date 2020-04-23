using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace VentilatorDaemon.Models.Api
{
    public class AlarmEvent
    {
        [JsonProperty("raisedAlarms")]
        public uint RaisedAlarms { get; set; }
        [JsonProperty("resolvedAlarms")]
        public uint ResolvedAlarms { get; set; }
        [JsonProperty("value")]
        public uint Value { get; set; }
    }
}
