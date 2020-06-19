using System;
using System.Collections.Generic;
using System.Text;

namespace VentilatorDaemon.Models
{
    public class ProgramSettings
    {
        public int WebServerPort { get; set; } = 3001;
        public string DatabaseHost { get; set; }
        public string WebServerHost { get; set; }
        public string SerialPort { get; set; }
        public string LogDirectory { get; set; }
        public string DbCollectionPrefix { get; set; } = null;

        public string GetDatabaseName()
        {
            if (string.IsNullOrEmpty(DbCollectionPrefix))
            {
                return "beademing";
            }
            else
            {
                return string.Format("{0}_beademing", DbCollectionPrefix);
            }
        }
    }
}
