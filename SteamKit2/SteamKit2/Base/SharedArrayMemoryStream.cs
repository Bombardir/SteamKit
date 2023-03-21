using System.Collections.Generic;
using System.IO;

namespace SteamKit2;

public class SharedArrayMemoryStream : MemoryStream
{
    private const int SharedLen = 0x10000;
    private static readonly Queue<byte[]> _sharedBuffers = new();

    /*
    public SharedArrayMemoryStream() : base( GetSharedBuffer(out var len), 0, len, true, true)
    {
        SetLength(0);
    }

    protected override void Dispose( bool disposing )
    {
        FreeSharedBuffer( GetBuffer() );
        base.Dispose( disposing );
    }*/

    private static byte[] GetSharedBuffer(out int len)
    {
        lock ( _sharedBuffers )
        {
            if ( !_sharedBuffers.TryDequeue( out byte[]? buffer ) )
                buffer = new byte[ SharedLen ];

            len = buffer.Length;
            return buffer;
        }
    }

    private static void FreeSharedBuffer( byte[] buffer )
    {
        lock ( _sharedBuffers )
        {
            if ( !_sharedBuffers.Contains( buffer ))
                _sharedBuffers.Enqueue( buffer );
        }
    }
}
