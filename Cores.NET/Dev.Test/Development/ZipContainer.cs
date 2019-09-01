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
// ZIP 形式のファイルコンテナ操作クラス

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
using System.IO.Compression;

namespace IPA.Cores.Basic
{
    public class ZipContainerOptions : FileContainerOptions
    {
        public ZipContainerOptions(IRandomAccess<byte> physicalFile, FileContainerFlags flags = FileContainerFlags.CreateNewSequential, PathParser? pathParser = null)
            : base(physicalFile, flags, pathParser ?? PathParser.Mac)
        {
        }
    }

    // ZIP コンテナフォーマットを読み書きするための抽象クラス
    // 注: 各オペレーションメソッドはスレッドセーフではない。シリアルな利用が前提である。
    //     スレッドセーフにする必要がある場合 (例: 上位のファイルシステムと接続する) は、自前で同期を取る必要があるので注意すること。
    // 現状は、新規作成 (かつ個別ファイルの更新は不可) しかできない。
    // 将来機能を追加する場合は、派生クラスとして実装することを推奨する。
    public class ZipContainer : FileContainer
    {
        public new ZipContainerOptions Options => (ZipContainerOptions)base.Options;

        readonly SequentialWritable<byte> Writer; // 物理ファイルライタ
        readonly SequentialWritableBasedStream WriterStream; // 物理ファイルライタに書き込むための Stream

        // セントラルディレクトリヘッダ等の、ZIP ファイルの最後に集中して保存される情報のバッファ
        // ファイルを追加していく際に内容を作っていく必要があり、かつ、ファイル数が極めて膨大になる場合でもメモリを消費しないように
        // ディスク上の一時ファイルとして保存する必要があるのである
        readonly MemoryOrDiskBuffer FooterBuffer;
        int NumTotalFiles;

