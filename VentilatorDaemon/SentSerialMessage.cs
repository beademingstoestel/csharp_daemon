using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    public class SentSerialMessage
    {
        public SentSerialMessage(byte[] messageBytes, DateTime lastTriedAt)
        {
            MessageBytes = messageBytes;
            LastTriedAt = lastTriedAt;
        }

        public SentSerialMessage(byte[] messageBytes, DateTime lastTriedAt, Func<byte, Task> messageAcknowledged)
        {
            MessageBytes = messageBytes;
            LastTriedAt = lastTriedAt;
            this.messageAcknowledged = messageAcknowledged;
        }

        public byte[] MessageBytes { get; set; }
        public DateTime LastTriedAt { get; set; }

        private Func<byte, Task> messageAcknowledged = null;
        public Func<byte, Task> MessageAcknowledged
        {
            get => messageAcknowledged;
        }

        public async Task TriggerMessageAcknowledgedAsync(byte messageId)
        {
            if (messageAcknowledged != null)
            {
                await messageAcknowledged(messageId);
            }
        }
    }
}
