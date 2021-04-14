﻿
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Reference from: https://gist.github.com/ayende/c2bb440bb448dc290132956c6a9fff3b

using IPA.Cores.Helper.Basic;
using IPA.Cores.Basic;


using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Diagnostics;
using System.Reflection;

using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;
using IPA.Cores.ClientApi.Acme;
using Newtonsoft.Json.Converters;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.FileProviders;
using System.Web;
using IPA.Cores.Basic.App.DaemonCenterLib;
using IPA.Cores.ClientApi.GoogleApi;


namespace IPA.TestDev
{
    public class GmapPhoto
    {
        public DateTimeOffset TimeStamp { get; private set; }
        public int Zoom { get; }
        public int X { get; }
        public int Y { get; }
        public string AccessUrl { get; }
        public byte[] PhotoData { get; private set; } = new byte[0];
        public SimpleHttpDownloaderResult? HttpAccessLog { get; private set; } = null;

        [JsonIgnore]
        public GmapPhotoKey PhotoKey { get; }

        public GmapPhoto(GmapPhotoKey key, int x, int y, int zoom)
        {
            this.PhotoKey = key;
            this.X = x;
            this.Y = y;
            this.Zoom = zoom;
            this.AccessUrl = $"https://streetviewpixels-pa.googleapis.com/v1/tile?cb_client=maps_sv.tactile&panoid={key}&x={x}&y={y}&zoom={zoom}";
        }

        public async Task<bool> GmapDownloadStreetViewPhotoCoreAsync(CancellationToken cancel = default)
        {
            this.PhotoData = new byte[0];
            this.TimeStamp = DtOffsetNow;

            try
            {
                return await RetryHelper.RunAsync(async () =>
                {
                    using var http = new WebApi();

                    string url = $"https://streetviewpixels-pa.googleapis.com/v1/tile?cb_client=maps_sv.tactile&panoid={this.PhotoKey.Key}&x={this.X}&y={this.Y}&zoom={this.Zoom}";

                    this.PhotoKey.Point.WriteLog($"StreetView 写真キー '{this.PhotoKey.Key}' を、パラメータ zoom = {this.Zoom}, x = {this.X}, y = {this.Y} で取得するための URL を組み立てた。URL は、'{url}' である。この URL からの取得を開始する。");

                    var downloadResult = await SimpleHttpDownloader.DownloadAsync(url, printStatus: true, cancel: cancel);

                    if (downloadResult.Data.Length <= 2000)
                    {
                        this.PhotoKey.Point.WriteLog($"上記 URL からのデータは受信できたが、サイズは 2,000 バイト以下であった。おそらく、このパラメータ zoom = {this.Zoom}, x = {this.X}, y = {this.Y} の画像は、Google 社のサーバーには、存在しないのであろう。");
                        return false;
                    }

                    this.PhotoData = downloadResult.Data.ToArray();
                    this.HttpAccessLog = downloadResult;

                    this.PhotoKey.Point.WriteLog($"上記 URL からの画像データの受信に成功した。サイズ: {this.PhotoData.Length._ToString3()} bytes");

                    return true;
                },
                retryInterval: 300,
                tryCount: 3,
                cancel);
            }
            catch (Exception ex)
            {
                ex._Debug();

                this.PhotoKey.Point.WriteLog($"予期しない HTTP エラーが発生した。詳細: {ex.ToString()}");

                return false;
            }
        }
    }

    public class GmapPhotoKey
    {
        public string Key { get; }
        public List<GmapPhoto> ObtainedPhotoList { get; private set; } = new List<GmapPhoto>();

        [JsonIgnore]
        public GmapPoint Point { get; }

        public GmapPhotoKey(GmapPoint point, string key)
        {
            this.Key = key;
            this.Point = point;
        }

        public async Task GmapDownloadStreetViewAllPhotosAsync(CancellationToken cancel = default)
        {
            using var http = new WebApi();

            List<GmapPhoto> obtainedPhotoList = new List<GmapPhoto>();

            for (int zoom = 0; zoom <= 5; zoom++)
            {
                int numOkInThisZoom = 0;

                for (int x = 0; x < 32; x++)
                {
                    int numOkInThisX = 0;

                    for (int y = 0; y < 32; y++)
                    {
                        var photo = new GmapPhoto(this, x, y, zoom);

                        if (await photo.GmapDownloadStreetViewPhotoCoreAsync(cancel) == false)
                        {
                            break;
                        }

                        obtainedPhotoList.Add(photo);

                        numOkInThisX++;
                        numOkInThisZoom++;

                        await Task.Delay(Util.GenRandInterval(200));
                    }

                    if (numOkInThisX == 0)
                    {
                        break;
                    }
                }

                if (numOkInThisZoom == 0)
                {
                    break;
                }
            }

            this.ObtainedPhotoList = obtainedPhotoList;
        }
    }

    public class GmapPoint
    {
        public DateTimeOffset TimeStamp { get; private set; }
        public string Name { get; }
        public string RootDir { get; }
        public string AccessUrl { get; }
        public SimpleHttpDownloaderResult? HttpAccessLog { get; private set; } = null;
        public List<GmapPhotoKey> PhotoKeyList { get; private set; } = new List<GmapPhotoKey>();

        [JsonIgnore]
        public StringWriter Log { get; } = new StringWriter();

        public GmapPoint(string name, string accessUrl, string rootDir)
        {
            this.Name = name;
            this.AccessUrl = accessUrl;
            this.RootDir = rootDir;
            this.Log.NewLine = Str.CrLf_Str;
        }

        public void WriteLog(string str)
        {
            StringWriter tmp = new StringWriter();
            tmp.WriteLine($"■ {DtOffsetNow._ToDtStr(true)}");
            tmp.WriteLine(str._NormalizeCrlf(CrlfStyle.CrLf).Trim());
            tmp.WriteLine();

            string str2 = tmp.ToString()._NormalizeCrlf(CrlfStyle.CrLf);

            this.Log.WriteLine(str2);

            Console.Write(str2);
        }

        public async Task GmapDownloadPointDataAsync(CancellationToken cancel = default)
        {
            this.TimeStamp = DtOffsetNow;

            this.WriteLog($"GmapDownloadPointDataAsync() 関数の処理を開始。\nGoogle StreetView のアクセス先 URL: {this.AccessUrl}");

            List<string> ret = new List<string>();

            using var http = new WebApi();

            this.WriteLog($"URL '{this.AccessUrl}' にアクセス中...");

            this.HttpAccessLog = await SimpleHttpDownloader.DownloadAsync(this.AccessUrl, printStatus: true, cancel: cancel);

            this.WriteLog($"URL '{this.AccessUrl}' から応答があった。応答コード: {this.HttpAccessLog.StatusCode}, データサイズ: {this.HttpAccessLog.DataSize._ToString3()} bytes");

            string body = this.HttpAccessLog.Data._GetString_UTF8();

            this.WriteLog($"URL '{this.AccessUrl}' の取得結果は、以下のとおり。\n" +
                "---------- ここから ----------\n" +
                body + "\n" +
                "---------- ここまで ----------");

            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == '\"')
                {
                    if (body.ElementAtOrDefault(i + 23) == '\"')
                    {
                        string keyword = body.Substring(i + 1, 22);

                        if (keyword.Any(c => c == '\"' || c == '\'') == false && keyword.All(c => c <= 127))
                        {
                            ret.Add(keyword);
                        }
                    }
                }
            }

