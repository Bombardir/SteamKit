using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteamKit2.Base.epoll;

public class WindowsSocketPollImpl : ISocketPollImplementation
{
    public readonly struct FdSet
    {
        public readonly IntPtr[] _fd_array;

        private FdSet( IntPtr[] fd_array )
        {
            _fd_array = fd_array;
        }

        public int Capacity => _fd_array.Length - 1;

        public int Length
        {
            get => _fd_array[ 0 ].ToInt32();
            private set => _fd_array[ 0 ] = value;
        }

        public IntPtr Get( int index )
        {
            if (index >= Length)
                throw new IndexOutOfRangeException();

            return _fd_array[ index + 1 ];
        }

        public void Add( IntPtr fd )
        {
            var len = Length;

            if ( Capacity <= len )
                throw new Exception( "FdSet is ran out of capacity" );

            _fd_array[ len + 1 ] = fd;
            Length++;
        }

        public void Clear()
        {
            Length = 0;
        }

        public static FdSet Create(int capacity)
        {
            return new FdSet( new IntPtr[ capacity + 1 ] );
        }
    }

    [StructLayout( LayoutKind.Sequential )]
    internal struct TimeValue
    {
        public int Seconds;
        public int Microseconds;
    }

    [DllImport( "ws2_32.dll", SetLastError = true )]
    internal static extern int select([In] int ignoredParameter, [In, Out] IntPtr[] readfds, [In, Out] IntPtr[] writefds, [In, Out] IntPtr[] exceptfds, [In] ref TimeValue timeout );

    private readonly Dictionary<nint, PollEvents> _pollSockets;

    private FdSet _readSockets;
    private FdSet _writeSockets;
    private FdSet _errorSockets;

    public WindowsSocketPollImpl( int maxSocketCount )
    {
        _pollSockets = new Dictionary<nint, PollEvents>( maxSocketCount );
        CreateNewFdSets( maxSocketCount );
    }

    public void Dispose()
    {
        // empty
    }

    public void Add( Socket socket, PollEvents events )
    {
        lock ( _pollSockets )
            _pollSockets.Add( socket.Handle, events );
    }

    public void Remove( Socket socket )
    {
        lock ( _pollSockets )
            _pollSockets.Remove( socket.Handle );
    }

    public void Modify( Socket socket, PollEvents events )
    {
        lock ( _pollSockets )
            _pollSockets[ socket.Handle ] = events;
    }

    public int Poll( int maxCount, int timeoutMs )
    {
        if ( _readSockets.Capacity < maxCount + 1 )
        {
            int newLength = Math.Max( maxCount + 1, _readSockets.Capacity + ( _readSockets.Capacity >> 2 ) );
            CreateNewFdSets(newLength);
        }

        ClearFdSets();

        lock ( _pollSockets )
        {
            foreach ( var (socket, events) in _pollSockets )
            {
                if ( maxCount <= 0 )
                    break;

                if ( ( events & PollEvents.Read ) != 0 )
                    _readSockets.Add( socket );

                if ( ( events & PollEvents.Write ) != 0 )
                    _writeSockets.Add( socket );

                if ( ( events & PollEvents.Error ) != 0 )
                    _errorSockets.Add( socket );

                maxCount--;
            }
        }

        TimeValue timeoutValue = GetTimeoutValueFromMs( timeoutMs );
        int socketCount = -1;

        unsafe
        {
            socketCount = select( ignoredParameter: 0, _readSockets._fd_array, _writeSockets._fd_array, _errorSockets._fd_array, ref timeoutValue );
        }

        if ( (SocketError) socketCount == SocketError.SocketError )
            throw new SocketException( Marshal.GetLastWin32Error() );

#if DEBUG
        if (socketCount != (_readSockets.Length + _writeSockets.Length + _errorSockets.Length) )
            throw new Exception( "Select socket count is different from actual socket count" );
#endif

        return socketCount;
    }

    public PollResult GetPollResult( int i )
    {
        PollEvents events;
        nint socketHandle;

        if ( i < _readSockets.Length )
        {
            events = PollEvents.Read;
            socketHandle = _readSockets.Get(i);
        }
        else if ( i < _readSockets.Length + _writeSockets.Length )
        {
            events = PollEvents.Write;
            socketHandle = _writeSockets.Get(i - _readSockets.Length);
        }
        else if ( i < _readSockets.Length + _writeSockets.Length + _errorSockets.Length )
        {
            events = PollEvents.Error;
            socketHandle = _errorSockets.Get(i - _writeSockets.Length - _readSockets.Length);
        }
        else
        {
            throw new Exception( "Invalid index: out of range." );
        }

        return new PollResult( socketHandle, events );
    }

    private void ClearFdSets()
    {
        _readSockets.Clear();
        _writeSockets.Clear();
        _errorSockets.Clear();
    }

    private void CreateNewFdSets( int newCapacity )
    {
        _readSockets = FdSet.Create( newCapacity );
        _writeSockets = FdSet.Create( newCapacity );
        _errorSockets = FdSet.Create( newCapacity );
    }

    private static TimeValue GetTimeoutValueFromMs( int timeoutMs )
    {
        const int microcnv = 1000000;

        int microSeconds = timeoutMs * 1000;

        return new TimeValue()
        {
            Seconds = microSeconds / microcnv,
            Microseconds = microSeconds % microcnv
        };
    }
}
