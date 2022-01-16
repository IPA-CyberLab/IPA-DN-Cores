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
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Castle.Core.Logging;
using Microsoft.Extensions.Options;
using System.Xml;

namespace IPA.Cores.Basic;

public class PoderosaSettingsContents
{
    public string? HostName;
    public int Port;
    public string? Method;
    public string? Username;
    public string? Password;

    public PoderosaSettingsContents() { }

    public PoderosaSettingsContents(string settingsBody)
    {
        var xml = new XmlDocument();
        xml.LoadXml(settingsBody);

        var shortcut = xml.SelectSingleNode("poderosa-shortcut");
        if (shortcut == null) throw new CoresLibException("Not a poderosa-shortcut file.");

        var ver = shortcut.Attributes!["version"]!.Value;
        if (ver != "4.0") throw new CoresLibException("ver != '4.0'");

        var sshParam = shortcut.SelectSingleNode("Poderosa.Protocols.SSHLoginParameter");
        if (sshParam == null) throw new CoresLibException("Not a SSH shortcut file.");

        this.HostName = sshParam!.Attributes!["destination"]?.Value._NonNull();
        this.Port = sshParam.Attributes["port"]?.Value._NonNull()._ToInt() ?? 0;
        if (this.Port == 0) this.Port = Consts.Ports.Ssh;

        this.Username = sshParam.Attributes["account"]?.Value._NonNull();
        this.Password = sshParam.Attributes["passphrase"]?.Value._NonNull();
    }

    public SecureShellClientSettings GetSshClientSettings(int connectTimeoutMsecs = 0, int commTimeoutMsecs = 0)
    {
        return new SecureShellClientSettings(this.HostName._NullCheck(), this.Port, this.Username._NullCheck(), this.Password._NonNull(), connectTimeoutMsecs, commTimeoutMsecs);
    }

    public SecureShellClient CreateSshClient(int connectTimeoutMsecs = 0, int commTimeoutMsecs = 0)
    {
        return new SecureShellClient(this.GetSshClientSettings(connectTimeoutMsecs, commTimeoutMsecs));
    }
}

public static class PoderosaSettingsContentsHelper
{
    public static async Task<PoderosaSettingsContents> ReadPoderosaFileAsync(this FileSystem fs, string fileName, CancellationToken cancel = default)
        => new PoderosaSettingsContents(await fs.ReadStringFromFileAsync(fileName, cancel: cancel));

    public static PoderosaSettingsContents ReadPoderosaFile(this FileSystem fs, string fileName, CancellationToken cancel = default)
        => ReadPoderosaFileAsync(fs, fileName, cancel)._GetResult();
}

public class BatchExecSshItem
{
    public string Host = "";
    public int Port = Consts.Ports.Ssh;
    public string Username = "root";
    public string Password = "";
    public string CommandLine = "";
}

