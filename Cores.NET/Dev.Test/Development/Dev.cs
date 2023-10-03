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
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
//using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Net.Mail;

namespace IPA.Cores.Basic;

// タイムスタンプ・ユーティリティの設定
public class TimeStampDocsSetting : INormalizable
{
    public SmtpClientSettings SmtpSettings = new SmtpClientSettings();

    public string MailZipPassword = "";

    public string TimeStampConfigFilename = "";

    public string OutputFilenamePrefix = "";

    public int MaxFilesInProject = 0;
    public int MaxFilesTotal = 0;

    public int SingleMailZipFileSize = 7140000;

    public int MailTryCount = 3;

    public string LogDir = "";

    public string TimeStampProjectLogDirName = "";

    public string MailFrom = "";
    public string MailTo = "";
    public string MailCc = "";

    public string MailSubjectPrefix = "";

    public void Normalize()
    {
        if (this.MailZipPassword._IsEmpty()) this.MailZipPassword = "password";

        if (this.SmtpSettings == null) this.SmtpSettings = new SmtpClientSettings();
        this.SmtpSettings.Normalize();

        if (this.TimeStampConfigFilename._IsEmpty()) this.TimeStampConfigFilename = "_timestamp.txt";

        if (this.MaxFilesInProject <= 0) this.MaxFilesInProject = 10_0000;
        if (this.MaxFilesTotal <= 0) this.MaxFilesTotal = 100_0000;

        if (OutputFilenamePrefix._IsEmpty()) OutputFilenamePrefix = Env.MachineName.ToLower();
        OutputFilenamePrefix = Str.MakeVerySafeAsciiOnlyNonSpaceFileName(OutputFilenamePrefix, true);

        if (this.LogDir._IsEmpty()) this.LogDir = PP.Combine(Env.AppRootDir, "TimeStampArchives_DoNotDeleteMe");

        if (this.TimeStampProjectLogDirName._IsEmpty()) this.TimeStampProjectLogDirName = "_timestamp_archives.do_not_delete_me";

        if (this.MailFrom._IsEmpty()) this.MailFrom = "nobody@example.org";
        if (this.MailTo._IsEmpty()) this.MailTo = "nobody@example.org";
        if (this.MailCc._IsEmpty()) this.MailCc = "nobody@example.org";

        if (this.MailSubjectPrefix._IsEmpty()) this.MailSubjectPrefix = "[TimeStampDocs]";
    }
}

// タイムスタンプ・ユーティリティ
public class TimeStampDocsUtil : AsyncService
{
    readonly HiveData<TimeStampDocsSetting> SettingsHive;
    public TimeStampDocsSetting Settings => SettingsHive.CachedFastSnapshot;

    [Flags]
    public enum TimeStampFileFilterAction
    {
        Include = 0,
        Exclude,
    }

    [Flags]
    public enum TimeStampFileFilterType
    {
        ForFile = 0,
        ForDir,
    }

    [Flags]
    public enum WriteFlags
    {
        None = 0,
        FullPath = 1,
    }

    public class TimeStampFileFilter
    {
        public TimeStampFileFilterAction Action = TimeStampFileFilterAction.Include;
        public TimeStampFileFilterType Type = TimeStampFileFilterType.ForFile;
        public string Wildcard = "";
    }

    public class FileData
    {
        public string FullPath = "";
        public string RelativePath = "";
        public long FileSize = 0;
        public DateTimeOffset DateCreated;
        public DateTimeOffset DateLastWrite;
        public string Md5 = "";
        public string Sha512 = "";
    }

    public class DirData
    {
        public string FullPath = "";
        public string RelativePath = "";

        public List<FileData> FileList = new List<FileData>();

