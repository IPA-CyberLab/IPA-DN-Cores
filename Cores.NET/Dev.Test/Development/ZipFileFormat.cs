// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

// Author: Daiyuu Nobori
// ZIP ファイルフォーマットヘッダ定義

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.CompilerServices;

namespace IPA.Cores.Basic
{
    // ZIP ファイル構造の参考ドキュメント類
    // https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
    // https://ja.wikipedia.org/wiki/ZIP_(%E3%83%95%E3%82%A1%E3%82%A4%E3%83%AB%E3%83%95%E3%82%A9%E3%83%BC%E3%83%9E%E3%83%83%E3%83%88)
    // https://atrsas.exblog.jp/9661531/
    // https://atrsas.exblog.jp/9671353/
    // https://atrsas.exblog.jp/9675346/
    // https://atrsas.exblog.jp/9677162/
    // https://atrsas.exblog.jp/9683786/
    //
    // 2019-08-26 メモ
    // 現状は ZIP Version 4.5 に対応している。
    // それ以降は対応していない。

    public static class ZipConsts
    {
        public const uint LocalFileHeaderSignature = 0x04034b50;
        public const uint DataDescriptorSignature = 0x08074b50;
        public const uint CentralFileHeaderSignature = 0x02014B50;
        public const uint EndOfCentralDirectorySignature = 0x06054b50;
        public const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
        public const uint Zip64EndOfCentralDirectoryLocatorSignature = 0x07064b50;

        public const int MaxFileNameSize = 65535;
    }

    // 4.4.4 general purpose bit flag: (2 bytes)
    [Flags]
    public enum ZipGeneralPurposeFlags : ushort
    {
        None = 0,
        Encrypted = 1,                  // bit 0
        Bit1 = 2,                       // bit 1
        Bit2 = 4,                       // bit 2
        UseDataDescriptor = 8,          // bit 3
        Bit4 = 16,                      // bit 4
        CompressedPatchedData = 32,     // bit 5
        StrongEncryption = 64,          // bit 6
        Reserved_0 = 128,               // bit 7
        Reserved_1 = 256,               // bit 8
        Reserved_2 = 512,               // bit 9
        Reserved_3 = 1024,              // bit 10
        Utf8 = 2048,                    // bit 11
        Reserved_4 = 4096,              // bit 12
        EncryptCentralDirectory = 8192, // bit 13
    }

    // 4.4.5 compression method: (2 bytes)
    [Flags]
    public enum ZipCompressionMethods : ushort
    {
        Raw = 0,
        Deflated = 8,
    }

    // 4.4.3 version needed to extract (2 bytes)
    [Flags]
    public enum ZipFileSystemTypes : byte
    {
        MsDos = 0,
        UNIX = 1,
        Ntfs = 10,
        Darwin = 19,
    }

    // 4.4.2 version made by (2 bytes)
    [Flags]
    public enum ZipFileVersions : byte
    {
        Ver2_0 = 20,
        Ver4_5 = 45,
    }

    // ローカルファイルヘッダ
    // 4.3.7  Local file header
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZipLocalFileHeader
    {
        public uint Signature;
        public ZipFileVersions NeedVersion;
        public byte Reserved;
        public ZipGeneralPurposeFlags GeneralPurposeFlag;
        public ZipCompressionMethods CompressionMethod;
        public ushort LastModFileTime;
        public ushort LastModFileDate;
        public uint Crc32;
        public uint CompressedSize;
        public uint UncompressedSize;
        public ushort FileNameSize;
        public ushort ExtraFieldSize;

        // Followed by:
        // [file name (variable size)]
        // [extra field (variable size)]
    }

    // 4.5.2 The current Header ID mappings defined by PKWARE
    [Flags]
    public enum ZipExtHeaderIDs : ushort
    {
        None = 0,
        Zip64 = 0x0001,
    }

    // データディスクリプタ
    // 4.3.9  Data descriptor
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZipDataDescriptor
    {
        public uint Signature;
        public uint Crc32;
        public uint CompressedSize;
        public uint UncompressedSize;
    }

