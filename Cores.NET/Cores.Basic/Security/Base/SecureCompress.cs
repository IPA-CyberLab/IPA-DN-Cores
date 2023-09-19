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
// Description

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.IO.Compression;

using IPA.Cores.Basic.Internal;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic;

public static partial class Consts
{
    public static partial class SecureCompress
    {
        public const string SecureCompressFirstHeader_Str = "\r\n!!__[MetaData:SecureCompressFirstHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressFirstHeader_Data = SecureCompressFirstHeader_Str._GetBytes_Ascii();

        public const string SecureCompressBlockHeader_Str = "\r\n!!__[MetaData:SecureCompressBlockHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressBlockHeader_Data = SecureCompressBlockHeader_Str._GetBytes_Ascii();

        public const string SecureCompressFinalHeader_Str = "\r\n!!__[MetaData:SecureCompressFinalHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressFinalHeader_Data = SecureCompressFinalHeader_Str._GetBytes_Ascii();

        public const int BlockSize = 4096;
        public const int DataBlockSrcSize = 1024 * 1024;
    }

    public class SecureCompressFinalHeader
    {
        public long ChunkCount;
        public long SrcSize;
        public long DestContentSize;
        public long DestPhysicalSize;

        public string SrcSha1 = "", SrcMd5 = "";
        public string DestSha1 = "", DestMd5 = "";

        public SecureCompressFirstHeader? CopyOfFirstHeader;
    }

    public class SecureCompressBlockHeader
    {
        public long ChunkIndex;
        public int DestDataSize;
        public int DestDataSizeWithoutPadding;
        public long SrcDataPosition;
        public int SrcDataLength;
        public string SrcSha1 = "", SrcMd5 = "";
        public string DestSha1 = "", DestMd5 = "";
    }

    public class SecureCompressFirstHeader
    {
        public int Version;
        public bool Encrypted;
        public bool Compressed;
        public string SaltedPassword = "";
        public string MasterKeyEncryptedByPassword = "";
        public long SrcDataSizeHint;
    }

    public class SecureCompressOptions
    {
        public bool Encrypt { get; }
        public bool Compress { get; }
        public string Password { get; }
        public int NumCpu { get; }

        public SecureCompressOptions(bool encrypt, string password, bool compress, int numCpu = -1)
        {
            if (encrypt == false && compress == false)
            {
                throw new CoresLibException("encrypt == false && compress == false");
            }

            this.Encrypt = encrypt;
            this.Password = password._NonNull();

            this.Compress = compress;

            if (numCpu <= 0)
            {
                numCpu = Env.NumCpus;
            }

            if (numCpu >= 32)
            {
                numCpu = 32;
            }

            this.NumCpu = numCpu;
        }
    }

    public class SecureCompressWriter : StreamImplBase
    {
        public bool AutoDispose { get; }
        public Stream DestStream { get; }

        readonly StreamBasedSequentialWritable DestWriter;

        public override bool DataAvailable => throw new NotImplementedException();

        public SecureCompressOptions Options { get; }

        readonly SecureCompressFirstHeader FirstHeader;

        readonly SecureCompressFinalHeader CurrentFinalHeader = new SecureCompressFinalHeader();

        readonly ReadOnlyMemory<byte> CurrentMasterKey;

        int CurrentSrcDataBufferMaxSize => Consts.SecureCompress.DataBlockSrcSize * Options.NumCpu;
        readonly MemoryBuffer<byte> CurrentSrcDataBuffer;

        long CurrentSrcDataPosition = 0;

        long CurrentNumChunks = 0;

        bool FirstWriteFlag = false;

        Xts CurrentXts = null!;
        XtsCryptoTransform CurrentEncrypter = null!;

        HashCalc SrcHash_Sha1 = new HashCalc(SHA1.Create());
        HashCalc SrcHash_Md5 = new HashCalc(MD5.Create());

        HashCalc DestHash_Sha1 = new HashCalc(SHA1.Create());
        HashCalc DestHash_Md5 = new HashCalc(MD5.Create());

