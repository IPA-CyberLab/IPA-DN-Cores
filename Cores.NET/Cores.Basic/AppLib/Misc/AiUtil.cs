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
using System.Collections.Immutable;
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

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DefaultAiUtilSettings
    {
        public static readonly Copenhagen<int> DefaultMaxStdOutBufferSize = 256 * 1024 * 1024;
        public static readonly Copenhagen<double> AdjustAudioTargetMaxVolume = 0.0;
        public static readonly Copenhagen<double> AdjustAudioTargetMeanVolume = -15.0;
    }
}

public static class AiUtilOkFileVersion
{
    public const int CurrentVersion = 20250329_02;
}

public static class AiTask
{
    public static async Task ExtractMusicAndVocal(string srcDirPath, string dstDirPath, CancellationToken cancel = default)
    {
        var groupDirList = await Lfs.EnumDirectoryAsync(srcDirPath, cancel: cancel);

        foreach (var groupDir in groupDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false).OrderBy(x => x.Name, StrCmpi))
        {
            string groupName = groupDir.Name._NormalizeSoftEther(true);

            var srcMusicList = await Lfs.EnumDirectoryAsync(groupDir.FullPath, true, cancel: cancel);

            //foreach (var srcMusic in srcMusicList.Where(x=>x.IsFile && x.Name._IsExtensionMatch(
        }
    }
}

public class AiUtilBasicSettings
{
    public string AiTest_UvrCli_BaseDir = "";
    public int AiTest_UvrCli_Timeout = 60 * 1000;
    public double AdjustAudioTargetMaxVolume = CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMaxVolume;
    public double AdjustAudioTargetMeanVolume = CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMeanVolume;
}

public class AiUtilUvrEngine : AiUtilBasicEngine
{
    public FfMpegUtil FfMpeg { get; }

    public AiUtilUvrEngine(AiUtilBasicSettings basicSettings, FfMpegUtil ffMpeg) : base(basicSettings, "UVR", basicSettings.AiTest_UvrCli_BaseDir)
    {
        this.FfMpeg = ffMpeg;
    }

    public async Task<FfMpegParsedList> ExtractAsync(string srcFilePath, string? dstMusicWavPath = null, string? dstVocalWavPath = null, string tagTitle = "", bool useOkFile = true, CancellationToken cancel = default)
    {
        if (tagTitle._IsEmpty()) tagTitle = PP.GetFileNameWithoutExtension(srcFilePath);

        if (dstMusicWavPath._IsEmpty() && dstVocalWavPath._IsEmpty()) throw new CoresLibException("dstMusicWavPath and dstVocalWavPath are both empty.");

        if (useOkFile)
        {
            FfMpegParsedList? savedResult = null;
            if (dstMusicWavPath._IsFilled())
            {
                var okFileForDstMusicWavPath = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstMusicWavPath, "", AiUtilOkFileVersion.CurrentVersion, cancel);
                if (okFileForDstMusicWavPath.IsOk)
                {
                    dstMusicWavPath = null;
                    savedResult = okFileForDstMusicWavPath;
                }
            }

            if (dstVocalWavPath._IsFilled())
            {
                var okFileForDstVocalWavPath = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstVocalWavPath, "", AiUtilOkFileVersion.CurrentVersion, cancel);
                if (okFileForDstVocalWavPath.IsOk)
                {
                    dstVocalWavPath = null;
                    savedResult = okFileForDstVocalWavPath;
                }
            }

