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
using System.Net.Mail;
using System.Security.Cryptography;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Castle.Core.Logging;
using Microsoft.Extensions.Options;
using System.Xml;

using IPA.Cores.Basic.Legacy;

using HtmlAgilityPack;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DefaultExpandIncludesSettings
    {
        public static readonly Copenhagen<int> WebTimeoutMsecs = 5 * 1000;
        public static readonly Copenhagen<int> WebTryCount = 2;
        public static readonly Copenhagen<int> WebRetryIntervalMsecs = 100;
    }
}




// 動画データ変換整理ユーティリティの設定
public class MovYaiUtilSettings : IValidatable, INormalizable
{
    public string FfMpegExePath = "";
    public string SrcDir = "";
    public string DestDir = "";
    public string SrcExtList = "";
    public string SeriesStr = "";
    public double MaxVolume = 0.0;
    public bool Overwrite = false;
    public string DestFormatExt = ".mkv";

    public int Hash_MaxPrefixDirLevel1Int = 20;
    public int Hash_MaxPrefixDirLevel2Int = 99999999;
    public string Hash_DelimiterStr = "-";
    public int Hash_InsertStrPositionOnLevel2 = 4;

    public void Validate()
    {
        FfMpegExePath._NotEmptyCheck(nameof(FfMpegExePath));
        SrcDir._NotEmptyCheck(nameof(SrcDir));
        DestDir._NotEmptyCheck(nameof(DestDir));
    }

    public void Normalize()
    {
        if (this.SeriesStr._IsEmpty())
        {
            this.SeriesStr = "Unknown";
        }
        if (this.SrcExtList._IsEmpty())
        {
            this.SrcExtList = ".mp4 .avi .mkv .m4v";
        }
        if (this.Hash_MaxPrefixDirLevel1Int <= 0)
        {
            this.Hash_MaxPrefixDirLevel1Int = 20;
        }
        if (this.Hash_MaxPrefixDirLevel2Int <= 0)
        {
            this.Hash_MaxPrefixDirLevel2Int = 99999999;
        }
        if (this.Hash_DelimiterStr._IsNullOrZeroLen())
        {
            this.Hash_DelimiterStr = "-";
        }
        if (this.Hash_InsertStrPositionOnLevel2 <= 0)
        {
            this.Hash_InsertStrPositionOnLevel2 = 4;
        }
        if (this.DestFormatExt._IsEmpty())
        {
            this.DestFormatExt = ".mkv";
        }
        if (this.DestFormatExt.StartsWith(".") == false)
        {
            this.DestFormatExt = "." + this.DestFormatExt;
        }
    }
}

// 動画データ変換整理ユーティリティ
public class MovYaiUtil
{
    MovYaiUtilSettings Settings;

    public MovYaiUtil(MovYaiUtilSettings settings)
    {
        this.Settings = settings._CloneDeep();
        this.Settings.Normalize();
        this.Settings.Validate();
    }

