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
    // ISequentialWritable<byte> に対して書き込むことができる Stream オブジェクト。シーケンシャルでない操作 (現在の番地以外へのシークなど) を要求すると例外が発生する
    public class SequentialWritableBasedStream : Stream
    {
        public ISequentialWritable<byte> Target { get; }

        long PositionCache;

        readonly Func<Task>? OnDisposing;

        public SequentialWritableBasedStream(ISequentialWritable<byte> target, Func<Task>? onDisposing = null)
        {
            Target = target;
            this.PositionCache = target.CurrentPosition;
            this.OnDisposing = onDisposing;
        }

        Once DisposeFlag;
        public override async ValueTask DisposeAsync()
        {
            try
            {
                if (DisposeFlag.IsFirstCall() == false) return;

                if (OnDisposing != null)
                    await OnDisposing();
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
                if (disposing) DisposeAsync()._GetResult();
            }
            finally { base.Dispose(disposing); }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Target.CurrentPosition;

        public override long Position
        {
            get => Target.CurrentPosition;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            Target.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.End:
                    if (offset == 0)
                    {
                        return PositionCache;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Seek is not suppoered.");
                    }

                case SeekOrigin.Begin:
                    if (offset == PositionCache)
                    {
                        return PositionCache;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Seek is not suppoered.");
                    }

                case SeekOrigin.Current:
                    if (offset == 0)
                    {
                        return PositionCache;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset), "Seek is not suppoered.");
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }
        }

        public override void SetLength(long value)
        {
            if (PositionCache == value)
                return;
            else
                throw new ArgumentOutOfRangeException(nameof(value), "Changing the length is not supported.");
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            this.Flush();
            return Task.CompletedTask;
        }

        override public int Read(Span<byte> buffer)
            => throw new NotSupportedException();

        override public void Write(ReadOnlySpan<byte> buffer)
        {
            Target.Append(buffer.ToArray());
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        override public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Target.AppendAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        override public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Target.AppendAsync(buffer, cancellationToken);
        }
    }
}

#endif

