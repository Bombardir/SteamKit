using System.Buffers;
using System.IO;

namespace SteamKit2;

public class SharedArrayMemoryStream : MemoryStream
{
    private const int _arrayLength = 0x10000;
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Create( _arrayLength, 8 );

    public SharedArrayMemoryStream() : base( _arrayPool.Rent( _arrayLength ), 0, _arrayLength, true, true)
    {
        SetLength(0);
    }

    protected override void Dispose( bool disposing )
    {
        _arrayPool.Return( GetBuffer() );
        base.Dispose( disposing );
    }
}
