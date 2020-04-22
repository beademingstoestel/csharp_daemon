using Microsoft.Extensions.Logging;
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
                    var changed = alarmValue ^ value;

                    if (changed > 0)
                    {
                        // get the new alarms (aka the bits who became one)
                        var alarmToSend = changed & value;
                        AlarmValuesToSend.Enqueue(alarmToSend);
                    }

                    alarmValue = value;
                }
            }
        }

        public void SetPCAlarmBits(uint alarmBits)
        {
            var previousArduinoValue = AlarmValue & 0xFFFF0000;

            AlarmValue = previousArduinoValue | alarmBits;
        }

        public void SetArduinoAlarmBits(uint alarmBits)
        {
            var previousPCValue = AlarmValue & 0x0000FFFF;

            AlarmValue = (alarmBits << 16)  | previousPCValue;
        }

        public bool AlarmMuted { get; set; } = false;

        private bool ShouldPlayAlarm
        {
            get => alarmValue > 0 && !AlarmMuted;
        }

        public void ResetAlarm()
        {
            AlarmValue = 0;
        }

        public byte[] ComposeAlarmMessage()
        {
            // send alarm state to arduino
            // 1 to play alarm
            // 0 everything is ok
            uint alarmValueToSend = 0;
                        
            if (alarmValue > 0)
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
