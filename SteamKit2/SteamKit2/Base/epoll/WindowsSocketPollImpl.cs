using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace SteamKit2.Base.epoll;

public class WindowsSocketPollImpl : ISocketPollImplementation
{
    private readonly Dictionary<Socket, PollEvents> _pollSockets;

    private readonly List<Socket> _readSockets;
    private readonly List<Socket> _writeSockets;
    private readonly List<Socket> _errorSockets;

    public WindowsSocketPollImpl( int maxSocketCount )
    {
        _pollSockets = new Dictionary<Socket, PollEvents>( maxSocketCount );
        _readSockets = new List<Socket>( maxSocketCount );
        _writeSockets = new List<Socket>( maxSocketCount );
        _errorSockets = new List<Socket>( maxSocketCount );
    }

    public void Dispose()
    {
        // empty
    }

    public void Add( Socket socket, PollEvents events )
    {
        lock ( _pollSockets )
            _pollSockets.Add( socket, events );
    }

    public void Remove( Socket socket )
    {
        lock ( _pollSockets )
            _pollSockets.Remove( socket );
    }

    public void Modify( Socket socket, PollEvents events )
    {
        lock ( _pollSockets )
            _pollSockets[ socket ] = events;
    }

    public int Poll( int maxCount, int timeoutMs )
    {
        _readSockets.Clear();
        _writeSockets.Clear();
        _errorSockets.Clear();

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

        Socket.Select( _readSockets, _writeSockets, _errorSockets, timeoutMs * 1000 );

        return _readSockets.Count + _writeSockets.Count + _errorSockets.Count;
    }

    public PollResult GetPollResult( int i )
    {
        PollEvents events;
        Socket socket;

        if ( i < _readSockets.Count )
        {
            events = PollEvents.Read;
            socket = _readSockets[ i ];
        }
        else if ( i < _readSockets.Count + _writeSockets.Count )
        {
            events = PollEvents.Write;
            socket = _writeSockets[ i - _readSockets.Count ];
        }
        else if ( i < _readSockets.Count + _writeSockets.Count + _errorSockets.Count )
        {
            events = PollEvents.Error;
            socket = _errorSockets[ i - _writeSockets.Count - _readSockets.Count ];
        }
        else
        {
            throw new Exception( "Invalid index: out of range." );
        }

        return new PollResult( socket.Handle, events );
    }
}
