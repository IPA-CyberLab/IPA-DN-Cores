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

        public ZipContainer(ZipContainerOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
            try
            {
                if (options.Flags.BitAny(FileContainerFlags.Read))
                    throw new NotSupportedException("Read operation is unsupported.");

                this.Writer = new SequentialWritable<byte>(Options.PhysicalFile);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        // ユーザーが新しいファイルの追加要求を行なうとこの実装メソッドが呼び出される。
        // このメソッドで返した ISequentialWritable<byte> は、必ず Complete されることが保証されている。
        // また、多重呼び出しがされないことが保証されている。
        protected override async Task<SequentialWritableImpl<byte>> AddFileAsyncImpl(FileContainerEntityParam param, CancellationToken cancel = default)
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

            Writable w = new Writable(this, param, encoding);

            // ローカルファイルヘッダを書き込む
            await w.StartAsync(cancel);

            // 実装クラスを経由してユーザーに渡す
            return w;
        }

        public class Writable : SequentialWritableImpl<byte>
        {
            readonly ZipContainer Zip;
            readonly FileContainerEntityParam Param;
            readonly Encoding Encoding;
            readonly ReadOnlyMemory<byte> FileNameData;

            SequentialWritable<byte> Writer => Zip.Writer;

            public Writable(ZipContainer zip, FileContainerEntityParam param, Encoding encoding)
            {
                this.Zip = zip;
                this.Param = param;
                this.Encoding = encoding;
                FileNameData = param.PathString._GetBytes(this.Encoding);
            }

            // 新しいファイルの書き込みを開始いたします
            protected override async Task StartImplAsync(CancellationToken cancel = default)
            {
                checked
                {
                    Memory<byte> memory = Sync(() =>
                    {
                        Packet p = new Packet();

                        ref ZipLocalFileHeader h = ref p.AppendSpan<ZipLocalFileHeader>();

                        h.Signature = ZipConsts.LocalFileHeaderSignature._LE_Endian32();
                        h.NeedVersion = ZipFileVersions.Ver2_0;
                        h.GeneralPurposeFlag = this.Encoding._IsUtf8Encoding() ? ZipGeneralPurposeFlags.Utf8 : ZipGeneralPurposeFlags.None;
                        h.CompressionMethod = ZipCompressionMethods.Raw;
                        h.LastModFileTime = Util.DateTimeToDosTime(Param.MetaData.LastWriteTime?.LocalDateTime ?? default);
                        h.LastModFileDate = Util.DateTimeToDosDate(Param.MetaData.LastWriteTime?.LocalDateTime ?? default);
                        h.Crc32 = 0;
                        h.CompressedSize = 0;
                        h.UncompressedSize = 0;
                        h.FileNameSize = (ushort)this.FileNameData.Length;
                        h.ExtraFieldSize = 0;

                        p.AppendSpanWithData(this.FileNameData.Span);

                        return p.ToMemory();
                    });

                    await Writer.AppendAsync(memory, cancel);
                }
            }

            // ファイルへの追記データがきました
            protected override async Task AppendImplAsync(ReadOnlyMemory<byte> data, long hintCurrentLength, long hintNewLength, CancellationToken cancel = default)
            {
                await Writer.AppendAsync(data, cancel);
            }

            // Flush 要求を受けました
            protected override Task FlushImplAsync(long hintCurrentLength, CancellationToken cancel = default)
            {
                return Writer.FlushAsync(cancel);
            }

            // このファイルの書き込みが完了しました (成功または失敗)  最後に必ず呼ばれます
            protected override Task CompleteImplAsync(bool ok, long hintCurrentLength, CancellationToken cancel = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}

#endif