    public static string GenerateFilePrefixStr(byte[] fileHashSha1, int maxLevel1Number, long maxLevel2Number, string delimiterStr = "-", int insertStrPositionOnLevel2 = 4)
    {
        maxLevel1Number += 1;
        maxLevel2Number += 1;
        if (fileHashSha1.Length != Secure.SHA1Size)
        {
            throw new ArgumentException(nameof(fileHashSha1));
        }
        if (maxLevel1Number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLevel1Number));
        }

        SeedBasedRandomGenerator rand = new SeedBasedRandomGenerator(fileHashSha1, SHA1.Create());

        long v1 = rand.GetSInt63();

        int n1 = (int)(v1 % maxLevel1Number);

        string level1Str = n1.ToString();
        int maxLevel1Len = (maxLevel1Number - 1).ToString().Length;
        int level1PadSize = maxLevel1Len - level1Str.Length;
        level1Str = Str.MakeCharArray('0', level1PadSize) + level1Str;

        long v2 = rand.GetSInt63();
        long n2 = v2 % maxLevel2Number;

        string level2Str = n2.ToString();
        int maxLevel2Len = (maxLevel2Number - 1).ToString().Length;
        int level2PadSize = maxLevel2Len - level2Str.Length;
        level2Str = Str.MakeCharArray('0', level2PadSize) + level2Str;

        level2Str = level2Str._InsertStrIntoStr(delimiterStr, insertStrPositionOnLevel2);

        return level1Str + "-" + level2Str;
    }

    public async Task ExecAsync(CancellationToken cancel = default)
    {
        var encoding = Str.Utf8Encoding;

        // 元ディレクトリのファイルを列挙
        var items = await Lfs.EnumDirectoryAsync(Settings.SrcDir, recursive: true, cancel: cancel);

        // 指定された拡張子リストに一致するファイル一覧を取得
        var srcFiles = items.Where(x => x.IsFile && PP.GetExtension(x.FullPath)._IsFilled() && x.FullPath._IsExtensionMatch(Settings.SrcExtList)).OrderBy(x => x.FullPath, StrComparer.Get(StringComparison.CurrentCultureIgnoreCase));

        int counter = 0;

        using var sha1Algorithm = SHA1.Create();

        foreach (var srcFile in srcFiles)
        {
            try
            {
                string srcRelativePath = PP.GetRelativeFileName(srcFile.FullPath, Settings.SrcDir);
                $"Loading '{srcRelativePath}' ({counter + 1} / {srcFiles.Count()}) ..."._Print();

                byte[] fileHash = await Lfs.CalcFileHashAsync(srcFile.FullPath, sha1Algorithm, cancel: cancel);
                //byte[] fileHash = Secure.HashSHA1("Hello"._GetBytes_Ascii());

                string fileHashStr = GenerateFilePrefixStr(fileHash, Settings.Hash_MaxPrefixDirLevel1Int, Settings.Hash_MaxPrefixDirLevel2Int, Settings.Hash_DelimiterStr, Settings.Hash_InsertStrPositionOnLevel2);

                int fileHashInt = fileHashStr._ReplaceStr(Settings.Hash_DelimiterStr, "")._ToInt();

                // 出力先ディレクトリに、fileHashStr を含み、かつ本体および .ok.txt で終わるファイルが存在するかどうか検査
                try
                {
                    await Lfs.CreateDirectoryAsync(Settings.DestDir, cancel: cancel);
                }
                catch { }

                var dstOkFilesExists = await Lfs.EnumDirectoryAsync(Settings.DestDir, true, wildcard: $"*{fileHashStr}*.ok.txt", cancel: cancel);
                var dstMovFilesExists = await Lfs.EnumDirectoryAsync(Settings.DestDir, true, wildcard: $"*{fileHashStr}*{Settings.DestFormatExt}", cancel: cancel);
                if (dstOkFilesExists.Any() && dstMovFilesExists.Any())
                {
                    // すでに対象ファイルが存在するので何もしない
                    $"  Skip. Already exists."._Print();
                }
                else
                {
                    // 出力先フルパスを決定
                    string fileHashStrFirstToken = fileHashStr._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, Settings.Hash_DelimiterStr)[0];
                    string destRelativePath = fileHashStrFirstToken + Lfs.PathParser.DirectorySeparator + fileHashStr + " " + Lfs.PathParser.GetFileNameWithoutExtension(srcFile.Name)._NormalizeSoftEther(true) + Settings.DestFormatExt;

                    destRelativePath._Print();

                    List<string> audioFilters = new List<string>();
                    
                    // 現在の max_volume 値を取得
                    var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, $"-i \"{srcFile.FullPath._RemoveQuotation()}\" -vn -af volumedetect -f null -", PP.GetDirectoryName(Settings.FfMpegExePath),
                        flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
                        timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
                        inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

                    double currentMaxVolume = double.NaN;

                    foreach (var line in result.OutputAndErrorStr._GetLines())
                    {
                        string tag = "max_volume:";
                        int a = line._Search(tag, 0, true);
                        if (a != -1)
                        {
                            string tmp = line.Substring(a + tag.Length);
                            if (tmp.EndsWith(" dB"))
                            {
                                tmp = tmp.Substring(0, tmp.Length - 3);
                                tmp = tmp.Trim();

                                currentMaxVolume = tmp._ToDouble();
                            }
                        }
                    }

                    if (currentMaxVolume != double.NaN && currentMaxVolume < Settings.MaxVolume)
                    {
                        // 何 dB 上げるべきか計算
                        double addVolume = Settings.MaxVolume - currentMaxVolume;

                        audioFilters.Add($"volume={addVolume:F1}dB");
                    }

                    ("********** " + audioFilters._Combine(" / "))._Print();

                    string artist = Settings.SeriesStr._NormalizeSoftEther();

                    string destFullPath = Lfs.PP.Combine(Settings.DestDir, destRelativePath);

                    string audioFilterArgs = " ";
                    if (audioFilters.Any())
                    {
                        audioFilterArgs = $"-af \"{audioFilters._Combine(" , ")}\"";
                    }

                    // 動画を変換
                    await ProcessOneFileAsync(srcFile.FullPath, destFullPath, $"-crf 18 {audioFilterArgs}",
                        artist,
                        artist,
                        Settings.SeriesStr + " - " + fileHashStr + " " + Lfs.PathParser.GetFileNameWithoutExtension(srcFile.Name)._NormalizeSoftEther(true),
                        fileHashInt, int.MaxValue,
                        encoding, cancel);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            counter++;
        }
    }

    async Task ProcessOneFileAsync(string srcPath, string dstPath, string args, string artist, string album, string title, int track, int maxTracks, Encoding encoding, CancellationToken cancel = default)
    {
        var now = DtOffsetNow;
        string okTxtPath = PP.Combine(PP.GetDirectoryName(dstPath), ".okfiles", PP.GetFileName(dstPath)) + ".ok.txt";
        string okTxtDir = PP.GetDirectoryName(okTxtPath);

        try
        {
            await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(dstPath), cancel: cancel);
        }
        catch { }

        if (Settings.Overwrite == false)
        {
            // 宛先パス + .ok.txt ファイルが存在していれば何もしない
            if (await Lfs.IsFileExistsAsync(okTxtPath, cancel) && await Lfs.IsFileExistsAsync(dstPath, cancel))
            {
                return;
            }
        }

        // 変換を実施
        string cmdLine = $"-y -i \"{srcPath._RemoveQuotation()}\" {args} -metadata title=\"{title}\" -metadata album=\"{album}\" -metadata artist=\"{artist}\" -metadata track=\"{track}/{maxTracks}\" \"{dstPath._RemoveQuotation()}\"";

        (" * cmdline = " + cmdLine)._Print();

        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, cmdLine, PP.GetDirectoryName(Settings.FfMpegExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

        // 成功したら ok.txt を保存
        string okTxtBody = now._ToDtStr(true, DtStrOption.All, true) + "\r\n\r\n" + cmdLine + "\r\n\r\n" + result.ErrorAndOutputStr;
        try
        {
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, 0, FileAttributes.Hidden, cancel: cancel);
            await Lfs.WriteStringToFileAsync(okTxtPath, okTxtBody._NormalizeCrlf(CrlfStyle.CrLf, true), writeBom: true, flags: FileFlags.AutoCreateDirectory, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingDirAsync(okTxtDir, FileAttributes.Hidden, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, FileAttributes.Hidden, cancel: cancel);
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }
}




// e ラーニング用画像教材生成ユーティリティの設定
public class MovLearnUtilSettings : IValidatable, INormalizable
{
    public string FfMpegExePath = "";
    public string SrcDir = "";
    public string DestDir = "";
    public string SrcExtList = "";
    public double MaxVolume = 0.0;
    public bool Overwrite = false;

    public void Validate()
    {
        FfMpegExePath._NotEmptyCheck(nameof(FfMpegExePath));
        SrcDir._NotEmptyCheck(nameof(SrcDir));
        DestDir._NotEmptyCheck(nameof(DestDir));
    }

    public void Normalize()
    {
        if (this.SrcExtList._IsEmpty())
        {
            this.SrcExtList = ".mp4";
        }
    }
}

// e ラーニング用教材生成ユーティリティ
public class MovLearnUtil
{
    MovLearnUtilSettings Settings;

    public MovLearnUtil(MovLearnUtilSettings settings)
    {
        this.Settings = settings._CloneDeep();
        this.Settings.Normalize();
        this.Settings.Validate();
    }

    public async Task ExecAsync(CancellationToken cancel = default)
    {
        var encoding = Str.Utf8Encoding;

        // 元ディレクトリのファイルを列挙
        var items = await Lfs.EnumDirectoryAsync(Settings.SrcDir, recursive: true, cancel: cancel);

        // 指定された拡張子リストに一致するファイル一覧を取得
        var srcFiles = items.Where(x => x.IsFile && PP.GetExtension(x.FullPath)._IsFilled() && x.FullPath._IsExtensionMatch(Settings.SrcExtList)).OrderBy(x => x.FullPath, StrComparer.Get(StringComparison.CurrentCultureIgnoreCase));

        int counter = 0;

        foreach (var srcFile in srcFiles)
        {
            try
            {
                string relativeFileName = PP.GetRelativeFileName(srcFile.FullPath, Settings.SrcDir);

                var tokens = PP.SplitTokens(relativeFileName);
                if (tokens.Length == 3)
                {
                    // このファイルと同じディレクトリのファイルを列挙
                    string srcFileDirPath = PP.GetDirectoryName(srcFile.FullPath);
                    var sameDirFiles = srcFiles.Where(x => PP.GetDirectoryName(x.FullPath) == srcFileDirPath).ToArray();

                    int maxTracks = sameDirFiles.Length;
                    int trackNumber = 1;

                    for (int i = 0; i < sameDirFiles.Length; i++)
                    {
                        if (sameDirFiles[i] == srcFile)
                        {
                            trackNumber = (i + 1);
                            break;
                        }
                    }

                    maxTracks = Math.Max(maxTracks, trackNumber);

                    string artist = tokens[0]._NormalizeSoftEther(true);
                    string albumBase = tokens[1]._NormalizeSoftEther(true);
                    string titleBase = PP.GetFileNameWithoutExtension(tokens[2]._NormalizeSoftEther(true));

                    artist = Str.MakeSafePathNameShiftJis(artist).Replace("\"", "_").Replace("\'", "_");
                    albumBase = Str.MakeSafePathNameShiftJis(albumBase).Replace("\"", "_").Replace("\'", "_");
                    titleBase = Str.MakeSafePathNameShiftJis(titleBase).Replace("\"", "_").Replace("\'", "_");

                    string fn = Str.MakeSafePathNameShiftJis(PP.GetFileNameWithoutExtension(srcFile.FullPath)).Replace("\"", "_").Replace("\'", "_")._NormalizeSoftEther(true);

                    string albumSimple = albumBase._FilledOrDefault(fn);

                    artist = artist._FilledOrDefault(fn);
                    albumBase = artist + " - " + albumBase._FilledOrDefault(fn);
                    titleBase = titleBase._FilledOrDefault(fn);

                    string relativeDirName = PP.GetDirectoryName(relativeFileName);
                    string destDirPath = PP.Combine(Settings.DestDir, relativeDirName);
                    string srcFileMain = PP.GetFileNameWithoutExtension(srcFile.FullPath);

                    $"Processing '{relativeFileName}' ({counter + 1} / {srcFiles.Count()}) ..."._Print();

                    if (true)
                    {
                        // 1. まず、音声ファイル群の生成
                        List<string> audioFilters = new List<string>();

                        // 1.1. ベースの x1.0 ファイルの生成

                        // 現在の max_volume 値を取得
                        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, $"-i \"{srcFile.FullPath._RemoveQuotation()}\" -vn -af volumedetect -f null -", PP.GetDirectoryName(Settings.FfMpegExePath),
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
                            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
                            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

                        double currentMaxVolume = double.NaN;

                        foreach (var line in result.OutputAndErrorStr._GetLines())
                        {
                            string tag = "max_volume:";
                            int a = line._Search(tag, 0, true);
                            if (a != -1)
                            {
                                string tmp = line.Substring(a + tag.Length);
                                if (tmp.EndsWith(" dB"))
                                {
                                    tmp = tmp.Substring(0, tmp.Length - 3);
                                    tmp = tmp.Trim();

                                    currentMaxVolume = tmp._ToDouble();
                                }
                            }
                        }

                        if (currentMaxVolume != double.NaN && currentMaxVolume < Settings.MaxVolume)
                        {
                            // 何 dB 上げるべきか計算
                            double addVolume = Settings.MaxVolume - currentMaxVolume;

                            audioFilters.Add($"volume={addVolume:F1}dB");
                        }

                        // 無音除去を実施、音量調整も実施
                        string audio_base_path = PP.Combine(destDirPath, albumBase + $" - audio.x1.0", $"{albumSimple} [{trackNumber:D2}] {titleBase} - audio.x1.0.mp3");
                        audioFilters.Add($"silenceremove=window=5:detection=peak:stop_mode=all:start_mode=all:stop_periods=-1:stop_threshold=-30dB");
                        await ProcessOneFileAsync(srcFile.FullPath, audio_base_path, $"-vn -f mp3 -ab 192k -af \"{audioFilters._Combine(" , ")}\"",
                            artist + $" - audio.x1.0",
                            albumBase + " - audio.x1.0",
                            albumSimple + $" [{trackNumber:D2}] - " + titleBase + " - audio.x1.0",
                            trackNumber, maxTracks,
                            encoding, cancel);

                        // 2.2. 数倍速再生版も作る
                        string[] xList = { "1.5", "2.0", "2.5", "3.0", "3.5" };

                        foreach (var xstr in xList)
                        {
                            string audio_x_path = PP.Combine(destDirPath, albumBase + $" - audio.x{xstr}", $"{albumSimple} [{trackNumber:D2}] {titleBase} - audio.x{xstr}.mp3");
                            await ProcessOneFileAsync(audio_base_path, audio_x_path, $"-vn -f mp3 -ab 192k -af atempo={xstr}",
                                artist + $" - audio.x{xstr}",
                                albumBase + $" - audio.x{xstr}",
                                albumSimple + $" [{trackNumber:D2}] - " + titleBase + $" - audio.x{xstr}",
                                trackNumber, maxTracks,
                                encoding, cancel);
                        }
                    }

                    if (true)
                    {
                        // 2. 次に、動画ファイル群の生成
                        List<string> audioFilters = new List<string>();

                        // 2.1. ベースの x1.0 ファイルの生成

                        // 現在の max_volume 値を取得
                        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, $"-i \"{srcFile.FullPath._RemoveQuotation()}\" -vn -af volumedetect -f null -", PP.GetDirectoryName(Settings.FfMpegExePath),
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
                            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
                            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

                        double currentMaxVolume = double.NaN;

                        foreach (var line in result.OutputAndErrorStr._GetLines())
                        {
                            string tag = "max_volume:";
                            int a = line._Search(tag, 0, true);
                            if (a != -1)
                            {
                                string tmp = line.Substring(a + tag.Length);
                                if (tmp.EndsWith(" dB"))
                                {
                                    tmp = tmp.Substring(0, tmp.Length - 3);
                                    tmp = tmp.Trim();

                                    currentMaxVolume = tmp._ToDouble();
                                }
                            }
                        }

                        if (currentMaxVolume != double.NaN && currentMaxVolume < Settings.MaxVolume)
                        {
                            // 何 dB 上げるべきか計算
                            double addVolume = Settings.MaxVolume - currentMaxVolume;

                            audioFilters.Add($"volume={addVolume:F1}dB");
                        }
                        else
                        {
                            audioFilters.Add($"volume=0.0dB");
                        }

                        // 無音除去を実施、音量調整実施
                        string video_base_path = PP.Combine(destDirPath, albumBase + $" - video.x1.0", $"{albumSimple} [{trackNumber:D2}] {titleBase} - video.x1.0.mp4");
                        await ProcessOneFileAsync(srcFile.FullPath, video_base_path, $"-af \"{audioFilters._Combine(" , ")}\"",
                            artist + $" - video.x1.0",
                            albumBase + " - video.x1.0",
                            albumSimple + $" [{trackNumber:D2}] - " + titleBase + " - video.x1.0",
                            trackNumber, maxTracks,
                            encoding, cancel);

                        // 2.2. 数倍速再生版も作る
                        string[] xList = { "1.5", "2.0", "2.5", "3.0", "3.5" };

                        foreach (var xstr in xList)
                        {
                            string video_x_path = PP.Combine(destDirPath, albumBase + $" - video.x{xstr}", $"{albumSimple} [{trackNumber:D2}] {titleBase} - video.x{xstr}.mp4");
                            await ProcessOneFileAsync(video_base_path, video_x_path, $"-vf setpts=PTS/{xstr} -af atempo={xstr}",
                                artist + $" - video.x{xstr}",
                                albumBase + $" - video.x{xstr}",
                                albumSimple + $" [{trackNumber:D2}] - " + titleBase + $" - video.x{xstr}",
                                trackNumber, maxTracks,
                                encoding, cancel);
                        }
                    }
                }
                else
                {
                    $"Skip: '{relativeFileName}' ({counter + 1} / {srcFiles.Count()})"._Print();
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            counter++;
        }
    }

    async Task ProcessOneFileAsync(string srcPath, string dstPath, string args, string artist, string album, string title, int track, int maxTracks, Encoding encoding, CancellationToken cancel = default)
    {
        var now = DtOffsetNow;
        string okTxtPath = PP.Combine(PP.GetDirectoryName(dstPath), ".okfiles", PP.GetFileName(dstPath)) + ".ok.txt";
        string okTxtDir = PP.GetDirectoryName(okTxtPath);

        try
        {
            await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(dstPath), cancel: cancel);
        }
        catch { }

        if (Settings.Overwrite == false)
        {
            // 宛先パス + .ok.txt ファイルが存在していれば何もしない
            if (await Lfs.IsFileExistsAsync(okTxtPath, cancel))
            {
                return;
            }
        }

        // 変換を実施
        string cmdLine = $"-y -i \"{srcPath._RemoveQuotation()}\" {args} -metadata title=\"{title}\" -metadata album=\"{album}\" -metadata artist=\"{artist}\" -metadata track=\"{track}/{maxTracks}\" \"{dstPath._RemoveQuotation()}\"";

        var result = await EasyExec.ExecAsync(Settings.FfMpegExePath, cmdLine, PP.GetDirectoryName(Settings.FfMpegExePath),
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: Timeout.Infinite, cancel: cancel, throwOnErrorExitCode: true,
            inputEncoding: encoding, outputEncoding: encoding, errorEncoding: encoding);

        // 成功したら ok.txt を保存
        string okTxtBody = now._ToDtStr(true, DtStrOption.All, true) + "\r\n\r\n" + cmdLine + "\r\n\r\n" + result.ErrorAndOutputStr;
        try
        {
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, 0, FileAttributes.Hidden, cancel: cancel);
            await Lfs.WriteStringToFileAsync(okTxtPath, okTxtBody._NormalizeCrlf(CrlfStyle.CrLf, true), writeBom: true, flags: FileFlags.AutoCreateDirectory, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingDirAsync(okTxtDir, FileAttributes.Hidden, cancel: cancel);
            await Lfs.TryAddOrRemoveAttributeFromExistingFileAsync(okTxtPath, FileAttributes.Hidden, cancel: cancel);
        }
        catch (Exception ex)
        {
            ex._Error();
        }
    }
}

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

        //// テスト用に増幅
        //if (false)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    for (int i = 0; i < 250; i++)
        //    {
        //        sb.Append(forZipTextBody);
        //    }
        //    forZipTextBody = sb.ToString();
        //}

        var zipDataList = FileUtil.CompressTextToZipFilesSplittedWithMinSize(forZipTextBody, this.Settings.SingleMailZipFileSize, $"timestamptxt-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}", this.Settings.MailZipPassword, ".txt");

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

        CoresLib.Report_SimpleResult = $"Projects: {projectResultsList.Count}, NumFiles: {TotalNumFiles._ToString3()}, ZIPs: {zipDataList.Count} (Total: {zipDataList.Sum(x => x.Length)._GetFileSizeStr()}), UID: {uniqueId}";

        Con.WriteLine($"Unique ID: {uniqueId}");

        for (int i = 0; i < zipDataList.Count; i++)
        {
            var zipData = zipDataList[i];

            string zipFnSimple = $"timestampzip-{yyyymmdd}-{this.Settings.OutputFilenamePrefix}-{(i + 1):D3}.zip";
            string zipFn = this.Fs.PP.Combine(entireLogsDirPath, zipFnSimple);
            string zipPwFn = zipFn + ".password.txt";

            // 分割された ZIP ファイル本体をファイルに保存
            //Dbg.Where(zipFn);
            await this.Fs.WriteDataToFileAsync(zipFn, zipData, flags: FileFlags.AutoCreateDirectory);

            await this.Fs.WriteStringToFileAsync(zipPwFn, this.Settings.MailZipPassword + "\r\n", flags: FileFlags.AutoCreateDirectory);

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
            mailText.WriteLine($"この ZIP ファイルの MD5 ハッシュ値: {Secure.HashMD5(zipData.Span)._GetHexString().ToLowerInvariant()}");
            mailText.WriteLine($"この ZIP ファイルの SHA256 ハッシュ値: {Secure.HashSHA256(zipData.Span)._GetHexString().ToLowerInvariant()}");
            mailText.WriteLine($"この ZIP ファイルの SHA512 ハッシュ値: {Secure.HashSHA512(zipData.Span)._GetHexString().ToLowerInvariant()}");
            mailText.WriteLine();
            mailText.WriteLine($"このメールを含めて、合計 {zipDataList.Count} 通のメールが送付されています。");
            mailText.WriteLine($"これらのメールにそれぞれ添付されている合計 {zipDataList.Count} 個の ZIP ファイルに分割されています。");
            mailText.WriteLine($"合計 ZIP ファイルサイズ: {zipDataList.Sum(x => x.Length)._ToString3()} bytes ({zipDataList.Sum(x => x.Length)._GetFileSizeStr()})");
            mailText.WriteLine();
            mailText.WriteLine($"これらの ZIP ファイルは、タイムスタンプ署名が施されることになります。");
            mailText.WriteLine($"しばらくすると、署名サービスにより、ZIP ファイルのハッシュ値 (上記) を記載した PDF ファイルにタイムスタンプ署名が施されたものが、返信メールとして送り返されてきます。");
            mailText.WriteLine("送り返されてきたタイムスタンプ署名が施されたメールは、このメール本体とともに、長期間保存しましょう。");
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
            mailLog.WriteLine($"Body size: {mailText.ToString().Length._ToString3()} characters");
            mailLog.WriteLine("");
            mailLog.WriteLine("---- 本文 ここから ----");
            mailLog.WriteLine(mailText.ToString());
            mailLog.WriteLine("---- 本文 ここまで ----");
            mailLog.WriteLine("");

            await TaskUtil.RetryAsync(async () =>
            {
                try
                {
                    Con.WriteLine($"Sending email from {this.Settings.MailFrom} to {this.Settings.MailTo} (subject: {subject}, length = {mailText.ToString().Length._ToString3()} characters)");
                    await mail.SendAsync(smtpConfig, cancel);
                    Con.WriteLine($"Ok.");
                }
                catch (Exception ex)
                {
                    ex._Error();
                    throw;
                }
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
                        $"Error: NumFiles ({result.NumFiles_Inc}) >= MaxFilesInProject ({Settings.MaxFilesInProject}). Project: \"{result.RootDirPath}\", FilePath: \"{fileEntry.FullPath}\""._Error();
                        return;
                    }

                    if (this.TotalNumFiles >= this.Settings.MaxFilesTotal)
                    {
                        // ファイル個数超過 (全体)
                        $"Error: TotalNumFiles ({TotalNumFiles}) >= MaxFilesTotal ({Settings.MaxFilesTotal}). Project: \"{result.RootDirPath}\", FilePath: \"{fileEntry.FullPath}\""._Error();
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
                    fileData.Md5 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, MD5.Create(), flags: FileFlags.BackupMode, cancel: cancel, totalReadSize: size))._GetHexString();
                    fileData.Sha512 = (await Fs.CalcFileHashAsync(fileEntry.FullPath, SHA512.Create(), flags: FileFlags.BackupMode, cancel: cancel))._GetHexString();
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

                if (subDirEntry.Name._IsDiffi(this.Settings.TimeStampProjectLogDirName))
                {
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

public class ExpandIncludesSettings
{
    public int MaxIncludes { init; get; } = 16;
    public CachedDownloaderSettings DownloaderSettings { init; get; } = new CachedDownloaderSettings();
    public bool AllowIncludeLocalFileByLocalFile { init; get; } = true;
    public bool AllowIncludeLocalFileByNetworkFile { init; get; } = false;
    public Encoding? Encoding { init; get; } = null;
    public int MaxReadFileSize { init; get; } = Consts.MaxLens.MaxCachedFileDownloadSizeDefault;
}

// 色々なおまけユーティリティ
public static partial class MiscUtil
{
    public static async Task HttpFileSpiderAsync(string baseUrl, FileDownloadOption? options = null, CancellationToken cancel = default)
    {
        options ??= new FileDownloadOption();

        List<Task> tasksList = new List<Task>();

        await using var cancelWatcher = new CancelWatcher(cancel);

        Memory<byte> tmpbuf = new byte[options.BufferSize];

        for (int i = 0; i < options.MaxConcurrentThreads; i++)
        {
            var task = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                while (true)
                {
                    try
                    {
                        await PerformUrlAsync(baseUrl, cancelWatcher);
                    }
                    catch (Exception ex)
                    {
                        ex._Error();
                    }

                    await Task.Delay(100, cancelWatcher);

                    // 1 つの指定された URL へアクセスする。アクセス先の URL からファイルを取得する。ファイルの内容が html である場合は、内容をパースしてクロールする。
                    // ファイルの内容が html でない場合は、ファイル本文をすべてダウンロードする。
                    async Task PerformUrlAsync(string url, CancellationToken cancel = default)
                    {
                        url._Print();

                        await using var http = new WebApi(options.WebApiOptions);

                        try
                        {
                            await using var webret = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url, cancel));

                            if (webret.DownloadContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                            {
                                var htmlData = await webret.DownloadStream._ReadToEndAsync(1_000_000, cancel);
                                string htmlStr = htmlData._GetString(untilNullByte: true);
                                var html = htmlStr._ParseHtml();
                                var children = html.DocumentNode.GetAllChildren();
                                var linkTargets = children.Where(x => x.Name._IsSamei("a")).Select(x => x.GetAttributeValue("href", "")).Where(x => x._IsFilled() && x._InStri("?") == false && x._InStri("#") == false).Distinct()._Shuffle();

                                await webret._DisposeSafeAsync();
                                await http._DisposeSafeAsync();

                                foreach (var linkTarget in linkTargets)
                                {
                                    string subUrl = url._CombineUrl(linkTarget).ToString();

                                    if (subUrl.StartsWith(url, StringComparison.OrdinalIgnoreCase) && subUrl.Length > url.Length)
                                    {
                                        await PerformUrlAsync(subUrl, cancel);
                                    }
                                }
                            }
                            else
                            {
                                var st = webret.DownloadStream;
                                while (true)
                                {
                                    if (await st.ReadAsync(tmpbuf, cancelWatcher) <= 0)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Error();
                        }
                    }
                }
            });

            tasksList.Add(task);
        }

        Con.ReadLine("Enter key to abort>");

        cancelWatcher.Cancel();

        foreach (var task in tasksList)
        {
            await task._TryWaitAsync();
        }
    }

    public static async Task ValidateIfMyLocalClockCorrectAsync(CancellationToken cancel = default)
    {
        var res = await CompareLocalClockToInternetServersClockAsync(cancel: cancel);

        res.ThrowIfException();
    }

    public static async Task<OkOrExeption> CompareLocalClockToInternetServersClockAsync(TimeSpan? allowDiff = null, bool ignoreInternetError = false, int commTimeoutMsecs = 10 * 1000, RefBool? internetCommError = null, Func<Task<DateTimeOffset>>? getLocalDtProc = null, CancellationToken cancel = default)
    {
        internetCommError ??= new RefBool();
        allowDiff ??= TimeSpan.FromSeconds(15); // 15 秒以内のずれを許容

        if (getLocalDtProc == null)
        {
            getLocalDtProc = () => DateTimeOffset.Now._TR();
        }

        internetCommError.Set(false);

        List<bool> disableProxyFlags = new();

        disableProxyFlags.Add(false);

        if (Environment.GetEnvironmentVariable("http_proxy")._IsFilled() || Environment.GetEnvironmentVariable("https_proxy")._IsFilled())
        {
            disableProxyFlags.Add(true);
        }

        var urlsList = @"
https://www.google.com/
http://www.google.co.jp/
https://www.yahoo.com/
http://www.yahoo.co.jp/
https://www.yahoo.co.jp/
http://www.youtube.com/
https://www.msn.com/
http://www.msn.co.jp/
https://www.facebook.com/
https://www.twitter.com/
http://www.apple.com/
https://www.apple.com/
"._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, "\r", "\n", " ", "\t", "　", ",", ";").Distinct(StrCmpi);

        // A: URL
        // B: disableProxy フラグ
        List<Pair2<string, bool>> urlsList2 = new List<Pair2<string, bool>>();

        foreach (var urls in urlsList)
        {
            foreach (var disableProxyFlag in disableProxyFlags)
            {
                urlsList2.Add(new(urls, disableProxyFlag));
            }
        }

        // A: サーバーの時刻
        // B: ローカル時刻
        List<Pair3<DateTimeOffset, DateTimeOffset, string>> resultsList = new();

        await TaskUtil.ForEachExAsync(urlsList2, async (url, c) =>
        {
            try
            {
                var dt = await GetCurrentDateTimeFromWebSever(url.A, commTimeoutMsecs, url.B, c);

                var now = await getLocalDtProc();

                if (dt._IsZeroDateTime() == false)
                {
                    lock (resultsList)
                    {
                        resultsList.Add(new(dt, now.ToLocalTime(), url.A));
                    }
                }
            }
            catch { }
        },
        8,
        flags: ForEachExAsyncFlags.None,
        cancel: cancel);

        int okCount = resultsList.Where(x => x.A._IsZeroDateTime() == false).Count();

        //resultsList._PrintAsJson();

        if (okCount == 0)
        {
            internetCommError.Set(true);

            if (ignoreInternetError == false)
            {
                return new CoresException($"Check Local Clock: Failed to connect to any Internet servers.");
            }
            else
            {
                return new OkOrExeption();
            }
        }

        foreach (var item in resultsList.Where(x => x.A._IsZeroDateTime() == false))
        {
            var diff = (item.B - item.A);

            diff = TimeSpan.FromTicks(Math.Abs(diff.Ticks)); // 絶対値

            if (diff <= allowDiff)
            {
                // OK
                return new OkOrExeption();
            }
        }

        var nearestResult = resultsList.Where(x => x.A._IsZeroDateTime() == false).OrderBy(x => Math.Abs((x.B - x.A).Ticks)).First();

        // Error
        return new CoresException($"Local clock is different to Internet clock. Local = {nearestResult.B.ToLocalTime()._ToDtStr(withMSsecs: true)}, Internet = {nearestResult.A.ToLocalTime()._ToDtStr(withMSsecs: true)} (from {nearestResult.C})");
    }

    static async Task<DateTimeOffset> GetCurrentDateTimeFromWebSever(string url, int timeoutMsecs = 10 * 1000, bool disableProxy = false, CancellationToken cancel = default)
    {
        await using var web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, AllowAutoRedirect = false, UseProxy = !disableProxy, DoNotThrowHttpResultError = true, MaxRecvSize = 1_000_000, Timeout = timeoutMsecs }, doNotUseTcpStack: true));

        var ret = await web.SimpleQueryAsync(WebMethods.HEAD, url, cancel);

        return (ret.Headers.Date ?? Util.ZeroDateTimeOffsetValue).ToLocalTime();
    }

    // Web server health check
    public static async Task<OkOrExeption> HttpHealthCheckAsync(string url, int numTry = 3, int timeoutMsecs = 5 * 1000, CancellationToken cancel = default, WebApiOptions? options = null)
    {
        options ??= new WebApiOptions(new WebApiSettings { Timeout = timeoutMsecs, SslAcceptAnyCerts = true }, doNotUseTcpStack: true);

        try
        {
            await RetryHelper.RunAsync(async () =>
            {
                await using WebApi api = new WebApi(options);

                await api.SimpleQueryAsync(WebMethods.GET, url, cancel);
            },
            0, numTry, cancel, false, true);

            return new OkOrExeption();
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // ファイルの #include を展開する
    public static async Task<string> ExpandIncludesToStrAsync(string srcFileBody, FilePath? srcFilePathIfPhysicalFile = null, ExpandIncludesSettings? settings = null, CancellationToken cancel = default, string? newLineStr = null)
    {
        var tmp = await ExpandIncludesToListAsync(srcFileBody, srcFilePathIfPhysicalFile, settings, cancel);

        return tmp._LinesToStr(newLineStr);
    }
    public static async Task<List<string>> ExpandIncludesToListAsync(string srcFileBody, FilePath? srcFilePathIfPhysicalFile = null, ExpandIncludesSettings? settings = null, CancellationToken cancel = default)
    {
        settings ??= new ExpandIncludesSettings
        {
            DownloaderSettings = new CachedDownloaderSettings(flags: CachedDownloaderFlags.None,
                webOptions: new WebApiOptions(new WebApiSettings { Timeout = CoresConfig.DefaultExpandIncludesSettings.WebTimeoutMsecs, MaxRecvSize = Consts.MaxLens.MaxCachedFileDownloadSizeDefault }),
                maxTry: CoresConfig.DefaultExpandIncludesSettings.WebTryCount,
                retryInterval: CoresConfig.DefaultExpandIncludesSettings.WebRetryIntervalMsecs),
        };

        List<string> destLines = new List<string>();

        RefInt counter = new RefInt();

        await ProcessAsync(destLines, srcFileBody, srcFilePathIfPhysicalFile, counter);

        return destLines;

        async Task ProcessAsync(List<string> destLines, string srcFileBody, FilePath? srcFilePathIfPhysicalFile, RefInt includeCount)
        {
            includeCount.Increment();

            var fs = srcFilePathIfPhysicalFile?.FileSystem ?? null;

            var lines = srcFileBody._GetLines();

            foreach (var line in lines)
            {
                string tmp = line.Trim(' ', '　', '\t');
                bool consumed = false;

                if (tmp.StartsWith("#include", StringComparison.InvariantCultureIgnoreCase) && tmp.Length >= 9)
                {
                    if (tmp[8] == ' ' || tmp[8] == '\t' || tmp[8] == '　')
                    {
                        string name = tmp.Substring(9);
                        name = name._RemoveQuotation().Trim();

                        if (name._IsFilled())
                        {
                            if (includeCount > settings!.MaxIncludes)
                            {
                                throw new CoresLibException($"'{line}': includeCount ({includeCount}) > settings.MaxIncludes ({settings.MaxIncludes})");
                            }

                            if (name.StartsWith("http://", StrCmpi) || name.StartsWith("https://", StrCmpi))
                            {
                                // HTTP URL
                                var r = await CachedDownloader.DownloadAsync(name, cancel, settings!.DownloaderSettings);

                                var encoding = settings.Encoding;

                                string targetBody;

                                if (encoding == null)
                                    targetBody = Str.DecodeStringAutoDetect(r.Data.Span, out _);
                                else
                                    targetBody = Str.DecodeString(r.Data.Span, encoding, out _);

                                await ProcessAsync(destLines, targetBody, null, includeCount);
                            }
                            else
                            {
                                // ローカルファイル
                                if (fs == null)
                                {
                                    throw new CoresLibException($"'{line}': Current file is not a local file.");
                                }

                                if (settings!.AllowIncludeLocalFileByNetworkFile == false && srcFilePathIfPhysicalFile == null)
                                {
                                    throw new CoresLibException($"'{line}': AllowIncludeLocalFileByNetworkFile == false");
                                }

                                name = fs.PathParser.NormalizeDirectorySeparatorIncludeWindowsBackslash(name);

                                string targetFilePath = srcFilePathIfPhysicalFile!.GetParentDirectory().Combine(name);

                                var targetBody = await fs.ReadStringFromFileAsync(targetFilePath, settings.Encoding, settings.MaxReadFileSize, FileFlags.None, cancel: cancel);

                                await ProcessAsync(destLines, targetBody, targetFilePath, includeCount);
                            }

                            consumed = true;
                        }
                    }
                }

                if (consumed == false)
                {
                    destLines.Add(line);
                }
            }
        }
    }

    public static async Task<List<string>> ReadIncludesFileLinesAsync(string srcFilePathOrUrl, ExpandIncludesSettings? settings = null, CancellationToken cancel = default)
    {
        string srcBody;
        FilePath? srcFilePathIfPhysicalFile;

        if (srcFilePathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || srcFilePathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // URL
            srcBody = "#include " + srcFilePathOrUrl + "\n";
            srcFilePathIfPhysicalFile = null;
        }
        else
        {
            // Local file
            srcBody = await Lfs.ReadStringFromFileAsync(srcFilePathOrUrl, cancel: cancel);
            srcFilePathIfPhysicalFile = srcFilePathOrUrl;
        }

        return await MiscUtil.ExpandIncludesToListAsync(srcBody, srcFilePathIfPhysicalFile, settings, cancel);
    }

    public static async Task ExpandIncludesFileAsync(string srcFilePathOrUrl, string destFilePath, ExpandIncludesSettings? settings = null, CancellationToken cancel = default, string? newLineStr = null, bool writeBom = false)
    {
        var list = await ReadIncludesFileLinesAsync(srcFilePathOrUrl, settings, cancel);

        byte[] data = list._LinesToStr(newLineStr)._GetBytes_UTF8(writeBom);

        await Lfs.WriteDataToFileAsync(destFilePath, data, FileFlags.WriteOnlyIfChanged, false, cancel, true);
    }

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
    public bool IgnoreErrorInMultiFileDownload { get; }

    public FileDownloadOption(int maxConcurrentThreads = -1, int maxConcurrentFiles = -1, int retryIntervalMsecs = -1, int tryCount = -1, int bufferSize = 0, int additionalConnectionIntervalMsecs = -1, WebApiOptions? webApiOptions = null, bool ignoreErrorInMultiFileDownload = false)
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
        IgnoreErrorInMultiFileDownload = ignoreErrorInMultiFileDownload;
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
    public static async Task<long> DownloadFileParallelAsync(string url, Stream destStream, FileDownloadOption? option = null, Ref<WebSendRecvResponse>? responseHeader = null, ProgressReporterBase? progressReporter = null, CancellationToken cancel = default)
    {
        if (option == null) option = new FileDownloadOption();
        if (responseHeader == null) responseHeader = new Ref<WebSendRecvResponse>();
        if (progressReporter == null) progressReporter = new NullProgressReporter();
        AsyncLock streamLock = new AsyncLock();

        // まずファイルサイズを取得してみる
        RetryHelper<long> h = new RetryHelper<long>(option.RetryIntervalMsecs, option.TryCount);

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

        return await h.RunAsync(async cancel =>
        {
            destStream.Seek(0, SeekOrigin.Begin);
            destStream.SetLength(0);
            await destStream.FlushAsync(cancel);

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
                                await using var http = new WebApi(option.WebApiOptions);
                                await using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url + "", cancel2, rangeStart: partial.StartPosition));
                                await using var src = res.DownloadStream;

                                // Normal copy
                                using (MemoryHelper.FastAllocMemoryWithUsing(option.BufferSize, out Memory<byte> buffer))
                                {
                                    while (true)
                                    {
                                        cancel2.ThrowIfCancellationRequested();

                                        Memory<byte> thisTimeBuffer = buffer;

                                        int readSize = await src._ReadAsyncWithTimeout(thisTimeBuffer, timeout: option.WebApiOptions.Settings.Timeout, cancel: cancel2, allowEof: true);

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

                return totalDownloadSize;
            }
            else
            {
                // 分割ダウンロード NG の場合は、通常の方法でダウンロードする
                await using var http = new WebApi(option.WebApiOptions);

                await using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, url, cancel));

                long totalSize = await res.DownloadStream.CopyBetweenStreamAsync(destStream, reporter: progressReporter, cancel: cancel, readTimeout: option.WebApiOptions.Settings.Timeout);

                progressReporter.ReportProgress(new ProgressData(totalSize, isFinish: true));

                return totalSize;
            }
        }, retryInterval: option.RetryIntervalMsecs, tryCount: option.TryCount, cancel: cancel);
    }

    // 指定された URL (のテキストファイル) をダウンロードし、その URL に記載されているすべてのファイルをダウンロードする
    public static async Task<bool> DownloadUrlListedAsync(string urlListedFileUrl, string destDir, string extensions, FileDownloadOption? option = null, ProgressReporterFactoryBase? reporterFactory = null, CancellationToken cancel = default)
    {
        if (option == null) option = new FileDownloadOption();
        if (reporterFactory == null) reporterFactory = new NullReporterFactory();

        string body;

        if (urlListedFileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            urlListedFileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // ファイル一覧のファイルをダウンロードする
            using var web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true }));

            var urlFileBody = await web.SimpleQueryAsync(WebMethods.GET, urlListedFileUrl, cancel);
            body = urlFileBody.Data._GetString_UTF8();
        }
        else
        {
            // ローカルのテキストファイルを読み込む
            body = Lfs.ReadStringFromFile(urlListedFileUrl);
        }

        int currentPos = 0;

        List<string> fileUrlList = new List<string>();

        List<Tuple<string, string>> errorList = new List<Tuple<string, string>>();

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

            try
            {
                await DownloadFileParallelAsync(fileUrl, fileStream, option, progressReporter: reporter, cancel: cancel);
            }
            catch (Exception ex)
            {
                Con.WriteError($"Error. URL: {fileUrl} ErrorMessage = {ex.Message}");

                errorList.Add(new Tuple<string, string>(fileUrl, ex.Message));

                if (option.IgnoreErrorInMultiFileDownload == false)
                {
                    throw;
                }
            }
        }, cancel: cancel);

        if (errorList.Count >= 1)
        {
            Con.WriteLine("----------");
        }

        for (int i = 0; i < errorList.Count; i++)
        {
            var err = errorList[i];

            Con.WriteError($"Error occured: {i + 1}/{errorList.Count}: {err.Item1} {err.Item2}");
        }

        return (errorList.Count == 0);
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
    public IEnumerable<int> TcpPorts { get; set; } = new List<int>();

    public void Normalize()
    {
        this.NumThreads = Math.Max(this.NumThreads, 1);
        if (Interval < 0) Interval = 100;
        if (NumTry <= 0) NumTry = 1;
        if (this.TcpPorts == null) this.TcpPorts = new List<int>();
    }
}

