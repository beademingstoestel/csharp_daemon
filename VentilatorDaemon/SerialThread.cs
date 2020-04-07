using Flurl.Http;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO.Ports;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    [BsonIgnoreExtraElements]
    public class ValueEntry
    {
        [BsonElement("value")]
        public float Value { get; set; }
        [BsonElement("loggedAt")]
        public DateTime LoggedAt { get; set; }
    }

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

        public static byte CalculateCrc(this byte[] message, int length)
        {
            byte checksum = 0;
            for (var i = 0; i < length; i++)
            {
                checksum ^= message[i];
            }

            return checksum;
        }
    }

    public class SerialThread
    {
        private SerialPort serialPort = new SerialPort();
        private Object lockObj = new Object();
        private SemaphoreSlim saveSettingLock = new SemaphoreSlim(1, 1);
        private SemaphoreSlim saveMongoLock = new SemaphoreSlim(1, 1);
        private byte msgId = 0;
        private bool alarmReceived = false;

        private CancellationTokenSource ackTokenSource = new CancellationTokenSource();

        FlurlClient flurlClient = new FlurlClient("http://localhost:3001");

        public List<Tuple<string, byte[]>> measurementIds = new List<Tuple<string, byte[]>>()
        {
            Tuple.Create("breathperminute_values", ASCIIEncoding.ASCII.GetBytes("BPM=")),  // Breaths per minute
            Tuple.Create("volume_values", ASCIIEncoding.ASCII.GetBytes("VOL=")),  // Volume
            Tuple.Create("trigger_values", ASCIIEncoding.ASCII.GetBytes("TRIG=")), // Trigger
            Tuple.Create("pressure_values", ASCIIEncoding.ASCII.GetBytes("PRES=")), // Pressure
            Tuple.Create("targetpressure_values", ASCIIEncoding.ASCII.GetBytes("TPRES=")), // Target pressure
            Tuple.Create("flow_values", ASCIIEncoding.ASCII.GetBytes("FLOW=")), // Liters/min
            Tuple.Create("cpu_values", ASCIIEncoding.ASCII.GetBytes("CPU=")),   // CPU usage
        };

        public List<Tuple<string, byte[]>> settingIds = new List<Tuple<string, byte[]>>()
        {
            Tuple.Create("RR", ASCIIEncoding.ASCII.GetBytes("RR=")),  // Breaths per minute
            Tuple.Create("VT", ASCIIEncoding.ASCII.GetBytes("VT=")),   // Tidal Volume
            Tuple.Create("PK", ASCIIEncoding.ASCII.GetBytes("PK=")),   // Peak Pressure
            Tuple.Create("TS", ASCIIEncoding.ASCII.GetBytes("TS=")),   // Breath Trigger Threshold
            Tuple.Create("IE", ASCIIEncoding.ASCII.GetBytes("IE=")),   // Inspiration/Expiration (N for 1/N)
            Tuple.Create("PP", ASCIIEncoding.ASCII.GetBytes("PP=")),   // PEEP (positive end expiratory pressure)
            Tuple.Create("ADPK", ASCIIEncoding.ASCII.GetBytes("ADPK=")), // Allowed deviation Peak Pressure
            Tuple.Create("ADVT", ASCIIEncoding.ASCII.GetBytes("ADVT=")), // Allowed deviation Tidal Volume
            Tuple.Create("ADPP", ASCIIEncoding.ASCII.GetBytes("ADPP=")), // Allowed deviation PEEP
            Tuple.Create("MODE", ASCIIEncoding.ASCII.GetBytes("MODE=")),  // Machine Mode (Volume Control / Pressure Control)
            Tuple.Create("ACTIVE", ASCIIEncoding.ASCII.GetBytes("ACTIVE=")),  // Machine on / off
            Tuple.Create("PS", ASCIIEncoding.ASCII.GetBytes("PS=")), // support pressure
            Tuple.Create("RP", ASCIIEncoding.ASCII.GetBytes("RP=")), // ramp time
            Tuple.Create("TP", ASCIIEncoding.ASCII.GetBytes("TP=")), // trigger pressure
            Tuple.Create("MT", ASCIIEncoding.ASCII.GetBytes("MT=")), // mute
            Tuple.Create("FW", ASCIIEncoding.ASCII.GetBytes("FW=")), // firmware version
        };

        private byte[] ack = ASCIIEncoding.ASCII.GetBytes("ACK=");
        private byte[] alarm = ASCIIEncoding.ASCII.GetBytes("ALARM=");
        private readonly MongoClient client;
        private readonly IMongoDatabase database;

        private ConcurrentDictionary<byte, Tuple<byte[], DateTime>> waitingForAck = new ConcurrentDictionary<byte, Tuple<byte[], DateTime>>();

        public SerialThread()
        {
            serialPort.BaudRate = 115200;
            serialPort.ReadTimeout = 1500;
            serialPort.WriteTimeout = 1500;

            client = new MongoClient("mongodb://localhost/beademing");
            database = client.GetDatabase("beademing");
        }

        public void WriteData(byte[] bytes)
        {
            lock (lockObj)
            {
                try
                {
                    //add space for id byte and CRC
                    var bytesToSend = new byte[bytes.Length + 5];
                    Array.Copy(bytes, bytesToSend, bytes.Length);

                    //add id
                    bytesToSend[bytes.Length] = 61; //=
                    bytesToSend[bytes.Length + 1] = msgId;
                    // calculate CRC
                    bytesToSend[bytes.Length + 2] = 61; //=
                    bytesToSend[bytes.Length + 3] = bytesToSend.CalculateCrc(bytes.Length + 3);
                    bytesToSend[bytes.Length + 4] = 10; // \n

                    Console.WriteLine("Send message {0} ", ASCIIEncoding.ASCII.GetString(bytesToSend));

                    waitingForAck.TryAdd(msgId, Tuple.Create(bytes, DateTime.UtcNow));
                    // send message
                    serialPort.Write(bytesToSend, 0, bytesToSend.Length);

                    msgId++;
                }
                catch (Exception) { }
            }
        }

        private void CreateAndSendAck(byte messageId)
        {
            lock (lockObj)
            {
                try
                {
                    //add space for id byte and CRC
                    var bytesToSend = new byte[8];
                    Array.Copy(ack, bytesToSend, ack.Length);
                    bytesToSend[4] = messageId;
                    bytesToSend[5] = 61; //=
                    bytesToSend[6] = bytesToSend.CalculateCrc(6);
                    bytesToSend[7] = 10; // \n

                    serialPort.Write(bytesToSend, 0, 8);
                }
                catch (Exception) { }
            }
        }

        public async Task SendSettingToServer(string key, float value)
        {
            await saveSettingLock.WaitAsync();
            try
            {
                Dictionary<string, float> dict = new Dictionary<string, float>();
                dict.Add(key, value);

                await flurlClient.Request("/api/settings")
                    .PutJsonAsync(dict);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while sending setting to server: {0}", e.Message);
            }
            finally
            {
                saveSettingLock.Release();
            }
        }

        public async Task SendMeasurementToMongo(string collection, DateTime timeStamp, float value)
        {
            await saveMongoLock.WaitAsync();
            try
            {
                await database.GetCollection<ValueEntry>(collection).InsertOneAsync(new ValueEntry()
                {
                    Value = value,
                    LoggedAt = timeStamp,
                });
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while sending setting to server: {0}", e.Message);
            }
            finally
            {
                saveMongoLock.Release();
            }
        }

        public void HandleMessage(byte[] message, DateTime timeStamp)
        {
            //calculate crc
            var crc = message.CalculateCrc(message.Length - 1);

            if (crc != message[message.Length - 1])
            {
                return;
            }

            if (message.StartsWith(ack))
            { 
                if (waitingForAck.ContainsKey(message[4]))
                {
                    try
                    {
                        waitingForAck.TryRemove(message[4], out _);
                    }
                    catch (Exception) { }
                }
            }
            else if (message.StartsWith(alarm))
            {
                var messageId = message[message.Length - 3];
                CreateAndSendAck(messageId);


                if (!alarmReceived)
                {
                    alarmReceived = true;

                    Task.Run(async () =>
                    {
                        while (alarmReceived)
                        {
                            // send alarm ping
                            var bytes = ASCIIEncoding.ASCII.GetBytes(string.Format("ALARM={0}", 0));
                            WriteData(bytes);

                            await Task.Delay(500);
                        }
                    });
                }
            }
            else
            {
                var handled = false;
                foreach (var setting in settingIds)
                {
                    if (message.StartsWith(setting.Item2))
                    {
                        var messageId = message[message.Length - 3];

                        // we need to send an ack
                        CreateAndSendAck(messageId);

                        //get value of the setting
                        var settingString = ASCIIEncoding.ASCII.GetString(message);
                        var tokens = settingString.Split('=', StringSplitOptions.RemoveEmptyEntries);

                        var floatValue = 0.0f;

                        Console.WriteLine("Received setting {0} with value {1}", setting.Item1, floatValue);

                        if (float.TryParse(tokens[1], out floatValue))
                        {
                            // send to server
                            _ = Task.Run(async () =>
                             {
                                 await SendSettingToServer(setting.Item1, floatValue);
                             });
                        }

                        handled = true;
                        break;
                    }
                }

                if (!handled)
                {
                    foreach (var measurement in measurementIds)
                    {
                        if (message.StartsWith(measurement.Item2))
                        {

                            var measurementString = ASCIIEncoding.ASCII.GetString(message);
                            var tokens = measurementString.Split('=', StringSplitOptions.RemoveEmptyEntries);

                            var floatValue = 0.0f;

                            if (float.TryParse(tokens[1], out floatValue))
                            {
                                // send to mongo
                                _ = Task.Run(async () =>
                                {
                                    await SendMeasurementToMongo(measurement.Item1, timeStamp, floatValue);
                                });
                            }

                            break;
                        }
                    }
                }
            }
        }



        public Task Start(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[2048];
            int bufferOffset = 0;

            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!serialPort.IsOpen)
                        {
                            ackTokenSource.Cancel();
                            waitingForAck.Clear();
                            alarmReceived = false;
                            serialPort.Open();

                            ackTokenSource = new CancellationTokenSource();
                            var token = ackTokenSource.Token;

                            // thread that checks for messages to resend
                            _ = Task.Run(async () =>
                            {
                                while (!token.IsCancellationRequested)
                                {
                                    try
                                    {
                                        var now = DateTime.UtcNow;
                                        List<byte> toRemove = new List<byte>();
                                        foreach (var kvp in waitingForAck)
                                        {
                                            if ((now - kvp.Value.Item2).TotalMilliseconds > 999)
                                            {
                                                // message is too old and not acked, resend
                                                toRemove.Add(kvp.Key);
                                            }
                                        }

                                        foreach (var msgId in toRemove)
                                        {
                                            // in extreme circumstances it might have been deleted by now
                                            if (waitingForAck.ContainsKey(msgId))
                                            {
                                                Tuple<byte[], DateTime> tuple;
                                                if (waitingForAck.TryRemove(msgId, out tuple))
                                                {
                                                    WriteData(tuple.Item1);
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception) { }

                                    await Task.Delay(1000);
                                }
                            });
                        }

                        try
                        {
                            int readBytes = await serialPort.BaseStream.ReadAsync(buffer, bufferOffset, 2048 - bufferOffset);

                            bufferOffset += readBytes;

                            // get the messages out of the buffer by looking for line endings not preceded by =
                            var lengthMessage = 0;
                            var startMessage = 0;
                            for (int i = 0; i < bufferOffset; i++)
                            {
                                if (buffer[i] == '\n' && lengthMessage > 1)
                                {
                                    byte[] message = new byte[lengthMessage - 1];
                                    Array.Copy(buffer, startMessage, message, 0, lengthMessage - 1);

                                    startMessage = i + 1;
                                    lengthMessage = -1;

                                    var utcNow = DateTime.UtcNow;
                                    _ = Task.Run(() => HandleMessage(message, utcNow));
                                }
                                lengthMessage++;
                            }

                            // we found a message, shift the buffer
                            if (startMessage > 0)
                            {
                                bufferOffset -= startMessage;

                                if (bufferOffset > 0)
                                {
                                    Array.Copy(buffer, startMessage, buffer, 0, bufferOffset);
                                }
                                else
                                {
                                    Array.Fill<byte>(buffer, 0);
                                }
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
