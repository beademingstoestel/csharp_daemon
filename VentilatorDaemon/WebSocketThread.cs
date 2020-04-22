using System;
using System.Collections.Generic;
using System.Text;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json.Linq;
using VentilatorDaemon.Helpers.WebSocket;
using VentilatorDaemon.Models;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Data.SqlTypes;
using VentilatorDaemon.Services;

namespace VentilatorDaemon
{
    public class WebSocketThread
    {
        private string uri;
        private readonly SerialThread serialThread;
        private readonly AlarmThread alarmThread;
        private readonly IApiService apiService;
        private readonly ILogger<WebSocketThread> logger;
        private WebSocketWrapper webSocketWrapper;

        private CancellationTokenSource muteResetCancellationTokenSource = new CancellationTokenSource();

        private bool connected = false;
        private int id = 0;

        private struct SettingToSend
        {
            public SettingToSend(string settingKey, bool causesAlarmInactivity)
            {
                SettingKey = settingKey;
                CausesAlarmInactivity = causesAlarmInactivity;
            }

            public string SettingKey { get; set; }
            public bool CausesAlarmInactivity { get; set; }
        }

        private List<SettingToSend> settingsToSendThrough = new List<SettingToSend>()
        {
            new SettingToSend("RR", true),   // Respiratory rate
            new SettingToSend("VT", true),   // Tidal Volume
            new SettingToSend("PK", true),   // Peak Pressure
            new SettingToSend("TS", true),   // Breath Trigger Threshold
            new SettingToSend("IE", true),   // Inspiration/Expiration (N for 1/N)
            new SettingToSend("PP", true),   // PEEP (positive end expiratory pressure)
            new SettingToSend("ADPK", true), // Allowed deviation Peak Pressure
            new SettingToSend("ADVT", true), // Allowed deviation Tidal Volume
            new SettingToSend("ADPP", true), // Allowed deviation PEEP
            new SettingToSend("MODE", false),  // Machine Mode (Volume Control / Pressure Control)
            //"ACTIVE",  // Machine on / off, do not send through automatically, we want to monitor ack
            new SettingToSend("PS", false), // support pressure
            new SettingToSend("RP", true), // ramp time
            new SettingToSend("TP", true), // trigger pressure
            new SettingToSend("MT", false), // mute
            new SettingToSend("FW", false), // firmware version
        };

        private readonly string settingsPath = "/api/settings";

        public WebSocketThread(ProgramSettings programSettings, 
            SerialThread serialThread, 
            AlarmThread alarmThread,
            IApiService apiService,
            ILoggerFactory loggerFactory)
        {
            this.uri = $"ws://{programSettings.WebServerHost}:3001";
            this.serialThread = serialThread;
            this.alarmThread = alarmThread;
            this.apiService = apiService;

            this.logger = loggerFactory.CreateLogger<WebSocketThread>();
        }

        public Dictionary<string, float> Settings
        {
            get;
        } = new Dictionary<string, float>();

        public DateTime LastSettingReceivedAt
        {
            private set;
            get;
        }

        private async Task<WebSocketWrapper> ConnectWebSocket()
        {
            return await WebSocketWrapper
                .Create(uri)
                .OnConnect(async (wsw) =>
                {
                    connected = true;
                    // send hello to initiate protocol
                    await wsw.SendMessageAsync($"{{ \"id\": {++id}, \"type\": \"hello\", \"version\": \"2\"}}");
                    // subscribe to settings
                    await SendSubscriptionRequest(wsw, settingsPath);
                })
                .OnDisconnect((wsw) =>
                {
                    connected = false;
                    return Task.CompletedTask;
                })
                .OnMessage(async (message, wsw) => await MessageReceived(message, wsw).ConfigureAwait(false))
                .ConnectAsync();
        }

        private async Task SendPing(WebSocketWrapper webSocketWrapper)
        {
            await webSocketWrapper.SendMessageAsync($"{{ \"id\": {++id}, \"type\": \"ping\"}}").ConfigureAwait(false);
        }

        private async Task SendSubscriptionRequest(WebSocketWrapper webSocketWrapper, string path)
        {
            await webSocketWrapper.SendMessageAsync($"{{ \"id\": {++id}, \"type\": \"sub\", \"path\": \"{path}\"}}").ConfigureAwait(false);
        }

