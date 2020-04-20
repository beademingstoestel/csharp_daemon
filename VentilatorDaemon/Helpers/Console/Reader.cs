using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace VentilatorDaemon.Helpers.Console
{
    class Reader
    {
        private static Thread inputThread;
        private static AutoResetEvent getInput, gotInput;
        private static string input;

        static Reader()
        {
            getInput = new AutoResetEvent(false);
            gotInput = new AutoResetEvent(false);
            inputThread = new Thread(reader);
            inputThread.IsBackground = true;
            inputThread.Start();
        }

        private static void reader()
        {
            while (true)
            {
                getInput.WaitOne();
                input = System.Console.ReadLine();
                gotInput.Set();
            }
        }

        // omit the parameter to read a line without a timeout
        public static string ReadLine(int timeOutMillisecs = Timeout.Infinite, string defaultValue = null)
        {
            getInput.Set();
            bool success = gotInput.WaitOne(timeOutMillisecs);
            if (success)
            {
                return input;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(defaultValue))
                {
                    throw new TimeoutException("User did not provide input within the timelimit.");
                }
                else
                {
                    return defaultValue;
                }
            }
        }
    }
}