// 色々なおまけユーティリティ
public static partial class MiscUtil
{
    // バイナリファイルの内容をバイナリで置換する
    public static async Task<KeyValueList<string, int>> ReplaceBinaryFileAsync(FilePath srcFilePath, FilePath? destFilePath, KeyValueList<string, string> oldNewList, FileFlags additionalFlags = FileFlags.None, byte fillByte = 0x0A, int bufferSize = Consts.Numbers.DefaultVeryLargeBufferSize, CancellationToken cancel = default)
    {
        if (destFilePath == null) destFilePath = srcFilePath;

        checked
        {
            List<Pair4<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, HashSet<long>>> list = new List<Pair4<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, HashSet<long>>>();

            // 引数チェック等
            foreach (var kv in oldNewList)
            {
                string oldStr = kv.Key;
                string newStr = kv.Value;

                Memory<byte> oldData = oldStr._GetHexOrString();
                Memory<byte> newData = newStr._GetHexOrString();

                if (oldData.Length == 0 && newData.Length == 0)
                {
                    continue;
                }

                if (oldData.Length != newData.Length)
                {
                    if (oldData.Length == 0)
                    {
                        throw new CoresException("oldData.Length == 0");
                    }
                    else if (oldData.Length < newData.Length)
                    {
                        throw new CoresException("oldData.Length < newData.Length");
                    }
                    else
                    {
                        Memory<byte> newData2 = new byte[oldData.Length];

                        newData.CopyTo(newData2);

                        newData2.Slice(newData.Length).Span.Fill(fillByte);

                        newData = newData2;
                    }
                }

                list.Add(new Pair4<string, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, HashSet<long>>(oldStr, oldData, newData, new HashSet<long>()));
            }

            if (list.Any() == false)
            {
                // 置換なし
                return new KeyValueList<string, int>();
            }

            bool same = (srcFilePath.Equals(destFilePath));

            await using FileObject srcFile = same ? await srcFilePath.OpenAsync(true, cancel: cancel, additionalFlags: additionalFlags) : await srcFilePath.OpenAsync(false, cancel: cancel, additionalFlags: additionalFlags);
            await using FileObject destFile = same ? srcFile : await destFilePath.CreateAsync(cancel: cancel, additionalFlags: additionalFlags);

            long filesize = await srcFile.GetFileSizeAsync(cancel: cancel);

            bufferSize = (int)Math.Min(bufferSize, filesize);

            bufferSize = Math.Max(bufferSize, list.Max(x => x.B.Length) * 2);

            using (MemoryHelper.FastAllocMemoryWithUsing(bufferSize, out Memory<byte> buffer))
            {
                for (long pos = 0; pos < filesize; pos += ((bufferSize + 1) / 2))
                {
                    long blockSize = Math.Min(bufferSize, filesize - pos);

                    int actualSize = await srcFile.ReadRandomAsync(pos, buffer, cancel);

                    if (actualSize >= 1)
                    {
                        Memory<byte> target = buffer.Slice(0, actualSize);

                        bool modified = false;

                        Sync(() =>
                        {
                            var span = target.Span;

                            foreach (var item in list)
                            {
                                int start = 0;
                                while (true)
                                {
                                    int found = span._IndexOfAfter(item.B.Span, start);
                                    if (found == -1) break;
                                    start = found + 1;

                                    item.C.Span.CopyTo(span.Slice(found));
                                    item.D.Add(found + pos);
                                    modified = true;
                                }
                            }
                        });

                        if (same == false || modified)
                        {
                            await destFile.WriteRandomAsync(pos, target, cancel);
                        }
                    }
                }
            }

            KeyValueList<string, int> ret = new KeyValueList<string, int>();

            foreach (var item in list)
            {
                ret.Add(item.A, item.D.Count);
            }

            return ret;
        }
    }

    // 複数のホストに対して SSH コマンドをバッチ実行する
    public static async Task BatchExecSshAsync(IEnumerable<BatchExecSshItem> items, UnixShellProcessorSettings? settings = null)
    {
        foreach (var item in items)
        {
            $"------- {item.Host} ------"._Print();
            try
            {
                await using SecureShellClient ssh = new SecureShellClient(new SecureShellClientSettings(item.Host, item.Port, item.Username, item.Password));
                await using ShellClientSock sock = await ssh.ConnectAndGetSockAsync();
                await using var proc = sock.CreateUnixShellProcessor(settings);

                var result = await proc.ExecBashCommandAsync(item.CommandLine);


                $"\n\n----- OK -----\n\n"._Print();
                result.StringList._OneLine()._Print();
            }
            catch (Exception ex)
            {
                "\n\n-- !! ERROR !! --\n\n"._Print();
                ex._Debug();
                "\n\n"._Print();
            }
        }
    }

    public static void GenkoToHtml(string srcTxt, string dstHtml)
    {
        string body = Lfs.ReadStringFromFile(srcTxt);

        string[] lines = body._GetLines();

        StringWriter w = new StringWriter();

        foreach (string line in lines)
        {
            string s = line;

            if (s._IsEmpty())
            {
                s = $"<p>{Str.HtmlSpacing}</p>";
            }
            else if (s.All(c => c <= 0x7f))
            {
                s = $"<p style=\"text-align: center\">{s._EncodeHtml()}</p>";
            }
            else
            {
                s = $"<p>{s._EncodeHtml()}</p>";
            }

            w.WriteLine(s);
        }

        Lfs.WriteStringToFile(dstHtml, w.ToString(), FileFlags.AutoCreateDirectory, writeBom: true);
    }