        // handle messages received from websocket
        private async Task MessageReceived(string message, WebSocketWrapper webSocketWrapper)
        {
            if (!string.IsNullOrEmpty(message))
            {
                var messageObject = JObject.Parse(message);

                if (messageObject["type"].ToString() == "ping")
                {
                    // ping received as heartbeat, reply with a ping
                    await SendPing(webSocketWrapper).ConfigureAwait(false);
                }
                else if (messageObject["type"].ToString() == "pub")
                {
                    // message from one of the subscriptions, see if it is a setting
                    if (messageObject["path"].ToString() == settingsPath)
                    {
                        var settings = messageObject["message"];

                        foreach (var key in settings.Children())
                        {
                            var property = key as JProperty;

                            if (property != null)
                            {
                                var name = property.Name;
                                var propertyValue = property.Value.ToObject<float>();
                                this.Settings[name] = propertyValue;

                                if (name == "RA")
                                {
                                    alarmThread.ResetAlarm();
                                }
                                else if (name == "MT")
                                {
                                    alarmThread.AlarmMuted = propertyValue > 0.0f;

                                    // we are only allowed to mute for one minute
                                    if (propertyValue > 0.0f)
                                    {
                                        muteResetCancellationTokenSource.Cancel();
                                        muteResetCancellationTokenSource = new CancellationTokenSource();
                                        var cancellationToken = muteResetCancellationTokenSource.Token;

                                        _ = Task.Run(async () =>
                                        {
                                            await Task.Delay(60000);

                                            if (!cancellationToken.IsCancellationRequested)
                                            {
                                                logger.LogInformation("Resetting the mute state to zero");
                                                await this.apiService.SendSettingToServerAsync("MT", 0);
                                            }
                                            
                                        }, muteResetCancellationTokenSource.Token);
                                    }
                                }
                                else if (name == "ACTIVE" && propertyValue >= 0.9f && propertyValue < 1.1f) // see if float value is close to 1
                                {
                                    _ = Task.Run(() =>
                                    {
                                        // ACTIVE changed to 1, play short beep
                                        logger.LogInformation("Received setting from server: {0}={1}", name, propertyValue);
                                        var bytes = ASCIIEncoding.ASCII.GetBytes(string.Format("{0}={1}", name, propertyValue.ToString("0.00")));

                                        serialThread.WriteData(bytes, (messageId) =>
                                        {
                                            serialThread.PlayBeep();
                                            alarmThread.ResetAlarm();
                                            alarmThread.SetInactive();
                                            return Task.CompletedTask;
                                        });
                                    });
                                }
                                else if (name == "ACTIVE")
                                {
                                    LastSettingReceivedAt = DateTime.Now;

                                    _ = Task.Run(() =>
                                    {
                                        logger.LogInformation("Received setting from server: {0}={1}", name, propertyValue);
                                        var bytes = ASCIIEncoding.ASCII.GetBytes(string.Format("{0}={1}", name, propertyValue.ToString("0.00")));
                                        serialThread.WriteData(bytes);

                                        alarmThread.ResetAlarm();
                                        alarmThread.SetInactive();
                                    });
                                }

                                if (settingsToSendThrough.Any(s => s.SettingKey == name))
                                {
                                    LastSettingReceivedAt = DateTime.Now;
                                    _ = Task.Run(() =>
                                     {
                                         logger.LogInformation("Received setting from server: {0}={1}", name, propertyValue);
                                         var bytes = ASCIIEncoding.ASCII.GetBytes(string.Format("{0}={1}", name, propertyValue.ToString("0.00")));
                                         serialThread.WriteData(bytes);
                                     });
                                }
                            }
                        }
                    }
                }
            }
        }

        public Task Start(CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                try
                {
                    webSocketWrapper = await ConnectWebSocket();
                }
                catch (Exception e)
                {
                    // todo log error
                    logger.LogError(e, e.Message);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!connected)
                    {
                        try
                        {
                            webSocketWrapper = await ConnectWebSocket();
                        }
                        catch (Exception e)
                        {
                            // todo log error
                            logger.LogDebug(e.Message);
                        }
                    }

                    Thread.Sleep(10);
                }

                if (connected)
                {
                    webSocketWrapper.Disconnect();
                }
            });
        }
    }
}
