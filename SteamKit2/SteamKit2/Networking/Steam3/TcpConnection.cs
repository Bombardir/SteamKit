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
    class TcpConnection : IConnection
    {
        public const uint MAGIC = 0x31305456; // "VT01"

        private static readonly GlobalTcpConnectionSocket _globalTcpConnection;

        static TcpConnection()
        {
            _globalTcpConnection = new GlobalTcpConnectionSocket( new DebugLogContext() );
        }

        private readonly EndPoint _localEndPoint;
        private ILogContext log;
        private Socket socket;
        private ValueTask? _disconnectTask;
        private readonly object netLock;

        public TcpConnection(EndPoint localEndPoint, ILogContext log)
        {
            _localEndPoint = localEndPoint;
            this.log = log ?? throw new ArgumentNullException( nameof( log ) );
            netLock = new object();
            socket = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );
            socket.Bind( _localEndPoint );
            socket.Blocking = false;
        }

        public event EventHandler<NetMsgEventArgs>? NetMsgReceived;

        public event EventHandler? Connected;

        public event EventHandler<DisconnectedEventArgs>? Disconnected;

        public EndPoint? CurrentEndPoint { get; private set; }

        public ProtocolTypes ProtocolTypes => ProtocolTypes.Tcp;

        public void Disconnect( bool userRequestedDisconnect )
        {
            lock ( netLock )
            {
                try
                {
                    _globalTcpConnection.StopSocketListen( socket );

                    if ( socket.Connected )
                    {
                        socket.Shutdown( SocketShutdown.Both );
                        _disconnectTask = socket.DisconnectAsync( false );
                    }
                }
                catch
                {
                    // Shutdown is throwing when the remote end closes the connection before SteamKit attempts to
                    // so this should be safe as a no-op
                    // see: https://bitbucket.org/VoiDeD/steamre/issue/41/socketexception-thrown-when-closing
                }
            }

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
                lock ( netLock )
                {
                    _globalTcpConnection.ListenSocket( socket, OnSocketMessage, OnSocketError );
                    CurrentEndPoint = socket!.RemoteEndPoint;
                }

                Connected?.Invoke( this, EventArgs.Empty );
            }
            catch ( Exception ex )
            {
                log.LogDebug( nameof( TcpConnection ), "Exception while setting up connection to {0}: {1}", CurrentEndPoint, ex );
                Disconnect( userRequestedDisconnect: false );
            }
        }

        private void OnSocketError()
        {
            Disconnect( userRequestedDisconnect: false );
        }

        private void OnSocketMessage( byte[] packData )
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

        private void TryConnect(int timeout)
        {
            DebugLog.Assert( socket != null, nameof( TcpConnection ), "socket should not be null when connecting (we hold the net lock)" );
            DebugLog.Assert( CurrentEndPoint != null, nameof( TcpConnection ), "CurrentEndPoint should be non-null when connecting." );

            try
            {
                using var timeoutTokenSource = new CancellationTokenSource( timeout );
                socket.ConnectAsync( CurrentEndPoint, timeoutTokenSource.Token ).AsTask().Wait();
            }
            catch ( Exception ex )
            {
                log.LogDebug( nameof( TcpConnection ), "Exception while connecting to {0}: {1}", CurrentEndPoint, ex );
            }

            ConnectCompleted( socket.Connected );
        }

        /// <summary>
        /// Connects to the specified end point.
        /// </summary>
        /// <param name="endPoint">The end point to connect to.</param>
        /// <param name="timeout">Timeout in milliseconds</param>
        public void Connect(EndPoint endPoint, int timeout)
        {
            lock ( netLock )
            {
                try
                {
                    _disconnectTask?.AsTask().Wait();
                }
                catch ( Exception ex )
                {
                    log.LogDebug( nameof( TcpConnection ), "Socket {0} disconnect ended with exception: {1}", CurrentEndPoint, ex );
                }

                socket.ReceiveTimeout = timeout;
                socket.SendTimeout = timeout;
                CurrentEndPoint = endPoint;

                log.LogDebug( nameof( TcpConnection ), "Connecting to {0}...", CurrentEndPoint );
                TryConnect( timeout );
            }
        }

        public void Send( byte[] data )
        {
            lock ( netLock )
            {
                if (!socket.Connected)
                {
                    log.LogDebug( nameof( TcpConnection ), "Attempting to send client data when not connected." );
                    return;
                }

                _globalTcpConnection.Send( socket, data );
            }
        }

        public IPAddress GetLocalIP()
        {
            lock (netLock)
            {
                try
                {
                    return NetHelpers.GetLocalIP(socket);
                }
                catch (Exception ex)
                {
                    log.LogDebug( nameof( TcpConnection ), "Socket exception trying to read bound IP: {0}", ex );
                    return IPAddress.None;
                }
            }
        }
    }
}