public class DnsHostNameScannerEntry
{
    public IPAddress Ip { get; set; } = null!;
    public List<string>? HostnameList { get; set; }
    public bool NotFound { get; set; }
    public List<int> TcpPorts { get; set; } = new List<int>();
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

        IEnumerable<IPAddress?> ipList = targetIpAddressList.Select(x => x._ToIPAddress(noExceptionAndReturnNull: true)).Where(x => x != null).Distinct().OrderBy(x => x, IpComparer.Comparer);

        if (this.Settings.Shuffle) ipList = ipList._Shuffle();

        // キューを作成する
        BeforeQueue = new Queue<DnsHostNameScannerEntry>();
        AfterResult = new List<DnsHostNameScannerEntry>();

        // キューに挿入する
        ipList._DoForEach(x => BeforeQueue.Enqueue(new DnsHostNameScannerEntry { Ip = x! }));

        int numTry = Settings.NumTry;

        bool usePortScan = Settings.TcpPorts.Any();

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

                        List<int> okPorts = new List<int>();

                        if (usePortScan)
                        {
                            // ポートスキャンの実施
                            foreach (var port in Settings.TcpPorts)
                            {
                                if (await IPUtil.CheckTcpPortWithRetryAsync(target.Ip.ToString(), port, cancel: cancel))
                                {
                                    okPorts.Add(port);
                                }
                            }
                        }

