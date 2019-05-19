// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
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

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class FileLazyWriterSettings
        {
            public static readonly Copenhagen<int> DefaultBufferSize = 4 * 1024 * 1024;
            public static readonly Copenhagen<int> DefaultDefragmentWriteBlockSize = 1 * 1024 * 1024;
            public static readonly Copenhagen<int> DefaultDelay = 250;

            public static readonly Copenhagen<int> FileWriteErrorRetryIntervalStd = 1 * 1000;
            public static readonly Copenhagen<int> FileWriteErrorRetryIntervalMax = 10 * 1000;
        }
    }

    class FileLazyWriterOptions
    {
        public FilePath FilePath { get; }
        public int BufferSize { get; }
        public int DefragmentWriteBlockSize { get; }
        public int Delay { get; }
        public bool AppendMode { get; }
        public FastStreamNonStopWriteMode OverflowBehavior { get; }

        public FileLazyWriterOptions(FilePath filePath, bool appendMode = true, FastStreamNonStopWriteMode overflowBehavior = FastStreamNonStopWriteMode.DiscardWritingData, int delay = 0, int bufferSize = 0, int defragmentWriteBlockSize = 0)
        {
            if (bufferSize <= 0) bufferSize = CoresConfig.FileLazyWriterSettings.DefaultBufferSize;
            if (defragmentWriteBlockSize <= 0) defragmentWriteBlockSize = CoresConfig.FileLazyWriterSettings.DefaultDefragmentWriteBlockSize;
            if (delay <= 0) delay = CoresConfig.FileLazyWriterSettings.DefaultDelay;

            this.FilePath = filePath;
            this.OverflowBehavior = overflowBehavior;
            this.DefragmentWriteBlockSize = defragmentWriteBlockSize;
            this.BufferSize = bufferSize;
            this.Delay = delay;
            this.AppendMode = appendMode;
        }
    }

    class FileLazyWriter : AsyncServiceWithMainLoop
    {
        public FileLazyWriterOptions Options { get; }

        PipeEnd Reader;
        PipeEnd Writer;

        public FileLazyWriter(FileLazyWriterOptions options, CancellationToken cancel = default) : base(cancel)
        {
            this.Options = options;

            this.Reader = PipeEnd.NewDuplexPipeAndGetOneSide(PipeEndSide.A_LowerSide, this.GrandCancel, Options.BufferSize);

            this.Writer = this.Reader.CounterPart;

            this.StartMainLoop(MainLoop);
        }

        public void Write(ReadOnlyMemory<byte> data)
        {
            CheckNotCanceled();

            try
            {
                lock (this.Writer.StreamWriter.LockObj)
                {
                    this.Writer.StreamWriter.NonStopWrite(data, false, this.Options.OverflowBehavior);
                }
            }
            catch { }
        }

        async Task MainLoop(CancellationToken cancel)
        {
            FileObject file = null;
            try
            {
                var st = this.Reader.StreamReader;
                int numFailed = 0;

                while (true)
                {
                    await TaskUtil.AwaitWithPoll(Timeout.Infinite, Options.Delay, () => this.Reader.StreamReader.IsReadyToRead(), cancel);

                    IReadOnlyList<ReadOnlyMemory<byte>> dataToWrite = this.Reader.StreamReader.DequeueAllWithLock(out long totalSize);
                    if (totalSize == 0)
                    {
                        if (cancel.IsCancellationRequested)
                        {
                            break;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    dataToWrite = Util.DefragmentMemoryArrays(dataToWrite, this.Options.DefragmentWriteBlockSize);

                    LABEL_CREATE_FILE_RETRY:

                    if (file == null)
                    {
                        try
                        {
                            if (this.Options.AppendMode)
                                file = await this.Options.FilePath.OpenOrCreateAppendAsync(additionalFlags: FileOperationFlags.AutoCreateDirectory);
                            else
                                file = await this.Options.FilePath.CreateAsync(additionalFlags: FileOperationFlags.AutoCreateDirectory);

                            numFailed = 0;
                        }
                        catch (Exception ex)
                        {
                            Con.WriteDebug($"FileLazyWriter -> MainLoop: OpenOrCreateAppendAsync('{this.Options.FilePath.ToString()}' failed. error: {ex.ToString()}");
                            numFailed++;
                            await cancel._WaitUntilCanceledAsync(Util.GenRandIntervalWithRetry(CoresConfig.FileLazyWriterSettings.FileWriteErrorRetryIntervalStd, numFailed, CoresConfig.FileLazyWriterSettings.FileWriteErrorRetryIntervalMax));
                            goto LABEL_CREATE_FILE_RETRY;
                        }
                    }

                    foreach (ReadOnlyMemory<byte> data in dataToWrite)
                    {
                        try
                        {
                            await file.WriteAsync(data);
                        }
                        catch (Exception ex)
                        {
                            Con.WriteDebug($"FileLazyWriter -> MainLoop: WriteAsync('{this.Options.FilePath.ToString()}' failed. error: {ex.ToString()}");
                            await cancel._WaitUntilCanceledAsync(1000);
                            file = null;
                        }
                    }

                    if (this.Reader.StreamReader.IsReadyToRead() == false)
                    {
                        await file.FlushAsync();
                    }
                }
            }
            finally
            {
                file._DisposeSafe();
            }
        }

        protected override void DisposeImpl(Exception ex)
        {
            try
            {
                this.Reader._DisposeSafe(ex);
                this.Writer._DisposeSafe(ex);
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }
    }
}

