﻿using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SimpleSocketIoClient.Utilities;

namespace SimpleSocketIoClient
{
    public sealed class WebSocketClient : IAsyncDisposable
    {
        #region Properties


        public ClientWebSocket Socket { get; private set; } = new ClientWebSocket();

        private Task ReceiveTask { get; set; }
        private CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();

        #endregion

        #region Events

        public event EventHandler<EventArgs> Connected;
        public event EventHandler<DataEventArgs<string, WebSocketCloseStatus?>> Disconnected;

        public event EventHandler<DataEventArgs<string>> AfterText;
        public event EventHandler<DataEventArgs<byte[]>> AfterBinary;
        public event EventHandler<DataEventArgs<Exception>> AfterException;

        private void OnConnected()
        {
            Connected?.Invoke(this, EventArgs.Empty);
        }

        private void OnDisconnected(string value1, WebSocketCloseStatus? value2)
        {
            Disconnected?.Invoke(this, new DataEventArgs<string, WebSocketCloseStatus?>(value1, value2));
        }

        private void OnAfterText(string value)
        {
            AfterText?.Invoke(this, new DataEventArgs<string>(value));
        }

        private void OnAfterBinary(byte[] value)
        {
            AfterBinary?.Invoke(this, new DataEventArgs<byte[]>(value));
        }

        private void OnAfterException(Exception value)
        {
            AfterException?.Invoke(this, new DataEventArgs<Exception>(value));
        }

        #endregion

        #region Public methods

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (Socket.State == WebSocketState.Open)
            {
                return;
            }

            uri = uri ?? throw new ArgumentNullException(nameof(uri));

            await Socket.ConnectAsync(uri, cancellationToken);

            ReceiveTask = Task.Run(async () => await Receive(), CancellationTokenSource.Token);

            OnConnected();
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (Socket.State != WebSocketState.Open)
            {
                return;
            }

            await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", cancellationToken);
        }

        public async Task SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            var bytes = Encoding.UTF8.GetBytes(message);

            await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (ReceiveTask != null && CancellationTokenSource != null && Socket != null)
            {
                CancellationTokenSource.Cancel();

                await ReceiveTask;
            }

            CancellationTokenSource?.Dispose();
            CancellationTokenSource = null;

            ReceiveTask?.Dispose();
            ReceiveTask = null;

            Socket?.Dispose();
            Socket = null;
        }

        #endregion

        #region Private methods

        private async Task Receive()
        {
            try
            {
                while (Socket.State == WebSocketState.Open)
                {
                    var buffer = new byte[1024 * 1024];

                    WebSocketReceiveResult result;
                    await using var stream = new MemoryStream();
                    do
                    {
                        result = await Socket.ReceiveAsync(buffer, CancellationTokenSource.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            OnDisconnected(result.CloseStatusDescription, result.CloseStatus);
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    stream.Seek(0, SeekOrigin.Begin);

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                        {
                            using var reader = new StreamReader(stream, Encoding.UTF8);
                            var message = reader.ReadToEnd();
                            OnAfterText(message);
                            break;
                        }

                        case WebSocketMessageType.Binary:
                            OnAfterBinary(stream.ToArray());
                            break;
                    }

                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                OnAfterException(exception);
            }
        }

        #endregion
    }
}
