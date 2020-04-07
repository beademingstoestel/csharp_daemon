using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VentilatorDaemon
{
    // based on: https://gist.github.com/xamlmonkey/4737291
    public class WebSocketWrapper
    {
        private const int ReceiveChunkSize = 1024;
        private const int SendChunkSize = 1024;

        private readonly ClientWebSocket ws;
        private readonly Uri uri;
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken cancellationToken;

        private Func<WebSocketWrapper, Task> onConnected;
        private Func<string, WebSocketWrapper, Task> onMessage;
        private Func<WebSocketWrapper, Task> onDisconnected;

        protected WebSocketWrapper(string uri)
        {
            ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            this.uri = new Uri(uri);
            cancellationToken = cancellationTokenSource.Token;
        }

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        /// <param name="uri">The URI of the WebSocket server.</param>
        /// <returns></returns>
        public static WebSocketWrapper Create(string uri)
        {
            return new WebSocketWrapper(uri);
        }

        /// <summary>
        /// Connects to the WebSocket server.
        /// </summary>
        /// <returns></returns>
        public WebSocketWrapper Connect()
        {
            return ConnectAsync().Result;
        }

        public WebSocketWrapper Disconnect()
        {
            ws.Dispose();

            return this;
        }

        /// <summary>
        /// Set the Action to call when the connection has been established.
        /// </summary>
        /// <param name="onConnect">The Action to call.</param>
        /// <returns></returns>
        public WebSocketWrapper OnConnect(Func<WebSocketWrapper, Task> onConnect)
        {
            onConnected = onConnect;
            return this;
        }

        /// <summary>
        /// Set the Action to call when the connection has been terminated.
        /// </summary>
        /// <param name="onDisconnect">The Action to call</param>
        /// <returns></returns>
        public WebSocketWrapper OnDisconnect(Func<WebSocketWrapper, Task> onDisconnect)
        {
            onDisconnected = onDisconnect;
            return this;
        }

        /// <summary>
        /// Set the Action to call when a messages has been received.
        /// </summary>
        /// <param name="onMessage">The Action to call.</param>
        /// <returns></returns>
        public WebSocketWrapper OnMessage(Func<string, WebSocketWrapper, Task> onMessage)
        {
            this.onMessage = onMessage;
            return this;
        }

        /// <summary>
        /// Send a message to the WebSocket server.
        /// </summary>
        /// <param name="message">The message to send</param>
        public void SendMessage(string message)
        {
            SendMessageAsync(message).Wait();
        }

        public async Task SendMessageAsync(string message)
        {
            if (ws.State != WebSocketState.Open)
            {
                throw new Exception("Connection is not open.");
            }

            var messageBuffer = Encoding.UTF8.GetBytes(message);
            var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

            for (var i = 0; i < messagesCount; i++)
            {
                var offset = (SendChunkSize * i);
                var count = SendChunkSize;
                var lastMessage = ((i + 1) == messagesCount);

                if ((count * (i + 1)) > messageBuffer.Length)
                {
                    count = messageBuffer.Length - offset;
                }

                await ws.SendAsync(new ArraySegment<byte>(messageBuffer, offset, count), WebSocketMessageType.Text, lastMessage, cancellationToken);
            }
        }

        public async Task<WebSocketWrapper> ConnectAsync()
        {
            await ws.ConnectAsync(uri, cancellationToken);
            CallOnConnected();
            _ = StartListen();

            return this;
        }

        private async Task StartListen()
        {
            var buffer = new byte[ReceiveChunkSize];

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var stringResult = new StringBuilder();


                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await
                                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                            CallOnDisconnected();
                        }
                        else
                        {
                            var str = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            stringResult.Append(str);
                        }

                    } while (!result.EndOfMessage);

                    CallOnMessage(stringResult);

                }
            }
            catch (Exception)
            {
                CallOnDisconnected();
            }
            finally
            {
                ws.Dispose();
            }
        }

        private void CallOnMessage(StringBuilder stringResult)
        {
            if (onMessage != null)
            {
                Task.Run(async () => await onMessage(stringResult.ToString(), this));
            }
        }

        private void CallOnDisconnected()
        {
            if (onDisconnected != null)
            {
                Task.Run(async () => await onDisconnected(this));
            }
        }

        private void CallOnConnected()
        {
            if (onConnected != null)
            {
                Task.Run(async () => await onConnected(this));
            }
        }
    }
}
