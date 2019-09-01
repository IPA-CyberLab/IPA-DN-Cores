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
// 開発中のクラスの一時置き場

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
    // 複数の Stream を積み重ねて読み書きするためのクラス
    // コンストラクタで一番下のレイヤ (たいていは物理ファイルやネットワークソケット) を指定する。
    // その後、下から上に向かって Add() で順に中間ストリームを積み重ねていく。
    // アプリケーションからの書き込み要求は、最後に追加されたレイヤーから順に処理される。
    // たとえば、圧縮と暗号化を積み重ねる場合、まず暗号化を Add() して、その後に圧縮を Add() する必要がある。(逆にすると圧縮されない)
    public class StreamsStack : StreamImplBase, IHasError
    {
        public readonly StreamImplBaseOptions ImplBaseOptions;

        class Layer
        {
            public readonly Stream Stream;
            public readonly bool AutoDispose;

            public Layer(Stream stream, bool autoDispose)
            {
                stream._NullCheck();

                Stream = stream;
                AutoDispose = autoDispose;
            }
        }

        readonly List<Layer> LayerList = new List<Layer>(); // このリスト上は逆順 Bottom -> Top に並んでいるので注意
        readonly Layer BottomLayer;
        Layer TopLayer = null!;

        public Exception? LastError => throw new NotImplementedException();

        public StreamsStack(Stream bottomStream, StreamImplBaseOptions options, bool autoDispose = false)
        {
            try
            {
                if (options.CanSeek) throw new ArgumentOutOfRangeException(nameof(options), "Cannot support seek.");

                this.ImplBaseOptions = new StreamImplBaseOptions(options.CanRead, options.CanWrite, false); // Seek はサポートしなくてよい

                AddInternal(bottomStream, autoDispose);

                this.BottomLayer = this.TopLayer;
            }
            catch
            {
                throw;
            }
        }

        // ストリームレイヤを追加
        public void Add(Func<Stream, Stream> newStream, bool autoDispose = false)
        {
            newStream._NullCheck();

            Stream newSt = newStream(this.LayerList.Last().Stream);

            try
            {
                AddInternal(newSt, autoDispose);
            }
            catch
            {
                newSt._DisposeSafe();

                throw;
            }
        }

        void AddInternal(Stream newStream, bool autoDispose)
        {
            newStream._NullCheck();

            if (ImplBaseOptions.CanRead && newStream.CanRead == false) throw new ArgumentException("ImplBaseOptions.CanRead && newStream.CanRead == false", nameof(newStream));
            if (ImplBaseOptions.CanWrite && newStream.CanWrite == false) throw new ArgumentException("ImplBaseOptions.CanWrite && newStream.CanWrite == false", nameof(newStream));

            Layer layer = new Layer(newStream, autoDispose);
            LayerList.Add(layer);

            this.TopLayer = layer;
        }

        Once DisposeFlag;

        public override bool DataAvailable => throw new NotImplementedException();

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
            // Top から Bottom に向かってすべての Stream を Dispose する
            foreach (Layer a in this.LayerList.Reverse<Layer>())
            {
                if (a.AutoDispose)
                {
                    await a.Stream._DisposeAsyncSafe();
                }
            }
        }

        protected override long GetLengthImpl() => throw new NotImplementedException();
        protected override void SetLengthImpl(long length) => throw new NotImplementedException();
        protected override long GetPositionImpl() => throw new NotImplementedException();
        protected override void SetPositionImpl(long position) => throw new NotImplementedException();
        protected override long SeekImpl(long offset, SeekOrigin origin) => throw new NotImplementedException();

        protected override async Task FlushImplAsync(CancellationToken cancellationToken = default)
        {
            // Top から Bottom に向かってすべての Stream を Flush する
            foreach (Layer a in this.LayerList.Reverse<Layer>())
            {
                await a.Stream.FlushAsync(cancellationToken);
            }
        }

        protected override async ValueTask<int> ReadImplAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await this.TopLayer.Stream.ReadAsync(buffer, cancellationToken);
        }

        protected override async ValueTask WriteImplAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await this.TopLayer.Stream.WriteAsync(buffer, cancellationToken);
        }
    }
}

#endif