            ret = ret.Distinct().OrderBy(x=>x).ToList();

            if (ret.Any() == false)
            {
                this.WriteLog($"URL '{this.AccessUrl}' の結果 (上記) には、Google StreetView の写真キーであると思われるキー文字列は 1 件も存在しなかった。URL に誤りがある可能性がある。十分確認して、再実行すること。");
            }
            else
            {
                StringWriter tmp = new StringWriter();
                for (int i = 0; i < ret.Count; i++)
                {
                    tmp.WriteLine($"{i + 1} 件目のキーは、'{ret[i]}' であった。");
                }

                this.WriteLog($"上記の取得結果のうち、Google StreetView の写真キーであると思われるキー文字列は合計 {ret.Count} 件あった。\n" +
                    tmp.ToString());

                this.WriteLog($"そこで、今から、これらのキーに 1 つずつアクセスを試みて、Google StreetView の画像をダウンロードするのである。");

                this.PhotoKeyList = new List<GmapPhotoKey>();

                for (int i = 0; i < ret.Count; i++)
                {
                    var key = ret[i];

                    this.WriteLog($"{i + 1} 件目 (合計 {ret.Count} 件中) の Google StreetView の写真キー '{key}' への取得処理を開始する。");

                    GmapPhotoKey photoKey = new GmapPhotoKey(this, key);

                    await photoKey.GmapDownloadStreetViewAllPhotosAsync(cancel);

                    if (photoKey.ObtainedPhotoList.Any())
                    {
                        this.WriteLog($"{i + 1} 件目 (合計 {ret.Count} 件中) の Google StreetView の写真キー '{key}' への取得処理は完了した。このキーでは、合計 {photoKey.ObtainedPhotoList.Count} 枚の写真が取得できたのである。");

                        this.PhotoKeyList.Add(photoKey);

                        // 写真の保存
                        foreach (var photo in photoKey.ObtainedPhotoList)
                        {
                            string photoPath = Lfs.PP.Combine(this.RootDir, $"{i:D4}_{key}", $"zoom_level_{photo.Zoom}", $"{i:D4}_{key}__zoom_level_{photo.Zoom}__x_{photo.X}__y_{photo.Y}.jpg");

                            photo.PhotoData._Save(photoPath, FileFlags.AutoCreateDirectory, cancel: cancel);

                            this.WriteLog($"{i + 1} 件目 (合計 {ret.Count} 件中) の Google StreetView の写真キー '{key}' のズームレベル = {photo.Zoom}, x = {photo.X}, y = {photo.Y} の写真データを、ファイル '{photoPath}' として保存した。ファイルサイズは、{photo.PhotoData.Length._ToString3()} bytes である。");
                        }
                    }
                    else
                    {
                        this.WriteLog($"{i + 1} 件目 (合計 {ret.Count} 件中) の Google StreetView の写真キー '{key}' への取得処理は完了した。このキーでは、写真は 1 枚も取得することができなかった。");
                    }
                }

                this.WriteLog($"{ret.Count} 件のキーすべてに対するアクセス試行が完了した。URL '{this.AccessUrl}' に関する処理は、これですべて終了した。");
            }

            string logPath = Lfs.PP.Combine(this.RootDir, "アクセスログ.txt");
            Lfs.WriteStringToFile(logPath, this.Log.ToString()._NormalizeCrlf(CrlfStyle.CrLf, true), FileFlags.AutoCreateDirectory, writeBom: true, cancel: cancel);

