using System;
using System.Runtime.InteropServices;

namespace SteamKit2.Base.epoll;

internal static class EPoll
{
    [Flags]
    public enum epoll_flags
    {
        NONE = 0,
        CLOEXEC = 0x02000000,
        NONBLOCK = 0x04000,
    }

    [Flags]
    public enum epoll_events : uint
    {
        EPOLLIN = 0x001,
        EPOLLPRI = 0x002,
        EPOLLOUT = 0x004,
        EPOLLRDNORM = 0x040,
        EPOLLRDBAND = 0x080,
        EPOLLWRNORM = 0x100,
        EPOLLWRBAND = 0x200,
        EPOLLMSG = 0x400,
        EPOLLERR = 0x008,
        EPOLLHUP = 0x010,
        EPOLLRDHUP = 0x2000,
        EPOLLONESHOT = 1 << 30,
        EPOLLET = unchecked(( uint )( 1 << 31 ))
    }

    public enum epoll_op
    {
        EPOLL_CTL_ADD = 1,
        EPOLL_CTL_DEL = 2,
        EPOLL_CTL_MOD = 3,
    }

    [StructLayout( LayoutKind.Explicit, Size = 8 )]
    public struct epoll_data
    {
        [FieldOffset( 0 )]
        public int fd;
        [FieldOffset( 0 )]
        public IntPtr ptr;
        [FieldOffset( 0 )]
        public uint u32;
        [FieldOffset( 0 )]
        public ulong u64;
    }

    [StructLayout( LayoutKind.Explicit, Pack = 4 )]
    public struct epoll_event
    {
        [FieldOffset( 0 )]
        public epoll_events events;
        [FieldOffset( 4 )]
        public epoll_data data;
    }

    public static class Linux
    {
        [DllImport( "libc", SetLastError = false )]
        public static extern int epoll_create1( epoll_flags flags );

        [DllImport( "libc", SetLastError = false )]
        public static extern int epoll_close( int epfd );

        [DllImport( "libc", SetLastError = false )]
        public static extern int epoll_ctl( int epfd, epoll_op op, int fd, ref epoll_event ee );

        [DllImport( "libc", SetLastError = false )]
        public static extern int epoll_wait( int epfd, [In, Out] epoll_event[] ee, int maxevents, int timeout );
    }
}