                        bool anyPortOk = usePortScan && okPorts.Any();

                        Ref<DnsAdditionalResults> additional = new Ref<DnsAdditionalResults>();

                        List<string>? ret = await Dr.GetHostNameListAsync(target.Ip, additional, cancel);

                        string portsStr = "TCP Ports: " + (anyPortOk ? okPorts.Select(x => x.ToString())._Combine(" / ") : "None");
                        if (usePortScan == false) portsStr = "";

                        if (ret._IsFilled())
                        {
                            target.HostnameList = ret;

                            if (Settings.PrintStat)
                            {
                                $"Try #{tryCount + 1}: {target.Ip.ToString()._AddSpacePadding(19)} {target.HostnameList._Combine(" / ")}   {portsStr}".Trim()._Print();
                            }
                        }
                        else
                        {
                            if (usePortScan && anyPortOk)
                            {
                                target.HostnameList = target.Ip.ToString()._SingleList();

                                if (Settings.PrintStat)
                                {
                                    $"Try #{tryCount + 1}: {target.Ip.ToString()._AddSpacePadding(19)} {target.HostnameList._Combine(" / ")}   {portsStr}".Trim()._Print();
                                }
                            }
                            else
                            {
                                if (usePortScan == false)
                                {
                                    target.NotFound = additional?.Value?.IsNotFound ?? false;
                                }
                            }
                        }

