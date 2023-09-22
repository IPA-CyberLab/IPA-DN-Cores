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
        // Header Str はいずれも長さが同一でなければならない
        public const string SecureCompressFirstHeader_Str = "\r\n\r\n!!__[MetaData:SecureCompressFirstHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressFirstHeader_Data = SecureCompressFirstHeader_Str._GetBytes_Ascii();

        public const string SecureCompressBlockHeader_Str = "\r\n\r\n!!__[MetaData:SecureCompressBlockHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressBlockHeader_Data = SecureCompressBlockHeader_Str._GetBytes_Ascii();

        public const string SecureCompressFinalHeader_Str = "\r\n\r\n!!__[MetaData:SecureCompressFinalHeader]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> SecureCompressFinalHeader_Data = SecureCompressFinalHeader_Str._GetBytes_Ascii();

        public const int BlockSize = 4096;
        public const int DataBlockSrcSize = 1024 * 1024;
    }
}

public class SecureCompressFinalHeader
{
    public long ChunkCount;
    public long SrcSize;
    public long DestContentSize;
    public long DestPhysicalSize;

    public string SrcSha1 = "";
    public string DestSha1 = "";

    public SecureCompressFirstHeader? CopyOfFirstHeader;
}

public class SecureCompressBlockHeader : IValidatable
{
    public long ChunkIndex;
    public int DestDataSize;
    public int DestDataSizeWithoutPadding;
    public long SrcDataPosition;
    public int SrcDataLength;
    public string SrcSha1 = "";
    public string DestSha1 = "";

    public void Validate()
    {
        if (DestDataSize < 0) throw new CoresLibException($"DestDataSize ({DestDataSize}) < 0");
        if (DestDataSizeWithoutPadding < 0) throw new CoresLibException($"DestDataSizeWithoutPadding ({DestDataSizeWithoutPadding}) < 0");
        if (SrcDataPosition < 0) throw new CoresLibException($"SrcDataPosition ({SrcDataPosition}) < 0");
        if (SrcDataLength < 0) throw new CoresLibException($"SrcDataLength ({SrcDataLength}) < 0");
    }
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
    public string FileNameHint { get; }
    public bool Encrypt { get; }
    public bool Compress { get; }
    public string Password { get; }
    public int NumCpu { get; }

