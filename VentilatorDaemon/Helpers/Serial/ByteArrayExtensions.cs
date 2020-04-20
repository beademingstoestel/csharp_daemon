using System;
using System.Collections.Generic;
using System.Text;

namespace VentilatorDaemon.Helpers.Serial
{
    public static class ByteArrayExtensions
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
}