        public SecureCompressWriter(Stream destStream, SecureCompressOptions options, long srcDataSizeHint = -1, bool autoDispose = false)
            : base(new StreamImplBaseOptions(false, true, false))
        {
            try
            {
                this.AutoDispose = autoDispose;
                this.DestStream = destStream;
                this.Options = options;

                this.DestWriter = new StreamBasedSequentialWritable(this.DestStream, autoDispose);

                CurrentMasterKey = Secure.Rand(XtsAesRandomAccess.XtsAesKeySize);

                FirstHeader = new SecureCompressFirstHeader
                {
                    Version = 1,
                    Encrypted = this.Options.Encrypt,
                    SrcDataSizeHint = srcDataSizeHint,
                    SaltedPassword = Secure.SaltPassword(options.Password),
                    MasterKeyEncryptedByPassword = ChaChaPoly.EasyEncryptWithPassword(this.CurrentMasterKey, this.Options.Password)._GetHexString(),
                };

                if (FirstHeader.Encrypted == false)
                {
                    FirstHeader.SaltedPassword = "";
                    FirstHeader.MasterKeyEncryptedByPassword = "";
                }
                else
                {
                    this.CurrentXts = XtsAes256.Create(this.CurrentMasterKey.ToArray());
                    this.CurrentEncrypter = this.CurrentXts.CreateEncryptor();
                }

                this.CurrentSrcDataBuffer = new MemoryBuffer<byte>(Consts.SecureCompress.DataBlockSrcSize * this.Options.NumCpu);
            }
            catch
            {
                this._DisposeSafeSync();
                throw;
            }
        }

        public static Memory<byte> HeaderToData(string signature, object header)
        {
            MemoryBuffer<byte> tmp = new MemoryBuffer<byte>(Consts.SecureCompress.BlockSize);

            tmp.Write(signature._GetBytes_Ascii());

            string jsonBody = "\r\n" + header._ObjectToJson() + "\r\n";

            jsonBody = jsonBody._NormalizeCrlf(CrlfStyle.CrLf);

            tmp.Write(jsonBody._GetBytes_UTF8());

            int currentSize = tmp.Length;

            if (currentSize > (Consts.SecureCompress.BlockSize - 4))
            {
                throw new CoresLibException($"currentSize ({currentSize}) > (Consts.SecureCompress.BlockSize - 1) ({Consts.SecureCompress.BlockSize - 1})");
            }

            int padSize = Consts.SecureCompress.BlockSize - currentSize;

            var padMemory = Util.GetZeroedSharedBuffer<byte>(padSize);

            tmp.Write(padMemory);

            var ret = tmp.Memory;

            ret.Span[ret.Length - 2] = 13;
            ret.Span[ret.Length - 1] = 10;

            return ret;
        }

        class Chunk
        {
            public long SrcPosition;
            public int DestWithoutPaddingSize;
            public Memory<byte> SrcData;
            public Memory<byte> DestData;
            public string SrcSha1 = "", SrcMd5 = "";
            public string DestSha1 = "", DestMd5 = "";
        }

        Exception? LastException = null;

