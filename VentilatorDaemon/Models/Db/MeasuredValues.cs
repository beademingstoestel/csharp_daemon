using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace VentilatorDaemon.Models.Db
{
    [BsonIgnoreExtraElements]
    public class MeasuredValues
    {
        [BsonElement("volume")]
        public double Volume { get; set; }
        [BsonElement("pressure")]
        public double Pressure { get; set; }
        [BsonElement("targetPressure")]
        public double TargetPressure { get; set; }
        [BsonElement("flow")]
        public double Flow { get; set; }
        [BsonElement("trigger")]
        public int Trigger { get; set; }
        [BsonElement("breathsPerMinute")]
        public double BreathsPerMinute { get; set; }

        [BsonElement("fiO2")]
        public double FiO2 { get; set; }
        public long ArduinoTime { get; set; }
    }
}
