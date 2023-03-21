namespace SteamKit2.Base.epoll;

public struct PollResult
{
    public nint SocketHandle { get; }
    public PollEvents Events { get; }

    public PollResult( nint socketHandle, PollEvents events )
    {
        SocketHandle = socketHandle;
        Events = events;
    }
}