        // メモリ上の CurrentSrcDataBuffer の内容 (これは、1MB * CPU 数のサイズが上限なはずである) を出力ストリームに書き出す
        async Task ProcessAndSendBufferedDataAsync(CancellationToken cancel = default)
        {
            if (LastException != null)
            {
                throw LastException;
            }

            try
            {
                if (this.CurrentSrcDataBuffer.Length > (this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length))
                {
                    // バッファサイズがおかしな状態になっている (ここには到達し得ないはず)
                    throw new CoresLibException($"this.CurrentSrcDataBuffer.Length ({this.CurrentSrcDataBuffer.Length}) > (this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length ({this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length}))");
                }

                // バッファを CPU 数で分割
                int numCpu = (this.CurrentSrcDataBuffer.Length + (Consts.SecureCompress.DataBlockSrcSize - 1)) / Consts.SecureCompress.DataBlockSrcSize;
                int currentPos = 0;
                List<Chunk> chunkList = new List<Chunk>();

                for (int i = 0; i < numCpu; i++)
                {
                    Chunk chunk = new Chunk
                    {
                        SrcPosition = this.CurrentSrcDataPosition + currentPos,
                        SrcData = this.CurrentSrcDataBuffer.Slice(currentPos, Math.Min(this.CurrentSrcDataBuffer.Length - currentPos, Consts.SecureCompress.DataBlockSrcSize)),
                    };

                    chunkList.Add(chunk);

                    currentPos += chunk.SrcData.Length;
                }

                CurrentSrcDataPosition += currentPos;

                // 分割タスクを並列実行
                await TaskUtil.ForEachAsync(int.MaxValue, chunkList, (chunk, index, cancel) =>
                {
                    Memory<byte> tmp = chunk.SrcData;

                    CurrentFinalHeader.SrcSize += tmp.Length;

                    chunk.SrcSha1 = Secure.HashSHA1(tmp.Span)._GetHexString();
                    chunk.SrcMd5 = Secure.HashMD5(tmp.Span)._GetHexString();

                    if (this.Options.Compress)
                    {
                        // 圧縮の実施
                        tmp = DeflateUtil.EasyCompressRetMemoryFast(tmp.Span, CompressionLevel.Fastest);
                    }

                    CurrentFinalHeader.DestContentSize += tmp.Length;

                    chunk.DestWithoutPaddingSize = tmp.Length;

                    int paddingSize = 0;

                    // パディングの実施 (4096 の倍数でない場合)
                    if ((tmp.Length % Consts.SecureCompress.BlockSize) != 0)
                    {
                        paddingSize = Consts.SecureCompress.BlockSize - (tmp.Length % Consts.SecureCompress.BlockSize);
                    }

                    int paddedTotalSize = tmp.Length + paddingSize;

                    MemoryBuffer<byte> tmp2 = new MemoryBuffer<byte>(paddedTotalSize);
                    tmp2.Write(tmp);

                    if (paddingSize >= 1)
                    {
                        if (this.Options.Encrypt == false)
                        {
                            tmp2.Write(Util.GetZeroedSharedBuffer<byte>(paddingSize));
                        }
                        else
                        {
                            tmp2.Write(Secure.Rand(paddingSize));
                        }
                    }

                    Memory<byte> tmp3 = tmp2.Memory;

                    if (this.Options.Encrypt)
                    {
                        // 暗号化の実施
                        var srcSeg = tmp3._AsSegment();

                        Memory<byte> destMemory = new byte[tmp3.Length];

                        var destSeg = destMemory._AsSegment();

                        this.CurrentEncrypter.TransformBlock(srcSeg.Array!, srcSeg.Offset, srcSeg.Count, destSeg.Array!, destSeg.Offset, (ulong)chunk.SrcPosition);

                        tmp3 = destMemory;
                    }

                    chunk.DestData = tmp3;

                    chunk.DestSha1 = Secure.HashSHA1(tmp3.Span)._GetHexString();
                    chunk.DestMd5 = Secure.HashMD5(tmp3.Span)._GetHexString();

                    return TR();
                },
                cancel);

                // 一時メモリサイズの計算
                int tmpBufMemSize = 0;
                foreach (var chunk in chunkList)
                {
                    tmpBufMemSize += chunk.DestData.Length;
                    tmpBufMemSize += Consts.SecureCompress.BlockSize;
                }
                if (FirstWriteFlag == false)
                {
                    tmpBufMemSize += Consts.SecureCompress.BlockSize * 2;
                }

                MemoryBuffer<byte> tmpBuf = new MemoryBuffer<byte>(tmpBufMemSize);

                if (FirstWriteFlag == false)
                {
                    // 1 個目なのでファイル全体のヘッダを書き込み
                    var headerData = HeaderToData(Consts.SecureCompress.SecureCompressFirstHeader_Str, this.FirstHeader);

                    // ヘッダは、万一の破損に備えて 2 回書く
                    tmpBuf.Write(headerData);
                    tmpBuf.Write(headerData);

                    FirstWriteFlag = true;
                }

                // チャンクを書き込み
                foreach (var chunk in chunkList)
                {
                    // チャンクヘッダを書き込み
                    SecureCompressBlockHeader h = new SecureCompressBlockHeader
                    {
                        DestDataSize = chunk.DestData.Length,
                        DestDataSizeWithoutPadding = chunk.DestWithoutPaddingSize,
                        SrcDataPosition = chunk.SrcPosition,
                        SrcDataLength = chunk.SrcData.Length,
                        SrcSha1 = chunk.SrcSha1,
                        SrcMd5 = chunk.SrcMd5,
                        DestSha1 = chunk.DestSha1,
                        DestMd5 = chunk.DestMd5,
                        ChunkIndex = CurrentNumChunks,
                    };

                    var header = HeaderToData(Consts.SecureCompress.SecureCompressBlockHeader_Str, h);

                    tmpBuf.Write(header);

                    tmpBuf.Write(chunk.DestData);

                    CurrentNumChunks++;

                    CurrentFinalHeader.ChunkCount++;
                }

                await this.AppendToDestBufferAsync(tmpBuf, cancel);

                // バッファをクリア
                this.CurrentSrcDataBuffer.Clear();
            }
            catch (Exception ex)
            {
                this.LastException = ex;
                throw;
            }
        }

