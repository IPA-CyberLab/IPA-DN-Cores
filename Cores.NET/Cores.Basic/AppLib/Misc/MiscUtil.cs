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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Castle.Core.Logging;
using Microsoft.Extensions.Options;

namespace IPA.Cores.Basic
{
    // JSON をパースするとこの型が出てくる
    public class LogJsonParseAsRuntimeStat
    {
        public DateTimeOffset? TimeStamp;
        public CoresRuntimeStat? Data;
        public string? TypeName;    // "CoresRuntimeStat"
        public string? Kind;        // "Stat"
        public string? Priority;
        public string? Tag;         // "Snapshot"
        public string? AppName;
        public string? MachineName;
        public string? Guid;
    }

    public class LogStatMemoryLeakAnalyzerCsvRow
    {
        public DateTime Dt;
        public long Mem;
    }

    // stat ログをもとにメモリリークしていないかどうか分析するためのユーティリティクラス
    public static class LogStatMemoryLeakAnalyzer
    {
        public static List<LogStatMemoryLeakAnalyzerCsvRow> AnalyzeLogFiles(string logDir)
        {
            Dictionary<DateTime, long> table = new Dictionary<DateTime, long>();

            var files = Lfs.EnumDirectory(logDir).Where(x => x.IsFile && x.Name._IsExtensionMatch(".log")).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer);

