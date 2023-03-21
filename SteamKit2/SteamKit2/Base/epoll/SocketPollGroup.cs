using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteamKit2.Base.epoll;

public sealed class SocketPollGroup<T> where T : class
{
    private readonly Dictionary<nint, T> _socketHandlers;
    private readonly ISocketPollImplementation _socketPollImpl;

    public SocketPollGroup( int maxSocketCount )
    {
        _socketPollImpl = RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
            ? new WindowsSocketPollImpl(maxSocketCount)
            : new LinuxSocketPollImpl(maxSocketCount);

        _socketHandlers = new Dictionary<nint, T>(maxSocketCount );
    }

    public bool IsEmpty()
    {
        lock ( _socketHandlers )
            return _socketHandlers.Count == 0;
    }

    public T? Get( Socket socket )
    {
        lock ( _socketHandlers )
            return _socketHandlers.TryGetValue( socket.Handle, out var handler )
                ? handler
                : null;
    }

    public IEnumerable<T> GetAll()
    {
        lock ( _socketHandlers )
            return _socketHandlers.Values.ToArray();
    }

    public void Dispose()
    {
        lock ( _socketHandlers )
        {
            _socketHandlers.Clear();
            _socketPollImpl.Dispose();
        }
    }

    public void Add( Socket socket, T handler, PollEvents events )
    {
        lock ( _socketHandlers )
        {
            _socketHandlers.Add( socket.Handle, handler );

            try
            {
                _socketPollImpl.Add( socket, events );
            }
            catch
            {
                _socketHandlers.Remove( socket.Handle );
                throw;
            }
        }
    }

    public T? Remove( Socket socket )
    {
        lock ( _socketHandlers )
        {
            _socketHandlers.Remove( socket.Handle, out var handler );
            _socketPollImpl.Remove( socket );
            return handler;
        }
    }

    public void Modify( Socket socket, PollEvents events )
    {
        lock ( _socketHandlers )
        {
            if ( _socketHandlers.ContainsKey( socket.Handle ) ) 
                _socketPollImpl.Modify( socket, events );
        }
    }

    public int Poll( int timeoutMs )
    {
        int maxCount;

        lock ( _socketHandlers )
            maxCount = _socketHandlers.Count;

        return _socketPollImpl.Poll( maxCount, timeoutMs );
    }

    public bool TryGetPollResult( int i, out T handler )
    {
        var result = _socketPollImpl.GetPollResult( i );

        lock ( _socketHandlers )
            return _socketHandlers.TryGetValue( result.SocketHandle, out handler );
    }

    public bool TryGetPollResult( int i, out T handler, out PollEvents events )
    {
        var result = _socketPollImpl.GetPollResult( i );
        events = result.Events;

        lock ( _socketHandlers )
            return _socketHandlers.TryGetValue( result.SocketHandle, out handler );
    }
}
