using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SteamKit2.Networking.Steam3
{
    public class GlobalTcpConnectionSocket
    {
        public const Int32 NetCycleTime = 100;
        public const Int32 MaxOpeationPerCycle = 100;
        public const Int32 SleepBetweenOpeations = 25;

        private readonly ILogContext _log;

        public class SocketHandler
        {
            private Action _onSocketError { get; }
            public Action<byte[]> OnSocketMessage { get; }
            public Socket Socket { get; }
            public ConcurrentQueue<byte[]> SendQueue { get; }
            public byte[]? ReceiveBuffer;
            public int ReceiveBufferDataLen;

            public SocketHandler( Socket socket, Action<byte[]> onSocketMessage, Action onSocketError )
            {
                Socket = socket;
                OnSocketMessage = onSocketMessage;
                SendQueue = new ConcurrentQueue<byte[]>();
                _onSocketError = onSocketError;
            }

            public void OnSocketErrorSafe(ILogContext log)
            {
                try
                {
                    _onSocketError.Invoke();
                }
                catch ( Exception e )
                {
                    log.LogDebug( nameof( SocketHandler ), "OnSocketError invoke failed for socket {0}: {1}", Socket.Handle, e );
                }
            }
        }

        private readonly Task _listenThread;
        private readonly byte[] _receiveBuffer;
        private readonly ArraySegment<byte>[] _sendSegments;

        private readonly ConcurrentDictionary<nint, SocketHandler> _socketHandlers;
        private readonly List<Socket> _listenSocketList;
        private readonly List<Socket> _errorSocketList;
        private readonly List<Socket> _writeSocketList;

        private int _operationCounter;

        public GlobalTcpConnectionSocket( ILogContext log, int socketsCount = 2300 )
        {
            _log = log;
            _receiveBuffer = new byte[ 0x10000 ];
            _sendSegments = new ArraySegment<byte>[ 2 ];
            _sendSegments[0] = new byte[8];

            _socketHandlers = new ConcurrentDictionary<nint, SocketHandler>(2, socketsCount );
            _listenSocketList = new List<Socket>(socketsCount);
            _writeSocketList = new List<Socket>(socketsCount);
            _errorSocketList = new List<Socket>(socketsCount);
            _listenThread = Task.Run( ListenThreadSafe ).IgnoringCancellation( CancellationToken.None );

            BinaryPrimitives.WriteUInt32LittleEndian( _sendSegments[0].AsSpan(4), TcpConnection.MAGIC );
        }

        public void ListenSocket( Socket socket, Action<byte[]> onSocketMessage, Action onSocketError )
        {
            if (!_socketHandlers.TryAdd( socket.Handle, new SocketHandler( socket, onSocketMessage, onSocketError ) ))
                throw new Exception("Failed to start listen socket: socket is already added");
        }

        public void StopSocketListen( Socket socket )
        {
            if ( _socketHandlers.TryRemove( socket.Handle, out var socketHandler ) )
                socketHandler.SendQueue.Clear();
        }

        public void Send( Socket socket, byte[] data )
        {
            if ( _socketHandlers.TryGetValue( socket.Handle, out var socketHandler ) )
                socketHandler.SendQueue.Enqueue( data );
        }

        private async Task ListenThreadSafe()
        {
            while ( true )
            {
                try
                {
                    if (await ListenThreadCycle())
                        await ResetOperationCounterAndSleep();
                }
                catch ( Exception e )
                {
                    _log.LogDebug( nameof( GlobalTcpConnectionSocket ), "Socket listen thread failed with exception: {0}", e );
                }
            }
        }

        private async Task<bool> ListenThreadCycle()
        {
            if ( _socketHandlers.IsEmpty )
                return true;

            _listenSocketList.Clear();
            _errorSocketList.Clear();
            _writeSocketList.Clear();

            foreach ( var socketHandler in _socketHandlers.Values )
            {
                var socket = socketHandler.Socket;

                if (socket.Connected)
                    _listenSocketList.Add( socketHandler.Socket );
                else
                    socketHandler.OnSocketErrorSafe( _log );
            }
            
            _errorSocketList.AddRange( _listenSocketList );
            _writeSocketList.AddRange( _listenSocketList );

            try
            {
                Socket.Select( _listenSocketList, _writeSocketList, _errorSocketList, NetCycleTime * 1000 );
            }
            catch ( Exception ex )
            {
                _log.LogDebug( nameof( GlobalTcpConnectionSocket ), "Sockets listen error: {0}", ex );
                return false;
            }

            foreach ( var socket in _errorSocketList )
            {
                if ( !_socketHandlers.TryGetValue( socket.Handle, out var socketHandler ) ) 
                    continue;

                socketHandler.OnSocketErrorSafe( _log );
            }

            foreach (var socket in _listenSocketList)
            {
                if ( !_socketHandlers.TryGetValue( socket.Handle, out var socketHandler ) )
                    continue;

                try
                {
                    ReceiveSocket(socketHandler);
                }
                catch ( Exception e )
                {
                    _log.LogDebug( nameof(SocketHandler), "Socket message receive failed for socket {0}: {1}", socket.Handle, e );
                    socketHandler.OnSocketErrorSafe( _log );
                }

                await SleepAfterOperationIfNeeded();
            }

            foreach ( var socket in _writeSocketList )
            {
                if ( !_socketHandlers.TryGetValue( socket.Handle, out var socketHandler ) )
                    continue;

                while ( socketHandler.SendQueue.TryDequeue( out var data ) )
                {
                    try
                    {
                        SendSocket( data, socketHandler );
                    }
                    catch ( Exception e )
                    {
                        _log.LogDebug( nameof( SocketHandler ), "Socket message send failed for socket {0}: {1}", socketHandler.Socket.Handle, e );
                        socketHandler.OnSocketErrorSafe( _log );
                        socketHandler.SendQueue.Clear();
                        break;
                    }

                    await SleepAfterOperationIfNeeded();
                }
            }

            return true;
        }

        private void SendSocket(byte[] data, SocketHandler socketHandler)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_sendSegments[0], (uint) data.Length);
            _sendSegments[1] = data;
            socketHandler.Socket.Send(_sendSegments);
        }

        private void ReceiveSocket(SocketHandler socketHandler)
        {
            var receivedBytes = socketHandler.Socket.Receive(_receiveBuffer);

            using var memoryStream = new MemoryStream(_receiveBuffer, 0, receivedBytes, writable: false);
            using var netReader = new BinaryReader(memoryStream);

            while ( memoryStream.Position < receivedBytes )
            {
                int bytesToRead;

                if ( socketHandler.ReceiveBuffer == null )
                { 
                    var packetLen = (int) netReader!.ReadUInt32();
                    var packetMagic = netReader.ReadUInt32();

                    if ( packetMagic != TcpConnection.MAGIC )
                        throw new Exception( "Invalid TCP header" );

                    socketHandler.ReceiveBuffer = new byte[ packetLen ];
                    socketHandler.ReceiveBufferDataLen = 0;
                    bytesToRead = packetLen;
                }
                else
                {
                    bytesToRead = socketHandler.ReceiveBuffer.Length - socketHandler.ReceiveBufferDataLen;
                }

                socketHandler.ReceiveBufferDataLen += netReader.Read( socketHandler.ReceiveBuffer, socketHandler.ReceiveBufferDataLen, bytesToRead );

                if ( socketHandler.ReceiveBufferDataLen == socketHandler.ReceiveBuffer.Length )
                {
                    socketHandler.OnSocketMessage.Invoke( socketHandler.ReceiveBuffer );
                    socketHandler.ReceiveBuffer = null;
                    socketHandler.ReceiveBufferDataLen = 0;
                }
            }
        }

        private async ValueTask SleepAfterOperationIfNeeded()
        {
            _operationCounter++;
            if (_operationCounter >= MaxOpeationPerCycle)
            {
                await ResetOperationCounterAndSleep();
            }
        }

        private async ValueTask ResetOperationCounterAndSleep()
        {
            _operationCounter = 0;
            await Task.Delay(SleepBetweenOpeations);
        }
    }
}
