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

        private readonly uint BPM_TOO_LOW = 1;
        private readonly uint PEEP_NOT_OK = 16;
        private readonly uint PRESSURE_NOT_OK = 32;
        private readonly uint VOLUME_NOT_OK = 64;
        private readonly uint RESIDUAL_VOLUME_NOT_OK = 128;

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

                        // are we active?
                        if (!(settings.ContainsKey("ACTIVE") && settings["ACTIVE"] > 1.0f))
                        {
                            // make sure we stop playing the alarm
                            serialThread.ResetAlarm();
                            await Task.Delay(2000);
                            continue;
                        }

                        uint alarmBits = 0;

                        var targetPressureValues = GetDocuments("targetpressure_values", 500);
                        var pressureValues = GetDocuments("pressure_values", 500);
                        var volumeValues = GetDocuments("volume_values", 500);
                        var triggerValues = GetDocuments("trigger_values", 500);

                        if (pressureValues.Count == 0 || volumeValues.Count == 0 || targetPressureValues.Count == 0)
                        {
                            // no data yet, wait for it to become available
                            await Task.Delay(2000);
                            continue;
                        }

                        // find the lowest datetime value that we all have
                        var maxDateTime = targetPressureValues.First().LoggedAt;

                        if (volumeValues.Last().LoggedAt < maxDateTime)
                        {
                            maxDateTime = volumeValues.First().LoggedAt;
                        }

                        if (pressureValues.Last().LoggedAt < maxDateTime)
                        {
                            maxDateTime = pressureValues.First().LoggedAt;
                        }

                        if (triggerValues.Last().LoggedAt < maxDateTime)
                        {
                            maxDateTime = pressureValues.First().LoggedAt;
                        }

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
                            if (targetPressure.LoggedAt <= maxDateTime)
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
                        }

                        // do we have a full cycle
                        if (startBreathingCycle.HasValue && endBreathingCycle.HasValue)
                        {
                            var minValTargetPressure = targetPressureValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle)
                                .Min(v => v.Value);

                            var maxValTargetPressure = targetPressureValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle)
                                .Max(v => v.Value);

                            var targetPressureExhale = targetPressureValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle && v.Value == minValTargetPressure)
                                .FirstOrDefault();

                            DateTime exhalemoment = targetPressureExhale.LoggedAt.AddMilliseconds(-40);

                            var breathingCycleDuration = (endBreathingCycle.Value - startBreathingCycle.Value).TotalSeconds;
                            var bpm = 60.0 / breathingCycleDuration;

                            var inhaleTime = (exhalemoment - startBreathingCycle.Value).TotalSeconds;

                            var tidalVolume = volumeValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle)
                                .Max(v => v.Value);

                            if (settings.ContainsKey("VT") && settings.ContainsKey("ADVT"))
                            {
                                if (Math.Abs(tidalVolume - settings["VT"]) > settings["ADVT"])
                                {
                                    alarmBits |= VOLUME_NOT_OK;
                                }
                            }

                            var residualVolume = volumeValues
                                .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle.Value)
                                .Min(v => v.Value);

                            if (Math.Abs(residualVolume) > 50)
                            {
                                alarmBits |= RESIDUAL_VOLUME_NOT_OK;
                            }

                            var peakPressureMoment = pressureValues
                               .Where(v => v.LoggedAt >= startBreathingCycle && v.LoggedAt <= endBreathingCycle.Value)
                               .Aggregate((i1, i2) => i1.Value > i2.Value ? i1 : i2);

                            var plateauMinimumPressure = pressureValues
                               .Where(v => v.LoggedAt >= peakPressureMoment.LoggedAt && v.LoggedAt <= exhalemoment)
                               .Min(v => v.Value);

                            if (settings.ContainsKey("ADPK"))
                            {
                                if (!(Math.Abs(peakPressureMoment.Value - maxValTargetPressure) < settings["ADPK"]
                                    && Math.Abs(plateauMinimumPressure - maxValTargetPressure) < settings["ADPK"]))
                                {
                                    alarmBits |= PRESSURE_NOT_OK;
                                }
                            }

                            //did we have a trigger within this cycle?
                            var triggerMoment = triggerValues
                                .FirstOrDefault(p => p.LoggedAt >= exhalemoment && p.LoggedAt <= endBreathingCycle.Value && p.Value > 0.0f);
                            var endPeep = endBreathingCycle.Value;
                            if (triggerMoment != null)
                            {
                                endPeep = triggerMoment.LoggedAt.AddMilliseconds(-50);
                            }

                            if (settings.ContainsKey("PP") && settings.ContainsKey("ADPP"))
                            {
                                bool firstPeepPressureIteration = true;
                                ValueEntry previousPoint = null;
                                List<float> slopes = new List<float>();
                                int plateauCounter = 0;
                                bool foundPlateau = false;

                                var pressureExhaleValues = pressureValues
                                    .Where(v => v.LoggedAt >= exhalemoment.AddMilliseconds(100) && v.LoggedAt <= endPeep)
                                    .OrderByDescending(v => v.LoggedAt)
                                    .ToList();

                                for (int i = 0; i < pressureExhaleValues.Count - 2; i += 2)
                                {
                                    var pressureValue = pressureExhaleValues[i];

                                    // if last value is above PEEP, assume everything is ok
                                    if (firstPeepPressureIteration)
                                    {
                                        if (pressureValue.Value > settings["PP"] - settings["ADPP"])
                                        {
                                            foundPlateau = true;
                                            break;
                                        }

                                        firstPeepPressureIteration = false;
                                    }

                                    // we are still here, so last value was not in peep threshold
                                    // go back until we are above PEEP again and start calculating slope
                                    // when the average slope is steadily declining, we have a leak

                                    if (previousPoint != null)
                                    {
                                        var gradient = (previousPoint.Value - pressureValue.Value) / ((float)(previousPoint.LoggedAt - pressureValue.LoggedAt).TotalSeconds);

                                        if (gradient > -5.0f)
                                        {
                                            plateauCounter++;

                                            if (plateauCounter == 3)
                                            {
                                                if (pressureValue.Value > settings["PP"] - settings["ADPP"])
                                                {
                                                    foundPlateau = true;
                                                    break;
                                                }
                                                else
                                                {
                                                    plateauCounter = 0;
                                                }
                                            }

                                        }
                                        else
                                        {
                                            plateauCounter = 0;
                                        }
                                    }
                                    previousPoint = pressureValue;
                                }

                                if (!foundPlateau)
                                {
                                    // all points are below peep, clearly we should raise an alarm
                                    alarmBits |= PEEP_NOT_OK;
                                }
                            }

                            // only for debug purposes, send the moments to the frontend
                            //await serialThread.SendSettingToServer("breathingCycleStart", startBreathingCycle.Value.);
                        }
                        else if (startBreathingCycle.HasValue && startBreathingCycle.Value < DateTime.UtcNow.AddSeconds(-10))
                        {
                            // our last complete cycle is already more than 10 seconds ago
                            alarmBits |= BPM_TOO_LOW;
                        }


                        Console.WriteLine("Alarmbits: {0}", alarmBits);
                        serialThread.AlarmValue = alarmBits;


                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }

                    await Task.Delay(500);
                }
            });
        }
    }
}