        public async Task<long> AppendToDestBufferAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            this.DestHash_Sha1.Write(data);
            this.DestHash_Md5.Write(data);

            CurrentFinalHeader.DestPhysicalSize += data.Length;

            return await this.DestWriter.AppendAsync(data, cancel);
        }

        // 本クラスの呼び出し元が書き込み要求をしたときに、このメソッドが呼ばれる
        // メモリ上の CurrentSrcDataBuffer が一杯になるまで、呼び出し元が書き込み要求をしたデータを追記していく
        // 一杯になったら、ProcessAndSendBufferedDataAsync を呼び出す
        protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
        {
            if (buffer.IsEmpty)
            {
                return;
            }

            while (buffer.Length >= 1)
            {
                int remainBufSize = this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length;

                if (remainBufSize >= 1)
                {
                    int sz = Math.Min(buffer.Length, remainBufSize);

                    var part = buffer._WalkRead(sz);

                    this.CurrentSrcDataBuffer.Write(part);

                    this.SrcHash_Md5.Write(part);
                    this.SrcHash_Sha1.Write(part);
                }

                if (this.CurrentSrcDataBuffer.Length >= this.CurrentSrcDataBufferMaxSize)
                {
                    await ProcessAndSendBufferedDataAsync(cancel);
                }
            }
        }

        protected override Task FlushImplAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        async Task FinalizeAsync(CancellationToken cancel = default)
        {
            if (LastException != null)
            {
                throw LastException;
            }

            try
            {
                // 必ず 1 回は呼び出す
                await ProcessAndSendBufferedDataAsync(cancel);

                // 末尾ヘッダの作成
                SecureCompressFinalHeader h = this.CurrentFinalHeader;
                h.CopyOfFirstHeader = this.FirstHeader;

                h.SrcSha1 = this.SrcHash_Sha1.GetFinalHash()._GetHexString();
                h.SrcMd5 = this.SrcHash_Md5.GetFinalHash()._GetHexString();

                h.DestSha1 = this.DestHash_Sha1.GetFinalHash()._GetHexString();
                h.DestMd5 = this.DestHash_Md5.GetFinalHash()._GetHexString();

                var headerData = HeaderToData(Consts.SecureCompress.SecureCompressFinalHeader_Str, h);
            }
            catch (Exception ex)
            {
                this.LastException = ex;
                throw;
            }
        }

        Once DisposeFlag;
        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (DisposeFlag.IsFirstCall() == false) return;
                await DisposeInternalAsync();
            }
            finally
            {
                await base.DisposeAsync();
            }
        }
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                DisposeInternalAsync()._GetResult();
            }
            finally { base.Dispose(disposing); }
        }
        async Task DisposeInternalAsync()
        {
            await this.DestWriter._DisposeSafeAsync();

            if (this.AutoDispose)
            {
                await this.DestStream._DisposeSafeAsync();
            }
        }

        protected override long GetLengthImpl()
        {
            throw new NotImplementedException();
        }

        protected override void SetLengthImpl(long length)
        {
            throw new NotImplementedException();
        }

        protected override long GetPositionImpl()
        {
            throw new NotImplementedException();
        }

        protected override void SetPositionImpl(long position)
        {
            throw new NotImplementedException();
        }

        protected override long SeekImpl(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        protected override ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}

#endif

