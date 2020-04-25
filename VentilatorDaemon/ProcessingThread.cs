﻿using Flurl.Http;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentilatorDaemon.Models.Db;
using VentilatorDaemon.Models.Api;
using VentilatorDaemon.Services;
using Microsoft.Extensions.Logging;

namespace VentilatorDaemon
{
    public class ProcessingThread
    {
        private readonly SerialThread serialThread;
        private readonly WebSocketThread webSocketThread;
        private readonly AlarmThread alarmThread;
        private readonly IApiService apiService;
        private readonly IDbService dbService;
        private readonly ILogger<ProcessingThread> logger;

        // values for the alarm bits
        private readonly uint BPM_TOO_LOW = 1;
        private readonly uint PEEP_NOT_OK = 16;
        private readonly uint PRESSURE_NOT_OK = 32;
        private readonly uint VOLUME_NOT_OK = 64;
        private readonly uint RESIDUAL_VOLUME_NOT_OK = 128;
        private readonly uint ARDUINO_CONNECTION_NOT_OK = 256;

        public ProcessingThread(SerialThread serialThread,
            WebSocketThread webSocketThread,
            AlarmThread alarmThread,
            IApiService apiService,
            IDbService dbService,
            ILoggerFactory loggerFactory)
        {
            this.serialThread = serialThread;
            this.webSocketThread = webSocketThread;
            this.alarmThread = alarmThread;
            this.apiService = apiService;
            this.dbService = dbService;

            this.logger = loggerFactory.CreateLogger<ProcessingThread>();
        }

        private double GetMaximum(List<ValueEntry> values, Func<ValueEntry, double> comparison, long startDateTime, long endDateTime)
        {
            return values
                .Where(v => v.Value.ArduinoTime >= startDateTime && v.Value.ArduinoTime <= endDateTime)
                .Max(v => comparison(v));
        }

        private double GetMinimum(List<ValueEntry> values, Func<ValueEntry, double> comparison, long startDateTime, long endDateTime)
        {
            return values
                .Where(v => v.Value.ArduinoTime >= startDateTime && v.Value.ArduinoTime <= endDateTime)
                .Min(v => comparison(v));
        }

        private List<Tuple<long, long>> GetBreathingCyclesFromTargetPressure(List<ValueEntry> values, long maxDateTime)
        {
            List<Tuple<long, long>> breathingCycles = new List<Tuple<long, long>>();
            long? startBreathingCycle = null;
            long? endBreathingCycle = null;
            var lastValue = 0.0;
            var firstLoop = true;
            var goingUp = false;
            long previousLoggedAt = 0;

            foreach (var valueEntry in values)
            {
                if (valueEntry.Value.ArduinoTime <= maxDateTime)
                {
                    if (firstLoop)
                    {
                        lastValue = valueEntry.Value.TargetPressure;
                        previousLoggedAt = valueEntry.Value.ArduinoTime;
                        firstLoop = false;
                    }
                    else
                    {
                        if (valueEntry.Value.TargetPressure > lastValue)
                        {
                            if (!goingUp)
                            {
                                // positive edge
                                startBreathingCycle = endBreathingCycle;
                                endBreathingCycle = previousLoggedAt;
                                goingUp = true;

                                if (startBreathingCycle.HasValue && endBreathingCycle.HasValue)
                                {
                                    breathingCycles.Add(Tuple.Create(startBreathingCycle.Value, endBreathingCycle.Value));
                                }
                            }
                        }
                        else if (valueEntry.Value.TargetPressure < lastValue)
                        {
                            goingUp = false;
                        }

                        lastValue = valueEntry.Value.TargetPressure;
                        previousLoggedAt = valueEntry.Value.ArduinoTime;
                    }
                }
            }

            return breathingCycles;
        }