        public void WriteTo(TextWriter t, DateTimeOffset now, WriteFlags flags)
        {
            t.WriteLine($"# このフォルダには、{this.FileList.Count._ToString3()} 個のファイルが存在します。");
            t.WriteLine($"# これらのファイルの合計サイズは、{this.FileList.Sum(x => x.FileSize)._ToString3()} bytes ({this.FileList.Sum(x => x.FileSize)._GetFileSizeStr()}) です。");
            t.WriteLine($"# ファイルの名前やサイズ、ハッシュ値は以下のとおりです。");
            t.WriteLine($"# 以下のファイルサイズおよびハッシュ値の計算結果は、{now._ToDtStr(false)} 時点のものです。");
            t.WriteLine($"# created (作成日時) と lastwrite (更新日時) は、ファイルシステム上の情報なので、これらは実際の作成・更新時よりも後を示す可能性があります。");
            t.WriteLine($"# このハッシュ表のテキストファイルを、タイムスタンプ署名で署名している場合、その署名のタイムスタンプ日時において、以下のハッシュ値を有するファイルが既に確かに存在していたことが、本システムによって、証明されます。");
            t.WriteLine($"# このテキストファイルには、ハッシュ値しかありません。ファイル本体そのものは、別に保存されていなければなりません。ファイル本体がなければ、ハッシュ値だけが残っていても、肝心のファイル内容を証明することはできません。");
            t.WriteLine("# index,filename,size,md5,sha512,created,lastwrite");

            int index = 0;

            foreach (var file in FileList.OrderBy(x => x.FullPath, StrCmpi))
            {
                index++;

                List<string> tokens = new List<string>
                {
                    index.ToString(),
                    PPMac.GetFileName(flags.Bit( WriteFlags.FullPath) ? file.FullPath : file.RelativePath).Replace(",", "，"),
                    file.FileSize.ToString(),
                    file.Md5,
                    file.Sha512,
                    file.DateCreated._ToDtStr(true),
                    file.DateLastWrite._ToDtStr(true)
                };

                t.WriteLine(tokens._Combine(","));
            }
        }
    }

    public class ProjectResult
    {
        public int NumFiles_Inc;

        public List<DirData> DirList = new List<DirData>();
        public string RootDirPath = "";
        public string RootDirName = "";

        public int NumFiles => this.DirList.Sum(x => x.FileList.Count);
        public long TotalFileSize => this.DirList.Sum(x => x.FileList.Sum(y => y.FileSize));

        public List<string> SettingsFiles = new List<string>();

        public void WriteTo(TextWriter t, DateTimeOffset now, WriteFlags flags)
        {
            t.WriteLine($"# このプロジェクトには、{this.DirList.Count._ToString3()} 個のフォルダ中に、{this.DirList.Sum(x => x.FileList.Count)._ToString3()} 個のファイルが存在します。");
            t.WriteLine($"# これらのファイルの合計サイズは、{this.DirList.Sum(y => y.FileList.Sum(x => x.FileSize))._ToString3()} bytes ({this.DirList.Sum(y => y.FileList.Sum(x => x.FileSize))._GetFileSizeStr()}) です。");
            t.WriteLine($"# 以下で、フォルダごとのファイルの一覧を並べ上げて、各ファイルのハッシュ値を明示いたします。");
            t.WriteLine();

            var list = this.DirList.OrderBy(x => x.FullPath, StrCmpi).ToList();

            for (int i = 0; i < list.Count; i++)
            {
                var dir = list[i];

                t.WriteLine($"■ フォルダ ({i + 1}/{list.Count}) 『{(flags.Bit(WriteFlags.FullPath) ? dir.FullPath : dir.RelativePath)}』");

                dir.WriteTo(t, now, flags);

                t.WriteLine();
            }
        }
    }

    public class TimeStampConfig
    {
        public List<TimeStampFileFilter> Filters = new List<TimeStampFileFilter>();
        public StrDictionary<string> Settings = new StrDictionary<string>(StrTrimCmpi);

        public TimeStampConfig() { }

