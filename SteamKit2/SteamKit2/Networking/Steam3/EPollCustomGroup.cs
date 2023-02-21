using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteamKit2.Networking.Steam3;

public sealed class EPollCustomGroup<T> where T : class
{
    private readonly int _epHandle;
    private readonly bool _isWindows;
    private EPoll.epoll_event[] _events;
    private readonly ConcurrentDictionary<nint, GCHandle> _socketHandles;

    public EPollCustomGroup(int maxSocketCount)
    {
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _epHandle = _isWindows ? EPoll.Windows.epoll_create1(EPoll.epoll_flags.NONE) : EPoll.Linux.epoll_create1(EPoll.epoll_flags.NONE);
        _socketHandles = new ConcurrentDictionary<nint, GCHandle>( 2, maxSocketCount );
        _events = System.GC.AllocateArray<EPoll.epoll_event>( maxSocketCount, pinned: true );

        if ( _epHandle == 0)
            throw new Exception("Unable to initialize poll group");
    }

    public Boolean IsEmpty() => _socketHandles.Count == 0;

    public T? Get(Socket socket)
    {
        return _socketHandles.TryGetValue( socket.Handle, out var gcHandle ) 
            ? CastGCHandle( gcHandle ) 
            : null;
    }

    public IEnumerable<T> GetAll()
    {
        return _socketHandles.Values.Select(CastGCHandle);
    }

    public void Dispose()
    {
        foreach ( var handle in _socketHandles.Values )
            handle.Free();
        
        _socketHandles.Clear();

        _ = _isWindows ? EPoll.Windows.epoll_close( _epHandle ) : EPoll.Linux.epoll_close( _epHandle );
    }

    public void Add(Socket socket, T handler, EPoll.epoll_events events)
    {
        var ev = new EPoll.epoll_event
        {
            events = events
        };

        if ( _isWindows )
            ev.events |= EPoll.epoll_events.EPOLLOUT;

        var gcHandle = GCHandle.Alloc( handler );

        if ( !_socketHandles.TryAdd( socket.Handle, gcHandle ) )
        {
            gcHandle.Free();
            throw new Exception( "Socket already added" );
        }

        ev.data.ptr = (IntPtr) gcHandle;

        int rc = _isWindows ?
            EPoll.Windows.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_ADD, (int) socket.Handle, ref ev ) :
            EPoll.Linux.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_ADD, (int) socket.Handle, ref ev );

        if (rc != 0)
        {
            if (_socketHandles.TryRemove( socket.Handle, out _ ))
                gcHandle.Free();

            throw new Exception($"epoll_ctl add failed with error code {Marshal.GetLastWin32Error()}");
        }
    }

    public T? Remove(Socket socket)
    {
        if (!_socketHandles.TryRemove( socket.Handle, out var gcHandle ))
            return null;

        var result = CastGCHandle( gcHandle );
        gcHandle.Free();

        EPoll.epoll_event ev = default;

        _ = _isWindows
            ? EPoll.Windows.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_DEL, ( int )socket.Handle, ref ev )
            : EPoll.Linux.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_DEL, ( int )socket.Handle, ref ev );

        return result;
    }

    public void Modify( Socket socket, EPoll.epoll_events events )
    {
        if (!_socketHandles.TryGetValue(socket.Handle, out var gcHandle))
            return;

        EPoll.epoll_event ev = new EPoll.epoll_event
        {
            events = events
        };

        ev.data.ptr = (IntPtr) gcHandle;

        int rc;

        if ( _isWindows )
        {
            // do nothing
            rc = 0;
        }
        else
        {
            rc = EPoll.Linux.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_MOD, ( int )socket.Handle, ref ev );
        }

        if ( rc != 0 )
            throw new Exception( $"epoll_ctl modify failed with error code {Marshal.GetLastWin32Error()}" );
    }

    public int Poll(int timeout)
    {
        var maxEvents = _socketHandles.Count;

        if (maxEvents > _events.Length)
        {
            var newLength = Math.Max(maxEvents, _events.Length + (_events.Length >> 2));
            _events = System.GC.AllocateArray<EPoll.epoll_event>(newLength, pinned: true);
        }

        return _isWindows
            ? EPoll.Windows.epoll_wait( _epHandle, _events, maxEvents, timeout )
            : EPoll.Linux.epoll_wait( _epHandle, _events, maxEvents, timeout );
    }

    public T GetPollResult(int i, out EPoll.epoll_events events)
    {
        var handleEvent = _events[i];
        events = handleEvent.events;
        var handle = (GCHandle) _events[i].data.ptr;
        return CastGCHandle(handle);
    }

    private static T CastGCHandle(GCHandle handle)
    {
        if (handle.Target == null)
            throw new Exception("Handle was holding null value");

        return (T) handle.Target;
    }
}