        public Task Start(CancellationToken cancellationToken)
        {
            CalculatedValues calculatedValues = new CalculatedValues();

            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var start = DateTime.Now;
                    try
                    {
                        var settings = webSocketThread.Settings;
                        calculatedValues.ResetValues();

                        // are we active?
                        if (!(settings.ContainsKey("ACTIVE") && settings["ACTIVE"] > 1.0f))
                        {
                            await Task.Delay(2000);
                            continue;
                        }
                        else
                        {
                            if (!alarmThread.Active && settings.ContainsKey("RR"))
                            {
                                if ((DateTime.UtcNow - alarmThread.InactiveSince).TotalSeconds > 60.0f / settings["RR"] * 5.0f)
                                {
                                    // we have been inactive for 5 breathing cycles, check alarms again
                                    alarmThread.Active = true;
                                }
                            }
                        }


                        uint alarmBits = 0;

                        var values = dbService.GetDocuments("measured_values", DateTime.UtcNow.AddSeconds(-70));

                        if (values.Count == 0)
                        {
                            // no data yet, wait for it to become available
                            await Task.Delay(2000);
                            continue;
                        }

                        // find the lowest datetime value that we have
                        var maxDateTime = values.First().Value.ArduinoTime;

                        values.Reverse();

                        // check breaths per minute
                        List<Tuple<long, long>> breathingCycles = GetBreathingCyclesFromTargetPressure(values, maxDateTime);

                        // do we have a full cycle
                        if (breathingCycles.Count > 0)
                        {
                            long startBreathingCycle = breathingCycles.Last().Item1;
                            long endBreathingCycle = breathingCycles.Last().Item2;

                            var minValTargetPressure = GetMinimum(values, (valueEntry) => valueEntry.Value.TargetPressure, startBreathingCycle, endBreathingCycle);

                            var maxValTargetPressure = GetMaximum(values, (valueEntry) => valueEntry.Value.TargetPressure, startBreathingCycle, endBreathingCycle);

                            var targetPressureExhale = values
                                .Where(v => v.Value.ArduinoTime > startBreathingCycle && v.Value.ArduinoTime <= endBreathingCycle && v.Value.TargetPressure == minValTargetPressure)
                                .FirstOrDefault();

                            long exhalemoment = targetPressureExhale.Value.ArduinoTime - 40;

                            if (targetPressureExhale == null)
                            {
                                continue;
                            }

                            var breathingCycleDuration = (endBreathingCycle - startBreathingCycle) / 1000.0;
                            var bpm = 60.0 / breathingCycleDuration;

                            calculatedValues.RespatoryRate = bpm;

                            if (settings.ContainsKey("RR"))
                            {
                                if (bpm <= settings["RR"] - 1.0)
                                {
                                    alarmBits |= BPM_TOO_LOW;
                                }
                            }

                            var inhaleTime = (exhalemoment - startBreathingCycle) / 1000.0;
                            var exhaleTime = (endBreathingCycle - exhalemoment) / 1000.0;

                            calculatedValues.IE = inhaleTime / breathingCycleDuration;

                            var tidalVolume = GetMaximum(values, (valueEntry) => valueEntry.Value.Volume, startBreathingCycle, endBreathingCycle);

                            if (settings.ContainsKey("VT") && settings.ContainsKey("ADVT"))
                            {
                                if (Math.Abs(tidalVolume - settings["VT"]) > settings["ADVT"])
                                {
                                    alarmBits |= VOLUME_NOT_OK;
                                }
                            }

                            calculatedValues.TidalVolume = tidalVolume;

                            var residualVolume = GetMinimum(values, (valueEntry) => valueEntry.Value.Volume, exhalemoment, endBreathingCycle - 80);

                            if (Math.Abs(residualVolume) > 50)
                            {
                                alarmBits |= RESIDUAL_VOLUME_NOT_OK;
                            }

                            var peakPressureMoment = values
                               .Where(v => v.Value.ArduinoTime >= startBreathingCycle && v.Value.ArduinoTime <= exhalemoment)
                               .Aggregate((i1, i2) => i1.Value.Pressure > i2.Value.Pressure ? i1 : i2);

                            var plateauMinimumPressure = GetMinimum(values, (valueEntry) => valueEntry.Value.Pressure, peakPressureMoment.Value.ArduinoTime, exhalemoment);

                            if (settings.ContainsKey("ADPK"))
                            {
                                if (!(Math.Abs(peakPressureMoment.Value.Pressure - maxValTargetPressure) < settings["ADPK"]
                                    && Math.Abs(plateauMinimumPressure - maxValTargetPressure) < settings["ADPK"]))
                                {
                                    alarmBits |= PRESSURE_NOT_OK;
                                }
                            }

                            calculatedValues.PressurePlateau = plateauMinimumPressure;

                            //did we have a trigger within this cycle?
                            var triggerMoment = values
                                .FirstOrDefault(p => p.Value.ArduinoTime >= exhalemoment && p.Value.ArduinoTime <= endBreathingCycle && p.Value.Trigger > 0.0f);
                            var endPeep = endBreathingCycle;
                            if (triggerMoment != null)
                            {
                                endPeep = triggerMoment.Value.ArduinoTime - 50;
                            }

                            if (settings.ContainsKey("PP") && settings.ContainsKey("ADPP"))
                            {
                                bool firstPeepPressureIteration = true;
                                ValueEntry previousPoint = null;
                                List<float> slopes = new List<float>();
                                int plateauCounter = 0;
                                bool foundPlateau = false;

                                var pressureExhaleValues = values
                                    .Where(v => v.Value.ArduinoTime >= exhalemoment + 100 && v.Value.ArduinoTime <= endPeep)
                                    .OrderByDescending(v => v.Value.ArduinoTime)
                                    .ToList();

                                for (int i = 0; i < pressureExhaleValues.Count - 2; i += 2)
                                {
                                    var valueEntry = pressureExhaleValues[i];

                                    // if last value is above PEEP, assume everything is ok
                                    if (firstPeepPressureIteration)
                                    {
                                        if (valueEntry.Value.Pressure > settings["PP"] - settings["ADPP"])
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
                                        var gradient = (previousPoint.Value.Pressure - valueEntry.Value.Pressure) / ((float)(previousPoint.Value.ArduinoTime - valueEntry.Value.ArduinoTime) / 1000.0f);

                                        if (gradient > -1.0f)
                                        {
                                            plateauCounter++;

                                            if (plateauCounter == 3)
                                            {
                                                if (valueEntry.Value.Pressure > settings["PP"] - settings["ADPP"])
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
                                    previousPoint = valueEntry;
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

                        if (serialThread.ConnectionState != ConnectionState.Connected)
                        {
                            alarmBits |= ARDUINO_CONNECTION_NOT_OK;
                        }

                        if (alarmThread.Active)
                        {
                            alarmThread.SetPCAlarmBits(alarmBits);
                        }

                        
                        if (breathingCycles.Count > 1)
                        {
                            foreach (var breathingCycle in breathingCycles)
                            {
                                var tidalVolume = values
                                    .Where(v => v.Value.ArduinoTime >= breathingCycle.Item1 && v.Value.ArduinoTime <= breathingCycle.Item2)
                                    .Max(v => v.Value.Volume);

                                calculatedValues.VolumePerMinute += tidalVolume / 1000.0;
                            }

                            calculatedValues.VolumePerMinute = calculatedValues.VolumePerMinute / ((breathingCycles.Last().Item2 - breathingCycles.First().Item1) / 1000.0) * 60.0;
                        }


                        await apiService.SendCalculatedValuesToServerAsync(calculatedValues);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }

                    var timeSpent = (DateTime.Now - start).TotalMilliseconds;
                    // Console.WriteLine("Time taken processing: {0}", timeSpent);
                    await Task.Delay(Math.Max(1, 500 - (int)timeSpent));
                }
            });
        }
    }
}