            string recordLogPath = Lfs.PP.Combine(this.RootDir, "すべての通信ログ.json");
            Lfs.WriteJsonToFile(recordLogPath, this, FileFlags.AutoCreateDirectory);
        }
    }

    partial class TestDevCommands
    {
        public static async Task GmapStreetViewPhotoUrlAnalysisAsync(string photoUrl, string destDir, string name, CancellationToken cancel = default)
        {
            string rootDir = Lfs.PP.Combine(destDir, $"{Str.DateTimeToStrShortWithMilliSecs(DateTime.Now)}_{name}");

            Lfs.CreateDirectory(rootDir);

            GmapPoint p = new GmapPoint(name, photoUrl, rootDir);

            await p.GmapDownloadPointDataAsync(cancel);
        }

        public static void ConvertCErrorsToCsErrors(string dir, string outputFileName)
        {
            List<Pair2<int, string>> list = new List<Pair2<int, string>>();

            Lfs.DirectoryWalker.WalkDirectory(dir, (info, entries, cancel) =>
            {
                var headerFiles = entries.Where(x => x.IsFile && x.Name._WildcardMatch("*.h", true)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer);

                headerFiles._DoForEach(x =>
                {
                    string body = Lfs.ReadStringFromFile(x.FullPath);
                    int count = 0;
                    foreach (string line in body._GetLines())
                    {
                        if (line._GetKeyAndValue(out string code, out string comment, "/"))
                        {
                            code = code.Trim();
                            comment = comment.Trim();

                            string[] tokens = code._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');

                            if (tokens.Length == 3 && tokens[0]._IsSamei("#define") && tokens[1].StartsWith("ERR_") && tokens[2]._IsNumber())
                            {
                                int num = tokens[2]._ToInt();

                                string result = $"{tokens[1]} = {num}, // {comment}";

                                list.Add(new Pair2<int, string>(num, result));
                                count++;
                            }
                        }
                    }
                    if (count >= 1)
                    {
                        x.FullPath._Print();
                    }
                });

                return true;
            },
            exceptionHandler: (info, ex, c) =>
            {
                ex._Print();
                return true;
            });

            StringWriter w = new StringWriter();

            foreach (string b in list.OrderBy(x => x.A).Select(x => x.B))
            {
                w.WriteLine(b);
            }

            Lfs.WriteStringToFile(outputFileName, w.ToString(), flags: FileFlags.AutoCreateDirectory);
        }

        [ConsoleCommand(
        "バイナリファイル内のデータを置換",
        "ReplaceBinary [srcFileName] [/DST:destFileName] [/REPLACE:replaceTextFileName] [/FILL:fillByte=10]",
        "バイナリファイル内のデータを置換します。",
        "[srcFileName]:元ファイル名を指定します。",
        "DST:保存先ファイル名を指定します。指定しない場合、元ファイルが上書きされます。",
        "REPLACE:置換定義ファイルを指定します。テキストファイルで、奇数行に置換元、偶数行に置換先のバイナリ文字列を記載します。0x で始まる行は 16 進数とみなされます。",
        "FILL:置換先のデータの長さが短い場合に埋めるバイト文字を 16 進数で指定います。省略すると UNIX 改行文字で埋められます。"
        )]
        static int ReplaceBinary(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[srcFileName]", ConsoleService.Prompt, "元ファイル名: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("DST"),
                new ConsoleParam("REPLACE", ConsoleService.Prompt, "換定義ファイル: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("FILL"),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string srcFileName = vl.DefaultParam.StrValue;
            string dstFileName = vl["DST"].StrValue;
            if (dstFileName._IsEmpty()) dstFileName = srcFileName;

            string replaceFileName = vl["REPLACE"].StrValue;

            byte fillByte = (byte)(vl["FILL"].StrValue._FilledOrDefault("10")._ToInt());

            Async(async () =>
            {
                KeyValueList<string, string> list = new KeyValueList<string, string>();

                string body = await Lfs.ReadStringFromFileAsync(replaceFileName);

                string[] lines = body._GetLines();

                for (int i = 0; i < lines.Length; i += 2)
                {
                    string oldstr = lines[i];
                    string newstr = lines[i + 1];

                    list.Add(oldstr, newstr);
                }

                var ret = await MiscUtil.ReplaceBinaryFileAsync(srcFileName, dstFileName, list, FileFlags.AutoCreateDirectory, fillByte);

                ret._PrintAsJson();
            });

            return 0;
        }


        [ConsoleCommand(
        "テキスト原稿を HTML 化",
        "GenkoToHtml [src] [/DEST:dest]",
        "テキスト原稿を HTML 化"
        )]
        static int GenkoToHtml(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[src]", ConsoleService.Prompt, "元ファイル: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("DEST", ConsoleService.Prompt, "出力先ファイル: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string dest = vl["DEST"].StrValue;

            MiscUtil.GenkoToHtml(vl.DefaultParam.StrValue, dest);

            return 0;
        }

        [ConsoleCommand(
        "ファイル内の文字を置換",
        "ReplaceString [dirName] [/PATTERN:pattern] [/OLDSTRING:oldstring] [/NEWSTRING:newstring] [/CASESENSITIVE:yes|no]",
        "指定されたディレクトリ内のパターンに一致するファイルの文字コードを変更します。",
        "[dirName]:ディレクトリ名を指定します。",
        "PATTERN:ファイル名のパターンを指定します。たとえば、'*.txt' などと指定します。'*.txt,*.c,*.h' など複数指定も可能です。",
        "OLDSTRING:古い文字列を指定します。",
        "NEWSTRING:新しい文字列を指定します。",
        "CASESENSITIVE:yes の場合は大文字・小文字を区別します。"
        )]
        static int ReplaceString(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[dirName]", ConsoleService.Prompt, "ディレクトリ名: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("PATTERN", ConsoleService.Prompt, "ファイル名のパターン: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("OLDSTRING", ConsoleService.Prompt, "古い文字列: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("NEWSTRING", ConsoleService.Prompt, "新しい文字列: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("CASESENSITIVE", null, null, null, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            MiscUtil.ReplaceStringOfFiles(vl.DefaultParam.StrValue, vl["PATTERN"].StrValue, vl["OLDSTRING"].StrValue, vl["NEWSTRING"].StrValue, vl["CASESENSITIVE"].BoolValue);

            return 0;
        }


        [ConsoleCommand(
            "ファイルの文字コードを変換",
            "ChangeEncoding [dirName] [/PATTERN:pattern] [/ENCODING:encoding] [/BOM:yes|no]",
            "指定されたディレクトリ内のパターンに一致するファイルの文字コードを変更します。",
            "[dirName]:ディレクトリ名を指定します。",
            "PATTERN:ファイル名のパターンを指定します。たとえば、'*.txt' などと指定します。'*.txt,*.c,*.h' など複数指定も可能です。",
            "ENCODING:保存先ファイルの文字コードを指定します。",
            "BOM:yes を指定した場合、Unicode 関係のフォーマットの場合は BOM を付加します。"
            )]
        static int ChangeEncoding(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[dirName]", ConsoleService.Prompt, "ディレクトリ名: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("PATTERN", ConsoleService.Prompt, "ファイル名のパターン: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("ENCODING", ConsoleService.Prompt, "文字コード名: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("BOM", null, null, null, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            MiscUtil.ChangeEncodingOfFiles(vl.DefaultParam.StrValue, vl["PATTERN"].StrValue, vl["BOM"].BoolValue, vl["ENCODING"].StrValue);

            return 0;
        }


        [ConsoleCommand(
            "改行コードを CRLF に統一",
            "NormalizeCrLf [dirName] [/PATTERN:pattern]",
            "指定されたディレクトリ内のパターンに一致するファイルの改行コードを変更します。",
            "[dirName]:ディレクトリ名を指定します。",
            "PATTERN:ファイル名のパターンを指定します。たとえば、'*.txt' などと指定します。'*.txt,*.c,*.h' など複数指定も可能です。"
            )]
        static int NormalizeCrLf(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[dirName]", ConsoleService.Prompt, "ディレクトリ名: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("PATTERN", ConsoleService.Prompt, "ファイル名のパターン: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            MiscUtil.NormalizeCrLfOfFiles(vl.DefaultParam.StrValue, vl["PATTERN"].StrValue);

            return 0;
        }


        [ConsoleCommand(
            "指定されたディレクトリ内の最新のいくつかのサブディレクトリのみコピー (同期) し、他は削除する",
            "SyncLatestFewDirs [srcdir] [/destdir:DESTDIR] [/num:HowManyDirs=1]",
            "指定されたディレクトリ内の最新のいくつかのサブディレクトリのみコピー (同期) し、他は削除する")]
        static int SyncLatestFewDirs(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[srcdir]", ConsoleService.Prompt, "Src Directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("destdir", ConsoleService.Prompt, "Dest Directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("num", ConsoleService.Prompt, "How Many Dirs: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string srcDir = vl.DefaultParam.StrValue;
            string dstDir = vl["destdir"].StrValue;
            int num = vl["num"].IntValue;

            // ソースディレクトリのサブディレクトリを列挙いたします
            Async(async () =>
            {
                await FileUtil.SyncLatestFewDirsAsync(srcDir, dstDir, num);
            });

            return 0;
        }

        [ConsoleCommand(
            "クラッシュさん",
            "Crash",
            "クラッシュさん")]
        static int Crash(ConsoleService c, string cmdName, string str)
        {
            unsafe
            {
                byte[] tmp = new byte[4096];

                var fs = File.Create("/tmp/test.txt");

                IntPtr fd = fs.SafeFileHandle.DangerousGetHandle();

                $"fd = {fd.ToInt64()}"._Print();

                fixed (byte* tmpptr = tmp)
                {
                    long ptr = (long)tmpptr;
                    while (true)
                    {
                        ptr++;
                        byte* p = (byte*)ptr;

                        int r = UnixApi.Write(fd, p, 1);

                        $"{ptr} : {r}"._Print();

                        if (r == -1) break;
                    }
                }
            }

            return 0;
        }

        [ConsoleCommand(
            "Git 並列アップデータ",
            "GitParallelUpdate [dir] [/concurrent:NUM] [/setting:TXTFILENAME]",
            "Git 並列アップデータ")]
        static int GitParallelUpdate(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[rootDir]", ConsoleService.Prompt, "Directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("concurrent"),
                new ConsoleParam("setting"),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string dir = vl.DefaultParam.StrValue;

            int numConcurrentTasks = vl["num"].IntValue;

            if (numConcurrentTasks <= 0) numConcurrentTasks = 16;

            string setting = vl["setting"].StrValue;

            GitParallelUpdater.ExecGitParallelUpdaterAsync(dir, numConcurrentTasks, setting)._GetResult();

            return 0;
        }

        [ConsoleCommand(
            "ファイルシステム ストレステスト",
            "FileSystemStressTest [dir] [/num:NUM]",
            "ファイルシステム ストレステスト")]
        static int FileSystemStressTest(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[dir]", ConsoleService.Prompt, "Directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("num"),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string dir = vl.DefaultParam.StrValue;

            Lfs.CreateDirectory(dir);

            int numThreads = vl["num"].IntValue;

            if (numThreads <= 0) numThreads = 1;

            RefBool stopFlag = new RefBool();

            RefInt numWrittenFiles = new RefInt();

            Event startEvent = new Event(true);

            ThreadObj[] threadList = ThreadObj.StartMany(numThreads, (param) =>
            {
                int index = ThreadObj.Current.Index;
                startEvent.Wait();

                try
                {
                    string subdir = Lfs.PathParser.Combine(dir, index.ToString("D4"));

                    try
                    {
                        Lfs.CreateDirectory(subdir);

                        while (stopFlag.Value == false)
                        {
                            string filename = Str.GenRandStr() + ".test";
                            string fileFillPath = Lfs.PathParser.Combine(subdir, filename);
                            int size = Util.RandSInt31() % 1_000_000 + 128;
                            int numCount = Util.RandSInt31() % 64;

                            try
                            {
                                numWrittenFiles.Increment();

                                Con.WriteLine($"File #{numWrittenFiles}");

                                using (var file = Lfs.Create(fileFillPath, flags: FileFlags.SparseFile))
                                {
                                    for (int i = 0; i < numCount; i++)
                                    {
                                        file.WriteRandom(Util.RandSInt31() % size, Util.Rand(64));
                                    }
                                }
                            }
                            finally
                            {
                                try
                                {
                                    Lfs.DeleteFile(fileFillPath);
                                }
                                catch { }
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (Lfs.EnumDirectory(subdir).Where(x => x.IsCurrentOrParentDirectory == false).All(x => x.IsFile && x.Name.EndsWith(".test", StringComparison.OrdinalIgnoreCase)))
                            {
                                Lfs.DeleteDirectory(subdir, true);
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Con.WriteLine("*** ERROR !!! ***");

                    ex._Print();

                    stopFlag.Set(true);
                }
            });

            startEvent.Set();

            Con.ReadLine();

            stopFlag.Set(true);

            threadList._DoForEach(x => x.WaitForEnd());

            return 0;
        }

        [ConsoleCommand(
        "ログ stat データからメモリリーク分析",
        "AnalyzeLogStatMemoryLeak [srcDir] [/dest:csvfilename]",
        "ログ stat データからメモリリーク分析")]
        static int AnalyzeLogStatMemoryLeak(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[srcDir]", ConsoleService.Prompt, "Input source log directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dest", ConsoleService.Prompt, "Input dest CSV file name: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            var csvData = LogStatMemoryLeakAnalyzer.AnalyzeLogFiles(vl.DefaultParam.StrValue);

            csvData._ObjectArrayToCsv(true)._WriteTextFile(vl["dest"].StrValue);

            return 0;
        }

        [ConsoleCommand(
        "Cacti ホスト登録",
        "CactiRegisterHosts [taskFileName]",
        "Cacti ホスト登録")]
        static int CactiRegisterHosts(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[taskFileName]", ConsoleService.Prompt, "Input task file name: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            CactiClientApp.ExecuteRegisterTasksAsync(vl.DefaultParam.StrValue)._GetResult();

            return 0;
        }

        [ConsoleCommand(
        "Cacti グラフダウンロード",
        "CactiDownloadGraphs [destdir] [/cacti:baseUrl] [/username:username] [/password:password] [/graphs:id1,id2,id3,...]",
        "Cacti グラフダウンロード")]
        static int CactiDownloadGraphs(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[destdir]", ConsoleService.Prompt, "Input destination directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("cacti", ConsoleService.Prompt, "Input Cacti Base Dir: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("username", ConsoleService.Prompt, "Input Cacti Username: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("password", ConsoleService.Prompt, "Input Cacti Password: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("graphs", ConsoleService.Prompt, "Input Graph ID List (id1,id2,id3,...): ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string[] ids = vl["graphs"].StrValue._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t', ',', '/', ';');

            List<int> idList = new List<int>();
            ids._DoForEach(s => idList.Add(s._ToInt()));
            idList.Sort();

            CactiClientApp.DownloadGraphsAsync(vl.DefaultParam.StrValue, vl["cacti"].StrValue, vl["username"].StrValue, vl["password"].StrValue, idList)._GetResult();

            return 0;
        }

        [ConsoleCommand]
        static int MergeResourceHeader(ConsoleService c, string cmdName, string str)
        {
            string baseFile = @"C:\git\IPA-DNP-DeskVPN\src\PenCore\resource.h";
            string targetFile = @"C:\sec\Desk\current\Desk\DeskVPN\PenCore\resource.h";
            string destFile = @"c:\tmp\200404\resource.h";
            int minId = 2500;

            var baseDict = DevTools.ParseHeaderConstants(Lfs.ReadStringFromFile(baseFile));
            var targetDict = DevTools.ParseHeaderConstants(Lfs.ReadStringFromFile(targetFile));

            KeyValueList<string, int> adding = new KeyValueList<string, int>();

            // 利用可能な ID の最小値
            int newId = Math.Max(baseDict.Values.Where(x => x < 40000).Max(), minId);

            foreach (var kv in targetDict.OrderBy(x => x.Value))
            {
                if (baseDict.ContainsKey(kv.Key) == false)
                {
                    adding.Add(kv.Key, ++newId);
                }
            }

            // 結果を出力
            StringWriter w = new StringWriter();
            foreach (var kv in adding)
            {
                int paddingCount = Math.Max(31 - kv.Key.Length, 0);

                w.WriteLine($"#define {kv.Key}{Str.MakeCharArray(' ', paddingCount)} {kv.Value}");
            }

            Lfs.WriteStringToFile(destFile, w.ToString(), FileFlags.AutoCreateDirectory);

            return 0;
        }

        [ConsoleCommand(
        "テキストファイルの変換",
        "ConvertTextFiles [srcdir] [/dst:destdir] [/encode:sjis|euc|utf8] [/bom:yes|no] [/newline:crlf|lf|platform]",
        "テキストファイルの変換")]
        static int ConvertTextFiles(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[srcdir]", ConsoleService.Prompt, "Input source directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dst", ConsoleService.Prompt, "Input destination directory: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("encode"),
                new ConsoleParam("bom"),
                new ConsoleParam("newline"),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string srcdir = vl.DefaultParam.StrValue;

            string dstdir = vl["dst"].StrValue;

            string encode = vl["encode"].StrValue._FilledOrDefault("utf8");

            bool bom = vl["bom"].BoolValue;

            string newline = vl["newline"].StrValue._FilledOrDefault("crlf");

            Encoding? encoding = null;

            switch (encode.ToLower())
            {
                case "sjis":
                    encoding = Str.ShiftJisEncoding;
                    break;

                case "euc":
                    encoding = Str.EucJpEncoding;
                    break;

                case "utf8":
                    encoding = Str.Utf8Encoding;
                    break;

                default:
                    throw new CoresException("encode param is invalid.");
            }

            CrlfStyle crlfStyle = CrlfStyle.CrLf;

            switch (newline.ToLower())
            {
                case "crlf":
                    crlfStyle = CrlfStyle.CrLf;
                    break;

                case "lf":
                    crlfStyle = CrlfStyle.Lf;
                    break;

                case "platform":
                    crlfStyle = CrlfStyle.LocalPlatform;
                    break;

                default:
                    throw new CoresException("newline param is invalid.");
            }

            var srcFileList = Lfs.EnumDirectory(srcdir, true);

            foreach (var srcFile in srcFileList)
            {
                if (srcFile.IsFile)
                {
                    string relativeFileName = Lfs.PathParser.GetRelativeFileName(srcFile.FullPath, srcdir);

                    string destFileName = Lfs.PathParser.Combine(dstdir, relativeFileName);

                    string body = Lfs.ReadStringFromFile(srcFile.FullPath);

                    body = Str.NormalizeCrlf(body, crlfStyle);

                    Con.WriteLine(relativeFileName);

                    Lfs.WriteStringToFile(destFileName, body, FileFlags.AutoCreateDirectory, encoding: encoding, writeBom: bom);
                }
            }

            return 0;
        }

        [ConsoleCommand(
            "Authenticode 署名の実施 (内部用)",
            "SignAuthenticodeInternal [filename] [/out:output] [/comment:string] [/driver:yes] [/cert:type]",
            "Authenticode 署名の実施 (内部用)")]
        static int SignAuthenticodeInternal(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "Input Filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("out"),
                new ConsoleParam("comment"),
                new ConsoleParam("driver"),
                new ConsoleParam("cert"),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string srcPath = vl.DefaultParam.StrValue;

            string dstPath = vl["out"].StrValue;
            if (dstPath._IsEmpty()) dstPath = srcPath;

            string comment = vl["comment"].StrValue;
            bool driver = vl["driver"].BoolValue;
            string cert = vl["cert"].StrValue;

            using (AuthenticodeSignClient ac = new AuthenticodeSignClient("https://codesignserver:7006/sign", "7BDBCA40E9C4CE374C7889CD3A26EE8D485B94153C2943C09765EEA309FCA13D"))
            {
                var srcData = Load(srcPath);

                var dstData = ac.SignSeInternalAsync(srcData, cert, driver ? "Driver" : "", comment._FilledOrDefault("Authenticode"))._GetResult();

                dstData._Save(dstPath, flags: FileFlags.AutoCreateDirectory);

                Con.WriteInfo();
                Con.WriteInfo($"Code sign OK. Written to: '{dstPath}'");
            }

            return 0;
        }

        [ConsoleCommand(
            "Authenticode 署名の実施 (内部用)",
            "SignAuthenticodeLabInternal [filename] [/out:output] [/comment:string] [/driver:yes] [/cert:type]",
            "Authenticode 署名の実施 (内部用)")]
        static int SignAuthenticodeLabInternal(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "Input Filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("out"),
                new ConsoleParam("comment"),
                new ConsoleParam("driver"),
                new ConsoleParam("cert"),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string srcPath = vl.DefaultParam.StrValue;

            string dstPath = vl["out"].StrValue;
            if (dstPath._IsEmpty()) dstPath = srcPath;

            string comment = vl["comment"].StrValue;
            bool driver = vl["driver"].BoolValue;
            string cert = vl["cert"].StrValue;

            using (AuthenticodeSignClient ac = new AuthenticodeSignClient("https://10.40.0.243:7006/sign", "3CCE0F1B9F61AE5114E77C3A306DCBF7A96D22A22BBFC761FB762F2C295FAA5B"))
            {
                var srcData = Load(srcPath);

                var dstData = ac.SignSeInternalAsync(srcData, cert, driver ? "Driver" : "", comment._FilledOrDefault("Authenticode"), passwordFilePath: @"\\10.40.0.13\share\TMP\signserver\password.txt")._GetResult();

                dstData._Save(dstPath, flags: FileFlags.AutoCreateDirectory);

                Con.WriteInfo();
                Con.WriteInfo($"Code sign OK. Written to: '{dstPath}'");
            }

            return 0;
        }
        [ConsoleCommand(
            "自己署名証明書の作成",
            "CertSelfSignedGenerate [filename] /cn:hostName",
            "自己署名証明書の作成")]
        static int CertSelfSignedGenerate(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "Output filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("cn", ConsoleService.Prompt, "Common name: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string path = vl.DefaultParam.StrValue;
            string cn = vl["cn"].StrValue;

            PkiUtil.GenerateRsaKeyPair(2048, out PrivKey newKey, out _);

            Certificate newCert = new Certificate(newKey, new CertificateOptions(PkiAlgorithm.RSA, cn: cn.Trim(), c: "JP"));
            CertificateStore newCertStore = new CertificateStore(newCert, newKey);

            newCertStore.ExportPkcs12()._Save(path, FileFlags.AutoCreateDirectory);

            return 0;
        }

        [ConsoleCommand(
            "開発用証明書の作成",
            "CertDevSignedGenerate [filename] /cn:hostName",
            "開発用証明書の作成")]
        static int CertDevSignedGenerate(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "Output filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("cn", ConsoleService.Prompt, "Common name: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string path = vl.DefaultParam.StrValue;
            string cn = vl["cn"].StrValue;

            PkiUtil.GenerateRsaKeyPair(2048, out PrivKey newKey, out _);

            Certificate newCert = new Certificate(newKey, DevTools.CoresDebugCACert.PkiCertificateStore, new CertificateOptions(PkiAlgorithm.RSA, cn: cn.Trim(), c: "JP"));
            CertificateStore newCertStore = new CertificateStore(newCert, newKey);

            newCertStore.ExportPkcs12()._Save(path, FileFlags.AutoCreateDirectory);

            return 0;
        }

        public class DirectionCrossResults
        {
            public string? Start;
            public string? End;
            public string? Error;
            public string? StartAddress;
            public string? EndAddress;

            public TimeSpan Duration;
            public double DistanceKm;
            public string? RouteSummary;
        }

        [ConsoleCommand(
            "Google Maps 所要時間クロス表の作成",
            "GoogleMapsDirectionCross [dir]",
            "Google Maps 所要時間クロス表の作成",
            "[dir]:You can specify the directory.")]
        static int GoogleMapsDirectionCross(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[dir]", ConsoleService.Prompt, "Directory path: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string dir = vl.DefaultParam.StrValue;

            string apiKey = Lfs.ReadStringFromFile(dir._CombinePath("ApiKey.txt"), oneLine: true);

            string srcListText = Lfs.ReadStringFromFile(dir._CombinePath("Source.txt"));
            string destListText = Lfs.ReadStringFromFile(dir._CombinePath("Destination.txt"));

            string[] srcList = srcListText._GetLines(removeEmpty: true);
            string[] destList = destListText._GetLines(removeEmpty: true);

            using var googleMapsApi = new GoogleMapsApi(new GoogleMapsApiSettings(apiKey: apiKey));

            DateTimeOffset departure = Util.GetStartOfDay(DateTime.Now.AddDays(2))._AsDateTimeOffset(isLocalTime: true);

            List<DirectionCrossResults> csv = new List<DirectionCrossResults>();

            foreach (string src in srcList)
            {
                foreach (string dest in destList)
                {
                    Console.WriteLine($"「{src}」 → 「{dest}」 ...");

                    DirectionCrossResults r = new DirectionCrossResults();

                    r.Start = src;
                    r.End = dest;

                    try
                    {
                        var result = googleMapsApi.CalcDurationAsync(src, dest, departure)._GetResult();

                        if (result.IsError == false)
                        {
                            r.StartAddress = result.StartAddress;
                            r.EndAddress = result.EndAddress;
                            r.Error = "";
                            r.Duration = result.Duration;
                            r.DistanceKm = result.DistanceKm;
                            r.RouteSummary = result.RouteSummary;

                            $"  {r.Duration} - {r.DistanceKm} km ({r.RouteSummary})"._Print();
                        }
                        else
                        {
                            r.Error = result.ErrorString;
                            r.Error._Print();
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                        r.Error = ex.Message;
                    }

                    csv.Add(r);
                }
            }

            string csvText = csv._ObjectArrayToCsv(withHeader: true);

            Lfs.WriteStringToFile(dir._CombinePath("Result.csv"), csvText, writeBom: true);

            return 0;
        }
    }
}


class LetsEncryptClient
{
    public const string StagingV2 = "https://acme-staging-v02.api.letsencrypt.org/directory";

    private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
    };

    private static Dictionary<string, HttpClient> _cachedClients = new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);

    private static HttpClient GetCachedClient(string url)
    {
        if (_cachedClients.TryGetValue(url, out var value))
        {
            return value;
        }

        lock (Locker)
        {
            if (_cachedClients.TryGetValue(url, out value))
            {
                return value;
            }

            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; },
            };

            value = new HttpClient(httpClientHandler)
            {
                BaseAddress = new Uri(url)
            };

            _cachedClients = new Dictionary<string, HttpClient>(_cachedClients, StringComparer.OrdinalIgnoreCase)
            {
                [url] = value
            };
            return value;
        }
    }





#nullable disable

    /// <summary>
    ///     In our scenario, we assume a single single wizard progressing
    ///     and the locking is basic to the wizard progress. Adding explicit
    ///     locking to be sure that we are not corrupting disk state if user
    ///     is explicitly calling stuff concurrently (running the setup wizard
    ///     from two tabs?)
    /// </summary>
    private static readonly object Locker = new object();

    private Jws _jws;
    private readonly string _path;
    private readonly string _url;
    private string _nonce;
    private RSACryptoServiceProvider _accountKey;
    private RegistrationCache _cache;
    private HttpClient _client;
    private Directory _directory;
    private List<AuthorizationChallenge> _challenges = new List<AuthorizationChallenge>();
    private Order _currentOrder;

    public LetsEncryptClient(string url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        var hash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(url));
        var file = Jws.Base64UrlEncoded(hash) + ".lets-encrypt.cache.json";
        _path = Path.Combine(home, file);
    }

    public async Task Init(string email, CancellationToken token = default(CancellationToken))
    {
        _accountKey = new RSACryptoServiceProvider(4096);
        _client = GetCachedClient(_url);
        (_directory, _) = await SendAsyncStd<Directory>(HttpMethod.Get, new Uri("directory", UriKind.Relative), null, token);

        /*if (File.Exists(_path))
        {
            bool success;
            try
            {
                lock (Locker)
                {
                    _cache = JsonConvert.DeserializeObject<RegistrationCache>(File.ReadAllText(_path));
                }

                _accountKey.ImportCspBlob(_cache.AccountKey);
                _jws = new Jws(_accountKey, _cache.Id);
                success = true;
            }
            catch
            {
                success = false;
                // if we failed for any reason, we'll just
                // generate a new registration
            }

            if (success)
            {
                return;
            }
        }*/

        _jws = new Jws(_accountKey, null);

        string newAccountUrl = _directory.NewAccount.ToString().Replace("https:", "https:");

        //newAccountUrl = "https://pc37.sehosts.com/a";

        var (account, response) = await SendAsync2<Account>(IPA.Cores.Basic.HttpClientCore.HttpMethod.Post, new Uri(newAccountUrl), new Account
        {
            // we validate this in the UI before we get here, so that is fine
            TermsOfServiceAgreed = true,
            Contacts = new[] { "mailto:" + email },
        }, token);
        _jws.SetKeyId(account);

        if (account.Status != "valid")
            throw new InvalidOperationException("Account status is not valid, was: " + account.Status + Environment.NewLine + response);

        lock (Locker)
        {
            _cache = new RegistrationCache
            {
                Location = account.Location,
                AccountKey = _accountKey.ExportCspBlob(true),
                Id = account.Id,
                Key = account.Key
            };
            File.WriteAllText(_path,
                JsonConvert.SerializeObject(_cache, Formatting.Indented));
        }
    }


    private async Task<(TResult Result, string Response)> SendAsyncStd<TResult>(HttpMethod method, Uri uri, object message, CancellationToken token) where TResult : class
    {
        IPA.Cores.ClientApi.Acme.AcmeClient ac = new IPA.Cores.ClientApi.Acme.AcmeClient(new IPA.Cores.ClientApi.Acme.AcmeClientOptions());

        _nonce = await ac.GetNonceAsync();

        message._PrintAsJson();

        var request = new HttpRequestMessage(method, uri);

        string json = "";

        if (message != null)
        {
            var encodedMessage = _jws.Encode(message, new JwsHeader
            {
                Nonce = _nonce,
                Url = uri
            });
            json = JsonConvert.SerializeObject(encodedMessage, jsonSettings);

            json._Print();

            encodedMessage._PrintAsJson();

            request.Content = new StringContent(json, Encoding.UTF8, "application/jose+json");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jose+json");
        }

        var response = await _client.SendAsync(request, token).ConfigureAwait(false);

        //_nonce = response.Headers.GetValues("Replay-Nonce").First();

        if (response.Content.Headers.ContentType.MediaType == "application/problem+json")
        {
            var problemJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var problem = JsonConvert.DeserializeObject<Problem>(problemJson);
            problem.RawJson = problemJson;
            throw new LetsEncrytException(problem, response);
        }

        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (typeof(TResult) == typeof(string)
            && response.Content.Headers.ContentType.MediaType == "application/pem-certificate-chain")
        {
            return ((TResult)(object)responseText, null);
        }

        var responseContent = JObject.Parse(responseText).ToObject<TResult>();

        if (responseContent is IHasLocation ihl)
        {
            if (response.Headers.Location != null)
                ihl.Location = response.Headers.Location;
        }

        responseText._Print();

        return (responseContent, responseText);
    }

    private async Task<(TResult Result, string Response)> SendAsync2<TResult>(IPA.Cores.Basic.HttpClientCore.HttpMethod method, Uri uri, object message, CancellationToken token) where TResult : class
    {
        IPA.Cores.ClientApi.Acme.AcmeClient ac = new IPA.Cores.ClientApi.Acme.AcmeClient(new IPA.Cores.ClientApi.Acme.AcmeClientOptions());

        _nonce = await ac.GetNonceAsync();

        message._PrintAsJson();

        var request = new IPA.Cores.Basic.HttpClientCore.HttpRequestMessage(method, uri);

        string json = "";

        if (message != null)
        {
            var encodedMessage = _jws.Encode(message, new JwsHeader
            {
                Nonce = _nonce,
                Url = uri
            });
            json = JsonConvert.SerializeObject(encodedMessage, jsonSettings);

            json._Print();

            encodedMessage._PrintAsJson();

            request.Content = new IPA.Cores.Basic.HttpClientCore.StringContent(json, Encoding.UTF8, "application/jose+json");
            request.Content.Headers.ContentType = new IPA.Cores.Basic.HttpClientCore.MediaTypeHeaderValue("application/jose+json");
        }


        var webapi = new WebApi(new WebApiOptions(new WebApiSettings() { AllowAutoRedirect = true }));

        var response = await webapi.Client.SendAsync(request, token).ConfigureAwait(false);




        //_nonce = response.Headers.GetValues("Replay-Nonce").First();

        if (response.Content.Headers.ContentType.MediaType == "application/problem+json")
        {
            var problemJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var problem = JsonConvert.DeserializeObject<Problem>(problemJson);
            problemJson._Print();
            problem.RawJson = problemJson;
            throw new ApplicationException();
        }

        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (typeof(TResult) == typeof(string)
            && response.Content.Headers.ContentType.MediaType == "application/pem-certificate-chain")
        {
            return ((TResult)(object)responseText, null);
        }

        var responseContent = JObject.Parse(responseText).ToObject<TResult>();

        if (responseContent is IHasLocation ihl)
        {
            if (response.Headers.Location != null)
                ihl.Location = response.Headers.Location;
        }

        responseText._Print();


        return (responseContent, responseText);
    }

    private async Task<(TResult Result, string Response)> SendAsync<TResult>(HttpMethod method, Uri uri, object message, CancellationToken token) where TResult : class
    {
        IPA.Cores.ClientApi.Acme.AcmeClient ac = new IPA.Cores.ClientApi.Acme.AcmeClient(new IPA.Cores.ClientApi.Acme.AcmeClientOptions());

        _nonce = await ac.GetNonceAsync();

        message._PrintAsJson();

        var request = new HttpRequestMessage(method, uri);

        string json = "";

        if (message != null)
        {
            var encodedMessage = _jws.Encode(message, new JwsHeader
            {
                Nonce = _nonce,
                Url = uri
            });
            json = JsonConvert.SerializeObject(encodedMessage, jsonSettings);

            json._Print();

            encodedMessage._PrintAsJson();

            request.Content = new StringContent(json, Encoding.UTF8, "application/jose+json");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jose+json");
        }

        //var response = await _client.SendAsync(request, token).ConfigureAwait(false);

        var webapi = new WebApi(new WebApiOptions(new WebApiSettings() { SslAcceptAnyCerts = true, Timeout = CoresConfig.AcmeClientSettings.ShortTimeout }, null));

        var webret = await webapi.SimplePostJsonAsync(WebMethods.POST, uri.ToString(), json, default, "application/jose+json");


        webret.Data._GetString_Ascii()._Print();

        return default;

        /*
        //_nonce = response.Headers.GetValues("Replay-Nonce").First();

        if (response.Content.Headers.ContentType.MediaType == "application/problem+json")
        {
            var problemJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var problem = JsonConvert.DeserializeObject<Problem>(problemJson);
            problem.RawJson = problemJson;
            throw new LetsEncrytException(problem, response);
        }

        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (typeof(TResult) == typeof(string)
            && response.Content.Headers.ContentType.MediaType == "application/pem-certificate-chain")
        {
            return ((TResult)(object)responseText, null);
        }

        var responseContent = JObject.Parse(responseText).ToObject<TResult>();

        if (responseContent is IHasLocation ihl)
        {
            if (response.Headers.Location != null)
                ihl.Location = response.Headers.Location;
        }

        return (responseContent, responseText);*/
    }


    public async Task<Dictionary<string, string>> NewOrder(string[] hostnames, CancellationToken token = default(CancellationToken))
    {
        _challenges.Clear();
        var (order, response) = await SendAsync<Order>(HttpMethod.Post, _directory.NewOrder, new Order
        {
            Expires = DateTime.UtcNow.AddDays(2),
            Identifiers = hostnames.Select(hostname => new OrderIdentifier
            {
                Type = "dns",
                Value = hostname
            }).ToArray()
        }, token);

        if (order.Status != "pending")
            throw new InvalidOperationException("Created new order and expected status 'pending', but got: " + order.Status + Environment.NewLine +
                response);
        _currentOrder = order;
        var results = new Dictionary<string, string>();
        foreach (var item in order.Authorizations)
        {
            var (challengeResponse, responseText) = await SendAsync<AuthorizationChallengeResponse>(HttpMethod.Get, item, null, token);
            if (challengeResponse.Status == "valid")
                continue;

            if (challengeResponse.Status != "pending")
                throw new InvalidOperationException("Expected autorization status 'pending', but got: " + order.Status +
                    Environment.NewLine + responseText);

            var challenge = challengeResponse.Challenges.First(x => x.Type == "dns-01");
            _challenges.Add(challenge);
            var keyToken = _jws.GetKeyAuthorization(challenge.Token);
            using (var sha256 = SHA256.Create())
            {
                var dnsToken = Jws.Base64UrlEncoded(sha256.ComputeHash(Encoding.UTF8.GetBytes(keyToken)));
                results[challengeResponse.Identifier.Value] = dnsToken;
            }
        }

        return results;
    }

    public async Task CompleteChallenges(CancellationToken token = default(CancellationToken))
    {
        for (var index = 0; index < _challenges.Count; index++)
        {
            var challenge = _challenges[index];

            while (true)
            {
                var (result, responseText) = await SendAsync<AuthorizationChallengeResponse>(HttpMethod.Post, challenge.Url, new AuthorizeChallenge
                {
                    KeyAuthorization = _jws.GetKeyAuthorization(challenge.Token)
                }, token);

                if (result.Status == "valid")
                    break;
                if (result.Status != "pending")
                    throw new InvalidOperationException("Failed autorization of " + _currentOrder.Identifiers[index].Value + Environment.NewLine + responseText);

                await Task.Delay(500);
            }
        }
    }

    public async Task<(X509Certificate2 Cert, RSA PrivateKey)> GetCertificate(CancellationToken token = default(CancellationToken))
    {
        var key = new RSACryptoServiceProvider(4096);
        var csr = new CertificateRequest("CN=" + _currentOrder.Identifiers[0].Value,
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        foreach (var host in _currentOrder.Identifiers)
            san.AddDnsName(host.Value);

        csr.CertificateExtensions.Add(san.Build());

        var (response, responseText) = await SendAsync<Order>(HttpMethod.Post, _currentOrder.Finalize, new FinalizeRequest
        {
            CSR = Jws.Base64UrlEncoded(csr.CreateSigningRequest())
        }, token);

        while (response.Status != "valid")
        {
            (response, responseText) = await SendAsync<Order>(HttpMethod.Get, response.Location, null, token);

            if (response.Status == "processing")
            {
                await Task.Delay(500);
                continue;
            }
            throw new InvalidOperationException("Invalid order status: " + response.Status + Environment.NewLine +
                responseText);
        }
        var (pem, _) = await SendAsync<string>(HttpMethod.Get, response.Certificate, null, token);

        var cert = new X509Certificate2(Encoding.UTF8.GetBytes(pem));

        _cache.CachedCerts[_currentOrder.Identifiers[0].Value] = new CertificateCache
        {
            Cert = pem,
            Private = key.ExportCspBlob(true)
        };

        lock (Locker)
        {
            File.WriteAllText(_path,
                JsonConvert.SerializeObject(_cache, Formatting.Indented));
        }

        return (cert, key);
    }

    public class CachedCertificateResult
    {
        public RSA PrivateKey;
        public string Certificate;
    }

    public bool TryGetCachedCertificate(List<string> hosts, out CachedCertificateResult value)
    {
        value = null;
        if (_cache.CachedCerts.TryGetValue(hosts[0], out var cache) == false)
        {
            return false;
        }

        var cert = new X509Certificate2(cache.Cert);

        // if it is about to expire, we need to refresh
        if ((cert.NotAfter - DateTime.UtcNow).TotalDays < 14)
            return false;

        var rsa = new RSACryptoServiceProvider(4096);
        rsa.ImportCspBlob(cache.Private);

        value = new CachedCertificateResult
        {
            Certificate = cache.Cert,
            PrivateKey = rsa
        };
        return true;
    }


    public string GetTermsOfServiceUri(CancellationToken token = default(CancellationToken))
    {
        return _directory.Meta.TermsOfService;
    }

    public void ResetCachedCertificate(IEnumerable<string> hostsToRemove)
    {
        foreach (var host in hostsToRemove)
        {
            _cache.CachedCerts.Remove(host);
        }
    }


    private class RegistrationCache
    {
        public readonly Dictionary<string, CertificateCache> CachedCerts = new Dictionary<string, CertificateCache>(StringComparer.OrdinalIgnoreCase);
        public byte[] AccountKey;
        public string Id;
        public Jwk Key;
        public Uri Location;
    }

    private class CertificateCache
    {
        public string Cert;
        public byte[] Private;
    }

    private class AuthorizationChallengeResponse
    {
        [JsonProperty("identifier")]
        public OrderIdentifier Identifier { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("expires")]
        public DateTime? Expires { get; set; }

        [JsonProperty("wildcard")]
        public bool Wildcard { get; set; }

        [JsonProperty("challenges")]
        public AuthorizationChallenge[] Challenges { get; set; }
    }

    private class AuthorizeChallenge
    {
        [JsonProperty("keyAuthorization")]
        public string KeyAuthorization { get; set; }

    }

    private class AuthorizationChallenge
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

    }

    private class Jwk
    {
        [JsonProperty("kty")]
        public string KeyType { get; set; }

        [JsonProperty("kid")]
        public string KeyId { get; set; }

        [JsonProperty("use")]
        public string Use { get; set; }

        [JsonProperty("n")]
        public string Modulus { get; set; }

        [JsonProperty("e")]
        public string Exponent { get; set; }

        [JsonProperty("d")]
        public string D { get; set; }

        [JsonProperty("p")]
        public string P { get; set; }

        [JsonProperty("q")]
        public string Q { get; set; }

        [JsonProperty("dp")]
        public string DP { get; set; }

        [JsonProperty("dq")]
        public string DQ { get; set; }

        [JsonProperty("qi")]
        public string InverseQ { get; set; }

        [JsonProperty("alg")]
        public string Algorithm { get; set; }
    }

    private class Directory
    {
        [JsonProperty("keyChange")]
        public Uri KeyChange { get; set; }

        [JsonProperty("newNonce")]
        public Uri NewNonce { get; set; }

        [JsonProperty("newAccount")]
        public Uri NewAccount { get; set; }

        [JsonProperty("newOrder")]
        public Uri NewOrder { get; set; }

        [JsonProperty("revokeCert")]
        public Uri RevokeCertificate { get; set; }

        [JsonProperty("meta")]
        public DirectoryMeta Meta { get; set; }
    }

    private class DirectoryMeta
    {
        [JsonProperty("termsOfService")]
        public string TermsOfService { get; set; }
    }

    public class Problem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("detail")]
        public string Detail { get; set; }

        public string RawJson { get; set; }
    }

    public class LetsEncrytException : Exception
    {
        public LetsEncrytException(Problem problem, HttpResponseMessage response)
            : base($"{problem.Type}: {problem.Detail}")
        {
            Problem = problem;
            Response = response;
        }

        public Problem Problem { get; }

        public HttpResponseMessage Response { get; }
    }

    private class JwsMessage
    {
        [JsonProperty("header")]
        public JwsHeader Header { get; set; }

        [JsonProperty("protected")]
        public string Protected { get; set; }

        [JsonProperty("payload")]
        public string Payload { get; set; }

        [JsonProperty("signature")]
        public string Signature { get; set; }
    }

    private class JwsHeader
    {
        public JwsHeader()
        {
        }

        public JwsHeader(string algorithm, Jwk key)
        {
            Algorithm = algorithm;
            Key = key;
        }

        [JsonProperty("alg")]
        public string Algorithm { get; set; }

        [JsonProperty("jwk")]
        public Jwk Key { get; set; }


        [JsonProperty("kid")]
        public string KeyId { get; set; }


        [JsonProperty("nonce")]
        public string Nonce { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }
    }

    private interface IHasLocation
    {
        Uri Location { get; set; }
    }

    private class Order : IHasLocation
    {
        public Uri Location { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("expires")]
        public DateTime? Expires { get; set; }

        [JsonProperty("identifiers")]
        public OrderIdentifier[] Identifiers { get; set; }

        [JsonProperty("notBefore")]
        public DateTime? NotBefore { get; set; }

        [JsonProperty("notAfter")]
        public DateTime? NotAfter { get; set; }

        [JsonProperty("error")]
        public Problem Error { get; set; }

        [JsonProperty("authorizations")]
        public Uri[] Authorizations { get; set; }

        [JsonProperty("finalize")]
        public Uri Finalize { get; set; }

        [JsonProperty("certificate")]
        public Uri Certificate { get; set; }
    }

    private class OrderIdentifier
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

    }

    private class Account : IHasLocation
    {
        [JsonProperty("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        [JsonProperty("contact")]
        public string[] Contacts { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("key")]
        public Jwk Key { get; set; }

        [JsonProperty("initialIp")]
        public string InitialIp { get; set; }

        [JsonProperty("orders")]
        public Uri Orders { get; set; }

        public Uri Location { get; set; }
    }

    private class FinalizeRequest
    {
        [JsonProperty("csr")]
        public string CSR { get; set; }
    }

    private class Jws
    {
        private readonly Jwk _jwk;
        private readonly RSA _rsa;

        public Jws(RSA rsa, string keyId)
        {
            _rsa = rsa ?? throw new ArgumentNullException(nameof(rsa));

            var publicParameters = rsa.ExportParameters(false);

            _jwk = new Jwk
            {
                KeyType = "RSA",
                Exponent = Base64UrlEncoded(publicParameters.Exponent),
                Modulus = Base64UrlEncoded(publicParameters.Modulus),
                KeyId = keyId
            };
        }

        public JwsMessage Encode<TPayload>(TPayload payload, JwsHeader protectedHeader)
        {
            protectedHeader.Algorithm = "RS256";
            if (_jwk.KeyId != null)
            {
                protectedHeader.KeyId = _jwk.KeyId;
            }
            else
            {
                protectedHeader.Key = _jwk;
            }

            var message = new JwsMessage
            {
                Payload = Base64UrlEncoded(JsonConvert.SerializeObject(payload)),
                Protected = Base64UrlEncoded(JsonConvert.SerializeObject(protectedHeader))
            };

            message.Signature = Base64UrlEncoded(
                _rsa.SignData(Encoding.ASCII.GetBytes(message.Protected + "." + message.Payload),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));

            return message;
        }

        private string GetSha256Thumbprint()
        {
            var json = "{\"e\":\"" + _jwk.Exponent + "\",\"kty\":\"RSA\",\"n\":\"" + _jwk.Modulus + "\"}";

            using (var sha256 = SHA256.Create())
            {
                return Base64UrlEncoded(sha256.ComputeHash(Encoding.UTF8.GetBytes(json)));
            }
        }

        public string GetKeyAuthorization(string token)
        {
            return token + "." + GetSha256Thumbprint();
        }

        public static string Base64UrlEncoded(string s)
        {
            return Base64UrlEncoded(Encoding.UTF8.GetBytes(s));
        }

        public static string Base64UrlEncoded(byte[] arg)
        {
            var s = Convert.ToBase64String(arg); // Regular base64 encoder
            s = s.Split('=')[0]; // Remove any trailing '='s
            s = s.Replace('+', '-'); // 62nd char of encoding
            s = s.Replace('/', '_'); // 63rd char of encoding
            return s;
        }

        internal void SetKeyId(Account account)
        {
            _jwk.KeyId = account.Id;
        }
    }
}
