using Microsoft.Extensions.Logging;
using MongoDB.Bson.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentilatorDaemon.Helpers.Serial;
using VentilatorDaemon.Services;

namespace VentilatorDaemon
{
    public class AlarmThread
    {
        private uint alarmValue = 0;
        private object alarmModifierLock = new object();
        private readonly IApiService apiService;
        private readonly ILogger<AlarmThread> logger;

        // we want to make sure every beep lasts at least 3 seconds
        private DateTime lastBeepPlayed = DateTime.UtcNow.AddHours(-1);
        // some alarms need to keep beeping once triggered until a manual reset
        private bool shouldKeepBeepUntilReset = false;

        public AlarmThread(IApiService apiService,
            ILoggerFactory loggerFactory)
        {
            this.apiService = apiService;
            this.logger = loggerFactory.CreateLogger<AlarmThread>();
        }

        public ConcurrentQueue<uint> AlarmValuesToSend { get; private set; } = new ConcurrentQueue<uint>();

        // Highest 16 bits are reserved for arduino
        // the lower 16 bits are for the pc
        public uint AlarmValue
        {
            get => alarmValue;
            private set
            {
                lock (alarmModifierLock)
                {
                    // do we have changes?
                    if (alarmValue != value)
                    {
                        // get the new alarms (aka the bits who became one)
                        var changed = alarmValue ^ value;
                        var alarmToSend = changed & value;

                        if (alarmToSend > 0)
                        {
                            logger.LogDebug("Alarm triggered, we start a new beep now and send the new value to the server");
                            AlarmValuesToSend.Enqueue(alarmToSend);

                            lastBeepPlayed = DateTime.UtcNow;
                        }
                    }

                    alarmValue = value;
                }
            }
        }

        public DateTime InactiveSince
        {
            get; set;
        } = DateTime.MinValue;

        public bool Active
        {
            get; set;
        } = false;

        public void SetInactive()
        {
            InactiveSince = DateTime.UtcNow;
            Active = false;
        }

        public void SetPCAlarmBits(uint alarmBits)
        {
            var previousArduinoValue = AlarmValue & 0xFFFF0000;

            AlarmValue = previousArduinoValue | alarmBits;
        }

        public void SetArduinoAlarmBits(uint alarmBits)
        {
            var previousPCValue = AlarmValue & 0x0000FFFF;

            AlarmValue = (alarmBits << 16) | previousPCValue;
        }

        // todo: alarm can only be muted for one minute
        public bool AlarmMuted { get; set; } = false;

        private bool ShouldPlayAlarm
        {
            get
            {
                if (!Active)
                {
                    return false;
                }

                bool shouldBeep = shouldKeepBeepUntilReset;

                if (!shouldBeep)
                {
                    var timeSinceLastBeep = (DateTime.UtcNow - lastBeepPlayed).TotalSeconds;

                    shouldBeep = timeSinceLastBeep < 3.5;
                }

                return shouldBeep | (alarmValue > 0);
            }
        }

        public void ResetAlarm()
        {
            AlarmValue = 0;
            shouldKeepBeepUntilReset = false;
        }

        public byte[] ComposeAlarmMessage()
        {
            // send alarm state to arduino
            // 1 to play alarm
            // 0 everything is ok
            uint alarmValueToSend = 0;

            if (ShouldPlayAlarm)
            {
                alarmValueToSend = 1;
            }

            return string.Format("ALARM={0}", alarmValueToSend).ToASCIIBytes();
        }

        public Task PlayAlarmSoundTask(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (ShouldPlayAlarm)
                    {
                        //NetCoreAudio.Player player = new NetCoreAudio.Player();
                        //await player.Play("./assets/beep.wav");
                    }
                    await Task.Delay(400);
                }
            });
        }

        public Task SendAlarmToServerTask(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // see if there are alarm values we haven't sent to the server yet
                    // todo: we should also sent the timestamp of when the alarm was raised and use that on the server
                    try
                    {
                        uint alarmToSend = 0;

                        if (AlarmValuesToSend.TryPeek(out alarmToSend))
                        {
                            await apiService.SendAlarmToServerAsync(alarmToSend);

                            AlarmValuesToSend.TryDequeue(out _);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError("Error while trying to send alarmvalue to the server: {0}", e.Message);
                    }

                    await Task.Delay(10);
                }
            });
        }

        public Task Start(CancellationToken cancellationToken)
        {
            return Task.WhenAll(SendAlarmToServerTask(cancellationToken), PlayAlarmSoundTask(cancellationToken));
        }
    }
}