    // セントラルディレクトリファイルヘッダ
    // 4.3.12  Central directory structure
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZipCentralFileHeader
    {
        public uint Signature;
        public ZipFileVersions MadeVersion;
        public ZipFileSystemTypes MadeFileSystemType;
        public ZipFileVersions NeedVersion;
        public byte Reserved;
        public ZipGeneralPurposeFlags GeneralPurposeFlag;
        public ZipCompressionMethods CompressionMethod;
        public ushort LastModFileTime;
        public ushort LastModFileDate;
        public uint Crc32;
        public uint CompressedSize;
        public uint UncompressedSize;
        public ushort FileNameSize;
        public ushort ExtraFieldSize;
        public ushort FileCommentSize;
        public ushort DiskNumberStart;
        public ushort InternalFileAttributes;
        public uint ExternalFileAttributes;
        public uint RelativeOffsetOfLocalHeader;

        // Followed by:
        // [file name(variable size)]
        // [extra field(variable size)]
        // [file comment(variable size)]
    }

    // 4.5.3 Zip64 Extended Information Extra Field (0x0001)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZipExtZip64Field
    {
        public ulong UncompressedSize;
        public ulong CompressedSize;
        public ulong RelativeOffsetOfLocalHeader;
        public uint DiskNumberStart;
    }

    // エンドオブセントラルディレクトリレコード
    // 4.3.16  End of central directory record
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZipEndOfCentralDirectoryRecord
    {
        public uint Signature;
        public ushort NumberOfThisDisk;
        public ushort DiskNumberStart;
        public ushort NumberOfCentralDirectoryOnThisDisk;
        public ushort TotalNumberOfCentralDirectory;
        public uint SizeOfCentralDirectory;
        public uint OffsetStartCentralDirectory;
        public ushort CommentLength;

        // Followed by:
        // [.ZIP file comment (variable size)]
    }

    // ZIP64 エンドオブセントラルディレクトリレコード
    // 4.3.14  Zip64 end of central directory record
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct Zip64EndOfCentralDirectoryRecord
    {
        public uint Signature;
        public ulong SizeOfZip64EndOfCentralDirectoryRecord;        // 4.3.14.1 Size = SizeOfFixedFields + SizeOfVariableData - 12.
        public ZipFileVersions MadeVersion;
        public ZipFileSystemTypes MadeFileSystemType;
        public ZipFileVersions NeedVersion;
        public byte Reserved;
        public uint NumberOfThisDisk;
        public uint DiskNumberStart;
        public ulong TotalNumberOfCentralDirectory;
        public ulong TotalNumberOfEntriesOnCentralDirectory;
        public ulong SizeOfCentralDirectory;
        public ulong OffsetStartCentralDirectory;

        // Followed by:
        // [zip64 extensible data sector    (variable size)]
    }

    // ZIP64 エンドオブセントラルディレクトリロケータ
    // 4.3.15 Zip64 end of central directory locator
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct Zip64EndOfCentralDirectoryLocator
    {
        public uint Signature;
        public uint NumberOfThisDisk;
        public ulong OffsetStartZip64CentralDirectoryRecord;
        public uint TotalNumberOfDisk;
    }

    public class ZipExtraFieldsList : KeyValueList<ZipExtHeaderIDs, ReadOnlyMemory<byte>>
    {
        public void Add<T>(ZipExtHeaderIDs id, in T data, int dataSize = DefaultSize)
            where T : unmanaged
        {
            ReadOnlyMemory<byte> a = Unsafe.AsRef(in data)._CopyToMemory();

            base.Add(id, a);
        }

        public Memory<byte> ToMemory()
        {
            checked
            {
                MemoryBuffer<byte> buf = new MemoryBuffer<byte>();

                foreach (var item in this)
                {
                    // Header
                    buf.WriteUInt16((ushort)item.Key, littleEndian: true);
                    buf.WriteUInt16((ushort)item.Value.Length, littleEndian: true);

                    // Data
                    buf.Write(item.Value);
                }

                return buf.Memory;
            }
        }
    }
}

#endif

