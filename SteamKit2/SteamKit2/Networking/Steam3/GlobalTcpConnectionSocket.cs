using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SteamKit2.Networking.Steam3
{
    public class GlobalTcpConnectionSocket
    {
        private const EPoll.epoll_events ListenEPollEvents = EPoll.epoll_events.EPOLLIN | EPoll.epoll_events.EPOLLRDHUP | EPoll.epoll_events.EPOLLRDHUP;
        private const EPoll.epoll_events ListenAndSendEPollEvents = EPoll.epoll_events.EPOLLIN | EPoll.epoll_events.EPOLLOUT | EPoll.epoll_events.EPOLLRDHUP | EPoll.epoll_events.EPOLLRDHUP;

        public const Int32 NetCycleTimeMs = 150;
        public const Int32 MaxOpeationPerCycle = 500;

        private readonly ILogContext _log;

        public class SocketHandler
        {
            public Socket Socket { get; }
            public TcpConnection Connection { get; }
            public ConcurrentQueue<byte[]> SendQueue { get; }

            public byte[] ReceiveHeaderBuffer;
            public int ReceivedHeaderBytes;

            public byte[]? ReceiveDataBuffer;
            public int ReceivedDataBytes;

            public byte[]? SendBuffer;
            public int SentBytes;
            public int BytesToSend;

            public SocketHandler( Socket socket, TcpConnection connection )
            {
                Socket = socket;
                Connection = connection;
                SendQueue = new ConcurrentQueue<byte[]>();
                ReceiveHeaderBuffer = new byte[ 8 ];
            }

            public void OnSocketErrorSafe(ILogContext log)
            {
                try
                {
                    Connection.OnSocketError();
                }
                catch ( Exception e )
                {
                    log.LogDebug( nameof( SocketHandler ), "OnSocketError invoke failed for socket {0}: {1}", Socket.Handle, e );
                }
            }
        }

        private readonly Task _listenThread;
        private readonly byte[] _receiveBuffer;
        private readonly EPollCustomGroup<SocketHandler> _pollGroup;

        private int _operationCounter;

        public GlobalTcpConnectionSocket( ILogContext log )
        {
            _log = log;
            _receiveBuffer = System.GC.AllocateArray<byte>( 0x10000, pinned: true );
            _pollGroup = new EPollCustomGroup<SocketHandler>(2500);
            _listenThread = Task.Run( ListenThreadSafe );
        }

        public async Task<Socket> StartSocketAsync( EndPoint localEndPoint, EndPoint targetEndPoint, int timeout, CancellationToken token, TcpConnection connection)
        {
            var socket = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );

            try
            {
                socket.Bind( localEndPoint );
                socket.Blocking = false;
                socket.ReceiveTimeout = timeout;
                socket.SendTimeout = timeout;

                await socket.ConnectAsync( targetEndPoint, token );

                var socketHandler = new SocketHandler( socket, connection);
                _pollGroup.Add( socket, socketHandler, ListenEPollEvents );

                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public Task StopSocketAsync( Socket socket )
        {
            var socketHandler = _pollGroup.Remove( socket );
            socketHandler?.SendQueue.Clear();
            var disconnectTask = Task.CompletedTask;

            if ( socket.Connected )
            {
                try
                {
                    socket.Shutdown( SocketShutdown.Both );
                    disconnectTask = socket.DisconnectAsync( false ).AsTask();
                }
                catch ( Exception e )
                {
                    // Empty
                }
            }
            
            return disconnectTask.ContinueWith( _ => socket.Dispose() );
        }

        public void Send( Socket socket, byte[] data )
        {
            var handler = _pollGroup.Get(socket);
            if (handler == null)
                return;

            lock ( handler.SendQueue )
            {
                handler.SendQueue.Enqueue( data );
                _pollGroup.Modify( socket, ListenAndSendEPollEvents );
            }
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

        private async ValueTask<bool> ListenThreadCycle()
        {
            if ( _pollGroup.IsEmpty() )
                return true;
            
            int polledCount;

            try
            {
                polledCount = _pollGroup.Poll(0);
            }
            catch ( Exception ex )
            {
                _log.LogDebug( nameof( GlobalTcpConnectionSocket ), "Sockets listen error: {0}", ex );
                return false;
            }

            for ( int i = 0; i < polledCount; i++ )
            {
                var socketHandler = _pollGroup.GetPollResult( i, out var pollEvents );

                if ( ( pollEvents & (EPoll.epoll_events.EPOLLERR | EPoll.epoll_events.EPOLLRDHUP) ) != 0 )
                {
                    socketHandler.OnSocketErrorSafe( _log );
                    continue;
                }

                if ( ( pollEvents & EPoll.epoll_events.EPOLLIN ) != 0 )
                {
                    try
                    {
                        await ReceiveSocket( socketHandler );
                    }
                    catch ( Exception e )
                    {
                        _log.LogDebug( nameof( SocketHandler ), "Socket message receive failed for socket {0}: {1}", socketHandler.Socket.Handle, e );
                        socketHandler.OnSocketErrorSafe( _log );
                        continue;
                    }
                }

                if ( ( pollEvents & EPoll.epoll_events.EPOLLOUT ) != 0 )
                {
                    try
                    {
                        await SendFromSocket( socketHandler );
                    }
                    catch ( Exception e )
                    {
                        _log.LogDebug( nameof( SocketHandler ), "Socket message send failed for socket {0}: {1}", socketHandler.Socket.Handle, e );
                        socketHandler.OnSocketErrorSafe( _log );
                    }
                }
            }

            return true;
        }

        private async ValueTask SendFromSocket(SocketHandler socketHandler)
        {
            if ( socketHandler.BytesToSend > 0 )
            {
                var isBuffetSent = SendSocketBufferData( socketHandler );

                await SleepAfterOperationIfNeeded();

                if (!isBuffetSent)
                    return;
            }

            while (socketHandler.SendQueue.TryDequeue(out var data))
            {
                socketHandler.SentBytes = 0;
                socketHandler.BytesToSend = data.Length + 8;
                socketHandler.SendBuffer = new byte[ socketHandler.BytesToSend ];

                BinaryPrimitives.WriteUInt32LittleEndian( socketHandler.SendBuffer.AsSpan( 4 ), TcpConnection.MAGIC );
                BinaryPrimitives.WriteUInt32LittleEndian( socketHandler.SendBuffer, ( uint )data.Length );

                data.CopyTo( socketHandler.SendBuffer, 8 );

                var isBuffetSent = SendSocketBufferData( socketHandler );

                await SleepAfterOperationIfNeeded();

                if (!isBuffetSent)
                    break;
            }

            lock ( socketHandler.SendQueue )
            {
                if (socketHandler.SendQueue.IsEmpty)
                    _pollGroup.Modify( socketHandler.Socket, ListenEPollEvents );
            }
        }

        private bool SendSocketBufferData(SocketHandler socketHandler)
        {
            var data = socketHandler.SendBuffer.AsSpan( socketHandler.SentBytes, socketHandler.BytesToSend );

            var sentBytes = socketHandler.Socket.Send(data, SocketFlags.None, out var errorCode);

            if (errorCode != SocketError.Success)
            {
                if (IsBlockingSocketErrorCode(errorCode))
                {
                    _log.LogDebug(nameof(SocketHandler), "Got send socket blocked error: {0}", errorCode);
                    return false;
                }

                socketHandler.BytesToSend = 0;
                socketHandler.SendBuffer = null;
                socketHandler.SendQueue.Clear();

                throw new SocketException((int) errorCode);
            }

            socketHandler.SentBytes += sentBytes;
            socketHandler.BytesToSend -= sentBytes;

            if ( socketHandler.BytesToSend > 0 )
                return false;

            socketHandler.SendBuffer = null;
            return true;
        }

        private static bool IsBlockingSocketErrorCode(SocketError error) => error is SocketError.AlreadyInProgress or SocketError.WouldBlock;

        private async ValueTask ReceiveSocket(SocketHandler socketHandler)
        {
            int receivedBytes = socketHandler.Socket.Receive( _receiveBuffer, SocketFlags.None, out var errorCode);

            if ( errorCode != SocketError.Success )
            {
                if ( IsBlockingSocketErrorCode( errorCode ) )
                {
                    _log.LogDebug( nameof( SocketHandler ), "Got receive socket blocked error: {0}", errorCode );
                    return;
                }

                throw new SocketException( ( int )errorCode );
            }

            using var receiveStream = new MemoryStream(_receiveBuffer, 0, receivedBytes, writable: false);
            using var netReader = new BinaryReader( receiveStream );

            while ( receiveStream.Position < receivedBytes )
            {
                int bytesToRead;

                if ( socketHandler.ReceiveDataBuffer == null)
                {
                    if (!ReceivePacketHeader(socketHandler, netReaderAvailableBytes: receivedBytes - (int) receiveStream.Position, netReader, out var packetLen)) 
                        continue;

                    socketHandler.ReceiveDataBuffer = new byte[ packetLen ];
                    socketHandler.ReceivedDataBytes = 0;
                    bytesToRead = packetLen;
                }
                else
                {
                    bytesToRead = socketHandler.ReceiveDataBuffer.Length - socketHandler.ReceivedDataBytes;
                }

                socketHandler.ReceivedDataBytes += netReader.Read( socketHandler.ReceiveDataBuffer, socketHandler.ReceivedDataBytes, bytesToRead );

                if ( socketHandler.ReceivedDataBytes == socketHandler.ReceiveDataBuffer.Length )
                {
                    socketHandler.Connection.OnSocketMessage( socketHandler.ReceiveDataBuffer );
                    socketHandler.ReceiveDataBuffer = null;
                    socketHandler.ReceivedHeaderBytes = 0;
                    socketHandler.ReceivedDataBytes = 0;
                }

                await SleepAfterOperationIfNeeded();
            }
        }

        private static bool ReceivePacketHeader(SocketHandler socketHandler, int netReaderAvailableBytes, BinaryReader netReader, out int packetLen)
        {
            const int headerLen = 4 + 4;

            if ( socketHandler.ReceivedHeaderBytes >= headerLen )
                throw new Exception( "Header is already received" );

            uint packetMagic;

            if ( socketHandler.ReceivedHeaderBytes == 0 && netReaderAvailableBytes >= headerLen )
            {
                packetLen = ( int )netReader!.ReadUInt32();
                packetMagic = netReader.ReadUInt32();
                socketHandler.ReceivedHeaderBytes = headerLen;
            }
            else
            {
                socketHandler.ReceivedHeaderBytes += netReader.Read( socketHandler.ReceiveHeaderBuffer,
                    index: socketHandler.ReceivedHeaderBytes,
                    count: headerLen - socketHandler.ReceivedHeaderBytes );

                if ( socketHandler.ReceivedHeaderBytes < headerLen )
                {
                    packetLen = 0;
                    return false;
                }

                using var headerStream = new MemoryStream( socketHandler.ReceiveHeaderBuffer, 0, socketHandler.ReceivedHeaderBytes, writable: false );
                using var headerReader = new BinaryReader( headerStream );

                packetLen = ( int )headerReader!.ReadUInt32();
                packetMagic = headerReader.ReadUInt32();
            }

            if (packetMagic != TcpConnection.MAGIC)
                throw new Exception("Invalid TCP header");

            return true;
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
            await Task.Delay(NetCycleTimeMs);
        }
    }
}
