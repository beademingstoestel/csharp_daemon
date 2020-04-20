using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace VentilatorDaemon.Models.Db
{
    [BsonIgnoreExtraElements]
    public class ValueEntry
    {
        [BsonElement("value")]
        public MeasuredValues Value { get; set; }
        [BsonElement("loggedAt")]
        public DateTime LoggedAt { get; set; }
    }
}
