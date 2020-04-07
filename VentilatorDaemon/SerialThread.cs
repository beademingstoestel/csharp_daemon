using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    public static class ByteArrayHelpers
    {
        public static bool StartsWith(this byte[] hayStack, byte[] needle)
        {
            for (int i = 0; i < Math.Min(hayStack.Length, needle.Length); i++)
            {
                if (hayStack[i] != needle[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public class SerialThread
    {
        private SerialPort serialPort = new SerialPort();
        private Object lockObj = new Object();

        public List<byte[]> measurementIds = new List<byte[]>()
        {
            ASCIIEncoding.ASCII.GetBytes("BPM="),  // Breaths per minute
            ASCIIEncoding.ASCII.GetBytes("VOL="),  // Volume
            ASCIIEncoding.ASCII.GetBytes("TRIG="), // Trigger
            ASCIIEncoding.ASCII.GetBytes("PRES="), // Pressure
            ASCIIEncoding.ASCII.GetBytes("TPRES="), // Target pressure
            ASCIIEncoding.ASCII.GetBytes("FLOW="), // Liters/min
            ASCIIEncoding.ASCII.GetBytes("CPU="),   // CPU usage
        };

        public List<byte[]> settingIds = new List<byte[]>()
        {
            ASCIIEncoding.ASCII.GetBytes("RR="),  // Breaths per minute
            ASCIIEncoding.ASCII.GetBytes("VT"),   // Tidal Volume
            ASCIIEncoding.ASCII.GetBytes("PK"),   // Peak Pressure
            ASCIIEncoding.ASCII.GetBytes("TS"),   // Breath Trigger Threshold
            ASCIIEncoding.ASCII.GetBytes("IE"),   // Inspiration/Expiration (N for 1/N)
            ASCIIEncoding.ASCII.GetBytes("PP"),   // PEEP (positive end expiratory pressure)
            ASCIIEncoding.ASCII.GetBytes("ADPK"), // Allowed deviation Peak Pressure
            ASCIIEncoding.ASCII.GetBytes("ADVT"), // Allowed deviation Tidal Volume
            ASCIIEncoding.ASCII.GetBytes("ADPP"), // Allowed deviation PEEP
            ASCIIEncoding.ASCII.GetBytes("MODE"),  // Machine Mode (Volume Control / Pressure Control)
            ASCIIEncoding.ASCII.GetBytes("ACTIVE"),  // Machine on / off
            ASCIIEncoding.ASCII.GetBytes("PS"), // support pressure
            ASCIIEncoding.ASCII.GetBytes("RP"), // ramp time
            ASCIIEncoding.ASCII.GetBytes("TP"), // trigger pressure
            ASCIIEncoding.ASCII.GetBytes("MT"), // mute
            ASCIIEncoding.ASCII.GetBytes("FW"), // firmware version
        };

        private byte[] ack = ASCIIEncoding.ASCII.GetBytes("ACK=");
        private byte[] alarm = ASCIIEncoding.ASCII.GetBytes("ALARM=");

        public SerialThread()
        {
            serialPort.BaudRate = 115200;
            serialPort.ReadTimeout = 1500;
            serialPort.WriteTimeout = 1500;
        }

        public void WriteData(byte[] bytes)
        {
            lock(lockObj)
            {
                try
                {
                    // add message to the ack dictionary
                    // calculate CRC
                    // send message
                    serialPort.Write(bytes, 0, bytes.Length);
                }
                catch (Exception) { }
            }
        }

        public void HandleMessage(byte[] message, DateTime timeStamp)
        {
            if (message.StartsWith(ack))
            {

            }
            else if (message.StartsWith(alarm))
            {
                Console.WriteLine("ALARM received");
            }
            else
            {
                var handled = false;
                foreach (var setting in settingIds)
                {
                    if (message.StartsWith(setting))
                    {
                        Console.WriteLine("Received setting: " + ASCIIEncoding.ASCII.GetString(message));
                        break;
                        handled = true;
                    }
                }

                foreach (var measurement in measurementIds)
                {
                    if (message.StartsWith(measurement))
                    {
                        break;
                    }
                }
            }
        }

        

        public Task Start(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[512];
            int bufferOffset = 0;

            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!serialPort.IsOpen)
                        {
                            serialPort.Open();
                        }

                        try
                        {
                            int readBytes = await serialPort.BaseStream.ReadAsync(buffer, bufferOffset, 512 - bufferOffset);

                            // get the messages out of the buffer by looking for line endings not preceded by =
                            var lengthMessage = 0;
                            var startMessage = 0;
                            for (int i = 0; i < buffer.Length; i++)
                            {
                                if (buffer[i] == '\n' && lengthMessage > 0)
                                {
                                    byte[] message = new byte[lengthMessage];
                                    Array.Copy(buffer, startMessage, message, 0, lengthMessage);

                                    startMessage = i + 1;
                                    lengthMessage = 0;

                                    var utcNow = DateTime.UtcNow;
                                    _ = Task.Run(() => HandleMessage(message, utcNow));
                                }
                                lengthMessage++;
                            }
                        }
                        catch (TimeoutException) { }
                    }
                    catch (Exception e)
                    {
                        // todo log error
                        Console.WriteLine(e.Message);
                    }
                }

                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
            });
        }

        public void SetPortName()
        {
            string portName = null;

            Console.WriteLine("Available Ports:");
            foreach (string s in SerialPort.GetPortNames())
            {
                if (String.IsNullOrWhiteSpace(portName))
                {
                    portName = s;
                }
                Console.WriteLine("   {0}", s);
            }

            Console.Write("Enter COM port value (Default: {0}): ", portName);
            string chosenPortName = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(chosenPortName))
            {
                chosenPortName = portName;
            }

            serialPort.PortName = chosenPortName;
        }
    }
}