            foreach (var file in files)
            {
                file.FullPath._Print();

                using var f = Lfs.Open(file.FullPath);
                using var stream = f.GetStream();
                var r = new BinaryLineReader(stream);
                while (true)
                {
                    List<Memory<byte>>? list = r.ReadLines();
                    if (list == null) break;

                    foreach (var data in list)
                    {
                        string line = data._GetString_UTF8();

                        try
                        {
                            var lineData = line._JsonToObject<LogJsonParseAsRuntimeStat>();

                            if (lineData != null)
                            {
                                if (lineData.TypeName == "CoresRuntimeStat" && lineData.Tag == "Snapshot")
                                {
                                    CoresRuntimeStat? stat = lineData.Data;
                                    if (stat != null)
                                    {
                                        if (stat.Mem != 0)
                                        {
                                            DateTime dt = lineData.TimeStamp!.Value.LocalDateTime.Date;
                                            if (table.TryAdd(dt, stat.Mem) == false)
                                            {
                                                table[dt] = Math.Min(table[dt], stat.Mem);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }
                }
            }

            List<LogStatMemoryLeakAnalyzerCsvRow> ret = new List<LogStatMemoryLeakAnalyzerCsvRow>();

            var dates = table.Keys.OrderBy(x => x);
            if (dates.Any())
            {
                for (DateTime date = dates.First(); date <= dates.Last(); date = date.AddDays(1))
                {
                    long mem = table.GetValueOrDefault(date, 0);

                    ret.Add(new LogStatMemoryLeakAnalyzerCsvRow { Dt = date, Mem = mem });
                }
            }

            return ret;
        }
    }

    public static partial class CoresConfig
    {
        public static partial class FileDownloader
        {
            public static readonly Copenhagen<int> DefaultMaxConcurrentThreads = 20;
            public static readonly Copenhagen<int> DefaultRetryIntervalMsecs = 1000;
            public static readonly Copenhagen<int> DefaultTryCount = 5;
            public static readonly Copenhagen<int> DefaultBufferSize = 1 * 1024 * 1024; // 1MB
            public static readonly Copenhagen<int> DefaultAdditionalConnectionIntervalMsecs = 1000;
            public static readonly Copenhagen<int> DefaultMaxConcurrentFiles = 20;
        }
    }

    // ファイルダウンロードオプション
    public class FileDownloadOption
    {
        public int MaxConcurrentThreads { get; }
        public int MaxConcurrentFiles { get; }
        public int RetryIntervalMsecs { get; }
        public int TryCount { get; }
        public WebApiOptions WebApiOptions { get; }
        public int BufferSize { get; }
        public int AdditionalConnectionIntervalMsecs { get; }

        public FileDownloadOption(int maxConcurrentThreads = -1, int maxConcurrentFiles = -1, int retryIntervalMsecs = -1, int tryCount = -1, int bufferSize = 0, int additionalConnectionIntervalMsecs = -1, WebApiOptions? webApiOptions = null)
        {
            if (maxConcurrentThreads <= 0) maxConcurrentThreads = CoresConfig.FileDownloader.DefaultMaxConcurrentThreads;
            if (maxConcurrentFiles <= 0) maxConcurrentFiles = CoresConfig.FileDownloader.DefaultMaxConcurrentFiles;
            if (retryIntervalMsecs < 0) retryIntervalMsecs = CoresConfig.FileDownloader.DefaultRetryIntervalMsecs;
            if (tryCount <= 0) tryCount = CoresConfig.FileDownloader.DefaultTryCount;
            if (webApiOptions == null) webApiOptions = new WebApiOptions();
            if (bufferSize <= 0) bufferSize = CoresConfig.FileDownloader.DefaultBufferSize;
            if (additionalConnectionIntervalMsecs <= 0) additionalConnectionIntervalMsecs = CoresConfig.FileDownloader.DefaultAdditionalConnectionIntervalMsecs;

            MaxConcurrentThreads = maxConcurrentThreads;
            MaxConcurrentFiles = maxConcurrentFiles;
            RetryIntervalMsecs = retryIntervalMsecs;
            TryCount = tryCount;
            WebApiOptions = webApiOptions;
            BufferSize = bufferSize;
            AdditionalConnectionIntervalMsecs = additionalConnectionIntervalMsecs;
        }
    }

    // 並行ダウンロードのようなタスクの部分マップの管理
    public class ConcurrentDownloadPartialMaps
    {
        public long TotalSize { get; }
        public int MaxPartialFragments { get; }

        public readonly CriticalSection Lock = new CriticalSection();

        internal readonly SortedList<long, ConcurrentDownloadPartial> List = new SortedList<long, ConcurrentDownloadPartial>();

        public ConcurrentDownloadPartialMaps(long totalSize, int maxPartialFragments = Consts.Numbers.DefaultMaxPartialFragments)
        {
            if (totalSize < 0) throw new ArgumentOutOfRangeException(nameof(totalSize));

            this.TotalSize = totalSize;
            this.MaxPartialFragments = maxPartialFragments;
        }

        // 未完了のバイト数を取得する
        public long CalcUnfinishedTotalSize()
        {
            lock (Lock)
            {
                if (List.Count == 0) return this.TotalSize;

                long calcDistanceTotal = 0;

                for (int i = 0; i < List.Count; i++)
                {
                    // この partial の後に続く partial までの空白距離を計算する
                    ConcurrentDownloadPartial thisPartial = List.Values[i];
                    long nextPartialStartPos = this.TotalSize;

                    if ((i + 1) < List.Count)
                    {
                        nextPartialStartPos = List.Values[i + 1].StartPosition;
                    }

                    long distance = nextPartialStartPos - (thisPartial.StartPosition + thisPartial.CurrentLength);
                    if (distance < 0) distance = 0;

                    calcDistanceTotal += distance;
                }

                return Math.Min(this.TotalSize, calcDistanceTotal);
            }
        }

        // 完了したバイト数を取得する
        public long CalcFinishedTotalSize() => this.TotalSize - CalcUnfinishedTotalSize();

        // 現在未完了の領域のうち最小の部分の中心を返す (ない場合は null を返す)
        public long? GetMaxUnfinishedPartialStartCenterPosison()
        {
            lock (Lock)
            {
                if (List.Count == 0) return 0;

                long maxDistance = long.MinValue;
                int maxDistancePartialIndex = -1;

                for (int i = 0; i < List.Count; i++)
                {
                    // この partial の後に続く partial までの空白距離を計算する
                    ConcurrentDownloadPartial thisPartial = List.Values[i];
                    long nextPartialStartPos = this.TotalSize;

                    if ((i + 1) < List.Count)
                    {
                        nextPartialStartPos = List.Values[i + 1].StartPosition;
                    }

                    long distance = nextPartialStartPos - (thisPartial.StartPosition + thisPartial.CurrentLength);
                    if (distance < 0) distance = 0;

                    if (distance > maxDistance)
                    {
                        maxDistancePartialIndex = i;
                        maxDistance = distance;
                    }
                }

                Debug.Assert(maxDistancePartialIndex != -1);

                Debug.Assert(maxDistance >= 0);

                if (maxDistance <= 0)
                {
                    // もうない
                    return null;
                }

                var maxDistancePartial = List.Values[maxDistancePartialIndex];

                return maxDistancePartial.StartPosition + maxDistancePartial.CurrentLength + maxDistance / 2;
            }
        }

        // 部分を開始する。startPosition が null の場合は、現在未完了の領域のうち最長の部分の中心を startPartial とする。
        // もうこれ以上部分を開始することができない場合は、null を返す。
        public ConcurrentDownloadPartial? StartPartial(long? startPosition = null)
        {
            lock (Lock)
            {
                if (startPosition == null) startPosition = GetMaxUnfinishedPartialStartCenterPosison();

                if (startPosition == null) return null; // もうない

                if (this.List.Count >= this.MaxPartialFragments)
                {
                    // これ以上作成できない
                    throw new CoresException("this.List.Count >= this.MaxPartialFragments");
                }

                if (this.List.ContainsKey(startPosition.Value))
                {
                    // もうある
                    return null;
                }

                var newPartial = new ConcurrentDownloadPartial(this, startPosition.Value);

                this.List.Add(newPartial.StartPosition, newPartial);

                return newPartial;
            }
        }

        // すべて完了しているかどうか
        public bool IsAllFinished()
        {
            if (GetMaxUnfinishedPartialStartCenterPosison() == null)
            {
                return true;
            }

            return false;
        }
    }
    public class ConcurrentDownloadPartial
    {
        public ConcurrentDownloadPartialMaps Maps { get; }
        public long StartPosition { get; }
        public long CurrentLength { get; private set; }

        Once Finished;

        public ConcurrentDownloadPartial(ConcurrentDownloadPartialMaps maps, long startPosition)
        {
            this.Maps = maps;
            this.StartPosition = startPosition;
            this.CurrentLength = 0;
        }

        // CurrentLength を変更する。先の Partial の先頭とぶつかったら、すべて完了したということであるので false を返す。
        public bool UpdateCurrentLength(long currentLength)
        {
            if (currentLength < 0) throw new ArgumentOutOfRangeException(nameof(currentLength));
            if (Finished.IsSet) throw new CoresException("ConcurrentDownloadPartial.Finished.IsSet");

            lock (Maps.Lock)
            {
                if (currentLength < this.CurrentLength) throw new ArgumentException("currentLength < this.CurrentLength");
                this.CurrentLength = currentLength;

                int thisIndex = Maps.List.IndexOfKey(this.StartPosition);
                Debug.Assert(thisIndex != -1);

                long nextPartialStartPos;

                int nextIndex = thisIndex + 1;
                if (nextIndex >= this.Maps.List.Count)
                {
                    nextPartialStartPos = this.Maps.TotalSize;
                }
                else
                {
                    nextPartialStartPos = this.Maps.List.Values[nextIndex].StartPosition;
                }

                if ((this.StartPosition + this.CurrentLength) >= nextPartialStartPos)
                {
                    // 次とぶつかった
                    return false;
                }

                // まだぶつかっていない
                return true;
            }
        }

        // CurrentLength を増加する。先の Partial の先頭とぶつかったら、すべて完了したということであるので false を返す。
        public bool AdvanceCurrentLength(long size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));

            lock (Maps.Lock)
            {
                return UpdateCurrentLength(this.CurrentLength + size);
            }
        }

        // この partial の処理を完了またはキャンセルするときに呼び出される。進捗 Length が 0 の場合、親 Map から自分自身を GC (消去) する。
        public void FinishOrCancelPartial()
        {
            if (Finished.IsFirstCall())
            {
                lock (Maps.Lock)
                {
                    if (this.CurrentLength == 0)
                    {
                        bool b = this.Maps.List.Remove(this.StartPosition);
                        Debug.Assert(b);
                    }
                }
            }
        }
    }

    public static class FileDownloader
    {
        // 指定されたファイルを分割ダウンロードする
        public static async Task DownloadFileParallelAsync(string url, Stream destStream, FileDownloadOption? option = null, Ref<WebSendRecvResponse>? responseHeader = null, ProgressReporterBase? progressReporter = null, CancellationToken cancel = default)
        {
            if (option == null) option = new FileDownloadOption();
            if (responseHeader == null) responseHeader = new Ref<WebSendRecvResponse>();
            if (progressReporter == null) progressReporter = new NullProgressReporter();
            AsyncLock streamLock = new AsyncLock();

            // まずファイルサイズを取得してみる
            RetryHelper<int> h = new RetryHelper<int>(option.RetryIntervalMsecs, option.TryCount);

            long fileSize = -1;
            bool supportPartialDownload = false;

            await h.RunAsync(async c =>
            {
                using var http = new WebApi(option.WebApiOptions);

                using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.HEAD, url, cancel));

                fileSize = res.DownloadContentLength ?? -1;

                // ヘッダ情報を参考情報として呼び出し元に返す
                responseHeader.Set(res);

                if (res.HttpResponseMessage.Headers.AcceptRanges.Where(x => x._IsSamei("bytes")).Any())
                {
                    supportPartialDownload = true;
                }

                return 0;
            }, retryInterval: option.RetryIntervalMsecs, tryCount: option.TryCount, cancel: cancel);

            if (fileSize >= 0 && supportPartialDownload)
            {
                // 分割ダウンロードが可能な場合は、分割ダウンロードを開始する
                destStream.SetLength(fileSize);

                ConcurrentDownloadPartialMaps maps = new ConcurrentDownloadPartialMaps(fileSize);

                AsyncConcurrentTask concurrent = new AsyncConcurrentTask(option.MaxConcurrentThreads);

                //List<Task<bool>> runningTasks = new List<Task<bool>>();

                RefBool noMoreNeedNewTask = false;
                AsyncManualResetEvent noMoreNeedNewTaskEvent = new AsyncManualResetEvent();

                RefInt taskIdSeed = 0;
                RefLong totalDownloadSize = 0;

                RefInt currentNumConcurrentTasks = new RefInt();

                Ref<Exception?> lastException = new Ref<Exception?>(null);

                using var cancel2 = new CancelWatcher(cancel);
                AsyncManualResetEvent finishedEvent = new AsyncManualResetEvent();
                //bool isTimeout = false;

                // 一定時間経ってもダウンロードサイズが全く増えないことを検知するタスク
                Task monitorTask = AsyncAwait(async () =>
                {
                    if (option.WebApiOptions.Settings.Timeout == Timeout.Infinite)
                    {
                        return;
                    }

                    long lastSize = 0;
                    long lastChangedTick = Time.Tick64;

                    while (finishedEvent.IsSet == false && cancel2.IsCancellationRequested == false)
                    {
                        long now = Time.Tick64;
                        long currentSize = totalDownloadSize;
                        if (lastSize != totalDownloadSize)
                        {
                            currentSize = totalDownloadSize;
                            lastChangedTick = now;
                        }

                        if (now > (lastChangedTick + option.WebApiOptions.Settings.Timeout))
                        {
                            // タイムアウト発生
                            cancel2.Cancel();
                            Dbg.Where();
                            //isTimeout = true;
                            lastException.Set(new TimeoutException());

                            break;
                        }

                        await TaskUtil.WaitObjectsAsync(cancels: cancel2.CancelToken._SingleArray(), manualEvents: finishedEvent._SingleArray(), timeout: 100);
                    }
                });

                while (noMoreNeedNewTask == false)
                {
                    cancel2.CancelToken.ThrowIfCancellationRequested();

                    // 同時に一定数までタスクを作成する
                    var newTask = await concurrent.StartTaskAsync<int, bool>(async (p1, c1) =>
                    {
                        //maps.CalcUnfinishedTotalSize()._Debug();
                        bool started = false;
                        int taskId = taskIdSeed.Increment();

                        try
                        {
                            // 新しい部分を開始
                            var partial = maps.StartPartial();
                            if (partial == null)
                            {
                                // もう新しい部分を開始する必要がない
                                //$"Task {taskId}: No more partial"._Debug();
                                if (maps.IsAllFinished())
                                {
                                    // IsAllFinished() は必ずチェックする。
                                    // そうしないと、1 バイトの空白領域が残っているときに取得未了になるおそれがあるためである。
                                    noMoreNeedNewTask.Set(true);
                                    noMoreNeedNewTaskEvent.Set(true);
                                }
                                return false;
                            }

                            try
                            {
                                currentNumConcurrentTasks.Increment();

                                //$"Task {taskId}: Start from {partial.StartPosition}"._Debug();

                                // ダウンロードの実施
                                using var http = new WebApi(option.WebApiOptions);
                                using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url + "", cancel2, rangeStart: partial.StartPosition));
                                using var src = res.DownloadStream;

                                // Normal copy
                                using (MemoryHelper.FastAllocMemoryWithUsing(option.BufferSize, out Memory<byte> buffer))
                                {
                                    while (true)
                                    {
                                        cancel2.ThrowIfCancellationRequested();

                                        Memory<byte> thisTimeBuffer = buffer;

                                        int readSize = await src.ReadAsync(thisTimeBuffer, cancel2);

                                        Debug.Assert(readSize <= thisTimeBuffer.Length);

                                        if (readSize <= 0)
                                        {
                                            //$"Task {taskId}: No more recv data"._Debug();
                                            break;
                                        }

                                        started = true;

                                        ReadOnlyMemory<byte> sliced = thisTimeBuffer.Slice(0, readSize);

                                        using (await streamLock.LockWithAwait(cancel2))
                                        {
                                            destStream.Position = partial.StartPosition + partial.CurrentLength;
                                            await destStream.WriteAsync(sliced, cancel2);
                                            totalDownloadSize.Add(sliced.Length);
                                        }

                                        progressReporter.ReportProgress(new ProgressData(maps.CalcFinishedTotalSize(), maps.TotalSize, false, $"{currentNumConcurrentTasks} connections"));

                                        if (partial.AdvanceCurrentLength(sliced.Length) == false)
                                        {
                                            // 次の partial または末尾にぶつかった
                                            //$"Task {taskId}: Reached to the next partial"._Debug();
                                            break;
                                        }
                                    }
                                }

                                //$"Task {taskId}: Finished. Position: {partial.StartPosition + partial.CurrentLength}, size: {partial.CurrentLength}"._Debug();
                                return false;
                            }
                            finally
                            {
                                currentNumConcurrentTasks.Decrement();
                                partial.FinishOrCancelPartial();
                            }
                        }
                        catch (Exception ex)
                        {
                            if (started == false)
                            {
                                //$"Task {taskId}: error. {ex._GetSingleException().Message}"._Debug();
                            }
                            lastException.Set(ex);
                            return false;
                        }
                    },
                    0,
                    cancel2.CancelToken);

                    await noMoreNeedNewTaskEvent.WaitAsync(option.AdditionalConnectionIntervalMsecs, cancel2);
                }

                //Dbg.Where();
                await concurrent.WaitAllTasksFinishAsync();
                //Dbg.Where();

                finishedEvent.Set(true);

                await monitorTask._TryAwait(false);

                //if (isTimeout) lastException.Set(new TimeoutException());

                if (maps.IsAllFinished() == false)
                {
                    if (lastException.Value != null)
                    {
                        // エラーが発生していた
                        lastException.Value._ReThrow();
                    }
                    else
                    {
                        throw new CoresException("maps.IsAllFinished() == false");
                    }
                }
                else
                {
                    progressReporter.ReportProgress(new ProgressData(maps.TotalSize, maps.TotalSize, true));
                }

                //$"File Size = {fileSize._ToString3()}, Total Down Size = {totalDownloadSize.Value._ToString3()}"._Debug();
            }
            else
            {
                // 分割ダウンロード NG の場合は、通常の方法でダウンロードする
                using var http = new WebApi(option.WebApiOptions);

                using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url, cancel));

