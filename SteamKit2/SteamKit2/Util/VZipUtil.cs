﻿using System;
using System.IO;
using System.IO.Hashing;

namespace SteamKit2
{
    static class VZipUtil
    {
        private const ushort VZipHeader = 0x5A56;
        private const ushort VZipFooter = 0x767A;
        private const int HeaderLength = 7; // magic + version + timestamp/crc
        private const int FooterLength = 10; // crc + decompressed size + magic

        private const char Version = 'a';

        public static byte[] Decompress(byte[] buffer)
        {
            using MemoryStream ms = new MemoryStream( buffer );
            using BinaryReader reader = new BinaryReader( ms );
            if ( reader.ReadUInt16() != VZipHeader )
            {
                throw new Exception( "Expecting VZipHeader at start of stream" );
            }

            if ( reader.ReadChar() != Version )
            {
                throw new Exception( "Expecting VZip version 'a'" );
            }

            // Sometimes this is a creation timestamp (e.g. for Steam Client VZips).
            // Sometimes this is a CRC32 (e.g. for depot chunks).
            /* uint creationTimestampOrSecondaryCRC = */ reader.ReadUInt32();

            var properties = reader.ReadBytes( 5 );
            var compressedBytesOffset = ms.Position;

            // jump to the end of the buffer to read the footer
            ms.Seek( -FooterLength, SeekOrigin.End );
            var sizeCompressed = ms.Position - compressedBytesOffset;
            var outputCRC = reader.ReadUInt32();
            var sizeDecompressed = reader.ReadInt32();

            if ( reader.ReadUInt16() != VZipFooter )
            {
                throw new Exception( "Expecting VZipFooter at end of stream" );
            }

            // jump back to the beginning of the compressed data
            ms.Position = compressedBytesOffset;

            SevenZip.Compression.LZMA.Decoder decoder = new SevenZip.Compression.LZMA.Decoder();

            decoder.SetDecoderProperties( properties );

            var outData = new byte[ sizeDecompressed ];
            using MemoryStream outStream = new MemoryStream( outData );
            decoder.Code( ms, outStream, sizeCompressed, sizeDecompressed, null );

            if ( Crc32.HashToUInt32( outData ) != outputCRC )
            {
                throw new InvalidDataException( "CRC does not match decompressed data. VZip data may be corrupted." );
            }

            return outData;
        }

        public static byte[] Compress(byte[] buffer)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter( ms );
            byte[] crc = Crc32.Hash( buffer );

            writer.Write( VZipHeader );
            writer.Write( ( byte )Version );
            writer.Write( crc );

            int dictionary = 1 << 23;
            int posStateBits = 2;
            int litContextBits = 3;
            int litPosBits = 0;
            int algorithm = 2;
            int numFastBytes = 128;

            SevenZip.CoderPropID[] propIDs =
            [
                SevenZip.CoderPropID.DictionarySize,
                SevenZip.CoderPropID.PosStateBits,
                SevenZip.CoderPropID.LitContextBits,
                SevenZip.CoderPropID.LitPosBits,
                SevenZip.CoderPropID.Algorithm,
                SevenZip.CoderPropID.NumFastBytes,
                SevenZip.CoderPropID.MatchFinder,
                SevenZip.CoderPropID.EndMarker
            ];

            object[] properties =
            [
                dictionary,
                posStateBits,
                litContextBits,
                litPosBits,
                algorithm,
                numFastBytes,
                "bt4",
                false
            ];

            SevenZip.Compression.LZMA.Encoder encoder = new SevenZip.Compression.LZMA.Encoder();
            encoder.SetCoderProperties( propIDs, properties );
            encoder.WriteCoderProperties( ms );

            using ( MemoryStream input = new MemoryStream( buffer ) )
            {
                encoder.Code( input, ms, -1, -1, null );
            }

            writer.Write( crc );
            writer.Write( ( uint )buffer.Length );
            writer.Write( VZipFooter );

            return ms.ToArray();
        }
    }
}
