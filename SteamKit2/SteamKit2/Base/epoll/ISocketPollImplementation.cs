using System;
using System.Net.Sockets;

namespace SteamKit2.Base.epoll;

public interface ISocketPollImplementation : IDisposable
{
    void Add( Socket socket, PollEvents events );
    void Remove( Socket socket );
    void Modify( Socket socket, PollEvents events );
    int Poll( int maxCount, int timeoutMs );
    PollResult GetPollResult( int i );
}