        public TimeStampConfig(string body)
        {
            string[] lines = body._GetLines(true, true, new string[] { "#" }, trim: true);

            foreach (var line in lines)
            {
                if (line._GetKeyAndValue(out string key, out string value, ":"))
                {
                    key = key._NonNullTrim();
                    value = value._NonNullTrim();
                    if (key._IsFilled())
                    {
                        this.Settings.Add(key, value);
                    }
                }
                else
                {
                    string wildcard = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(line);

                    TimeStampFileFilter f = new TimeStampFileFilter();

                    if (wildcard.StartsWith("+"))
                    {
                        f.Action = TimeStampFileFilterAction.Include;
                        f.Wildcard = wildcard.Substring(1);
                    }
                    else if (wildcard.StartsWith("-") || wildcard.StartsWith("!"))
                    {
                        f.Action = TimeStampFileFilterAction.Exclude;
                        f.Wildcard = wildcard.Substring(1);
                    }
                    else
                    {
                        f.Action = TimeStampFileFilterAction.Include;
                        f.Wildcard = wildcard;
                    }

                    if (f.Wildcard.EndsWith("/"))
                    {
                        f.Type = TimeStampFileFilterType.ForDir;
                        f.Wildcard = f.Wildcard.Substring(0, f.Wildcard.Length - 1);
                    }

                    f.Wildcard = f.Wildcard.Trim();

                    if (f.Wildcard._IsFilled())
                    {
                        this.Filters.Add(f);
                    }
                }
            }
        }

        public TimeStampConfig MergeConfig(TimeStampConfig? child)
        {
            TimeStampConfig ret = this._CloneDeep();

            // フィルタのマージ (ファイル名に関するフィルタのみ)
            ret.Filters = (child == null ? new TimeStampFileFilter[0] : child.Filters.ToArray()).Concat(this.Filters.Where(x => x.Type == TimeStampFileFilterType.ForFile)).ToList();

            // 設定のマージ
            if (child != null)
            {
                foreach (var kv in child.Settings)
                {
                    if (kv.Key.StartsWith("-") == false)
                    {
                        ret.Settings[kv.Key] = kv.Value;
                    }
                    else
                    {
                        ret.Settings.Remove(kv.Key.Substring(1));
                    }
                }
            }

            return ret;
        }

        public bool ProcessFilterForFile(FileSystemEntity file, FileMetadata meta)
        {
            if (ProcessFilterForFileName(file, meta) == false)
            {
                return false;
            }

            if (ProcessFilterForFileDate(file, meta) == false)
            {
                return false;
            }

            return true;
        }

        bool ProcessFilterForFileDate(FileSystemEntity file, FileMetadata meta)
        {
            int daysInSettings = this.Settings._GetOrDefault("maxages")._ToInt();

            if (daysInSettings <= 0)
            {
                return true;
            }

            var date = meta.LastWriteTime ?? DtOffsetZero;
            DateTimeOffset now = DtOffsetNow;

            double days = (now - date).TotalDays;

            if (days < daysInSettings)
            {
                return true;
            }

            return false;
        }

        bool ProcessFilterForFileName(FileSystemEntity file, FileMetadata meta)
        {
            foreach (var f in this.Filters.Where(x => x.Type == TimeStampFileFilterType.ForFile))
            {
                if (file.Name._WildcardMatch(f.Wildcard, true))
                {
                    return f.Action == TimeStampFileFilterAction.Include ? true : false;
                }
            }

            return true;
        }

        public bool ProcessFilterForDirectory(string name)
        {
            foreach (var f in this.Filters.Where(x => x.Type == TimeStampFileFilterType.ForDir))
            {
                if (name._WildcardMatch(f.Wildcard, true))
                {
                    return f.Action == TimeStampFileFilterAction.Include ? true : false;
                }
            }

            return true;
        }
    }

    public FileSystem Fs { get; }

    public TimeStampDocsUtil(FileSystem fs)
    {
        try
        {
            this.Fs = fs;

            // Settings を読み込む
            this.SettingsHive = new HiveData<TimeStampDocsSetting>(Hive.SharedLocalConfigHive, $"TimeStampDocsSetting", null, HiveSyncPolicy.AutoReadWriteFile);
        }
        catch (Exception ex)
        {
            this._DisposeSafe(ex);
            throw;
        }
    }