                        target.TcpPorts = okPorts;

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
                    if ((item.HostnameList._IsEmpty() && item.NotFound == false) || (usePortScan && item.TcpPorts.Any() == false))
                    {
                        // 未解決ホストかつエラー発生ホストである
                        // または TCP で 1 つもポート応答がなかったホストである
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

        if (usePortScan)
        {
            // TCP ポートスキャンモードの場合、1 つもポートが開いていないホストはリストから消す
            AfterResult = AfterResult.Where(x => x.TcpPorts.Any()).ToList();
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
                string portsStr = "TCP Ports: " + item.TcpPorts.Select(x => x.ToString())._Combine(" / ");
                if (usePortScan == false) portsStr = "";

                $"{item.Ip.ToString()._AddSpacePadding(19)} {item.HostnameList!._Combine(" / ")}   {portsStr}".Trim()._Print();
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


public class SimpleHttpDownloaderMetaData
{
    public string Url { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string MediaType { get; set; } = "";
    public int Size { get; set; }
    public string HashSha256 { get; set; } = "";
    public DateTimeOffset TimeStamp { get; set; } = Util.ZeroDateTimeOffsetValue;
}

[Flags]
public enum CachedDownloaderFlags
{
    None = 0,
    AlwaysUseCacheIfExists,
}

// CloneDeep 禁止
public class CachedDownloaderSettings
{
    public DirectoryPath CacheRootDirPath { get; }
    public CachedDownloaderFlags Flags { get; }
    public WebApiOptions WebOptions { get; }
    public int MaxTry { get; }
    public int RetryInterval { get; }

    public CachedDownloaderSettings(DirectoryPath? cacheRootDirPath = null, CachedDownloaderFlags flags = CachedDownloaderFlags.None,
        WebApiOptions? webOptions = null, int maxTry = 1, int retryInterval = 1000)
    {
        this.CacheRootDirPath = cacheRootDirPath ?? new DirectoryPath(Lfs.PathParser.Combine(Env.AppLocalDir, "CachedDownloader"), Lfs);
        this.Flags = flags;
        this.WebOptions = webOptions ?? new WebApiOptions(new WebApiSettings { MaxRecvSize = Consts.MaxLens.MaxCachedFileDownloadSizeDefault });
        this.MaxTry = Math.Max(MaxTry, 1);
        this.RetryInterval = Math.Max(retryInterval, 0);
    }
}

public class CachedDownloaderResult
{
    public Memory<byte> Data { get; set; }

    public SimpleHttpDownloaderMetaData MetaData;

    public bool FromCache { get; set; }

    public CachedDownloaderResult(Memory<byte> data, SimpleHttpDownloaderMetaData metaData, bool fromCache)
    {
        this.Data = data;
        this.MetaData = metaData;
        this.FromCache = fromCache;
    }
}

public static class CachedDownloader
{
    public static async Task<CachedDownloaderResult> DownloadAsync(string url, CancellationToken cancel = default, CachedDownloaderSettings? settings = null)
    {
        settings ??= new CachedDownloaderSettings();

        var fs = settings.CacheRootDirPath.FileSystem;

        url = url.Trim();

        var flags = settings.Flags;

        // URL からハッシュを生成
        string urlHashStr = url._HashSHA256()._GetHexString().ToLowerInvariant();

        // URL ハッシュからサブディレクトリ名を生成
        string dirPath = settings.CacheRootDirPath.Combine(urlHashStr.Substring(0, 2), urlHashStr);
        string metaDataPath = fs.PathParser.Combine(dirPath, "metadata.json");
        string contentsPath = fs.PathParser.Combine(dirPath, "contents.dat");

        if (settings.Flags.Bit(CachedDownloaderFlags.AlwaysUseCacheIfExists))
        {
            // AlwaysUseCacheIfExists フラグが付いている場合は、キャッシュがもし存在すれば、ダウンロードはせずにキャッシュをすぐに返す
            var cached = await TryLoadCacheFileAndMetaDataAsync();
            if (cached != null)
            {
                return cached;
            }
        }

        // 本家からダウンロードを試みる。
        try
        {
            var downloaded = await DownloadRealAsync();

            // ダウンロード成功。キャッシュに保存する。
            await SaveCacheFileAndMetaDataAsync(downloaded.MetaData, downloaded.Data);

            return downloaded;
        }
        catch
        {
            // ダウンロード失敗。すでにキャッシュされているデータがないかどうか調べる。もしあれば、それを返す。
            var cached = await TryLoadCacheFileAndMetaDataAsync();

            if (cached != null)
            {
                return cached;
            }

            // キャッシュがなければ、例外をそのまま返す
            throw;
        }

        // ファイルキャッシュに書き込みする関数 (なお、全く同一のファイルがすでに存在する場合は、キャッシュへの書き込みはしない)
        async Task SaveCacheFileAndMetaDataAsync(SimpleHttpDownloaderMetaData metaData, Memory<byte> data)
        {
            try
            {
                // ルートディレクトリがまだない場合は作成する。NTFS 圧縮フラグを有効にする。
                await fs.CreateDirectoryAsync(settings.CacheRootDirPath, FileFlags.OnCreateSetCompressionFlag, cancel);
            }
            catch { }

            await fs.CreateDirectoryAsync(dirPath, cancel: cancel);

            await fs.WriteDataToFileAsync(contentsPath, data, cancel: cancel, flags: FileFlags.WriteOnlyIfChanged);

            await fs.WriteJsonToFileAsync(metaDataPath, metaData, cancel: cancel, flags: FileFlags.WriteOnlyIfChanged);
        }

        // キャッシュされているファイルとメタデータを読み込んでみて問題なければデータを返す関数
        async Task<CachedDownloaderResult?> TryLoadCacheFileAndMetaDataAsync()
        {
            try
            {
                checked
                {
                    if (await fs.IsFileExistsAsync(metaDataPath, cancel) == false) return null;
                    if (await fs.IsFileExistsAsync(contentsPath, cancel) == false) return null;

                    var metaData = await fs.ReadJsonFromFileAsync<SimpleHttpDownloaderMetaData>(metaDataPath, cancel: cancel);
                    if (metaData == null) return null;

                    var fileData = await fs.ReadDataFromFileAsync(contentsPath, metaData.Size + 8, cancel: cancel);
                    if (fileData.Length != metaData.Size) return null;

                    string sha256 = Secure.HashSHA256(fileData.Span)._GetHexString();
                    if (Str.IsSameHex(sha256, metaData.HashSha256) == false) return null;

                    return new CachedDownloaderResult(fileData, new SimpleHttpDownloaderMetaData
                    {
                        Size = fileData.Length,
                        ContentType = metaData.ContentType,
                        MediaType = metaData.MediaType,
                        Url = metaData.Url,
                        HashSha256 = sha256,
                        TimeStamp = metaData.TimeStamp,
                    }, true);
                }
            }
            catch
            {
                return null;
            }
        }

        // 実際にサーバーからダウンロードを行なう関数
        async Task<CachedDownloaderResult> DownloadRealAsync()
        {
            return await RetryHelper.RunAsync(async () =>
            {
                var r = await SimpleHttpDownloader.DownloadAsync(url, WebMethods.GET, options: settings.WebOptions, cancel: cancel);

                CachedDownloaderResult ret = new CachedDownloaderResult(r.Data, new SimpleHttpDownloaderMetaData
                {
                    Size = r.Data.Length,
                    ContentType = r.ContentType,
                    MediaType = r.MediaType,
                    Url = url,
                    HashSha256 = Secure.HashSHA256(r.Data)._GetHexString(),
                    TimeStamp = DtOffsetNow,
                }, false);

                return ret;
            },
            tryCount: settings.MaxTry,
            retryInterval: settings.RetryInterval,
            cancel: cancel,
            randomInterval: true,
            noDebugMessage: true);
        }
    }
}

public class LinuxTimeDateCtlResults
{
    public DateTime UniversalTime = Util.ZeroDateTimeValue;
    public DateTime RtcTime = Util.ZeroDateTimeValue;
    public bool SystemClockSynchronized;
    public bool NtpServiceActive;
}

public static class LinuxTimeDateCtlUtil
{
    public static async Task<DateTimeOffset> ExecuteNtpDigAndReturnResultDateTimeAsync(string ntpServer, int timeoutMsecs = 5 * 1000, CancellationToken cancel = default)
    {
        if (timeoutMsecs <= 0) timeoutMsecs = 5 * 1000;

        var res = await EasyExec.ExecAsync(Lfs.UnixGetFullPathFromCommandName("ntpdig"), arguments: $"--samples=1 --timeout=1 {ntpServer}", cancel: cancel, timeout: timeoutMsecs);

        var line = res.OutputStr._GetLines(trim: true, removeEmpty: true).Where(x => x.StartsWith("20") || x.StartsWith("21")).FirstOrDefault();

        if (line._IsFilled())
        {
            string[] tokens = line._Split(StringSplitOptions.None, " ", "\t");

            if (tokens.Length >= 4)
            {
                DateTime dateTimeBase = Str.StrToDateTime(tokens[0] + " " + tokens[1]);

                string timezone = tokens[2];

                if (timezone.StartsWith("(") && timezone.EndsWith(")"))
                {
                    timezone = timezone._RemoveQuotation('(', ')');

                    bool? positive = null;

                    if (timezone.StartsWith("+"))
                    {
                        positive = true;
                    }
                    else if (timezone.StartsWith("-"))
                    {
                        positive = false;
                    }

                    if (positive != null)
                    {
                        timezone = timezone.Substring(1);

                        int timezoneHour = timezone.Substring(0, 2)._ToInt();
                        int timezoneMinute = timezone.Substring(2, 2)._ToInt();

                        TimeSpan offset = new TimeSpan(timezoneHour, timezoneMinute, 0);
                        if (positive == false) offset = -offset;

                        return new DateTimeOffset(dateTimeBase.Year, dateTimeBase.Month, dateTimeBase.Day, dateTimeBase.Hour, dateTimeBase.Minute, dateTimeBase.Second, offset).AddTicks(dateTimeBase.Ticks % 10000000L);
                    }
                }
            }
        }

        return Util.ZeroDateTimeOffsetValue;
    }

    public static async Task<LinuxTimeDateCtlResults> GetStateFromTimeDateCtlCommandAsync(CancellationToken cancel = default)
    {
        LinuxTimeDateCtlResults ret = new LinuxTimeDateCtlResults();

        var res = await EasyExec.ExecAsync(Lfs.UnixGetFullPathFromCommandName("timedatectl"), cancel: cancel);

        var lines = res.OutputStr._GetLines();

        foreach (string line in lines)
        {
            if (line._GetKeyAndValue(out string? key, out string? value, ":"))
            {
                key = key._NonNullTrim();
                value = value._NonNullTrim();

                if (key._IsSamei("Universal time"))
                {
                    ret.UniversalTime = StrToDateTime(value);
                }
                else if (key._IsSamei("RTC time"))
                {
                    ret.RtcTime = StrToDateTime(value);
                }
                else if (key._IsSamei("System clock synchronized"))
                {
                    ret.SystemClockSynchronized = value._ToBool();
                }
                else if (key._IsSamei("NTP service"))
                {
                    ret.NtpServiceActive = value._ToBool();
                }
            }
        }

        return ret;
    }

    static DateTime StrToDateTime(string str)
    {
        try
        {
            str = str._NonNullTrim();

            string[] tokens = str._Split(StringSplitOptions.None, " ", "\t");

            if (tokens.Length >= 3)
            {
                return Str.StrToDateTime(tokens[1] + " " + tokens[2]);
            }
        }
        catch { }

        return Util.ZeroDateTimeValue;
    }
}

#endif

