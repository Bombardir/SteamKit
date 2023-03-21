/*
* This file is subject to the terms and conditions defined in
* file 'license.txt', which is part of this source code package.
*/

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2.Networking.Steam3;

namespace SteamKit2
{
    public class TcpConnection : IConnection
    {
        public const uint MAGIC = 0x31305456; // "VT01"

        private static readonly GlobalTcpConnectionSocket _globalTcpConnection;

        static TcpConnection()
        {
            _globalTcpConnection = new GlobalTcpConnectionSocket( new DebugLogContext() );
        }

        private ILogContext log;

        private readonly EndPoint _localEndPoint;
        private volatile Socket? socket;
        private volatile Task? _disconnectTask;

        public TcpConnection(EndPoint localEndPoint, ILogContext log)
        {
            _localEndPoint = localEndPoint;
            this.log = log ?? throw new ArgumentNullException( nameof( log ) );
        }

        public event EventHandler<NetMsgEventArgs>? NetMsgReceived;

        public event EventHandler? Connected;

        public event EventHandler<DisconnectedEventArgs>? Disconnected;

        public EndPoint? CurrentEndPoint { get; private set; }

        public ProtocolTypes ProtocolTypes => ProtocolTypes.Tcp;

        public void Disconnect( bool userRequestedDisconnect )
        {
            var socketToDisconnect = Interlocked.Exchange( ref socket, null );

            if ( socketToDisconnect != null )
                _disconnectTask = _globalTcpConnection.StopSocketAsync( socketToDisconnect );

            Disconnected?.Invoke( this, new DisconnectedEventArgs( userRequestedDisconnect ) );
        }

        private void ConnectCompleted(bool success)
        {
            if (!success)
            {
                log.LogDebug( nameof( TcpConnection ), "Failed connecting to {0}", CurrentEndPoint );
                Disconnect( userRequestedDisconnect: false );
                return;
            }

            log.LogDebug( nameof( TcpConnection ), "Connected to {0}", CurrentEndPoint );
            DebugLog.Assert( socket != null, nameof( TcpConnection ), "Socket should be non-null after connecting." );

            try
            {
                if (socket.RemoteEndPoint != null)
                    CurrentEndPoint = socket!.RemoteEndPoint;
                Connected?.Invoke( this, EventArgs.Empty );
            }
            catch ( Exception ex )
            {
                log.LogDebug( nameof( TcpConnection ), "Exception while setting up connection to {0}: {1}", CurrentEndPoint, ex );
                Disconnect( userRequestedDisconnect: false );
            }
        }

        public void OnSocketError()
        {
            Disconnect(userRequestedDisconnect: false);
        }

        public void OnSocketMessage( byte[] packData )
        {
            try
            {
                NetMsgReceived?.Invoke( this, new NetMsgEventArgs( packData, CurrentEndPoint! ) );
            }
            catch ( Exception ex )
            {
                log.LogDebug( nameof( TcpConnection ), "Unexpected exception propogated back to NetLoop: {0}", ex );
            }
        }

        private async Task TryConnect(int timeout)
        {
            DebugLog.Assert( CurrentEndPoint != null, nameof( TcpConnection ), "CurrentEndPoint should be non-null when connecting." );

            try
            {
                using var timeoutTokenSource = new CancellationTokenSource( timeout );
                socket = await _globalTcpConnection.StartSocketAsync( _localEndPoint, CurrentEndPoint, timeout, timeoutTokenSource.Token, this );
            }
            catch ( Exception ex )
            {
                log.LogDebug( nameof( TcpConnection ), "Exception while connecting to {0}: {1}", CurrentEndPoint, ex );
            }

            ConnectCompleted( socket?.Connected ?? false);
        }

        /// <summary>
        /// Connects to the specified end point.
        /// </summary>
        /// <param name="endPoint">The end point to connect to.</param>
        /// <param name="timeout">Timeout in milliseconds</param>
        public async Task Connect(EndPoint endPoint, int timeout)
        {
            try
            {
                _disconnectTask?.Wait();
            }
            catch ( Exception ex )
            {
                log.LogDebug( nameof( TcpConnection ), "Socket {0} disconnect ended with exception: {1}", CurrentEndPoint, ex );
            }

            CurrentEndPoint = endPoint;

            log.LogDebug( nameof( TcpConnection ), "Connecting to {0}...", CurrentEndPoint );
            await TryConnect( timeout );
        }

        public void Send( byte[] data )
        {
            if ( socket is not { Connected: true } )
            {
                log.LogDebug( nameof( TcpConnection ), "Attempting to send client data when not connected." );
                return;
            }

            _globalTcpConnection.Send( socket, data );
        }

        public IPAddress GetLocalIP()
        {
            try
            {
                return socket == null ? IPAddress.None : NetHelpers.GetLocalIP( socket );
            }
            catch ( Exception ex )
            {
                log.LogDebug( nameof( TcpConnection ), "Socket exception trying to read bound IP: {0}", ex );
                return IPAddress.None;
            }
        }
    }
}
