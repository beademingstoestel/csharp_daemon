using System;
using System.Collections.Generic;
using System.Text;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace VentilatorDaemon
{
    public class WebSocketThread
    {
        private string uri;
        private WebSocketWrapper webSocketWrapper;

        private bool connected = false;
        private int id = 0;

        private List<string> settingsToSendThrough = new List<string>()
        {
            "RR",   // Respiratory rate
            "VT",   // Tidal Volume
            "PK",   // Peak Pressure
            "TS",   // Breath Trigger Threshold
            "IE",   // Inspiration/Expiration (N for 1/N)
            "PP",   // PEEP (positive end expiratory pressure)
            "ADPK", // Allowed deviation Peak Pressure
            "ADVT", // Allowed deviation Tidal Volume
            "ADPP", // Allowed deviation PEEP
            "MODE",  // Machine Mode (Volume Control / Pressure Control)
            "ACTIVE",  // Machine on / off
            "PS", // support pressure
            "RP", // ramp time
            "TP", // trigger pressure
            "MT", // mute
            "FW", // firmware version
        };

        private readonly string settingsPath = "/api/settings";

        public WebSocketThread(string uri)
        {
            this.uri = uri;
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

                                if (settingsToSendThrough.Contains(name))
                                {
                                    var propertyValue = property.Value.ToObject<float>();
                                    Console.WriteLine($"{name} {propertyValue}");
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
                catch(Exception e)
                {
                    // todo log error
                    Console.WriteLine(e.Message);
                }

                while(!cancellationToken.IsCancellationRequested)
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
                            Console.WriteLine(e.Message);
                        }
                    }

                    Thread.Sleep(10);
                }
            });
        }
    }
}