    public static void ReplaceStringOfFiles(string dirName, string pattern, string oldString, string newString, bool caseSensitive)
    {
        IEnumerable<string> files;
        if (Directory.Exists(dirName) == false && File.Exists(dirName))
        {
            files = dirName._SingleArray();
        }
        else
        {
            files = Lfs.EnumDirectory(dirName, true)
                .Where(x => x.IsFile)
                .Where(x => Lfs.PathParser.IsFullPathExcludedByExcludeDirList(Lfs.PathParser.GetDirectoryName(x.FullPath)) == false)
                .Where(x => Str.MultipleWildcardMatch(x.Name, pattern, true))
                .Select(x => x.FullPath);
        }

        int n = 0;

        foreach (string file in files)
        {
            Con.WriteLine("処理中: '{0}'", file);

            byte[] data = File.ReadAllBytes(file);

            int bom;
            Encoding? enc = Str.GetEncoding(data, out bom);
            if (enc == null)
            {
                enc = Encoding.UTF8;
            }

            string srcStr = enc.GetString(Util.ExtractByteArray(data, bom, data.Length - bom));
            string dstStr = Str.ReplaceStr(srcStr, oldString, newString, caseSensitive);

            if (srcStr != dstStr)
            {
                Buf buf = new Buf();

                if (bom != 0)
                {
                    var bomData = Str.GetBOM(enc);
                    if (bomData != null)
                    {
                        buf.Write(bomData);
                    }
                }

                buf.Write(enc.GetBytes(dstStr));

                buf.SeekToBegin();

                File.WriteAllBytes(file, buf.Read());

                Con.WriteLine("  保存しました。");
                n++;
            }
            else
            {
                Con.WriteLine("  変更なし");
            }
        }

        Con.WriteLine("{0} 個のファイルを変換しましたよ!!", n);
    }

    public static void NormalizeCrLfOfFiles(string dirName, string pattern)
    {
        IEnumerable<string> files;
        if (Directory.Exists(dirName) == false && File.Exists(dirName))
        {
            files = dirName._SingleArray();
        }
        else
        {
            files = Lfs.EnumDirectory(dirName, true)
                .Where(x => x.IsFile)
                .Where(x => Lfs.PathParser.IsFullPathExcludedByExcludeDirList(Lfs.PathParser.GetDirectoryName(x.FullPath)) == false)
                .Where(x => Str.MultipleWildcardMatch(x.Name, pattern, true))
                .Select(x => x.FullPath);
        }

        int n = 0;

        foreach (string file in files)
        {
            Con.WriteLine("処理中: '{0}'", file);

            byte[] data = File.ReadAllBytes(file);

            var ret = Str.NormalizeCrlf(data, CrlfStyle.CrLf, true);

            File.WriteAllBytes(file, ret.ToArray());

            n++;
        }

        Con.WriteLine("{0} 個のファイルを変換しましたよ!!", n);
    }

    public static void ChangeEncodingOfFiles(string dirName, string pattern, bool bom, string encoding)
    {
        IEnumerable<string> files;
        if (Directory.Exists(dirName) == false && File.Exists(dirName))
        {
            files = dirName._SingleArray();
        }
        else
        {
            files = Lfs.EnumDirectory(dirName, true)
                .Where(x => x.IsFile)
                .Where(x => Lfs.PathParser.IsFullPathExcludedByExcludeDirList(Lfs.PathParser.GetDirectoryName(x.FullPath)) == false)
                .Where(x => Str.MultipleWildcardMatch(x.Name, pattern, true))
                .Select(x => x.FullPath);
        }

        Encoding enc = Encoding.GetEncoding(encoding);

        int n = 0;

        foreach (string file in files)
        {
            Con.WriteLine("処理中: '{0}'", file);

            byte[] data = File.ReadAllBytes(file);

            byte[] ret = Str.ConvertEncoding(data, enc, bom);

            File.WriteAllBytes(file, ret);

            n++;
        }

        Con.WriteLine("{0} 個のファイルを変換しましたよ!!", n);
    }
}

