using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    public class ProcessingThread
    {
        private readonly MongoClient client;
        private readonly IMongoDatabase database;
        private readonly SerialThread serialThread;
        private readonly WebSocketThread webSocketThread;

        private readonly int BPM_TOO_LOW = 1;
        private readonly int PEEP_NOT_OK = 16;
        private readonly int PRESSURE_NOT_OK = 32;
        private readonly int VOLUME_NOT_OK = 64;
        private readonly int RESIDUAL_VOLUME_NOT_OK = 128;

        public ProcessingThread(SerialThread serialThread, WebSocketThread webSocketThread)
        {
            client = new MongoClient("mongodb://localhost/beademing");
            database = client.GetDatabase("beademing");
            this.serialThread = serialThread;
            this.webSocketThread = webSocketThread;
        }

        private List<ValueEntry> GetDocuments(string collection, int number)
        {
            var builder = Builders<ValueEntry>.Filter;

            return database.GetCollection<ValueEntry>(collection)
                .Find<ValueEntry>(builder.Empty)
                .SortByDescending(entry => entry.LoggedAt)
                .Limit(number)
                .ToList();

        }

        public Task Start(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var settings = webSocketThread.Settings;

                        var alarmBits = 0;

                        var pressureValues = GetDocuments("pressure_values", 500);
                        var volumeValues = GetDocuments("volume_values", 500);
                        var targetPressureValues = GetDocuments("targetpressure_values", 500);

                        pressureValues.Reverse();
                        volumeValues.Reverse();
                        targetPressureValues.Reverse();

                        // check breaths per minute
                        var lastValue = 0.0f;
                        var firstLoop = true;
                        var goingUp = false;

                        DateTime? startBreathingCycle = null;
                        DateTime? endBreathingCycle = null;

                        foreach (var targetPressure in targetPressureValues)
                        {
                            if (firstLoop)
                            {
                                lastValue = targetPressure.Value;
                                firstLoop = false;
                            }
                            else
                            {
                                if (targetPressure.Value > lastValue)
                                {
                                    if (!goingUp)
                                    {
                                        // positive edge
                                        startBreathingCycle = endBreathingCycle;
                                        endBreathingCycle = targetPressure.LoggedAt;
                                        goingUp = true;
                                    }
                                }
                                else if (targetPressure.Value < lastValue)
                                {
                                    goingUp = false;
                                }

                                lastValue = targetPressure.Value;
                            }
                        }

                        // do we have a full cycle
                        if (startBreathingCycle.HasValue && endBreathingCycle.HasValue)
                        {
                            var minValTargetPressure = targetPressureValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle)
                                .Min(v => v.Value);

                            var targetPressureExhale = targetPressureValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle && v.Value == minValTargetPressure)
                                .FirstOrDefault();

                            DateTime exhalemoment = targetPressureExhale.LoggedAt.AddMilliseconds(-40);

                            var breathingCycleDuration = (endBreathingCycle.Value - startBreathingCycle.Value).TotalSeconds;
                            var bpm = 60.0 / breathingCycleDuration;

                            Console.WriteLine("BPM: {0}", bpm);

                            Console.WriteLine("INHALE TIME: {0}", (exhalemoment - startBreathingCycle.Value).TotalSeconds);

                            var inhaleTime = (exhalemoment - startBreathingCycle.Value).TotalSeconds;

                            if (settings.ContainsKey("RR"))
                            {
                                if (Math.Abs(bpm - settings["RR"]) < 0.75)
                                {
                                    Console.WriteLine("No alarm bpm");
                                }
                                else
                                {
                                    Console.WriteLine("Alarm bpm");
                                    alarmBits |= BPM_TOO_LOW;
                                }
                            }

                            var tidalVolume = volumeValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle)
                                .Max(v => v.Value);

                            Console.WriteLine("Tidal volume: {0}", tidalVolume);

                            if (settings.ContainsKey("VT") && settings.ContainsKey("ADVT"))
                            {
                                if (Math.Abs(tidalVolume - settings["VT"]) < settings["ADVT"])
                                {
                                    Console.WriteLine("Volume ok");
                                }
                                else
                                {
                                    Console.WriteLine("Volume nok");
                                    alarmBits |= VOLUME_NOT_OK;
                                }
                            }

                            var residualVolume = volumeValues
                                .Where(v => v.LoggedAt >= exhalemoment && v.LoggedAt <= endBreathingCycle)
                                .Min(v => v.Value);

                            Console.WriteLine("Residual volume: {0}", residualVolume);

                            if (Math.Abs(residualVolume) < 50)
                            {
                                Console.WriteLine("Residual volume ok");
                            }
                            else
                            {
                                Console.WriteLine("Residual volume nok");
                                alarmBits |= RESIDUAL_VOLUME_NOT_OK;
                            }

                            var peakPressure = pressureValues
                               .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle)
                               .Max(v => v.Value);

                            Console.WriteLine("Peak pressure: {0}", peakPressure);                            

                            var peakPressureMoment = pressureValues
                               .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle && v.Value == peakPressure)
                               .FirstOrDefault();

                            var plateauPressure = pressureValues
                               .Where(v => v.LoggedAt >= peakPressureMoment.LoggedAt && v.LoggedAt <= exhalemoment)
                               .Min(v => v.Value); // we don't know how to call this yet

                            Console.WriteLine("Minimum peak pressure: {0}", plateauPressure);

                            if (settings.ContainsKey("PK") && settings.ContainsKey("ADPK"))
                            {
                                if (Math.Abs(peakPressure - settings["PK"]) < settings["ADPK"]
                                    && Math.Abs(plateauPressure - settings["PK"]) < settings["ADPK"])
                                {
                                    Console.WriteLine("Peak pressure ok");
                                }
                                else
                                {
                                    Console.WriteLine("Peak pressure nok");
                                    alarmBits |= PRESSURE_NOT_OK;
                                }
                            }

                            // pressure value 0.25 seconds before end
                            var firstPeepValue = pressureValues
                               .Where(v => v.LoggedAt >= endBreathingCycle.Value.AddMilliseconds(-250))
                               .FirstOrDefault();

                            var secondPeepValue = pressureValues
                               .Where(v => v.LoggedAt >= endBreathingCycle.Value.AddMilliseconds(-200))
                               .FirstOrDefault();

                            var thirdPeepValue = pressureValues
                               .Where(v => v.LoggedAt >= endBreathingCycle.Value.AddMilliseconds(-10))
                               .FirstOrDefault();

                            if (firstPeepValue != null && secondPeepValue != null && thirdPeepValue != null)
                            {
                                var gradientPeep1 = (secondPeepValue.Value - firstPeepValue.Value) / ((secondPeepValue.LoggedAt - firstPeepValue.LoggedAt).TotalMilliseconds);
                                var gradientPeep2 = (thirdPeepValue.Value - secondPeepValue.Value) / ((thirdPeepValue.LoggedAt - secondPeepValue.LoggedAt).TotalMilliseconds);

                                if (settings.ContainsKey("PP") && settings.ContainsKey("ADPP"))
                                {
                                    if (Math.Abs(thirdPeepValue.Value - settings["PP"]) < settings["ADPP"])
                                    {
                                        Console.WriteLine("PP ok");
                                        
                                    }
                                    else
                                    {
                                        if (Math.Abs(gradientPeep1) <= Math.Abs(gradientPeep2) 
                                            && Math.Abs(firstPeepValue.Value - settings["PP"]) < settings["ADPP"])
                                        {
                                            Console.WriteLine("PP gradient ok");
                                        }
                                        else
                                        {
                                            Console.WriteLine("PP gradient nok");
                                            alarmBits |= PEEP_NOT_OK;
                                        }
                                    }
                                }
                            }
                        }


                        Console.WriteLine("Alarmbits: {0}", alarmBits);
                        serialThread.AlarmValue = alarmBits;
                    }
                    catch (Exception e)
                    {
                        //Console.WriteLine(e.Message);
                    }

                    await Task.Delay(500);
                }
            });
        }
    }
}
