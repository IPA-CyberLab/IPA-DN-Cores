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

namespace IPA.Cores.Basic;

// タイムスタンプ・ユーティリティの設定
public class TimeStampDocsSetting : INormalizable
{
    public SmtpClientSettings SmtpSettings = new SmtpClientSettings();

    public string MailZipPassword = "";

    public string TimeStampConfigFilename = "";

    public int MaxFilesInProject = 0;
    public int MaxFilesTotal = 0;

    public int SingleMailZipFileSize = 30000;//7140000;

    public void Normalize()
    {
        if (this.MailZipPassword._IsEmpty()) this.MailZipPassword = "password";

        if (this.SmtpSettings == null) this.SmtpSettings = new SmtpClientSettings();
        this.SmtpSettings.Normalize();

        if (this.TimeStampConfigFilename._IsEmpty()) this.TimeStampConfigFilename = "_timestamp.txt";

        if (this.MaxFilesInProject <= 0) this.MaxFilesInProject = 10_0000;
        if (this.MaxFilesTotal <= 0) this.MaxFilesTotal = 100_0000;
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

    public class TimeStampFileFilter
    {
        public TimeStampFileFilterAction Action = TimeStampFileFilterAction.Include;
        public TimeStampFileFilterType Type = TimeStampFileFilterType.ForFile;
        public string Wildcard = "";
    }

    public class DirData
    {
        public string DirPath = "";

        public List<FileData> FileList = new List<FileData>();
    }

    public class FileData
    {
        public string FilePath = "";
        public long FileSize = 0;
        public DateTimeOffset DateCreated;
        public DateTimeOffset DateLastWrite;
        public string Md5 = "";
        public string Sha512 = "";
    }

    public class ProjectResult
    {
        public int NumFiles_Inc;

        public List<DirData> DirList = new List<DirData>();
        public string RootDirPath = "";

        public int NumFiles => this.DirList.Sum(x => x.FileList.Count);
        public long TotalFileSize => this.DirList.Sum(x => x.FileList.Sum(y => y.FileSize));
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
                if (line._GetKeyAndValue(out string key, out string value, ":") && value._IsFilled())
                {
                    key = key.Trim();
                    value = value.Trim();
                    if (key._IsFilled() && value._IsFilled())
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
                    ret.Settings[kv.Key] = kv.Value;
                }
            }

            return ret;
        }

        public bool ProcessFilter(string name, bool isDirectory)
        {
            foreach (var f in this.Filters.Where(x => x.Type == (isDirectory ? TimeStampFileFilterType.ForDir : TimeStampFileFilterType.ForFile)))
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
            this.SettingsHive = new HiveData<TimeStampDocsSetting>(Hive.SharedLocalConfigHive, $"TimeStampDocsSetting", null, HiveSyncPolicy.ReadOnly);
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
                    };

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

        projectResultsList._PrintAsJson();
    }

    // config が設定されている 1 つのディレクトリ (ルートから走査していって出現する最初の config が存在する深さ) の処理
    async Task ProcessProjectDirAsync(TimeStampConfig config, DirectoryPathInfo dir, FileSystemEntity[] list, CancellationToken cancel, string rootDirPath, ProjectResult result)
    {
        await ProcessParentManagedSubDirAsync(config, dir.FullPath, cancel, rootDirPath, result);
    }

    async Task ProcessParentManagedSubDirAsync(TimeStampConfig config, string dirPath, CancellationToken cancel, string parentDirPathForFilter, ProjectResult result)
    {
        var dirEntityList = await this.Fs.EnumDirectoryAsync(dirPath, false, cancel: cancel);

        DirData? dirData = null;

        // ファイルの処理
        foreach (var fileEntry in dirEntityList.Where(x => x.IsFile).OrderBy(x => x.Name, this.Fs.PathParser.PathStringComparer).Where(x => x.Name._IsDiffi(this.Settings.TimeStampConfigFilename)))
        {
            if (config.ProcessFilter(fileEntry.Name, false))
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
                    //string relativeFilePath = this.Fs.PathParser.GetRelativeFileName(fileEntry.FullPath, parentDirPathForFilter);

                    //// 相対パスをスラッシュ区切りに変換
                    //relativeFilePath = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(relativeFilePath);

                    if (dirData == null)
                    {
                        dirData = new DirData
                        {
                            DirPath = dirPath,
                        };

                        result.DirList.Add(dirData);
                    }

                    var fileMetaData = await Fs.GetFileMetadataAsync(fileEntry.FullPath, cancel: cancel);

                    var fileData = new FileData
                    {
                        DateCreated = fileMetaData.CreationTime.GetValueOrDefault(DtOffsetZero),
                        DateLastWrite = fileMetaData.LastWriteTime.GetValueOrDefault(DtOffsetZero),
                        FilePath = fileEntry.FullPath,
                    };

                    RefLong size = new();
                    fileData.Md5 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, MD5.Create(), cancel: cancel, totalReadSize: size))._GetHexString();
                    fileData.Sha512 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, SHA512.Create(), cancel: cancel))._GetHexString();
                    fileData.FileSize = size;

                    dirData.FileList.Add(fileData);
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

                // フルパスから相対パスに変換
                string relativeFilePath = this.Fs.PathParser.GetRelativeDirectoryName(subDirEntry.FullPath, parentDirPathForFilter);

                // 相対パスをスラッシュ区切りに変換
                relativeFilePath = PPMac.NormalizeDirectorySeparatorIncludeWindowsBackslash(relativeFilePath);

                if (relativeFilePath.EndsWith("/") == false) relativeFilePath = relativeFilePath + "/";

                if (config.ProcessFilter(subDirEntry.Name, true))
                {
                    // このサブディレクトリに独自の Config はあるか?
                    var subDirConfig = await this.ParseConfigAsync(this.Fs.PathParser.Combine(subDirEntry.FullPath, this.Settings.TimeStampConfigFilename), cancel);

                    if (subDirConfig != null)
                    {
                        // 独自の Config がある場合は、一階層上の Config を承継した上で必要に応じて上書きをした新たな Config を作る
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