    public SecureCompressOptions(string fileNameHint, bool encrypt, string password, bool compress, int numCpu = -1)
    {
        if (encrypt == false && compress == false)
        {
            //throw new CoresLibException("encrypt == false && compress == false");
        }

        this.FileNameHint = fileNameHint;

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

public class SecureCompressDecoder : StreamImplBase
{
    public bool AutoDispose { get; }
    public Stream DestStream { get; }

    readonly StreamBasedSequentialWritable DestWritable;
    readonly SequentialWritableBasedRandomAccess<byte> DestRandomWriter;

    public override bool DataAvailable => throw new NotImplementedException();

    public SecureCompressOptions Options { get; }

    int CurrentSrcDataBufferMaxSize => Consts.SecureCompress.DataBlockSrcSize * Options.NumCpu * 2;
    readonly FastStreamBuffer CurrentSrcDataBuffer;

    ReadOnlyMemory<byte> MasterSecret;

    HashCalc DestHash_Sha1 = new HashCalc(SHA1.Create());

    Exception? LastException = null;

    public int NumError { get; private set; }
    public int NumWarning { get; private set; }

    public SecureCompressDecoder(Stream destStream, SecureCompressOptions options, long srcDataSizeHint = -1, bool autoDispose = false)
        : base(new StreamImplBaseOptions(false, true, false))
    {
        try
        {
            this.AutoDispose = autoDispose;
            this.DestStream = destStream;
            this.Options = options;

            this.DestWritable = new StreamBasedSequentialWritable(this.DestStream, autoDispose);
            this.DestRandomWriter = new SequentialWritableBasedRandomAccess<byte>(this.DestWritable, allowForwardSeek: true);

            this.CurrentSrcDataBuffer = new FastStreamBuffer();
        }
        catch
        {
            this._DisposeSafeSync();
            throw;
        }
    }

    SecureCompressFirstHeader? FirstHeader = null;
    SecureCompressFinalHeader? FinalHeader = null;

    public class Chunk
    {
        public SecureCompressBlockHeader Header = null!;
        public Memory<byte> SrcData;
        public long SrcOffset;
        public Memory<byte> DstData;

        public string? Warning;
        public string? Error;
    }

    async Task ProcessAndSendBufferedDataAsync(CancellationToken cancel = default)
    {
        if (LastException != null)
        {
            throw LastException;
        }

        try
        {
            List<Chunk> currentChunkList = new List<Chunk>();

            bool escapeFlag = false;

            while (this.CurrentSrcDataBuffer.Length >= Consts.SecureCompress.BlockSize)
            {
                // 次の 1 ブロックのヘッダ部分の読み出しを実施する
                var headerMemory = this.CurrentSrcDataBuffer.GetContiguous(this.CurrentSrcDataBuffer.PinHead, Consts.SecureCompress.BlockSize, false);

                if (this.CurrentSrcDataBuffer.PinHead == 0)
                {
                    if (!(ParseHeader(headerMemory, this.CurrentSrcDataBuffer.PinHead) is SecureCompressFirstHeader firstHeader))
                    {
                        // ファイルの先頭にヘッダが付いていない
                        throw new CoresException($"This file has no SecureCompressFirstHeader at the beginning");
                    }

                    this.FirstHeader = firstHeader;

                    if (this.FirstHeader.Encrypted)
                    {
                        if (Secure.VeritySaltedPassword(FirstHeader.SaltedPassword, this.Options.Password) == false)
                        {
                            throw new CoresException("Invalid decrypt password");
                        }

                        this.MasterSecret = ChaChaPoly.EasyDecryptWithPassword(this.FirstHeader.MasterKeyEncryptedByPassword._GetHexBytes(), this.Options.Password).Value;

                        //$"Master = {masterKey.Value._GetHexString()}"._Debug();
                    }

                    if (this.FirstHeader.Encrypted != this.Options.Encrypt)
                    {
                        throw new CoresException($"FirstHeader.Encrypted ({FirstHeader.Encrypted}) != Options.Encrypt ({Options.Encrypt})");
                    }

                    if (this.FirstHeader.Compressed != this.Options.Compress)
                    {
                        throw new CoresException($"FirstHeader.Compressed ({FirstHeader.Compressed}) != Options.Compress ({Options.Compress})");
                    }
                }

                try
                {
                    object? header = ParseHeader(headerMemory, this.CurrentSrcDataBuffer.PinHead);

                    int forwardSize = Consts.SecureCompress.BlockSize;

                    if (header != null)
                    {
                        if (header is SecureCompressFirstHeader firstHeader)
                        {
                            if (this.FirstHeader._ObjectToJson() != firstHeader._ObjectToJson())
                            {
                                throw new CoresException($"Duplicate different FirstHeader. Previous = {this.FirstHeader._ObjectToJson(compact: true)}, This = {firstHeader._ObjectToJson(compact: true)}");
                            }
                        }
                        else if (header is SecureCompressBlockHeader blockHeader)
                        {
                            blockHeader.Validate();

                            if (this.CurrentSrcDataBuffer.Length >= (Consts.SecureCompress.BlockSize + blockHeader.DestDataSize))
                            {
                                var srcData = this.CurrentSrcDataBuffer.GetContiguous(this.CurrentSrcDataBuffer.PinHead + Consts.SecureCompress.BlockSize, blockHeader.DestDataSize, false);

                                forwardSize += srcData.Length;

                                Chunk c = new Chunk
                                {
                                    Header = blockHeader,
                                    SrcData = srcData.ToArray(),
                                    SrcOffset = this.CurrentSrcDataBuffer.PinHead + Consts.SecureCompress.BlockSize,
                                };

                                currentChunkList.Add(c);

                                if (currentChunkList.Count >= this.Options.NumCpu)
                                {
                                    await DecodeAndProcessChunkListAsync(currentChunkList);
                                }
                            }
                            else
                            {
                                // 処理を予定しているブロックが尻切れトンボとなっているので、ここでの処理は諦めて次回に任せる
                                // (forwardSize を 0 にして、読み進めないようにする)
                                forwardSize = 0;
                                escapeFlag = true;
                            }
                        }
                        else if (header is SecureCompressFinalHeader finalHeader)
                        {
                            this.FinalHeader = finalHeader;
                        }
                        else
                        {
                            //Dbg.Where();
                        }
                    }
                    else
                    {
                        //Dbg.Where();
                    }

                    if (forwardSize >= 1)
                    {
                        this.CurrentSrcDataBuffer.Dequeue(forwardSize, out _, true);
                    }

                    if (escapeFlag)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    $"{this.Options.FileNameHint}: Offset = {this.CurrentSrcDataBuffer.PinHead}. Error = {ex.ToString()}"._Error();
                    this.NumError++;

                    this.CurrentSrcDataBuffer.Dequeue(Consts.SecureCompress.BlockSize, out _, true);
                }
            }

            await DecodeAndProcessChunkListAsync(currentChunkList);

            // 溜まったチャンクデータのデコードと書き込み処理
            async Task DecodeAndProcessChunkListAsync(List<Chunk> chunkList)
            {
                var copyOfList = chunkList.ToArray();

                chunkList.Clear();

                if (copyOfList.Length == 0)
                {
                    return;
                }

                await TaskUtil.ForEachAsync(int.MaxValue, copyOfList, (chunk, index, cancel) =>
                {
                    try
                    {
                        var tmp1 = chunk.SrcData;

                        // ハッシュ比較
                        var sha1 = Secure.HashSHA1(tmp1.Span);
                        if (sha1._GetHexString()._CompareHex(chunk.Header.DestSha1) != 0)
                        {
                            chunk.Warning = $"DestSha1 is different. Header: {chunk.Header.DestSha1}, Real: {sha1._GetHexString()}";
                        }

                        if (this.Options.Encrypt)
                        {
                            // 解読の実施
                            Memory<byte> destMemory = new byte[tmp1.Length];

                            XtsAes256Util.Decrypt(destMemory, tmp1, this.MasterSecret, (ulong)chunk.Header.SrcDataPosition);

                            //chunk.Header.SrcDataPosition._Print();

                            tmp1 = destMemory;
                        }

                        if (chunk.Header.DestDataSizeWithoutPadding != tmp1.Length)
                        {
                            if (chunk.Header.DestDataSizeWithoutPadding > tmp1.Length)
                            {
                                // おかしな値
                                throw new CoresException($"chunk.Header.DestDataSizeWithoutPadding ({chunk.Header.DestDataSizeWithoutPadding}) > tmp1.Length ({tmp1.Length})");
                            }
                            else if (chunk.Header.DestDataSizeWithoutPadding < 0)
                            {
                                // おかしな値
                                throw new CoresException($"chunk.Header.DestDataSizeWithoutPadding ({chunk.Header.DestDataSizeWithoutPadding}) < 0");
                            }
                            else
                            {
                                // パディング解除
                                tmp1 = tmp1.Slice(0, chunk.Header.DestDataSizeWithoutPadding);
                            }
                        }

                        if (this.Options.Compress)
                        {
                            //tmp1.Span[Secure.RandSInt31() % tmp1.Length] = Secure.RandUInt8();
                            // 圧縮解除の実施
                            tmp1 = DeflateUtil.EasyDecompress(tmp1, Consts.SecureCompress.DataBlockSrcSize);
                        }

                        // ハッシュ比較
                        sha1 = Secure.HashSHA1(tmp1.Span);
                        if (sha1._GetHexString()._CompareHex(chunk.Header.SrcSha1) != 0)
                        {
                            chunk.Warning = $"SrcSha1 is different. Header: {chunk.Header.SrcSha1}, Real: {sha1._GetHexString()}";
                        }

                        chunk.DstData = tmp1;

                        // メモリ節約のためソースデータを解放
                        chunk.SrcData = Memory<byte>.Empty;
                    }
                    catch (Exception ex)
                    {
                        chunk.Error = ex.Message;
                    }

                    return TR();
                },
                cancel);

                // 結果を書き込み
                foreach (var chunk in copyOfList)
                {
                    if (chunk.Warning._IsFilled())
                    {
                        $"{this.Options.FileNameHint}: Offset: {chunk.SrcOffset} Warning: {chunk.Warning}"._Error();
                        this.NumWarning++;
                    }

                    if (chunk.Error._IsFilled())
                    {
                        $"{this.Options.FileNameHint}: Offset: {chunk.SrcOffset} Error: {chunk.Error}"._Error();
                        this.NumError++;
                        await this.DestRandomWriter.WriteRandomAsync(chunk.Header.SrcDataPosition, Util.GetZeroedSharedBuffer<byte>(chunk.Header.SrcDataLength), cancel);
                    }
                    else
                    {
                        //long diff = chunk.Header.SrcDataPosition - lastPos;

                        //diff._Print();

                        //$"pos = {chunk.Header.SrcDataPosition}, diff = {diff}"._Print();

                        await this.DestRandomWriter.WriteRandomAsync(chunk.Header.SrcDataPosition, chunk.DstData, cancel);

                        DestHash_Sha1.Write(chunk.DstData);
                        //lastPos = chunk.Header.SrcDataPosition + chunk.DstData.Length;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.LastException = ex;
            throw;
        }
    }

    object? ParseHeader(ReadOnlyMemory<byte> data, long offsetHint)
    {
        try
        {
            if (data.Length != Consts.SecureCompress.BlockSize)
            {
                throw new CoresLibException($"Offset {offsetHint}: data.Length ({data.Length}) != Consts.SecureCompress.BlockSize ({Consts.SecureCompress.BlockSize})");
            }

            int headerStrSize = Consts.SecureCompress.SecureCompressFirstHeader_Data.Length;

            var headOfData = data.Slice(0, headerStrSize);

            data._Walk(headerStrSize);

            if (headOfData._MemEquals(Consts.SecureCompress.SecureCompressFirstHeader_Data))
            {
                string jsonStr = data._GetString_UTF8(true);

                return jsonStr._JsonToObject<SecureCompressFirstHeader>();
            }
            else if (headOfData._MemEquals(Consts.SecureCompress.SecureCompressBlockHeader_Data))
            {
                string jsonStr = data._GetString_UTF8(true);

                return jsonStr._JsonToObject<SecureCompressBlockHeader>();
            }
            else if (headOfData._MemEquals(Consts.SecureCompress.SecureCompressFinalHeader_Data))
            {
                string jsonStr = data._GetString_UTF8(true);

                return jsonStr._JsonToObject<SecureCompressFinalHeader>();
            }
        }
        catch (Exception ex)
        {
            $"{this.Options.FileNameHint}: ParseHeader error: Offset {offsetHint}"._Error();
            ex._Error();
            this.NumError++;
        }

        return null;
    }

    protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        while (buffer.Length >= 1)
        {
            long remainBufSize = this.CurrentSrcDataBufferMaxSize - this.CurrentSrcDataBuffer.Length;

            if (remainBufSize >= 1)
            {
                int sz = Math.Min(buffer.Length, (int)remainBufSize);

                var part = buffer._WalkRead(sz);

                this.CurrentSrcDataBuffer.Enqueue(part.ToArray());
            }

            if (this.CurrentSrcDataBuffer.Length >= this.CurrentSrcDataBufferMaxSize)
            {
                await ProcessAndSendBufferedDataAsync(cancel);
            }
        }
    }

    Once FinalizedFlag;

    public async Task FinalizeAsync(CancellationToken cancel = default)
    {
        if (LastException != null)
        {
            throw LastException;
        }

        try
        {
            if (FinalizedFlag.IsFirstCall())
            {
                // 必ず 1 回は呼び出す
                await ProcessAndSendBufferedDataAsync(cancel);

                if (this.FinalHeader == null)
                {
                    $"{this.Options.FileNameHint}: ParseHeader error: No final header found"._Error();
                    this.NumError++;
                }
                else
                {
                    if (this.FinalHeader.SrcSha1._CompareHex(this.DestHash_Sha1.GetFinalHash()._GetHexString()) != 0)
                    {
                        $"{this.Options.FileNameHint}: FinalHeader's SrcSha1 is different. Header: {this.FinalHeader.SrcSha1}, Real: {this.DestHash_Sha1.GetFinalHash()._GetHexString()}"._Error();
                        this.NumError++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.LastException = ex;
            throw;
        }
    }

    protected override Task FlushImplAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
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
        try
        {
            await FinalizeAsync();
        }
        finally
        {
            await this.DestRandomWriter._DisposeSafeAsync();

            await this.DestWritable._DisposeSafeAsync();

            if (this.AutoDispose)
            {
                await this.DestStream._DisposeSafeAsync();
            }
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

public class SecureCompressEncoder : StreamImplBase
{
    public bool AutoDispose { get; }
    public Stream DestStream { get; }

    public override bool DataAvailable => throw new NotImplementedException();

    public SecureCompressOptions Options { get; }

    readonly SecureCompressFirstHeader FirstHeader;

    readonly SecureCompressFinalHeader CurrentFinalHeader = new SecureCompressFinalHeader();

    readonly ReadOnlyMemory<byte> MasterSecret;

    int CurrentSrcDataBufferMaxSize => Consts.SecureCompress.DataBlockSrcSize * Options.NumCpu;
    readonly MemoryBuffer<byte> CurrentSrcDataBuffer;

    long CurrentSrcDataPosition = 0;

    long CurrentNumChunks = 0;

    bool FirstWriteFlag = false;

    HashCalc SrcHash_Sha1 = new HashCalc(SHA1.Create());

    HashCalc DestHash_Sha1 = new HashCalc(SHA1.Create());

    public SecureCompressEncoder(Stream destStream, SecureCompressOptions options, long srcDataSizeHint = -1, bool autoDispose = false)
        : base(new StreamImplBaseOptions(false, true, false))
    {
        try
        {
            this.AutoDispose = autoDispose;
            this.DestStream = destStream;
            this.Options = options;

            MasterSecret = Secure.Rand(XtsAesRandomAccess.XtsAesKeySize);

            //$"Master = {this.CurrentMasterKey._GetHexString()}"._Debug();

            FirstHeader = new SecureCompressFirstHeader
            {
                Version = 1,
                Encrypted = this.Options.Encrypt,
                Compressed = this.Options.Compress,
                SrcDataSizeHint = srcDataSizeHint,
                SaltedPassword = Secure.SaltPassword(options.Password),

                MasterKeyEncryptedByPassword = ChaChaPoly.EasyEncryptWithPassword(this.MasterSecret, this.Options.Password)._GetHexString(),
            };

            if (FirstHeader.Encrypted == false)
            {
                FirstHeader.SaltedPassword = "";
                FirstHeader.MasterKeyEncryptedByPassword = "";
            }

            this.CurrentSrcDataBuffer = new MemoryBuffer<byte>(EnsureSpecial.Yes, CurrentSrcDataBufferMaxSize);
        }
        catch
        {
            this._DisposeSafeSync();
            throw;
        }
    }

    public static Memory<byte> HeaderToData(string signature, object header)
    {
        MemoryBuffer<byte> tmp = new MemoryBuffer<byte>(EnsureSpecial.Yes, Consts.SecureCompress.BlockSize);

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
        public string SrcSha1 = "";
        public string DestSha1 = "";
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
            if (this.CurrentSrcDataBuffer.Length > this.CurrentSrcDataBufferMaxSize)
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

                lock (CurrentFinalHeader)
                {
                    CurrentFinalHeader.SrcSize += tmp.Length;
                }

                chunk.SrcSha1 = Secure.HashSHA1(tmp.Span)._GetHexString();

                if (this.Options.Compress)
                {
                    // 圧縮の実施
                    tmp = DeflateUtil.EasyCompressRetMemoryFast(tmp.Span, CompressionLevel.Fastest);
                }

                lock (CurrentFinalHeader)
                {
                    CurrentFinalHeader.DestContentSize += tmp.Length;
                }

                chunk.DestWithoutPaddingSize = tmp.Length;

                int paddingSize = 0;

                // パディングの実施 (4096 の倍数でない場合)
                if ((tmp.Length % Consts.SecureCompress.BlockSize) != 0)
                {
                    paddingSize = Consts.SecureCompress.BlockSize - (tmp.Length % Consts.SecureCompress.BlockSize);
                }

                int paddedTotalSize = tmp.Length + paddingSize;

                MemoryBuffer<byte> tmp2 = new MemoryBuffer<byte>(EnsureSpecial.Yes, paddedTotalSize);
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
                    //var srcSeg = tmp3._AsSegment();

                    //"Hello"._GetBytes_Ascii().CopyTo(tmp3);

                    Memory<byte> destMemory = new byte[tmp3.Length];

                    //var destSeg = destMemory._AsSegment();

                    XtsAes256Util.Encrypt(destMemory, tmp3, this.MasterSecret, (ulong)chunk.SrcPosition);
                    //this.CurrentEncrypter.TransformBlock(srcSeg.Array!, srcSeg.Offset, srcSeg.Count, destSeg.Array!, destSeg.Offset, (ulong)chunk.SrcPosition);

                    //chunk.SrcPosition._Print();

                    tmp3 = destMemory;
                }

                chunk.DestData = tmp3;

                chunk.DestSha1 = Secure.HashSHA1(tmp3.Span)._GetHexString();

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

            MemoryBuffer<byte> tmpBuf = new MemoryBuffer<byte>(EnsureSpecial.Yes, tmpBufMemSize);

            if (FirstWriteFlag == false)
            {
                // 1 個目なのでファイル全体のヘッダ (FirstHeader) を書き込み
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
                    DestSha1 = chunk.DestSha1,
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
            this.CurrentSrcDataBuffer.SetLength(0);
        }
        catch (Exception ex)
        {
            this.LastException = ex;
            throw;
        }
    }

    public async Task AppendToDestBufferAsync(ReadOnlyMemory<byte> data, CancellationToken cancel = default)
    {
        this.DestHash_Sha1.Write(data);

        CurrentFinalHeader.DestPhysicalSize += data.Length;

        await this.DestStream.WriteAsync(data, cancel);
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

    Once FinalizedFlag;

    public async Task FinalizeAsync(CancellationToken cancel = default)
    {
        if (LastException != null)
        {
            throw LastException;
        }

        try
        {
            if (FinalizedFlag.IsFirstCall())
            {
                // 必ず 1 回は呼び出す
                await ProcessAndSendBufferedDataAsync(cancel);

                // 末尾ヘッダの作成
                SecureCompressFinalHeader h = this.CurrentFinalHeader;
                h.CopyOfFirstHeader = this.FirstHeader;

                h.SrcSha1 = this.SrcHash_Sha1.GetFinalHash()._GetHexString();

                h.DestSha1 = this.DestHash_Sha1.GetFinalHash()._GetHexString();

                var headerData = HeaderToData(Consts.SecureCompress.SecureCompressFinalHeader_Str, h);

                //h._PrintAsJson();

                // 末尾ヘッダも予備のため 2 回書く
                MemoryBuffer<byte> tmpBuf = new MemoryBuffer<byte>();
                tmpBuf.Write(headerData);
                tmpBuf.Write(headerData);

                await this.AppendToDestBufferAsync(tmpBuf, cancel);
            }
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
        try
        {
            await this.FinalizeAsync();
        }
        finally
        {
            if (this.AutoDispose)
            {
                await this.DestStream._DisposeSafeAsync();
            }
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

#endif