        public ZipContainer(ZipContainerOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
            try
            {
                if (options.Flags.BitAny(FileContainerFlags.Read))
                    throw new NotSupportedException("Read operation is unsupported.");

                this.Writer = new SequentialWritable<byte>(Options.PhysicalFile);

                this.WriterStream = this.Writer.GetStream();

                this.FooterBuffer = new MemoryOrDiskBuffer();
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        protected override void DisposeImpl(Exception? ex)
        {
            try
            {
                this.FooterBuffer._DisposeSafe();

                this.WriterStream._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }

        // ユーザーが新しいファイルの追加要求を行なうとこの実装メソッドが呼び出される。
        // このメソッドで返した ISequentialWritable<byte> は、必ず Complete されることが保証されている。
        // また、多重呼び出しがされないことが保証されている。
        protected override async Task<SequentialWritableImpl<byte>> AddFileAsyncImpl(FileContainerEntityParam param, long? fileSizeHint, CancellationToken cancel = default)
        {
            // fileParam をコピー (加工するため)
            param = param._CloneDeep();

            // ファイル名などのチェック
            if (param.MetaData.IsDirectory) throw new ArgumentOutOfRangeException(nameof(param), "Directory is not supported.");

            param.PathString._FilledOrException();
            param.PathString = PathParser.NormalizeDirectorySeparatorIncludeWindowsBackslash(param.PathString);

            if (PathParser.IsAbsolutePath(param.PathString)) throw new ArgumentOutOfRangeException(nameof(param), $"Absolute path '{param.PathString}' is not supported.");

            Encoding encoding = param.GetEncoding();

            param.PathString._CheckStrSizeException(ZipConsts.MaxFileNameSize, encoding);

            Writable w = new Writable(this, param, encoding, fileSizeHint ?? long.MaxValue);

            // ローカルファイルヘッダを書き込む
            await w.StartAsync(cancel);

            // 実装クラスを経由してユーザーに渡す
            return w;
        }

        // すべてのファイルの書き込みが完了したのでセントラルディレクトリヘッダ等を集中的に書き出す
        protected override async Task FinishAsyncImpl(CancellationToken cancel = default)
        {
            checked
            {
                long sizeOfCentralDirectory = this.FooterBuffer.LongLength;
                long offsetOfCentralDirectory = this.Writer.CurrentPosition;

                // セントラルディレクトリヘッダ (一時バッファに溜めていたものを今こそ全部一気に書き出す)
                this.FooterBuffer.Seek(0, SeekOrigin.Begin);
                await this.FooterBuffer.CopyToSequentialWritableAsync(this.Writer, cancel);

                var memory = Sync(() =>
                {
                    checked
                    {
                        bool useZip64 = false;

                        // ZIP64 形式のヘッダが必要かどうか判定する
                        if (NumTotalFiles > ushort.MaxValue) useZip64 = true;
                        if (sizeOfCentralDirectory > uint.MaxValue) useZip64 = true;
                        if (offsetOfCentralDirectory > uint.MaxValue) useZip64 = true;

                        Packet p = new Packet();

                        long offsetStartZip64CentralDirectoryRecord = this.Writer.CurrentPosition;

                        if (useZip64)
                        {
                            // ZIP64 エンドオブセントラルディレクトリレコードの追記
                            ref Zip64EndOfCentralDirectoryRecord endZip64Record = ref p.AppendSpan<Zip64EndOfCentralDirectoryRecord>();

                            endZip64Record.Signature = ZipConsts.Zip64EndOfCentralDirectorySignature._LE_Endian32();

                            unsafe
                            {
                                endZip64Record.SizeOfZip64EndOfCentralDirectoryRecord = ((ulong)(sizeof(Zip64EndOfCentralDirectoryRecord) - 12))._LE_Endian64();
                            }

                            endZip64Record.MadeVersion = ZipFileVersions.Ver4_5;
                            endZip64Record.MadeFileSystemType = ZipFileSystemTypes.Ntfs;
                            endZip64Record.NeedVersion = ZipFileVersions.Ver4_5;
                            endZip64Record.Reserved = 0;
                            endZip64Record.NumberOfThisDisk = 0;
                            endZip64Record.DiskNumberStart = 0;
                            endZip64Record.TotalNumberOfCentralDirectory = ((ulong)NumTotalFiles)._LE_Endian64();
                            endZip64Record.TotalNumberOfEntriesOnCentralDirectory = ((ulong)NumTotalFiles)._LE_Endian64();
                            endZip64Record.SizeOfCentralDirectory = ((ulong)sizeOfCentralDirectory)._LE_Endian64();
                            endZip64Record.OffsetStartCentralDirectory = ((ulong)offsetOfCentralDirectory)._LE_Endian64();

                            // ZIP64 エンドオブセントラルディレクトリロケータの追記
                            ref Zip64EndOfCentralDirectoryLocator zip64Locator = ref p.AppendSpan<Zip64EndOfCentralDirectoryLocator>();

                            zip64Locator.Signature = ZipConsts.Zip64EndOfCentralDirectoryLocatorSignature;
                            zip64Locator.NumberOfThisDisk = 0;
                            zip64Locator.OffsetStartZip64CentralDirectoryRecord = offsetStartZip64CentralDirectoryRecord._LE_Endian64_U();
                            zip64Locator.TotalNumberOfDisk = 1._LE_Endian32_U();
                        }

                        // エンドオブセントラルディレクトリレコード
                        ref ZipEndOfCentralDirectoryRecord endRecord = ref p.AppendSpan<ZipEndOfCentralDirectoryRecord>();

                        endRecord.Signature = ZipConsts.EndOfCentralDirectorySignature._LE_Endian32();

                        if (useZip64 == false)
                        {
                            endRecord.NumberOfThisDisk = 0;
                            endRecord.NumberOfCentralDirectoryOnThisDisk = (ushort)NumTotalFiles._LE_Endian16();
                            endRecord.TotalNumberOfCentralDirectory = (ushort)NumTotalFiles._LE_Endian16();
                            endRecord.SizeOfCentralDirectory = ((uint)sizeOfCentralDirectory)._LE_Endian32();
                            endRecord.OffsetStartCentralDirectory = ((uint)offsetOfCentralDirectory)._LE_Endian32();
                        }
                        else
                        {
                            endRecord.NumberOfThisDisk = 0xFFFF;
                            endRecord.NumberOfCentralDirectoryOnThisDisk = 0xFFFF;
                            endRecord.TotalNumberOfCentralDirectory = 0xFFFF;
                            endRecord.SizeOfCentralDirectory = 0xFFFFFFFF;
                            endRecord.OffsetStartCentralDirectory = 0xFFFFFFFF;
                        }

                        endRecord.CommentLength = 0;

                        return p.ToMemory();
                    }
                });

                await Writer.AppendAsync(memory, cancel);

                // 最後に書き込みバッファを Flush する
                await Writer.FlushAsync(cancel);
            }
        }

        public class Writable : SequentialWritableImpl<byte>
        {
            readonly ZipContainer Zip;
            readonly FileContainerEntityParam Param;
            readonly Encoding Encoding;
            readonly ReadOnlyMemory<byte> FileNameData;
            readonly StreamsStack FileContentWriterStream; // データを書き込むべきストリーム 圧縮や暗号化などで多重レイヤが実装されている (ここに書き込まれたデータが 圧縮 -> 暗号化された後に RawWriterStream に書き込まれる)
            readonly ZipCompressionMethods CompressionMethod;

            readonly long FileSizeHint;

            long CurrentWrittenRawBytes;
            long WrittenDataStartOffset;

            ZipCrc32 Crc32;

            SequentialWritable<byte> RawWriter => Zip.Writer;
            SequentialWritableBasedStream RawWriterStream => Zip.WriterStream;

            MemoryOrDiskBuffer FooterBuffer => Zip.FooterBuffer;

            // 途中でメソッドにまたがって保存される必要がある状態変数
            ZipLocalFileHeader LocalFileHeader;
            ZipDataDescriptor DataDescriptor;
            ulong RelativeOffsetOfLocalHeader;

            public Writable(ZipContainer zip, FileContainerEntityParam param, Encoding encoding, long fileSizeHint)
            {
                this.Zip = zip;
                this.Param = param;
                this.Encoding = encoding;
                this.FileSizeHint = fileSizeHint._NonNegative();

                FileNameData = param.PathString._GetBytes(this.Encoding);

                this.FileContentWriterStream = new StreamsStack(zip.WriterStream, new StreamImplBaseOptions(canRead: false, canWrite: true, canSeek: false), autoDispose: false);

                if (param.Flags.Bit(FileContainerEntityFlags.EnableCompression))
                {
                    // 圧縮レイヤーを追加
                    this.FileContentWriterStream.Add((lower) => new DeflateStream(lower, param.Flags.Bit(FileContainerEntityFlags.CompressionMode_Fast) ? CompressionLevel.Fastest : CompressionLevel.Optimal, true),
                        autoDispose: true);

                    CompressionMethod = ZipCompressionMethods.Deflated;
                }
            }

            // 新しいファイルの書き込みを開始いたします
            protected override async Task StartImplAsync(CancellationToken cancel = default)
            {
                checked
                {
                    var memory = Sync(() =>
                    {
                        checked
                        {
                            // このファイル用のローカルファイルヘッダを生成します
                            LocalFileHeader.Signature = ZipConsts.LocalFileHeaderSignature._LE_Endian32();
                            LocalFileHeader.NeedVersion = ZipFileVersions.Ver4_5;
                            LocalFileHeader.GeneralPurposeFlag = (ZipGeneralPurposeFlags.UseDataDescriptor | ZipGeneralPurposeFlags.Utf8.If(this.Encoding._IsUtf8Encoding()))._LE_Endian16();
                            LocalFileHeader.CompressionMethod = CompressionMethod._LE_Endian16();
                            LocalFileHeader.LastModFileTime = Util.DateTimeToDosTime(Param.MetaData.LastWriteTime?.LocalDateTime ?? default)._LE_Endian16();
                            LocalFileHeader.LastModFileDate = Util.DateTimeToDosDate(Param.MetaData.LastWriteTime?.LocalDateTime ?? default)._LE_Endian16();
                            LocalFileHeader.Crc32 = 0;
                            LocalFileHeader.CompressedSize = 0;
                            LocalFileHeader.UncompressedSize = 0;
                            LocalFileHeader.FileNameSize = (ushort)this.FileNameData.Length._LE_Endian16();
                            LocalFileHeader.ExtraFieldSize = 0;

                            Packet p = new Packet();

                            p.AppendSpanWithData(in this.LocalFileHeader);

                            p.AppendSpanWithData(this.FileNameData.Span);

                            return p.ToMemory();
                        }
                    });

                    // ローカルヘッダを書き込む直前のオフセットを保存
                    RelativeOffsetOfLocalHeader = (ulong)RawWriter.CurrentPosition;

                    await RawWriter.AppendAsync(memory, cancel);

                    // 書き込み開始時の offset を保存します
                    WrittenDataStartOffset = RawWriter.CurrentPosition;
                }
            }

            // ファイルへの書き込みが開始された後、追記データ (実データ) がきました
            protected override async Task AppendImplAsync(ReadOnlyMemory<byte> data, long hintCurrentLength, long hintNewLength, CancellationToken cancel = default)
            {
                await this.FileContentWriterStream.WriteAsync(data, cancel);

                // CRC32 の計算を追加します
                Crc32.Append(data.Span);

                // 書き込んだ元データサイズを加算します
                CurrentWrittenRawBytes += data.Length;
            }

            // Flush 要求を受けました
            protected override async Task FlushImplAsync(long hintCurrentLength, CancellationToken cancel = default)
            {
                await this.FileContentWriterStream.FlushAsync(cancel);

                await RawWriter.FlushAsync(cancel);
            }

            // このファイルの書き込みが完了しました (成功または失敗)  最後に必ず呼ばれます
            protected override async Task CompleteImplAsync(bool ok, long hintCurrentLength, CancellationToken cancel = default)
            {
                if (ok == false)
                {
                    // ファイルの書き込みをキャンセルすることが指示されたので、データデスクリプタやセントラルディレクトリヘッダは書き込みしない。
                    // ただし、一度書き込んだデータそのものは、一方向性書き込みであるためキャンセルできない。
                    // そのため、単に関数を抜けるだけとする。

                    await this.FileContentWriterStream._DisposeAsyncSafe();
                    return;
                }

                await this.FileContentWriterStream.FlushAsync(cancel);

                await this.FileContentWriterStream._DisposeAsyncSafe();

                // 圧縮後データサイズを計算
                long currentWriteenCompressedBytes = this.RawWriter.CurrentPosition - WrittenDataStartOffset;

                // 結局 Zip64 形式が必要か不要かの判定
                bool useZip64 = false;

                if (CurrentWrittenRawBytes >= uint.MaxValue || currentWriteenCompressedBytes >= uint.MaxValue)
                {
                    useZip64 = true;
                }

                var data = Sync(() =>
                {
                    checked
                    {
                        // このファイル用のデータデスクリプタ (フッタのようなもの) を書き込みます
                        DataDescriptor.Signature = ZipConsts.DataDescriptorSignature._LE_Endian32();
                        DataDescriptor.Crc32 = Crc32.Value._LE_Endian32();

                        if (useZip64 == false)
                        {
                            DataDescriptor.CompressedSize = ((uint)currentWriteenCompressedBytes)._LE_Endian32();
                            DataDescriptor.UncompressedSize = ((uint)CurrentWrittenRawBytes)._LE_Endian32();
                        }
                        else
                        {
                            DataDescriptor.CompressedSize = 0xFFFFFFFF._LE_Endian32();
                            DataDescriptor.UncompressedSize = 0xFFFFFFFF._LE_Endian32();
                        }

                        Packet p = new Packet();

                        p.AppendSpanWithData(in DataDescriptor);

                        return p.ToMemory();
                    }
                });

                await RawWriter.AppendAsync(data, cancel);

                var memory = Sync(() =>
                {
                    checked
                    {
                        Memory<byte> extraFieldsMemory = default;

                        // このファイル用のセントラルディレクトリヘッダを生成します (後で書き込みますが、今は書き込みません。バッファに一時保存するだけです)
                        Packet p = new Packet();

                        ref ZipCentralFileHeader centralFileHeader = ref p.AppendSpan<ZipCentralFileHeader>();

                        centralFileHeader.Signature = ZipConsts.CentralFileHeaderSignature._LE_Endian32();
                        centralFileHeader.MadeVersion = LocalFileHeader.NeedVersion;
                        centralFileHeader.MadeFileSystemType = ZipFileSystemTypes.Ntfs;
                        centralFileHeader.NeedVersion = LocalFileHeader.NeedVersion;
                        centralFileHeader.GeneralPurposeFlag = LocalFileHeader.GeneralPurposeFlag;
                        centralFileHeader.CompressionMethod = LocalFileHeader.CompressionMethod;
                        centralFileHeader.LastModFileTime = LocalFileHeader.LastModFileTime;
                        centralFileHeader.LastModFileDate = LocalFileHeader.LastModFileDate;
                        centralFileHeader.Crc32 = Crc32.Value._LE_Endian32();

                        if (useZip64 == false)
                        {
                            centralFileHeader.CompressedSize = DataDescriptor.CompressedSize;
                            centralFileHeader.UncompressedSize = DataDescriptor.UncompressedSize;
                        }
                        else
                        {
                            centralFileHeader.CompressedSize = 0xFFFFFFFF;
                            centralFileHeader.UncompressedSize = 0xFFFFFFFF;
                        }

                        centralFileHeader.FileNameSize = (ushort)this.FileNameData.Length._LE_Endian16();
                        centralFileHeader.ExtraFieldSize = 0;
                        centralFileHeader.FileCommentSize = 0;

                        if (useZip64 == false)
                        {
                            centralFileHeader.DiskNumberStart = 0;
                        }
                        else
                        {
                            centralFileHeader.DiskNumberStart = 0xFFFF;
                        }

                        centralFileHeader.InternalFileAttributes = 0;
                        centralFileHeader.ExternalFileAttributes = (uint)(Param.MetaData.Attributes ?? FileAttributes.Normal)._LE_Endian32();

                        if (useZip64 == false)
                        {
                            centralFileHeader.RelativeOffsetOfLocalHeader = RelativeOffsetOfLocalHeader._LE_Endian32();
                        }
                        else
                        {
                            centralFileHeader.RelativeOffsetOfLocalHeader = 0xFFFFFFFF;
                        }

                        if (useZip64)
                        {
                            var extraList = new ZipExtraFieldsList();

                            ZipExtZip64Field zip64 = new ZipExtZip64Field
                            {
                                UncompressedSize = CurrentWrittenRawBytes._LE_Endian64_U(),
                                CompressedSize = currentWriteenCompressedBytes._LE_Endian64_U(),
                                RelativeOffsetOfLocalHeader = RelativeOffsetOfLocalHeader._LE_Endian64(),
                                DiskNumberStart = 0,
                            };

                            extraList.Add(ZipExtHeaderIDs.Zip64, zip64);

                            extraFieldsMemory = extraList.ToMemory();

                            centralFileHeader.ExtraFieldSize = ((ushort)extraFieldsMemory.Length)._LE_Endian16();
                        }

                        p.AppendSpanWithData(this.FileNameData.Span);

                        p.AppendSpanWithData(extraFieldsMemory.Span);

                        return p.ToMemory();
                    }
                });

                FooterBuffer.Write(memory.Span);

                // ファイル数カウント
                Zip.NumTotalFiles++;
            }
        }
    }
}

#endif