            if (dstMusicWavPath == null && dstVocalWavPath == null && savedResult != null)
            {
                return savedResult;
            }
        }

        // 音量調整
        string adjustedWavFile = await Lfs.GenerateUniqueTempFilePathAsync(srcFilePath, cancel: cancel);
        var result = await FfMpeg.AdjustAudioVolumeAsync(srcFilePath, adjustedWavFile, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, tagTitle, false, cancel);

        if (dstMusicWavPath._IsFilled())
        {
            // 音楽分離実行
            await ExtractInternalAsync(adjustedWavFile, dstMusicWavPath, true, tagTitle, cancel);

            if (useOkFile)
            {
                await Lfs.WriteOkFileAsync(dstMusicWavPath, result.Item1, "", AiUtilOkFileVersion.CurrentVersion, cancel);
            }
        }

        if (dstVocalWavPath._IsFilled())
        {
            // ボーカル分離実行
            await ExtractInternalAsync(adjustedWavFile, dstVocalWavPath, false, tagTitle, cancel);

            if (useOkFile)
            {
                await Lfs.WriteOkFileAsync(dstVocalWavPath, result.Item1, "", AiUtilOkFileVersion.CurrentVersion, cancel);
            }
        }

        return result.Item1;
    }

    async Task ExtractInternalAsync(string srcWavPath, string dstWavPath, bool music, string tagTitle = "", CancellationToken cancel = default)
    {
        string aiSrcPath = BaseDirPath._CombinePath("_src.wav");

        await Lfs.DeleteFileIfExistsAsync(aiSrcPath, cancel: cancel);

        await Lfs.CopyFileAsync(srcWavPath, aiSrcPath, new CopyFileParams(flags: FileFlags.AutoCreateDirectory), cancel: cancel);

        await DeleteAllAiProcessOutputFilesAsync(cancel);

        if (tagTitle._IsEmpty())
        {
            tagTitle = srcWavPath._GetFileNameWithoutExtension();
        }
        string tag = $"{(music ? "get_music" : "get_vocal")} ('{tagTitle._TruncStrEx(16)}')";

        var result = await this.RunVEnvPythonCommandsAsync($"python {(music ? "get_music" : "get_vocal")}_wav.py", Settings.AiTest_UvrCli_Timeout, printTag: tag, cancel: cancel);

        if (result.OutputAndErrorStr._GetLines().Where(x => x._InStr("instruments done")).Any() == false)
        {
            throw new CoresLibException($"{tag} failed.");
        }

        string aiOutDir = PP.Combine(this.BaseDirPath, "opt");

        var files = await Lfs.EnumDirectoryAsync(aiOutDir, wildcard: "instrument*.wav", cancel: cancel);
        var aiDstFile = files.Single();

        await Lfs.CopyFileAsync(aiDstFile.FullPath, dstWavPath, new CopyFileParams(flags: FileFlags.AutoCreateDirectory), cancel: cancel);
    }

    async Task DeleteAllAiProcessOutputFilesAsync(CancellationToken cancel = default)
    {
        string aiOutDir = PP.Combine(this.BaseDirPath, "opt");

        await Lfs.CreateDirectoryAsync(aiOutDir, cancel: cancel);

        var list = await Lfs.EnumDirectoryAsync(aiOutDir, false, wildcard: "*.wav", cancel: cancel);

        foreach (var f in list)
        {
            await Lfs.DeleteFileIfExistsAsync(f.FullPath, raiseException: true, cancel: cancel);
        }
    }
}

public class AiUtilBasicEngine : AsyncService
{
    public AiUtilBasicSettings Settings { get; }
    public string SimpleAiName { get; }
    public string BaseDirPath { get; }

    public AiUtilBasicEngine(AiUtilBasicSettings basicSettings, string simpleAiName, string baseDirPath)
    {
        this.SimpleAiName = simpleAiName._NonNullTrim()._NotEmptyOrDefault("AI");
        this.Settings = basicSettings;
        this.BaseDirPath = baseDirPath;
    }

    public async Task<EasyExecResult> RunVEnvPythonCommandsAsync(string commandLines,
        int timeout = Timeout.Infinite, bool throwOnErrorExitCode = true, string printTag = "",
        int easyOutputMaxSize = 0,
        Encoding? inputEncoding = null, Encoding? outputEncoding = null, Encoding? errorEncoding = null,
        CancellationToken cancel = default)
    {
        return await RunBatchCommandsDirectAsync(
            BuildLines(@".\venv\Scripts\activate",
            commandLines),
            timeout,
            throwOnErrorExitCode,
            printTag,
            easyOutputMaxSize,
            inputEncoding,
            outputEncoding,
            errorEncoding,
            cancel);
    }

    public async Task<EasyExecResult> RunBatchCommandsDirectAsync(string commandLines,
        int timeout = Timeout.Infinite, bool throwOnErrorExitCode = true, string printTag = "",
        int easyOutputMaxSize = 0,
        Encoding? inputEncoding = null, Encoding? outputEncoding = null, Encoding? errorEncoding = null,
        CancellationToken cancel = default)
    {
        if (easyOutputMaxSize <= 0) easyOutputMaxSize = CoresConfig.DefaultAiUtilSettings.DefaultMaxStdOutBufferSize;

        string win32cmd = Env.Win32_SystemDir._CombinePath("cmd.exe");

        commandLines = BuildLines(commandLines, "exit");

        string tmp1 = "";
        if (printTag._IsFilled())
        {
            tmp1 += ": " + printTag.Trim();
        }
        string printTagMain = $"[{this.SimpleAiName}{tmp1}]";

        EasyExecResult ret = await EasyExec.ExecAsync(win32cmd, "", this.BaseDirPath,
            easyInputStr: commandLines,
            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdOut | ExecFlags.EasyPrintRealtimeStdErr,
            timeout: timeout, cancel: cancel, throwOnErrorExitCode: true,
            easyOutputMaxSize: easyOutputMaxSize,
            printTag: printTagMain,
            inputEncoding: inputEncoding, outputEncoding: outputEncoding, errorEncoding: errorEncoding);

        return ret;
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

