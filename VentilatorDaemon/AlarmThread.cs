using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    public class AlarmThread
    {
        private readonly SerialThread serialThread;

        private uint alarmValue = 0;
        private bool hasToSendAlarmReset = false;
        private bool alarmMuted = false;

        public AlarmThread()
        {
        }

        public uint AlarmValue
        {
            get => alarmValue;
            set
            {
                alarmValue = alarmValue | value;
            }
        }

        public bool AlarmMuted
        {
            get => alarmMuted;
            set
            {
                alarmMuted = value;
            }
        }

        private bool ShouldPlayAlarm
        {
            get => alarmValue > 0 && !alarmMuted;
        }

        public void ResetAlarm()
        {
            hasToSendAlarmReset = true;
        }

        public byte[] ComposeAlarmMessage()
        {
            uint alarmValueToSend = 0;

            if (hasToSendAlarmReset)
            {
                alarmValueToSend = (alarmValue & 0xFFFF0000) >> 16;

                if ((alarmValue & 0x0000FFFF) > 0)
                {
                    alarmValueToSend |= 1;
                }

                alarmValue = 0;
                hasToSendAlarmReset = false;
            }
            else
            {
                if (alarmValue > 0)
                {
                    alarmValueToSend = 1;
                }
            }

            return ASCIIEncoding.ASCII.GetBytes(string.Format("ALARM={0}", alarmValueToSend));            
        }

        public Task Start(CancellationToken cancellationToken)
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
    }
}