                long totalSize = await res.DownloadStream.CopyBetweenStreamAsync(destStream, reporter: progressReporter, cancel: cancel);

                progressReporter.ReportProgress(new ProgressData(totalSize, isFinish: true));
            }
        }

        // 指定された URL (のテキストファイル) をダウンロードし、その URL に記載されているすべてのファイルをダウンロードする
        public static async Task DownloadUrlListedAsync(string urlListedFileUrl, string destDir, string extensions, FileDownloadOption? option = null, ProgressReporterFactoryBase? reporterFactory = null, CancellationToken cancel = default)
        {
            if (option == null) option = new FileDownloadOption();
            if (reporterFactory == null) reporterFactory = new NullReporterFactory();

            // ファイル一覧のファイルをダウンロードする
            using var web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true }));

            List<string> fileUrlList = new List<string>();

            var urlFileBody = await web.SimpleQueryAsync(WebMethods.GET, urlListedFileUrl, cancel);
            string body = urlFileBody.Data._GetString_UTF8();
            int currentPos = 0;

            while (true)
            {
                int r = body._FindStringsMulti2(currentPos, StringComparison.OrdinalIgnoreCase, out _, "http://", "https://");
                if (r == -1) break;
                currentPos = r + 1;

                string fileUrl = body.Substring(r);
                int t = fileUrl._FindStringsMulti2(0, StringComparison.OrdinalIgnoreCase, out _, " ", "　", "\t", "'", "\"", "\r", "\n", "]", ">");
                if (t != -1)
                {
                    fileUrl = fileUrl.Substring(0, t);
                }

                if (fileUrl._IsExtensionMatch(extensions))
                {
                    fileUrlList.Add(fileUrl);
                }
            }

            await TaskUtil.ForEachAsync(option.MaxConcurrentFiles, fileUrlList, async (fileUrl, cancel) =>
            {
                string destFileName = PathParser.Mac.GetFileName(fileUrl);
                string destFileFullPath = Lfs.PathParser.Combine(destDir, destFileName);

                using var reporter = reporterFactory.CreateNewReporter(destFileName);

                using var file = await Lfs.CreateAsync(destFileFullPath, false, FileFlags.AutoCreateDirectory, cancel: cancel);
                using var fileStream = file.GetStream();

                await DownloadFileParallelAsync(fileUrl, fileStream, option, progressReporter: reporter, cancel: cancel);
            }, cancel: cancel);
        }
    }

    public static partial class CoresConfig
    {
        public static partial class GitParallelUpdater
        {
            public static readonly Copenhagen<int> GitCommandTimeoutMsecs = 10 * 60 * 1000;
            public static readonly Copenhagen<int> GitCommandOutputMaxSize = 10 * 1024 * 1024;
        }
    }

    public static class GitParallelUpdater
    {
        class Entry
        {
            public string DirPath = null!;
            public string OriginName = "origin";
            public string BranchName = "master";
        }

        public static async Task ExecGitParallelUpdaterAsync(string rootDirPath, int maxConcurrentTasks, CancellationToken cancel = default)
        {
            string gitExePath = GitUtil.GetGitForWindowsExeFileName();

            List<Entry> entryList = new List<Entry>();

            // 指定されたディレクトリにあるサブディレクトリの一覧を列挙し、その中に .git サブディレクトリがあるものを git ローカルリポジトリとして列挙する
            var subDirList = await Lfs.EnumDirectoryAsync(rootDirPath);

            foreach (var subDir in subDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
            {
                string gitDirPath = Lfs.PathParser.Combine(subDir.FullPath, ".git");

                if ((await Lfs.IsDirectoryExistsAsync(gitDirPath, cancel)))
                {
                    subDir.FullPath._Debug();

                    entryList.Add(new Entry { DirPath = subDir.FullPath });
                }
            }

            List<Tuple<Entry, Task>> RunningTasksList = new List<Tuple<Entry, Task>>();

            using SemaphoreSlim sem = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
            int index = 0;
            foreach (var entry in entryList)
            {
                index++;
                Task t = AsyncAwait(async () =>
                {
                    int thisIndex = index;
                    var target = entry;

                    await sem.WaitAsync(cancel);

                    try
                    {

                        string printTag = "[" + thisIndex + ": " + Lfs.PathParser.GetFileName(target.DirPath) + "]";

                        try
                        {
                            var result1 = await EasyExec.ExecAsync(gitExePath, $"pull {target.OriginName} {target.BranchName}", target.DirPath,
                                timeout: CoresConfig.GitParallelUpdater.GitCommandTimeoutMsecs,
                                easyOutputMaxSize: CoresConfig.GitParallelUpdater.GitCommandOutputMaxSize,
                                cancel: cancel,
                                printTag: printTag,
                                flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut);

                            var result2 = await EasyExec.ExecAsync(gitExePath, $"submodule update --init --recursive", target.DirPath,
                                timeout: CoresConfig.GitParallelUpdater.GitCommandTimeoutMsecs,
                                easyOutputMaxSize: CoresConfig.GitParallelUpdater.GitCommandOutputMaxSize,
                                cancel: cancel,
                                printTag: printTag,
                                flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut);
                        }
                        catch (Exception ex)
                        {
                            string error = $"*** Error - {Lfs.PathParser.GetFileName(target.DirPath)} ***\n{ex.Message}\n\n";

                            Con.WriteError(error);

                            throw;
                        }
                    }
                    finally
                    {
                        sem.Release();
                    }
                });

                RunningTasksList.Add(new Tuple<Entry, Task>(entry, t));
            }

            while (true)
            {
                if (cancel.IsCancellationRequested)
                {
                    // キャンセルされた
                    // すべてのタスクが終了するまで待機する
                    RunningTasksList.ForEach(x => x.Item2._TryWait());
                    return;
                }

                if (RunningTasksList.Select(x => x.Item2).All(x => x.IsCompleted))
                {
                    // すべてのタスクが完了した
                    break;
                }

                // 未完了タスク数を表示する
                int numCompleted = RunningTasksList.Where(x => x.Item2.IsCompleted).Count();

                string str = $"\n--- Completed: {numCompleted} / {RunningTasksList.Count}\n" +
                    $"Running tasks: {RunningTasksList.Where(x=>x.Item2.IsCompleted == false).Select(x=>x.Item1.DirPath)._Combine(", ")}\n";

                Con.WriteLine(str);

                await TaskUtil.WaitObjectsAsync(tasks: RunningTasksList.Select(x => x.Item2).Where(x=>x.IsCompleted == false), cancels: cancel._SingleArray(), timeout: 1000);
            }

            Con.WriteLine($"\n--- All tasks completed.");
            if (RunningTasksList.Select(x => x.Item2).All(x => x.IsCompletedSuccessfully))
            {
                Con.WriteLine($"All {RunningTasksList.Count} tasks completed with OK.");
            }
            else
            {
                Con.WriteLine($"Error tasks: ");

                foreach (var item in RunningTasksList.Where(x => x.Item2.IsCompletedSuccessfully == false))
                {
                    Con.WriteLine($"  {Lfs.PathParser.GetFileName(item.Item1.DirPath)}: {(item.Item2.Exception?._GetSingleException().Message ?? "Unknown")}\n");
                }

                throw new CoresException("One or more git tasks resulted errors");
            }
        }
    }
}

#endif

