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
using System.Reflection.Metadata;
using MongoDB.Driver;
using System.Collections.Concurrent;

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

        private List<IVentilatorSetting> settingsToSendThrough = VentilatorSettingsFactory.GetVentilatorSettings();

        private readonly string settingsPath = "/api/settings";

        public WebSocketThread(ProgramSettings programSettings,
            SerialThread serialThread,
            AlarmThread alarmThread,
            IApiService apiService,
            ILoggerFactory loggerFactory)
        {
            this.uri = $"ws://{programSettings.WebServerHost}:{programSettings.WebServerPort}";
            this.serialThread = serialThread;
            this.alarmThread = alarmThread;
            this.apiService = apiService;

            this.logger = loggerFactory.CreateLogger<WebSocketThread>();
        }

        public ConcurrentDictionary<string, object> Settings
        {
            get;
        } = new ConcurrentDictionary<string, object>();

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
                                var handled = false;
                                var name = property.Name;
                                var setting = settingsToSendThrough.FirstOrDefault(s => s.SettingKey == name);

                                if (setting != null)
                                {
                                    object propertyValue = property.Value.ToObject(setting.SettingType);
                                    this.Settings.AddOrUpdate(name, propertyValue, (name, value) => propertyValue);

                                    if (name == "RA")
                                    {
                                        alarmThread.ResetAlarm();
                                    }
                                    else if (name == "MT")
                                    {
                                        alarmThread.AlarmMuted = (int)propertyValue > 0;

                                        // we are only allowed to mute for one minute
                                        if ((int)propertyValue > 0)
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
                                    else if (name == "ACTIVE" && (int)propertyValue == 1)
                                    {
                                        _ = Task.Run(() =>
                                        {
                                            // ACTIVE changed to 1, play short beep
                                            logger.LogInformation("Received setting from server: {0}={1}", name, propertyValue);
                                            var bytes = setting.ToBytes(propertyValue);

                                            serialThread.WriteData(bytes, (messageId) =>
                                            {
                                                logger.LogDebug("The machine should have played a beep after receiving active 1");
                                                serialThread.PlayBeep();
                                                alarmThread.ResetAlarm();
                                                alarmThread.SetInactive();
                                                return Task.CompletedTask;
                                            });
                                        });

                                        handled = true;

                                        serialThread.MachineState = (int)propertyValue;
                                    }
                                    else if (name == "ACTIVE")
                                    {
                                        LastSettingReceivedAt = DateTime.Now;

                                        _ = Task.Run(() =>
                                        {
                                            logger.LogInformation("Received setting from server: {0}={1}", name, propertyValue);
                                            var bytes = setting.ToBytes(propertyValue);
                                            serialThread.WriteData(bytes);

                                            alarmThread.ResetAlarm();
                                            alarmThread.SetInactive();
                                        });
                                        handled = true;

                                        serialThread.MachineState = (int)propertyValue;
                                    }

                                    if (!handled && setting.SendToArduino)
                                    {
                                        LastSettingReceivedAt = DateTime.Now;
                                        _ = Task.Run(() =>
                                         {
                                             logger.LogInformation("Received setting from server: {0}={1}", name, propertyValue);
                                             var bytes = setting.ToBytes(propertyValue);
                                             serialThread.WriteData(bytes);
                                         });

                                        if (setting.CausesAlarmInactivity)
                                        {
                                            alarmThread.SetInactive();
                                        }
                                    }
                                } // end if setting != null
                            } // end if property != null
                        } // end foreach
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
