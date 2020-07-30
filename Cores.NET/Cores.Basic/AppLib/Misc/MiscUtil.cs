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
        }
    }

    // ファイルダウンロードオプション
    public class FileDownloadOption
    {
        public int MaxConcurrentThreads { get; }
        public int RetryIntervalMsecs { get; }
        public int TryCount { get; }
        public WebApiOptions WebApiOptions { get; }

        public FileDownloadOption(int maxConcurrentThreads = -1, int retryIntervalMsecs = -1, int tryCount = -1, WebApiOptions? webApiOptions = null)
        {
            if (maxConcurrentThreads <= 0) maxConcurrentThreads = CoresConfig.FileDownloader.DefaultMaxConcurrentThreads;
            if (retryIntervalMsecs < 0) retryIntervalMsecs = CoresConfig.FileDownloader.DefaultRetryIntervalMsecs;
            if (tryCount <= 0) tryCount = CoresConfig.FileDownloader.DefaultTryCount;
            if (webApiOptions == null) webApiOptions = new WebApiOptions();

            MaxConcurrentThreads = maxConcurrentThreads;
            RetryIntervalMsecs = retryIntervalMsecs;
            TryCount = tryCount;
            WebApiOptions = webApiOptions;
        }
    }

    // 並行ダウンロードのようなタスクの部分マップの管理
    public class ConcurrentDownloadPartialMaps
    {
        public long TotalSize { get; }

        public readonly CriticalSection Lock = new CriticalSection();

        internal readonly SortedList<long, ConcurrentDownloadPartial> List = new SortedList<long, ConcurrentDownloadPartial>();

        public ConcurrentDownloadPartialMaps(long totalSize)
        {
            if (totalSize < 0) throw new ArgumentOutOfRangeException(nameof(totalSize));

            this.TotalSize = totalSize;
        }

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
                    ConcurrentDownloadPartial thisPartial = List[i];
                    long nextPartialStartPos = this.TotalSize;

                    if ((i + 1) < List.Count)
                    {
                        nextPartialStartPos = List[i + 1].StartPosition;
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

                if (maxDistance < 2)
                {
                    // もうない
                    return null;
                }

                var maxDistancePartial = List[maxDistancePartialIndex];

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

                var newPartial = new ConcurrentDownloadPartial(this, startPosition.Value);

                this.List.Add(newPartial.StartPosition, newPartial);

                return newPartial;
            }
        }
    }
    public class ConcurrentDownloadPartial : IComparable<ConcurrentDownloadPartial>
    {
        public ConcurrentDownloadPartialMaps Maps { get; }
        public long StartPosition { get; }
        public long CurrentLength { get; private set; }

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
                    nextPartialStartPos = this.Maps.List[nextIndex].StartPosition;
                }

                if (this.CurrentLength >= nextPartialStartPos)
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

        public int CompareTo(ConcurrentDownloadPartial? other)
        {
            return this.StartPosition.CompareTo(other!.StartPosition);
        }
    }

    public static class FileDownloader
    {
        // 指定されたファイルを分割ダウンロードする
        public static async Task DownloadFileAsync(string url, Stream destStream, FileDownloadOption? option = null, Ref<WebSendRecvResponse>? responseHeader = null, CancellationToken cancel = default)
        {
            if (option == null) option = new FileDownloadOption();
            if (responseHeader == null) responseHeader = new Ref<WebSendRecvResponse>();

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
            });

            if (fileSize >= 0 && supportPartialDownload)
            {
                // 分割ダウンロードが可能な場合は、分割ダウンロードを開始する
                destStream.SetLength(fileSize);

                ConcurrentDownloadPartialMaps maps = new ConcurrentDownloadPartialMaps(fileSize);

                AsyncConcurrentTask concurrent = new AsyncConcurrentTask(option.MaxConcurrentThreads);

                List<Task<bool>> runningTasks = new List<Task<bool>>();

                Ref<bool> noMoreNeedNewTask = false;

                while (noMoreNeedNewTask == false)
                {
                    // 同時に一定数までタスクを作成する
                    var newTask = await concurrent.StartTaskAsync<int, bool>(async (p1, c1) =>
                    {
                        // 新しい部分を開始
                        var partial = maps.StartPartial();
                        if (partial == null)
                        {
                            // もう新しい部分を開始する必要がない
                            noMoreNeedNewTask.Set(true);
                            return false;
                        }

                        // ダウンロードの実施
                        using var http = new WebApi(option.WebApiOptions);

                        using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url, cancel));
                    },
                    0,
                    cancel);

                    lock (runningTasks)
                        runningTasks.Add(newTask);
                }

                await concurrent.WaitAllTasksFinishAsync();
            }
        }

        // 指定された URL (のテキストファイル) をダウンロードし、その URL に記載されているすべてのファイルをダウンロードする
        public static async Task DownloadUrlListedAsync(string urlListedFileUrl, string destDir, string extensions, int numRetrt = 5, CancellationToken cancel = default)
        {
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

            foreach (string fileUrl in fileUrlList)
            {
                RetryHelper<int> h = new RetryHelper<int>(1000, numRetrt);

                await h.RunAsync(async c =>
                {
                    using var down = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true }));

                    using var res = await down.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, fileUrl, cancel));

                    string destFileFullPath = Lfs.PathParser.Combine(destDir, PathParser.Mac.GetFileName(fileUrl));

                    Con.WriteLine(destFileFullPath);

                    using var file = await Lfs.CreateAsync(destFileFullPath, flags: FileFlags.AutoCreateDirectory, cancel: cancel);

                    using var fileStream = file.GetStream();

                    await res.DownloadStream.CopyBetweenStreamAsync(fileStream);

                    return 0;
                },
                cancel: cancel);
            }
        }
    }
}

#endif