// ログファイルの JSON をパースするとこの型が出てくる
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

        // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
        public static void ApplyHeavyLoadServerConfig()
        {
            DefaultBufferSize.TrySet(65536);
        }
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

    public readonly CriticalSection Lock = new CriticalSection<ConcurrentDownloadPartialMaps>();

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

            await using var cancel2 = new CancelWatcher(cancel);
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

                    if (lastSize != currentSize)
                    {
                        lastSize = currentSize;
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

                                    int readSize = await src._ReadAsyncWithTimeout(thisTimeBuffer, timeout: option.WebApiOptions.Settings.Timeout, cancel: cancel2);

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

            long totalSize = await res.DownloadStream.CopyBetweenStreamAsync(destStream, reporter: progressReporter, cancel: cancel, readTimeout: option.WebApiOptions.Settings.Timeout);

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

        await TaskUtil.ForEachAsync(option.MaxConcurrentFiles, fileUrlList, async (fileUrl, taskIndex, cancel) =>
        {
            string destFileName = PathParser.Mac.GetFileName(fileUrl);
            string destFileFullPath = Lfs.PathParser.Combine(destDir, destFileName);

            using var reporter = reporterFactory.CreateNewReporter(destFileName);

            using var file = await Lfs.CreateAsync(destFileFullPath, false, FileFlags.AutoCreateDirectory, cancel: cancel);
            await using var fileStream = file.GetStream();

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
        public static readonly Copenhagen<string> GitParallelTxtFileName = "GitParallelUpdate.txt";
    }
}

public static class GitParallelUpdater
{
    class Entry
    {
        public string DirPath = null!;
        public string OriginName = "origin";
        public string BranchName = "";
        public bool Ignore = false;
    }

    public static async Task ExecGitParallelUpdaterAsync(string rootDirPath, int maxConcurrentTasks, string? settingFileName = null, CancellationToken cancel = default)
    {
        string gitExePath = Util.GetGitForWindowsExeFileName();

        List<Entry> entryList = new List<Entry>();

        List<Entry> settingsList = new List<Entry>();

        // 指定されたディレクトリに GitParallelUpdate.txt ファイルがあれば読み込む
        string txtFilePath = PP.Combine(rootDirPath, CoresConfig.GitParallelUpdater.GitParallelTxtFileName);

        if (settingFileName._IsFilled())
            txtFilePath = settingFileName;

        if (await Lfs.IsFileExistsAsync(txtFilePath))
        {
            string body = await Lfs.ReadStringFromFileAsync(txtFilePath, cancel: cancel);
            foreach (string line2 in body._GetLines(true))
            {
                string line = line2._StripCommentFromLine();

                if (line._IsFilled())
                {
                    string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, " ", "\t");

                    if (tokens.Length >= 1)
                    {
                        Entry e = new Entry
                        {
                            DirPath = tokens.ElementAtOrDefault(0)!,
                            OriginName = tokens.ElementAtOrDefault(1)!,
                            BranchName = tokens.ElementAtOrDefault(2)!,
                        };

                        if (e.OriginName._IsSamei("ignore"))
                        {
                            e.Ignore = true;
                        }
                        else
                        {
                            e.OriginName = e.OriginName._FilledOrDefault("origin");
                            e.BranchName = e.BranchName._FilledOrDefault("");
                        }

                        settingsList.Add(e);
                    }
                }
            }
        }

        // 指定されたディレクトリにあるサブディレクトリの一覧を列挙し、その中に .git サブディレクトリがあるものを git ローカルリポジトリとして列挙する
        var subDirList = await Lfs.EnumDirectoryAsync(rootDirPath);

        foreach (var subDir in subDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
        {
            string gitDirPath = Lfs.PathParser.Combine(subDir.FullPath, ".git");

            if ((await Lfs.IsDirectoryExistsAsync(gitDirPath, cancel)))
            {
                subDir.FullPath._Debug();

                var e = new Entry { DirPath = subDir.FullPath };

                var setting = settingsList.Where(x => x.DirPath._IsSamei(subDir.Name)).FirstOrDefault();

                if (setting != null)
                {
                    if (setting.Ignore)
                    {
                        continue;
                    }

                    e.OriginName = setting.OriginName;
                    e.BranchName = setting.BranchName;
                }

                entryList.Add(e);
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
                $"Running tasks: {RunningTasksList.Where(x => x.Item2.IsCompleted == false).Select(x => Lfs.PathParser.GetFileName(x.Item1.DirPath))._Combine(", ")}\n";

            Con.WriteLine(str);

            await TaskUtil.WaitObjectsAsync(tasks: RunningTasksList.Select(x => x.Item2).Where(x => x.IsCompleted == false), cancels: cancel._SingleArray(), timeout: 1000);
        }

        Con.WriteLine($"\n--- All tasks completed.");
        if (RunningTasksList.Select(x => x.Item2).All(x => x.IsCompletedSuccessfully))
        {
            Con.WriteLine($"All {RunningTasksList.Count} tasks completed with OK.\n\n");
        }
        else
        {
            Con.WriteLine($"OK tasks: {RunningTasksList.Where(x => x.Item2.IsCompletedSuccessfully).Count()}, Error tasks: {RunningTasksList.Where(x => x.Item2.IsCompletedSuccessfully == false).Count()}");

            foreach (var item in RunningTasksList.Where(x => x.Item2.IsCompletedSuccessfully == false))
            {
                Con.WriteLine($"  [{Lfs.PathParser.GetFileName(item.Item1.DirPath)}]: {(item.Item2.Exception?._GetSingleException().Message ?? "Unknown")}\n");
            }

            throw new CoresException("One or more git tasks resulted errors");
        }
    }
}

public class SqlVaultUtilReaderState
{
    public Dictionary<string, long> TableNameAndLastRowId = new Dictionary<string, long>(StrComparer.IgnoreCaseComparer);
}

public class SqlVaultUtil : AsyncService
{
    public IEnumerable<DataVaultClientOptions> DataVaultClientOptions { get; }
    public Database Db { get; }
    public string SettingsName { get; }

    readonly SingleInstance Instance;

    public HiveData<SqlVaultUtilReaderState> StateDb { get; }

    public SqlVaultUtil(Database database, string settingsName, params DataVaultClientOptions[] dataVaultClientOptions)
    {
        try
        {
            // 多重起動は許容しません!!
            this.Instance = new SingleInstance(settingsName);

            this.DataVaultClientOptions = dataVaultClientOptions;
            this.Db = database;
            this.SettingsName = settingsName;

            this.StateDb = Hive.SharedLocalConfigHive.CreateAutoSyncHive<SqlVaultUtilReaderState>("SqlVaultUtilLastState/" + settingsName, () => new SqlVaultUtilReaderState());
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public async Task ReadRowsAndWriteToVaultAsync(string tableName, string rowIdColumnName, Func<Row, IEnumerable<DataVaultData>> rowToDataListFunc, int maxRowsToFetchOnce = 4096, bool shuffle = true, CancellationToken cancel = default,
        string? lastMaxRowIdName = null)
    {
        if (lastMaxRowIdName._IsEmpty()) lastMaxRowIdName = tableName;


        $"Start: tableName = {lastMaxRowIdName}, rowIdColumnName = {rowIdColumnName}, maxRowsToFetchOnce = {maxRowsToFetchOnce}"._DebugFunc();

        long totalWrittenRows = 0;


        List<DataVaultClient> clients = new List<DataVaultClient>();
        try
        {
            // DataVault クライアントの作成
            foreach (var opt in this.DataVaultClientOptions)
            {
                clients.Add(new DataVaultClient(opt));
            }

            long lastMaxRowId = 0;
            lock (this.StateDb.DataLock)
                lastMaxRowId = this.StateDb.ManagedData.TableNameAndLastRowId._GetOrNew(lastMaxRowIdName);

            $"lastMaxRowId get = {lastMaxRowId._ToString3()}   from  tableName = {tableName}"._DebugFunc();

            while (true)
            {
                long dbCurrentMaxRowId = (await Db.QueryWithValueAsync($"select max({rowIdColumnName}) from {tableName}", cancel)).Int64;

                await Db.QueryAsync($"select top {maxRowsToFetchOnce} * from {tableName} with (nolock) where {rowIdColumnName} > {lastMaxRowId} order by {rowIdColumnName} asc", cancel);

                Data data = await Db.ReadAllDataAsync(cancel);

                if (data.IsEmpty) break;

                foreach (var row in data.RowList!)
                {
                    foreach (var client in clients)
                    {
                        IEnumerable<DataVaultData>? dataList = null;

                        try
                        {
                            dataList = rowToDataListFunc(row);
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                            continue;
                        }

                        if (shuffle)
                        {
                            dataList = dataList._Shuffle();
                        }

                        await dataList._DoForEachAsync(data => client.WriteDataAsync(data, cancel));
                    }

                    totalWrittenRows++;
                }

                lastMaxRowId = data.RowList.Last().ValueList[0].Int64;

                $"{lastMaxRowIdName}: Current WrittenRows: {totalWrittenRows._ToString3()}, Last Row ID: {lastMaxRowId._ToString3()}, DB Current Max Row ID: {dbCurrentMaxRowId._ToString3()}"._DebugFunc();

                lock (this.StateDb.DataLock)
                    this.StateDb.ManagedData.TableNameAndLastRowId[lastMaxRowIdName] = lastMaxRowId;

                await this.StateDb.SyncWithStorageAsync(HiveSyncFlags.ForceUpdate | HiveSyncFlags.SaveToFile, false, cancel);
            }
        }
        catch (Exception ex)
        {
            ex._Error();
            throw;
        }
        finally
        {
            // DataVault クライアントの解放
            clients._DoForEach(x => x._DisposeSafe());

            $"End: tableName = {lastMaxRowIdName}, rowIdColumnName = {rowIdColumnName}, maxRowsToFetchOnce = {maxRowsToFetchOnce}, totalWrittenRows = {totalWrittenRows._ToString3()}"._DebugFunc();
        }
    }

    protected override void DisposeImpl(Exception? ex)
    {
        try
        {
            this.StateDb._DisposeSafe();

            this.Instance._DisposeSafe();
        }
        finally
        {
            base.DisposeImpl(ex);
        }
    }
}

[Serializable]
public class DnsHostNameScannerSettings : INormalizable
{
    public int NumThreads { get; set; } = 64;
    public int Interval { get; set; } = 100;
    public bool RandomInterval { get; set; } = true;
    public bool Shuffle { get; set; } = true;
    public bool PrintStat { get; set; } = true;
    public bool PrintOrderByFqdn { get; set; } = true;
    public int NumTry { get; set; } = 3;

    public void Normalize()
    {
        this.NumThreads = Math.Max(this.NumThreads, 1);
        if (Interval < 0) Interval = 100;
        if (NumTry <= 0) NumTry = 1;
    }
}

public class DnsHostNameScannerEntry
{
    public IPAddress Ip { get; set; } = null!;
    public List<string>? HostnameList { get; set; }
    public bool NotFound { get; set; }
}

public class DnsHostNameScanner : AsyncService
{
    readonly DnsResolver Dr;
    readonly DnsHostNameScannerSettings Settings;

    public DnsHostNameScanner(DnsHostNameScannerSettings? settings = null, DnsResolverSettings? dnsSettings = null)
    {
        if (settings == null) settings = new DnsHostNameScannerSettings();

        this.Settings = settings._CloneDeepWithNormalize();

        Dr = DnsResolver.CreateDnsResolverIfSupported(dnsSettings);
    }

    Once Started;

    Queue<DnsHostNameScannerEntry> BeforeQueue = null!;
    List<DnsHostNameScannerEntry> AfterResult = null!;

    public Task<List<DnsHostNameScannerEntry>> PerformAsync(string targetIpAddressList, CancellationToken cancel = default)
        => PerformAsync(IPUtil.GenerateIpAddressListFromIpSubnetList(targetIpAddressList).Select(x => x.ToString()), cancel);

    public async Task<List<DnsHostNameScannerEntry>> PerformAsync(IEnumerable<string> targetIpAddressList, CancellationToken cancel = default)
    {
        if (Started.IsFirstCall() == false) throw new CoresException("Already started.");

        var ipList = targetIpAddressList.Select(x => x._ToIPAddress(noExceptionAndReturnNull: true)).Where(x => x != null).Distinct().OrderBy(x => x, IpComparer.Comparer);

        if (this.Settings.Shuffle) ipList = ipList._Shuffle();

        // キューを作成する
        BeforeQueue = new Queue<DnsHostNameScannerEntry>();
        AfterResult = new List<DnsHostNameScannerEntry>();

        // キューに挿入する
        ipList._DoForEach(x => BeforeQueue.Enqueue(new DnsHostNameScannerEntry { Ip = x! }));

        int numTry = Settings.NumTry;

        for (int tryCount = 0; tryCount < numTry; tryCount++)
        {
            if (Settings.PrintStat) $"--- Starting Try #{tryCount + 1}: {BeforeQueue.Count._ToString3()} hosts ---"._Print();

            // スレッドを開始する
            List<Task> tasksList = new List<Task>();
            for (int i = 0; i < Settings.NumThreads; i++)
            {
                Task t = AsyncAwait(async () =>
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();

                        DnsHostNameScannerEntry? target;
                        lock (BeforeQueue)
                        {
                            if (BeforeQueue.TryDequeue(out target) == false)
                            {
                                return;
                            }
                        }

                        Ref<DnsAdditionalResults> additional = new Ref<DnsAdditionalResults>();

                        List<string>? ret = await Dr.GetHostNameAsync(target.Ip, additional, cancel);

                        if (ret._IsFilled())
                        {
                            target.HostnameList = ret;

                            if (Settings.PrintStat)
                            {
                                $"Try #{tryCount + 1}: {target.Ip.ToString()._AddSpacePadding(19)} {target.HostnameList._Combine(" / ")}"._Print();
                            }
                        }
                        else
                        {
                            target.NotFound = additional?.Value?.IsNotFound ?? false;
                        }

                        lock (AfterResult)
                        {
                            AfterResult.Add(target);
                        }

                        lock (BeforeQueue)
                        {
                            if (BeforeQueue.Count == 0) return;
                        }

                        int nextInterval = Settings.Interval;

                        if (Settings.RandomInterval)
                        {
                            nextInterval = Util.GenRandInterval(Settings.Interval);
                        }

                        await cancel._WaitUntilCanceledAsync(nextInterval);
                    }
                });

                tasksList.Add(t);
            }

            // スレッドが完了するまで待つ
            await Task.WhenAll(tasksList);

            if (Settings.PrintStat) $"--- Finished: Try #{tryCount + 1} ---"._Print();

            if (tryCount != (numTry - 1))
            {
                List<DnsHostNameScannerEntry> unsolvedHosts = new List<DnsHostNameScannerEntry>();

                foreach (var item in AfterResult)
                {
                    if (item.HostnameList._IsEmpty() && item.NotFound == false)
                    {
                        // 未解決ホストかつエラー発生ホストである
                        unsolvedHosts.Add(item);
                    }
                }

                if (unsolvedHosts.Any() == false)
                {
                    // 未解決ホストなし
                    break;
                }

                unsolvedHosts.ForEach(x => AfterResult.Remove(x));

                if (Settings.Shuffle)
                {
                    unsolvedHosts._Shuffle()._DoForEach(x => BeforeQueue.Enqueue(x));
                }
                else
                {
                    unsolvedHosts.ForEach(x => BeforeQueue.Enqueue(x));
                }
            }
        }

        if (Settings.PrintStat)
        {
            Con.WriteLine();
            // おもしろソート
            var printResults = AfterResult.Where(x => x.HostnameList._IsFilled());

            if (Settings.PrintOrderByFqdn)
            {
                printResults = printResults.OrderBy(x => x.HostnameList!.First(), StrComparer.FqdnReverseStrComparer).ThenBy(x => x.Ip, IpComparer.Comparer);
            }
            else
            {
                printResults = printResults.OrderBy(x => x.Ip, IpComparer.Comparer);
            }

            Con.WriteLine($"--- Results: {printResults.Count()._ToString3()} ---");

            foreach (var item in printResults)
            {
                $"{item.Ip.ToString()._AddSpacePadding(19)} {item.HostnameList!._Combine(" / ")}"._Print();
            }
        }

        return AfterResult.OrderBy(x => x.Ip, IpComparer.Comparer).ToList();
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await Dr._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

public class CSharpEasyParse
{
    public HashSet<string> UsingList = new HashSet<string>();
    public Dictionary<string, StringWriter> CodeList = new Dictionary<string, StringWriter>();

    public static CSharpEasyParse ParseFile(FilePath path)
    {
        var ret = ParseCode(path.ReadStringFromFile());

        return ret;
    }

    public static CSharpEasyParse ParseCode(string code)
    {
        CSharpEasyParse ret = new CSharpEasyParse();

        var lines = code._GetLines();

        int mode = 0;

        StringWriter? currentWriter = null;

        foreach (var line in lines)
        {
            if (mode == 0)
            {
                if (line.StartsWith("using", StringComparison.Ordinal))
                {
                    mode = 1;
                }
            }

            if (line.StartsWith("namespace", StringComparison.Ordinal))
            {
                var tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');
                if (tokens.Length >= 2)
                {
                    string ns = tokens[1];

                    currentWriter = ret.CodeList._GetOrNew(ns, () => new StringWriter());

                    mode = 2;
                }
            }

            if (mode == 2)
            {
                if (line.StartsWith("{"))
                {
                    mode = 3;
                }
            }

            if (mode == 3)
            {
                if (line.StartsWith("}"))
                {
                    currentWriter?.WriteLine();
                    mode = 0;
                    currentWriter = null;
                }
            }

            switch (mode)
            {
                case 1:
                    if (line.StartsWith("using", StringComparison.Ordinal))
                    {
                        ret.UsingList.Add(line._NormalizeSoftEther(true));
                    }
                    break;

                case 3:
                    if (line.StartsWith("{") == false)
                    {
                        currentWriter!.WriteLine(line);
                    }
                    break;
            }
        }

        return ret;
    }
}

public static class CSharpConcatUtil
{
    public static void DoConcat(string srcRootDir, string destRootDir)
    {
        var dirs = Lfs.EnumDirectory(srcRootDir, true).Where(x => x.IsDirectory).OrderBy(x => x.FullPath, StrComparer.IgnoreCaseComparer);

        List<Tuple<string, CSharpEasyParse>> data = new List<Tuple<string, CSharpEasyParse>>();

        foreach (var dir in dirs)
        {
            var files = Lfs.EnumDirectory(dir.FullPath, false).Where(x => Lfs.PathParser.GetExtension(x.Name)._IsSamei(".cs")).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer);

            foreach (var file in files)
            {
                Con.WriteLine($"Parsing '{file.FullPath}' ...");
                CSharpEasyParse parsed = CSharpEasyParse.ParseFile(file.FullPath);

                data.Add(new Tuple<string, CSharpEasyParse>(PP.GetRelativeDirectoryName(PP.GetDirectoryName(file.FullPath), srcRootDir), parsed));
            }
        }

        foreach (var dir in data.Select(x => x.Item1).Distinct(StrComparer.IgnoreCaseComparer).OrderBy(x => x, StrComparer.IgnoreCaseComparer))
        {
            HashSet<string> usingList = new HashSet<string>();
            Dictionary<string, StringWriter> codeList = new Dictionary<string, StringWriter>();

            foreach (var file in data.Where(x => x.Item1._IsSamei(dir)).Select(x => x.Item2))
            {
                file.UsingList._DoForEach(x => usingList.Add(x));

                foreach (var code in file.CodeList)
                {
                    var tmp = codeList._GetOrNew(code.Key, () => new StringWriter());
                    tmp.Write(code.Value.ToString());
                }
            }

            StringWriter w = new StringWriter();

            int mode = 0;

            usingList.OrderBy(x => x, StrComparer.DevToolsCsUsingComparer)._DoForEach(x =>
            {
                if (x.StartsWith("using"))
                {
                    Str.GetKeyAndValue(x, out _, out x);
                }

                if (x.StartsWith("System"))
                {
                    mode = 1;
                }
                else
                {
                    if (mode == 1)
                    {
                        w.WriteLine();
                    }
                    mode = 2;
                }

                w.WriteLine("using " + x);
            });

            codeList.OrderBy(x => x.Key, StrComparer.IgnoreCaseComparer)._DoForEach(x =>
            {
                w.WriteLine();
                w.WriteLine($"namespace {x.Key}");
                w.WriteLine("{");
                w.Write(x.Value.ToString());
                w.WriteLine("}");
            });

            w.WriteLine();

            string dstPath = PP.Combine(destRootDir, dir) + ".cs";

            Lfs.WriteStringToFile(dstPath, w.ToString(), FileFlags.AutoCreateDirectory, writeBom: true);
        }
    }
}

#endif

