using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SteamKit2.Base.epoll;

internal class LinuxSocketPollImpl: ISocketPollImplementation
{
    private readonly int _epHandle;
    private EPoll.epoll_event[] _events;

    public LinuxSocketPollImpl(int maxSocketCount)
    {
        _epHandle = EPoll.Linux.epoll_create1( EPoll.epoll_flags.NONE );
        _events = new EPoll.epoll_event[maxSocketCount];
    }

    public void Dispose()
    {
        _ = EPoll.Linux.epoll_close( _epHandle );
    }

    public void Add( Socket socket, PollEvents events )
    {
        var ev = CreateEPollEvent( socket, events );

        var rc = EPoll.Linux.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_ADD, ( int )socket.Handle, ref ev );

        if ( rc != 0 )
            throw new Exception( $"epoll_ctl add failed with error code {Marshal.GetLastWin32Error()}" );
    }

    public void Remove( Socket socket )
    {
        EPoll.epoll_event ev = default;
        _ = EPoll.Linux.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_DEL, ( int )socket.Handle, ref ev );

    }

    public void Modify( Socket socket, PollEvents events )
    {
        var ev = CreateEPollEvent(socket, events);
        int rc = EPoll.Linux.epoll_ctl( _epHandle, EPoll.epoll_op.EPOLL_CTL_MOD, ( int )socket.Handle, ref ev );
        if ( rc != 0 )
            throw new Exception( $"epoll_ctl modify failed with error code {Marshal.GetLastWin32Error()}" );
    }

    public int Poll(int maxCount, int timeoutMs)
    {
        if ( maxCount > _events.Length )
        {
            var newLength = Math.Max( maxCount, _events.Length + ( _events.Length >> 2 ) );
            _events = new EPoll.epoll_event[newLength];
        }

        return EPoll.Linux.epoll_wait( _epHandle, _events, maxCount, timeoutMs );
    }

    public PollResult GetPollResult( int i )
    {
        var handleEvent = _events[ i ];
        PollEvents events = PollEvents.None;

        if ( (handleEvent.events & EPoll.epoll_events.EPOLLIN) != 0 )
            events |= PollEvents.Read;

        if ( (handleEvent.events & EPoll.epoll_events.EPOLLOUT) != 0 )
            events |= PollEvents.Write;

        if ( (handleEvent.events & (EPoll.epoll_events.EPOLLRDHUP | EPoll.epoll_events.EPOLLERR)) != 0)
            events |= PollEvents.Error;

        return new PollResult(handleEvent.data.fd, events);
    }

    private static EPoll.epoll_event CreateEPollEvent( Socket socket, PollEvents events )
    {
        var ev = new EPoll.epoll_event();

        ev.data.fd = ( int )socket.Handle;

        if ( ( events & PollEvents.Read ) != 0 )
            ev.events |= EPoll.epoll_events.EPOLLIN;

        if ( ( events & PollEvents.Write ) != 0 )
            ev.events |= EPoll.epoll_events.EPOLLOUT;

        if ( ( events & PollEvents.Error ) != 0 )
            ev.events |= EPoll.epoll_events.EPOLLRDHUP | EPoll.epoll_events.EPOLLERR;

        return ev;
    }
}