    async Task<TimeStampConfig?> ParseConfigAsync(string fileName, CancellationToken cancel = default)
    {
        if (await this.Fs.IsFileExistsAsync(fileName, cancel) == false)
        {
            return null;
        }

        try
        {
            string body = await this.Fs.ReadStringFromFileAsync(fileName, maxSize: 1_000_000, cancel: cancel);

            TimeStampConfig config = new TimeStampConfig(body);

            return config;
        }
        catch (Exception ex)
        {
            ex._Error();

            return null;
        }
    }

    Once OnceFlag;

    public int TotalNumFiles = 0;

    public async Task DoAsync(string targetDirsList, CancellationToken cancel = default)
    {
        if (OnceFlag.IsFirstCall() == false)
        {
            throw new CoresLibException("OnceFlag.IsFirstCall() == false");
        }

        string uniqueId = Str.GenRandStr();

        string[] targetDirs = targetDirsList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ";", ",");

        List<ProjectResult> projectResultsList = new List<ProjectResult>();

        // まず、指定されたディレクトリをどんどんとサブディレクトリに向かって走査する
        await this.Fs.DirectoryWalker.WalkDirectoriesAsync(targetDirs, async (di, list, c) =>
        {
            await Task.CompletedTask;

            try
            {
                if (this.TotalNumFiles >= this.Settings.MaxFilesTotal)
                {
                    // ファイル個数超過 (全体)
                    return false;
                }

                // このディレクトリ内に設定ファイルがあるか?
                string configPath = di.FileSystem.PathParser.Combine(di.FullPath, this.Settings.TimeStampConfigFilename);

                var config = await ParseConfigAsync(configPath, cancel); // これははエラーにならないはず

                if (config != null)
                {
                    ProjectResult result = new ProjectResult
                    {
                        RootDirPath = di.FullPath,
                        RootDirName = this.Fs.PathParser.GetFileName(di.FullPath),
                    };

                    result.SettingsFiles.Add(configPath);

                    await ProcessProjectDirAsync(config, di, list, cancel, di.FullPath, result);

                    projectResultsList.Add(result);

                    // config が発見されたので、これよりも下部のサブディリクトリの走査は行なわない
                    // (CoresNoSubDirException 例外を発生させれば、walker はより深いディレクトリへの走査を中止する)
                    throw new CoresNoSubDirException();
                }
            }
            catch (CoresNoSubDirException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            return true;
        },
        exceptionHandler: async (di, ex, c) =>
        {
            await Task.CompletedTask;
            // ディレクトリ列挙の例外が発生したら、エラー出力する
            ex._Error();

            return true;
        },
        cancel: cancel);

        DateTimeOffset now = DtOffsetNow;

        string yyyymmdd = now.LocalDateTime._ToYymmddStr() + "-" + now.DateTime._ToHhmmssStr();

        StringWriter forLocalFullPathText = new StringWriter();
        forLocalFullPathText.NewLine = Str.NewLine_Str_Windows;

        StringWriter forZipText = new StringWriter();
        forZipText.NewLine = Str.NewLine_Str_Windows;

        // 署名対象電文の生成
        WriteTo(forZipText, projectResultsList, now, WriteFlags.None, uniqueId);
        string forZipTextBody = forZipText.ToString();

        // ローカル保存用ハッシュ表の作成
        WriteTo(forLocalFullPathText, projectResultsList, now, WriteFlags.FullPath, uniqueId);
        string forLocalFullPathTextBody = forLocalFullPathText.ToString();

        //forSignBody._Print();

        //return;

        // テスト用に増幅
        if (false)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 250; i++)
            {
                sb.Append(forZipTextBody);
            }
            forZipTextBody = sb.ToString();
        }

        var zipDataList = FileUtil.CompressTextToZipFilesSplittedWithMinSize(forZipTextBody, this.Settings.SingleMailZipFileSize, $"timestamptxt-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}", "", ".txt");

        Util.PutGitIgnoreFileOnDirectory(this.Settings.LogDir, FileFlags.AutoCreateDirectory);
        string entireLogsDirPath = this.Fs.PP.Combine(this.Settings.LogDir, $"{yyyymmdd}-{this.Settings.OutputFilenamePrefix}");

        await this.Fs.CreateDirectoryAsync(entireLogsDirPath, cancel: cancel);

        // ハッシュ表本文を出力 (フルパスが入っているもの)
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"timestamptxt-fullpath-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forLocalFullPathTextBody, FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

        // ハッシュ表本文を出力 (フルパスが入っていないもの)
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"timestamptxt-sametozip-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forZipTextBody, FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

        StringWriter mailLog = new StringWriter();
        mailLog.NewLine = Str.NewLine_Str_Windows;

        mailLog.WriteLine($"タイムスタンプ署名用 ZIP ファイルを添付したメールの送信ログ");
        mailLog.WriteLine();
        mailLog.WriteLine($"ハッシュ計算ジョブユニーク ID: {uniqueId}");
        mailLog.WriteLine();

        for (int i = 0; i < zipDataList.Count; i++)
        {
            var zipData = zipDataList[i];

            string zipFnSimple = $"timestampzip-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}-{(i + 1):D3}.zip";
            string zipFn = this.Fs.PP.Combine(entireLogsDirPath, zipFnSimple);

            // 分割された ZIP ファイル本体をファイルに保存
            Dbg.Where(zipFn);
            await this.Fs.WriteDataToFileAsync(zipFn, zipData, flags: FileFlags.AutoCreateDirectory);

            // メールを送信
            string subject = $"{this.Settings.MailSubjectPrefix} {yyyymmdd}-{this.Settings.OutputFilenamePrefix} ({i + 1}/{zipDataList.Count}) - {zipData.Length._ToString3()} bytes";

            StringWriter mailText = new StringWriter();
            mailText.NewLine = Str.NewLine_Str_Windows;
            mailText.WriteLine($"タイムスタンプ署名用ハッシュ表が本メールに添付されて送信されております。");
            mailText.WriteLine();
            mailText.WriteLine($"送信日時: {DtOffsetNow._ToDtStr()}");
            mailText.WriteLine($"Subject: {subject}");
            mailText.WriteLine();
            mailText.WriteLine($"ハッシュ計算ジョブユニーク ID: {uniqueId}");
            mailText.WriteLine();
            mailText.WriteLine($"この ZIP ファイル: {i + 1} 個目 / {zipDataList.Count} 個中");
            mailText.WriteLine($"この ZIP ファイルのファイル名: {zipFnSimple}");
            mailText.WriteLine($"この ZIP ファイルのサイズ: {zipData.Length._ToString3()} bytes ({zipData.Length._GetFileSizeStr()})");
            mailText.WriteLine($"この ZIP ファイルの MD5 ハッシュ値: {Secure.HashMD5(zipData.Span)._GetHexString()}");
            mailText.WriteLine($"この ZIP ファイルの SHA512 ハッシュ値: {Secure.HashSHA512(zipData.Span)._GetHexString()}");
            mailText.WriteLine();
            mailText.WriteLine($"このメールを含めて、合計 {zipDataList.Count} 通のメールが送付されています。");
            mailText.WriteLine($"これらのメールにそれぞれ添付されている合計 {zipDataList.Count} 個の ZIP ファイルに分割されています。");
            mailText.WriteLine($"合計 ZIP ファイルサイズ: {zipDataList.Sum(x => x.Length)._ToString3()} bytes ({zipDataList.Sum(x => x.Length)._GetFileSizeStr()})");
            mailText.WriteLine();
            mailText.WriteLine($"これらの ZIP ファイルは、タイムスタンプ署名が施されることになります。");
            mailText.WriteLine($"タイムスタンプ署名が施された ZIP ファイルは、しばらくすると、署名サービスにより、返信メールとして送り返されてきます。");
            mailText.WriteLine("送り返されてきたタイムスタンプ署名が施されたメールは、長期間保存しましょう。");
            mailText.WriteLine($"これにより、{DtOffsetNow._ToDtStr()} の時点で、ZIP ファイル内のハッシュ表に含まれているファイルが存在していたことが証明されます。");
            mailText.WriteLine("タイムスタンプ署名とは、そのようなものなのです。");
            mailText.WriteLine();
            mailText.WriteLine($"ZIP ファイルは、暗号化されています。パスワードは、システム管理者が管理しています。");
            mailText.WriteLine();
            mailText.WriteLine();
            mailText.WriteLine($"# デバッグ情報 (技術者向け): ({new EnvInfoSnapshot()._ObjectToJson(compact: true)})");
            mailText.WriteLine();

            SmtpConfig smtpConfig = this.Settings.SmtpSettings.ToSmtpConfig();

            SmtpBody mail = new SmtpBody(new MailAddress(this.Settings.MailFrom), new MailAddress(this.Settings.MailTo), subject,
                mailText.ToString());

            mail.AddAttachedFile(zipData.ToArray(), Consts.MimeTypes.Zip, zipFnSimple, null);

            mail.CcList.Add(new MailAddress(this.Settings.MailCc));

            mailLog.WriteLine($"========== メール ({i + 1}/{zipDataList.Count}) 通目 ==========");
            mailLog.WriteLine($"送信日時: {DtOffsetNow._ToDtStr(true)}");
            mailLog.WriteLine($"メールサーバー: {smtpConfig.SmtpServer}:{smtpConfig.SmtpPort}");
            mailLog.WriteLine($"From: {this.Settings.MailFrom}");
            mailLog.WriteLine($"To: {this.Settings.MailTo}");
            mailLog.WriteLine($"Cc: {this.Settings.MailCc}");
            mailLog.WriteLine($"Subject: {subject}");
            mailLog.WriteLine("");
            mailLog.WriteLine("---- 本文 ここから ----");
            mailLog.WriteLine(mailText.ToString());
            mailLog.WriteLine("---- 本文 ここまで ----");
            mailLog.WriteLine("");

            await TaskUtil.RetryAsync(async () =>
            {
                await mail.SendAsync(smtpConfig, cancel);
                return 0;
            },
            1000,
            this.Settings.MailTryCount,
            cancel,
            true);
        }

        mailLog.WriteLine("");

        // メールデータ出力
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"maillog-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), mailLog.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

        StringWriter settingsFileNameTxt = new StringWriter();
        settingsFileNameTxt.NewLine = Str.NewLine_Str_Windows;

        // プロジェクト単位でのログ出力
        foreach (var proj in projectResultsList)
        {
            if (proj.SettingsFiles.Count() >= 1)
            {
                settingsFileNameTxt.WriteLine($"■ {proj.SettingsFiles.First()}");

                foreach (var settingsFile in proj.SettingsFiles)
                {
                    settingsFileNameTxt.WriteLine($"{settingsFile}");
                }
            }

            settingsFileNameTxt.WriteLine();

            StringWriter forProjLogFull = new StringWriter();
            forProjLogFull.NewLine = Str.NewLine_Str_Windows;

            StringWriter forProjLogSameToZip = new StringWriter();
            forProjLogSameToZip.NewLine = Str.NewLine_Str_Windows;

            WriteTo(forProjLogFull, projectResultsList, now, WriteFlags.FullPath, uniqueId, proj);
            WriteTo(forProjLogSameToZip, projectResultsList, now, WriteFlags.None, uniqueId, proj);

            string logBaseDir = this.Fs.PP.Combine(proj.RootDirPath, this.Settings.TimeStampProjectLogDirName);

            try
            {
                // ディレクトリを隠しディレクトリにする
                var dirMeta = await this.Fs.GetDirectoryMetadataAsync(logBaseDir, cancel: cancel);
                dirMeta.Attributes = dirMeta.Attributes.BitAdd(FileAttributes.Hidden);
                await this.Fs.SetDirectoryMetadataAsync(logBaseDir, dirMeta, cancel: cancel);
            }
            catch { }

            Util.PutGitIgnoreFileOnDirectory(logBaseDir, FileFlags.AutoCreateDirectory);

            string logDir = this.Fs.PP.Combine(logBaseDir, $"{yyyymmdd}-{this.Settings.OutputFilenamePrefix}");

            // ハッシュ表本文を出力 (フルパスが入っているもの)
            await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(logDir, $"timestamptxt-fullpath-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forProjLogFull.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

            // ハッシュ表本文を出力 (フルパスが入っていないもの)
            await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(logDir, $"timestamptxt-sametozip-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), forProjLogSameToZip.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);

            // メールデータ出力
            await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(logDir, $"maillog-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), mailLog.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);
        }

        // 設定ファイル一覧出力
        await this.Fs.WriteStringToFileAsync(this.Fs.PP.Combine(entireLogsDirPath, $"configfilelist-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}.txt"), settingsFileNameTxt.ToString(), FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag, writeBom: true);
    }

    void WriteTo(TextWriter allText, List<ProjectResult> projectResultsList, DateTimeOffset now, WriteFlags flags, string uniqueId, ProjectResult? targetProj = null)
    {
        allText.WriteLine($"# タイムスタンプ署名用の署名対象ファイルハッシュ表");
        allText.WriteLine("# ");
        allText.WriteLine($"# 作成日時: {now._ToDtStr(true)}");
        allText.WriteLine($"# 作成コンピュータ: {Env.DnsFqdnHostName}");
        allText.WriteLine($"# ハッシュ計算ジョブユニーク ID: {uniqueId}");
        allText.WriteLine();
        allText.WriteLine();

        for (int i = 0; i < projectResultsList.Count; i++)
        {
            var proj = projectResultsList[i];

            if (targetProj == null || proj == targetProj)
            {
                allText.WriteLine($"★★ プロジェクト ({i + 1}/{projectResultsList.Count}) 『{(flags.Bit(WriteFlags.FullPath) ? proj.RootDirPath : proj.RootDirName)}』");

                proj.WriteTo(allText, now, flags);
            }
        }

        allText.WriteLine();
        allText.WriteLine();
        allText.WriteLine("# 以上");
        allText.WriteLine();
        allText.WriteLine($"# デバッグ情報 (技術者向け): ({new EnvInfoSnapshot()._ObjectToJson(compact: true)})");
        allText.WriteLine($"");
    }

    // config が設定されている 1 つのディレクトリ (ルートから走査していって出現する最初の config が存在する深さ) の処理
    async Task ProcessProjectDirAsync(TimeStampConfig config, DirectoryPathInfo dir, FileSystemEntity[] list, CancellationToken cancel, string rootDirPath, ProjectResult result)
    {
        await ProcessParentManagedSubDirAsync(config, dir.FullPath, cancel, rootDirPath, result);
    }

    async Task ProcessParentManagedSubDirAsync(TimeStampConfig config, string dirPath, CancellationToken cancel, string parentDirPathForFilter, ProjectResult result)
    {
        // フルパスから相対パスに変換
        string relativeDirPath = this.Fs.PathParser.GetRelativeDirectoryName(dirPath, parentDirPathForFilter);

        // 相対パスをスラッシュ区切りに変換
        relativeDirPath = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(relativeDirPath);

        if (relativeDirPath.EndsWith("/") == false) relativeDirPath = relativeDirPath + "/";

        var dirEntityList = await this.Fs.EnumDirectoryAsync(dirPath, false, cancel: cancel);

        DirData? dirData = null;

        // ファイルの処理
        foreach (var fileEntry in dirEntityList.Where(x => x.IsFile).OrderBy(x => x.Name, this.Fs.PathParser.PathStringComparer).Where(x => x.Name._IsDiffi(this.Settings.TimeStampConfigFilename)))
        {
            var fileMetaData = await Fs.GetFileMetadataAsync(fileEntry.FullPath, cancel: cancel);

            if (config.ProcessFilterForFile(fileEntry, fileMetaData))
            {
                try
                {
                    if (result.NumFiles_Inc >= this.Settings.MaxFilesInProject)
                    {
                        // ファイル個数超過 (プロジェクト単位)
                        $"Error: NumFiles ({result.NumFiles_Inc}) >= MaxFilesInProject ({Settings.MaxFilesInProject})"._Error();
                        return;
                    }

                    if (this.TotalNumFiles >= this.Settings.MaxFilesTotal)
                    {
                        // ファイル個数超過 (全体)
                        $"Error: TotalNumFiles ({TotalNumFiles}) >= MaxFilesTotal ({Settings.MaxFilesTotal})"._Error();
                        return;
                    }

                    //// フルパスから相対パスに変換
                    string relativeFilePath = this.Fs.PathParser.GetRelativeFileName(fileEntry.FullPath, parentDirPathForFilter);

                    //// 相対パスをスラッシュ区切りに変換
                    relativeFilePath = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(relativeFilePath);

                    if (dirData == null)
                    {
                        dirData = new DirData
                        {
                            FullPath = dirPath,
                            RelativePath = relativeDirPath,
                        };

                        result.DirList.Add(dirData);
                    }

                    var fileData = new FileData
                    {
                        DateCreated = fileMetaData.CreationTime.GetValueOrDefault(DtOffsetZero),
                        DateLastWrite = fileMetaData.LastWriteTime.GetValueOrDefault(DtOffsetZero),
                        FullPath = fileEntry.FullPath,
                        RelativePath = relativeFilePath,
                    };

                    RefLong size = new();
                    fileData.Md5 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, MD5.Create(), cancel: cancel, totalReadSize: size))._GetHexString();
                    fileData.Sha512 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, SHA512.Create(), cancel: cancel))._GetHexString();
                    fileData.FileSize = size;

                    dirData.FileList.Add(fileData);
                    this.TotalNumFiles++;
                    result.NumFiles_Inc++;
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }
        }

        // サブディレクトリの処理
        foreach (var subDirEntry in dirEntityList.Where(x => x.IsDirectory && x.IsSymbolicLink == false && x.IsCurrentOrParentDirectory == false).OrderBy(x => x.Name, this.Fs.PathParser.PathStringComparer))
        {
            try
            {
                if (result.NumFiles_Inc >= this.Settings.MaxFilesInProject)
                {
                    // ファイル個数超過 (プロジェクト単位)
                    return;
                }

                if (this.TotalNumFiles >= this.Settings.MaxFilesTotal)
                {
                    // ファイル個数超過 (全体)
                    return;
                }

                if (config.ProcessFilterForDirectory(subDirEntry.Name))
                {
                    // このサブディレクトリに独自の Config はあるか?
                    string configPath = this.Fs.PathParser.Combine(subDirEntry.FullPath, this.Settings.TimeStampConfigFilename);
                    var subDirConfig = await this.ParseConfigAsync(configPath, cancel);

                    if (subDirConfig != null)
                    {
                        // 独自の Config がある場合は、一階層上の Config を承継した上で必要に応じて上書きをした新たな Config を作る
                        result.SettingsFiles.Add(configPath);

                        subDirConfig = config.MergeConfig(subDirConfig);
                    }
                    else
                    {
                        // 独自の Config がなければ、親 Config のうちディレクトリに関する設定を除いたフィルタをそのまま利用する
                        subDirConfig = config.MergeConfig(null);
                    }

                    // 再帰
                    await ProcessParentManagedSubDirAsync(subDirConfig, subDirEntry.FullPath, cancel, parentDirPathForFilter, result);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await Task.CompletedTask;

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

}





#endif

