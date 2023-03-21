using System;

namespace SteamKit2.Base.epoll;

[Flags]
public enum PollEvents
{
    None = 0,
    Read = 1,
    Write = 2,
    Error = 4,

    ReadAndError = Read | Error,
    All = ReadAndError | Write
}
