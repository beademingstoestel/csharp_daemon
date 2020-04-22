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

        public static void ToConsole(this byte[] message, bool asHex = false)
        {
            if (!asHex)
            {
                System.Console.WriteLine(message.ToASCIIString());
            }
            else
            {
                System.Console.WriteLine(message.ToHexString());
            }
        }

        public static string ToASCIIString(this byte[] message)
        {
            return ASCIIEncoding.ASCII.GetString(message);
        }

        public static string ToHexString(this byte[] message)
        {
            return BitConverter.ToString(message);
        }

        public static byte[] ToASCIIBytes(this string message)
        {
            return ASCIIEncoding.ASCII.GetBytes(message);
        }
    }
}
