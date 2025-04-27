﻿// IPA Cores.NET
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
// 一部のコード: ChatGPT o1 Pro が生成
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

using NAudio.Wave;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class DefaultAiUtilSettings
    {
        public static readonly Copenhagen<int> DefaultMaxStdOutBufferSize = 2_000_000_000;
        public static readonly Copenhagen<double> AdjustAudioTargetMaxVolume = 0.0;
        public static readonly Copenhagen<double> AdjustAudioTargetMeanVolume = -15.0;
    }
}

public static class AiUtilVersion
{
    public const int CurrentVersion = 20250330_04;
}

public class AiCompositWaveSettings
{
    public List<AiCompositRule> RulesList = null!;

    [JsonIgnore]
    public Func<AiTaskOperationDesc, CancellationToken, AiAudioEffectFilter?>? CreateAudioEffectFilter = null;
}

public class AiCompositWaveParam
{
    public double MarginSecs = 30;
    public double StdFadeInSecs = 2;
    public double StdFadeOutSecs = 13;
    public double MaxRandBeforeLengthSecs = 0;
    public double MaxRandAfterLengthSecs = 13;
    public double VolumeDelta = -17;
    public double VolumeDeltaRandomRange = 10;
    public double DurationWhenEndTagIsEmpty = 5;
    public AiAudioEffectSpeedType? EffectType = null;
}

public class AiCompositRuleData
{
    public AiCompositWaveParam Param = new();
    public IEndlessQueue<string> MaterialsWavPathQueue = null!;
    public int MultipleMode_Count = 0;
    public double Multiplemode_SpanRatio = 0;
}

public class AiCompositRule
{
    public string StartTagStr = Str.NewGuid();
    public string EndTagStr = Str.NewGuid();
    public Func<AiCompositRuleData> GetRuleFunc = null!;

    public AiCompositRuleData RuleData => _RuleDataCreatedInternal;
    readonly CachedProperty<AiCompositRuleData> _RuleDataCreatedInternal;

    public AiCompositRule()
    {
        this._RuleDataCreatedInternal = new(getter: () => this.GetRuleFunc());
    }
}

public class AiAudioEffectFilter
{
    public AiAudioEffectBase Effect = null!;
    public Action<Memory<byte>, CancellationToken> PerformFilterFunc = null!;
    public string FilterName = "";
    public AiAudioEffectSpeedType FilterSpeedType = AiAudioEffectSpeedType.Normal;
    public IAiAudioEffectSettings EffectSettings = null!;

    public AiAudioEffectFilter(AiAudioEffectBase effect, AiAudioEffectSpeedType type, double adjustAddVolume = 0.0, CancellationToken cancel = default)
    {
        this.Effect = effect;
        string name = PP.GetExtension(effect.ToString()._NotEmptyOrDefault("_unknown"));
        if (name.StartsWith(".")) name = name.Substring(1);
        if (name._IsEmpty()) name = "_unknown";

        this.FilterName = PP.MakeSafeFileName(name);
        this.FilterSpeedType = type;
        this.EffectSettings = effect.NewSettingsFactoryWithRandom(type);
        this.PerformFilterFunc = (wave, cancel) =>
        {
            var originalVolume = AiWaveVolumeUtils.CalcMeanVolume(wave, cancel);

            this.Effect.ProcessFilter(wave, this.EffectSettings, cancel);

            AiWaveVolumeUtils.AdjustVolume(wave, originalVolume + adjustAddVolume, cancel);
        };
    }
}

public class AiVoiceFilterRule
{
    public string StartTagStr = Str.NewGuid();
    public string EndTagStr = Str.NewGuid();
    public Func<AiAudioEffectFilter?>? CreateFilterFunc = null!;
}

public class AiTaskOperationDesc
{
    public double StartPosition = 0;
    public double EndPosition = 0;
    public AiCompositRule Rule = null!;
    public IEndlessQueue<string> MatFilesQueue = null!;

    public string Calced_MaterialWavPath = "";
    public double Calced_TargetPositionSecs;
    public double Calced_MeterialPositionSecs;
    public double Calced_LengthSecs;
    public double Calced_FadeInSecs;
    public double Calced_FadeOutSecs;
    public double Calced_VolumeDelta_Left;
    public double Calced_VolumeDelta_Right;

    public AiAudioEffectFilter? Filter = null;
}

public class AiTask
{
    public const double BgmVolumeDeltaForConstant = -9.3;
    public const double BgmVolumeDeltaForSmooth = -0.0;

    public static readonly RefLong TotalGpuProcessTimeMsecs = new RefLong();

    public const int DefaultFadeoutSecs = 10;

    public FfMpegUtil FfMpeg { get; }
    public AiUtilBasicSettings Settings { get; }

    public AiTask(AiUtilBasicSettings settings, FfMpegUtil ffMpeg)
    {
        this.Settings = settings;
        this.FfMpeg = ffMpeg;
    }

    public class RandomSegmentMetaData
    {
        public string SrcFileName = "";
        public int DurationMsecs;
        public int TotalLengthMsecs;
        public int Number;
        public int TotalCount;
    }

    public async Task ExtractRandomVocalSegmentsFromAllWavAsync(string wavFileDir, string dstDirName, int durationMsecs, CancellationToken cancel = default)
    {
        var wavFilesList = await Lfs.EnumDirectoryAsync(wavFileDir, wildcard: "*.wav", cancel: cancel);

        foreach (var wavFile in wavFilesList.Where(x => x.IsFile)._Shuffle().ToList())
        {
            string[] tokens = wavFile.Name._Split(StringSplitOptions.None, " - ");
            if (tokens.Length >= 3 && tokens[0]._IsSamei("vocalonly"))
            {
                string artistName = tokens[1].Replace("_", "");

                if (artistName._IsFilled())
                {
                    Con.WriteLine(wavFile.FullPath);
                    await ExtractRandomSegmentsFromWavAsync(wavFile.FullPath, dstDirName, artistName, durationMsecs, cancel);
                }
            }
        }
    }

    public async Task ExtractRandomSpeechSegmentsFromAllWavAsync(string wavFileDir, string dstDirName, int durationMsecs, CancellationToken cancel = default)
    {
        var wavFilesList = await Lfs.EnumDirectoryAsync(wavFileDir, wildcard: "*.wav", cancel: cancel);

        foreach (var wavFile in wavFilesList.Where(x => x.IsFile).OrderBy(x => x.Name, StrCmpi))
        {
            string[] tokens = wavFile.Name._Split(StringSplitOptions.None, " - ");
            if (tokens.Length >= 3 && tokens[0]._IsSamei("vocalonly"))
            {
                string tmp1 = tokens[2];

                string[] tokens2 = tmp1._Split(StringSplitOptions.None, "_");
                if (tokens2.Length >= 2)
                {
                    string prefix = tokens2[0].Trim();
                    if (prefix._IsFilled())
                    {
                        Con.WriteLine(wavFile.FullPath);
                        await ExtractRandomSegmentsFromWavAsync(wavFile.FullPath, dstDirName, prefix, durationMsecs, cancel);
                    }
                }
            }
        }
    }

    public async Task ExtractRandomSegmentsFromWavAsync(string wavFilePath, string dstDirName, string dstBaseFileName, int durationMsecs, CancellationToken cancel = default)
    {
        int waveLengthMsecs;
        await using (var reader = new WaveFileReader(wavFilePath))
        {
            waveLengthMsecs = (int)reader.TotalTime.TotalMilliseconds;
        }

        if (durationMsecs > waveLengthMsecs)
        {
            durationMsecs = waveLengthMsecs;
        }

        await Lfs.CreateDirectoryAsync(dstDirName, cancel: cancel);

        int count;
        if (waveLengthMsecs <= 1.5 * durationMsecs)
            count = 1;
        else if (waveLengthMsecs <= 2.0 * durationMsecs)
            count = 2;
        else if (waveLengthMsecs <= 3.0 * durationMsecs)
            count = 3;
        else if (waveLengthMsecs <= 4.0 * durationMsecs)
            count = 4;
        else
            count = 5;

        Random rand = new Random(Secure.RandSInt31());

        for (int i = 1; i <= count; i++)
        {
            int startMsec = rand.Next(waveLengthMsecs - durationMsecs + 1);

            await using (var reader = new WaveFileReader(wavFilePath))
            {
                reader.CurrentTime = TimeSpan.FromMilliseconds(startMsec);

                string outputPath;

                for (int j = 1; ; j++)
                {
                    string outputFileNameTmp = $"{dstBaseFileName}_{j.ToString("D3")}.wav";
                    string outputFilePathTmp = PP.Combine(dstDirName, outputFileNameTmp);

                    if (await Lfs.IsFileExistsAsync(outputFilePathTmp, cancel) == false || await Lfs.IsOkFileExists(outputFilePathTmp, cancel: cancel) == false)
                    {
                        outputPath = outputFilePathTmp;
                        break;
                    }
                }

                await using (var writer = new WaveFileWriter(outputPath, reader.WaveFormat))
                {
                    int bytesPerMillisecond = reader.WaveFormat.AverageBytesPerSecond / 1000;
                    int bytesToRead = bytesPerMillisecond * durationMsecs;

                    byte[] buffer = new byte[65536];
                    int bytesRemaining = bytesToRead;

                    while (bytesRemaining > 0)
                    {
                        int readBytes = await reader.ReadAsync(buffer, 0, Math.Min(buffer.Length, bytesRemaining), cancel);
                        if (readBytes == 0)
                        {
                            break;
                        }

                        await writer.WriteAsync(buffer, 0, readBytes, cancel);
                        bytesRemaining -= readBytes;
                    }
                }

                var meta = new RandomSegmentMetaData
                {
                    DurationMsecs = durationMsecs,
                    TotalLengthMsecs = waveLengthMsecs,
                    SrcFileName = wavFilePath,
                    Number = i,
                    TotalCount = count,
                };

                await Lfs.WriteOkFileAsync(outputPath, meta, "", 0, cancel);
            }
        }
    }

    public static async Task<int> GetWavFileLengthMSecAsync(string wavFilePath)
    {
        checked
        {
            await using (var reader = new WaveFileReader(wavFilePath))
            {
                return (int)reader.TotalTime.TotalMilliseconds;
            }
        }
    }

    public async Task GenerateRandomSampleOfTextToVoiceAsync(int maxTryCount, string srcText, int textLengthOfRandomPart, string sampleVoiceWavDirName, int speakerId, int diffusionSteps, string dstVoiceDirPath, string tmpVoiceBoxDir, string tmpVoiceWavDir, string testName, CancellationToken cancel = default)
    {
        var now = DtOffsetNow;
        string seriesName = $"{now._ToYymmddInt(yearTwoDigits: true)}_{now._ToHhmmssInt():D6}_{testName}";

        ShuffledEndlessQueue<string> sampleVoiceFileNameShuffleQueue;


        List<int> randIntListAll = new();
        for (int i = 0; i <= 98; i++)
        {
            randIntListAll.Add(i);
        }
        ShuffledEndlessQueue<int> speakerIdShuffleQueueAll = new ShuffledEndlessQueue<int>(randIntListAll);

        List<int> randIntListTokutei = new();
        //randIntListTokutei.Add(8);
        //randIntListTokutei.Add(4);
        //randIntListTokutei.Add(4);
        //randIntListTokutei.Add(43);
        //randIntListTokutei.Add(43);
        //randIntListTokutei.Add(48);
        //randIntListTokutei.Add(58);
        //randIntListTokutei.Add(58);
        //randIntListTokutei.Add(58);
        //randIntListTokutei.Add(60);
        //randIntListTokutei.Add(60);
        //randIntListTokutei.Add(68);
        //randIntListTokutei.Add(90);
        //randIntListTokutei.Add(90);


        randIntListTokutei.Add(58);
        randIntListTokutei.Add(58);
        randIntListTokutei.Add(60);
        randIntListTokutei.Add(60);
        randIntListTokutei.Add(43);
        randIntListTokutei.Add(48);
        randIntListTokutei.Add(90);

        ShuffledEndlessQueue<int> speakerIdShuffleQueueTokutei = new ShuffledEndlessQueue<int>(randIntListTokutei);


        var randSampleVoiceFilesList = await Lfs.EnumDirectoryAsync(sampleVoiceWavDirName, false, wildcard: "*.wav", cancel: cancel);
        if (randSampleVoiceFilesList.Any())
        {
            sampleVoiceFileNameShuffleQueue = new ShuffledEndlessQueue<string>(randSampleVoiceFilesList.Select(x => x.FullPath));
        }
        else
        {
            throw new CoresLibException($"Directory '{sampleVoiceWavDirName}' has no music files.");
        }

        for (int i = 0; i < maxTryCount; i++)
        {
            try
            {
                var sampleVoicePath = sampleVoiceFileNameShuffleQueue.Dequeue();

                string thisText = srcText.Substring(Secure.RandSInt31() % (srcText.Length - textLengthOfRandomPart), textLengthOfRandomPart);

                int speakerIdToUse = speakerId;

                if (speakerIdToUse < 0)
                {
                    if (speakerIdToUse == -2)
                    {
                        int rand1 = Secure.RandSInt31() % 3;
                        if (rand1 != 0)
                        {
                            speakerIdToUse = speakerIdShuffleQueueTokutei.Dequeue();
                        }
                        else
                        {
                            speakerIdToUse = speakerIdShuffleQueueAll.Dequeue();
                        }
                    }
                    else
                    {
                        speakerIdToUse = speakerIdShuffleQueueAll.Dequeue();
                    }
                }

                if (speakerIdToUse < 0) speakerIdToUse = 58;

                string storyTitle = testName + "_" + i.ToString("D5");

                await ConvertTextToVoiceAsync(thisText, sampleVoicePath, dstVoiceDirPath, tmpVoiceBoxDir, tmpVoiceWavDir, randIntListTokutei, diffusionSteps, seriesName, storyTitle, new int[] { 100 }, cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
                await Task.Delay(1000);
            }
        }
    }

    public static async Task<List<int>> ReadSpeakerIDListAsync(string listFileName, CancellationToken cancel = default)
    {
        List<int> ret = new List<int>();
        var body = await Lfs.ReadStringFromFileAsync(listFileName, cancel: cancel);
        foreach (var line in body._GetLines(true, true, trim: true))
        {
            var tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ");
            if (tokens.Length >= 2 && tokens[0]._IsFilled())
            {
                int speakerId = tokens[0]._ToInt();
                int num = tokens[1]._ToInt();

                for (int i = 0; i < num; i++)
                {
                    ret.Add(speakerId);
                }
            }
        }

        return ret;
    }

    public async Task ConvertAllTextToVoiceAsync(string srcDirPath, string srcSampleVoiceFileNameOrRandDir, string speakerIdStrOrListFilePath, bool mixedMode, int diffusionSteps, string dstVoiceDirPath, string tmpVoiceBoxDir, string tmpVoiceWavDir, int[]? speedPercentList = null,
        Func<Task>? finalizeProc = null,
        CancellationToken cancel = default)
    {
        ShuffledEndlessQueue<string>? sampleVoiceFileNameShuffleQueue = null;
        if (await Lfs.IsDirectoryExistsAsync(srcSampleVoiceFileNameOrRandDir, cancel))
        {
            var randSampleVoiceFilesList = await Lfs.EnumDirectoryAsync(srcSampleVoiceFileNameOrRandDir, false, wildcard: "*.wav", cancel: cancel);
            if (randSampleVoiceFilesList.Any())
            {
                sampleVoiceFileNameShuffleQueue = new ShuffledEndlessQueue<string>(randSampleVoiceFilesList.Select(x => x.FullPath));
            }
            else
            {
                throw new CoresLibException($"Directory '{srcSampleVoiceFileNameOrRandDir}' has no music files.");
            }
        }

        ShuffledEndlessQueue<int>? speakerIdShuffleForRotatin = null;

        List<int> speakerIdListInOneFile = new List<int>();

        if (speakerIdStrOrListFilePath._IsNumber())
        {
            speakerIdListInOneFile.Add(speakerIdStrOrListFilePath._ToInt());
            mixedMode = false;
        }
        else
        {
            if (mixedMode == false)
            {
                speakerIdShuffleForRotatin = new ShuffledEndlessQueue<int>(await ReadSpeakerIDListAsync(speakerIdStrOrListFilePath, cancel));
            }
            else
            {
                speakerIdListInOneFile = await ReadSpeakerIDListAsync(speakerIdStrOrListFilePath, cancel);
            }
        }

        var seriesDirList = await Lfs.EnumDirectoryAsync(srcDirPath, cancel: cancel);

        foreach (var seriesDir in seriesDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false && x.Name.StartsWith("_") == false).OrderBy(x => x.Name, StrCmpi))
        {
            var srcTextList = await Lfs.EnumDirectoryAsync(seriesDir.FullPath, true, cancel: cancel);

            foreach (var srcTextFile in srcTextList.Where(x => x.IsFile && x.Name._IsExtensionMatch(Consts.Extensions.Text) && x.Name.StartsWith("_") == false && x.Name.EndsWith(".ok.txt", StrCmpi) == false).OrderBy(x => x.Name, StrCmpi)
                ._Shuffle()
                .ToList())
            {
                try
                {
                    string srcSampleVoiceFile = srcSampleVoiceFileNameOrRandDir;

                    if (sampleVoiceFileNameShuffleQueue != null)
                    {
                        srcSampleVoiceFile = sampleVoiceFileNameShuffleQueue.Dequeue();
                    }

                    /*int speakerIdToUse = speakerId;

                    if (speakerIdToUse < 0)
                    {
                        speakerIdToUse = Secure.RandSInt31() % 99; /* 0 ～ 98 */
                    //}*/

                    string srcText = await Lfs.ReadStringFromFileAsync(srcTextFile.FullPath, maxSize: 2 * 1024 * 1024, cancel: cancel);

                    string seriesName = PP.GetFileName(PP.GetDirectoryName(srcTextFile.FullPath))._Normalize(false, true, false, true);
                    string storyTitle = PPWin.GetFileNameWithoutExtension(srcTextFile.FullPath)._Normalize(false, true, false, true);

                    List<int> speakerIdListForThisFile;

                    if (mixedMode == false)
                    {
                        speakerIdListForThisFile = speakerIdShuffleForRotatin!.Dequeue()._SingleList();
                    }
                    else
                    {
                        speakerIdListForThisFile = speakerIdListInOneFile.ToList();
                    }

                    await ConvertTextToVoiceAsync(srcText, srcSampleVoiceFile, dstVoiceDirPath, tmpVoiceBoxDir, tmpVoiceWavDir, speakerIdListForThisFile, diffusionSteps, seriesName, storyTitle, speedPercentList, cancel);

                    // テキストファイルの先頭に _ を付ける
                    string newFilePath = PP.Combine(PP.GetDirectoryName(srcTextFile.FullPath), "_" + PP.GetFileName(srcTextFile.FullPath));

                    await Lfs.MoveFileAsync(srcTextFile.FullPath, newFilePath);

                    try
                    {
                        if (finalizeProc != null)
                        {
                            await finalizeProc();
                        }
                    }
                    catch (Exception ex)
                    {
                        srcTextFile.FullPath._Error();
                        ex._Error();
                    }
                }
                catch (Exception ex)
                {
                    srcTextFile.FullPath._Error();
                    ex._Error();
                }
            }
        }
    }

    public async Task VoiceChangeAsync(string srcPath, string srcSampleVoicePath, string? dstVoiceDirPath, string tmpVoiceWavDir, int diffusionSteps, string product, string series, string title, int track, int[]? speedPercentList = null, int headOnlySecs = 0, string? productAlternativeForTmpWavFiles = null, bool useOkFile = true, CancellationToken cancel = default)
    {
        if (speedPercentList == null || speedPercentList.Any() == false)
            speedPercentList = new int[] { 100 };

        if (productAlternativeForTmpWavFiles._IsEmpty()) productAlternativeForTmpWavFiles = product;

        string safeProduct = PPWin.MakeSafeFileName(product, true, true, true);
        string safeProductForTmpWav = PPWin.MakeSafeFileName(productAlternativeForTmpWavFiles, true, true, true);
        string safeSeries = PPWin.MakeSafeFileName(series, true, true, true);
        string safeTitle = PPWin.MakeSafeFileName(title, true, true, true);

        string safeVoiceTitle = PPWin.GetFileNameWithoutExtension(srcSampleVoicePath)._Normalize(false, true, false, true);

        await Lfs.CreateDirectoryAsync(tmpVoiceWavDir, cancel: cancel);

        if (dstVoiceDirPath._IsFilled())
        {
            await Lfs.CreateDirectoryAsync(dstVoiceDirPath, cancel: cancel);
        }

        string tmpVoiceWavPath = PP.Combine(tmpVoiceWavDir, $"{safeProductForTmpWav} - {safeSeries} - {safeTitle} - {safeVoiceTitle}.wav");

        string tagTitle = $"{safeProduct} - {safeSeries} - {safeTitle} - {safeVoiceTitle}";

        string srcTmpWavPath = await Lfs.GenerateUniqueTempFilePathAsync("seedvc_src", ".wav", cancel: cancel);

        string digest = $"voiceSamplePath={srcSampleVoicePath},diffusionSteps={diffusionSteps},targetMaxVolume={Settings.AdjustAudioTargetMaxVolume},targetMeanVolume={Settings.AdjustAudioTargetMeanVolume},headOnlySecs={headOnlySecs}";

        if (useOkFile == false || await Lfs.IsOkFileExists(tmpVoiceWavPath, digest, AiUtilVersion.CurrentVersion, cancel) == false)
        {
            await FfMpeg.EncodeAudioAsync(srcPath, srcTmpWavPath, FfMpegAudioCodec.Wav, tagTitle: tagTitle, headOnlySecs: headOnlySecs, cancel: cancel);

            await using (var seedvc = new AiUtilSeedVcEngine(this.Settings, this.FfMpeg))
            {
                await seedvc.ConvertAsync(srcTmpWavPath, tmpVoiceWavPath, srcSampleVoicePath, diffusionSteps, tagTitle, false, cancel: cancel);
                if (useOkFile) await Lfs.WriteOkFileAsync(tmpVoiceWavPath, new OkFileEmptyMetaData(), digest, AiUtilVersion.CurrentVersion, cancel);
                await Lfs.DeleteFileIfExistsAsync(srcTmpWavPath, cancel: cancel);
            }
        }

        if (dstVoiceDirPath._IsFilled())
        {
            string trackStr = track.ToString("D2");

            foreach (int speed in speedPercentList)
            {
                string speedStr = "x" + ((double)speed / 100.0).ToString(".00");

                MediaMetaData meta = new MediaMetaData
                {
                    Artist = $"{safeProduct} - {safeSeries} - {speedStr}",
                    Album = $"{safeProduct} - {speedStr}",
                    Title = $"{safeSeries} - [{trackStr}] {safeTitle} - {safeVoiceTitle} - {speedStr} - {safeProduct}",
                    TrackTotal = 999,
                    Track = track,
                };

                string dstVoiceFlacPath = PP.Combine(dstVoiceDirPath, safeProduct, safeSeries, $"{safeProduct} - {safeSeries} - {speedStr} - [{trackStr}] {safeTitle} - {safeVoiceTitle}.m4a");

                await FfMpeg.EncodeAudioAsync(tmpVoiceWavPath, dstVoiceFlacPath, FfMpegAudioCodec.Aac, 0, speed, meta, tagTitle, useOkFile, cancel: cancel);
            }
        }
    }

    public async Task ConvertTextToVoiceAsync(string srcText, string srcSampleVoicePath, string dstVoiceDirPath, string tmpVoiceBoxDir, string tmpVoiceWavDir, IEnumerable<int> speakerIdList, int diffusionSteps, string seriesName, string storyTitle, int[]? speedPercentList = null, CancellationToken cancel = default)
    {
        if (speedPercentList == null || speedPercentList.Any() == false)
            speedPercentList = new int[] { 100 };

        string speakerIdStr;
        if (speakerIdList.Count() == 1)
        {
            speakerIdStr = speakerIdList.Single().ToString("D3");
        }
        else
        {
            speakerIdStr = "mixed";
        }

        string safeSeriesName = PPWin.MakeSafeFileName(seriesName, true, true, true);

        string safeStoryTitle = PPWin.MakeSafeFileName(storyTitle, true, true, true);

        string safeVoiceTitle = PPWin.GetFileNameWithoutExtension(srcSampleVoicePath)._Normalize(false, true, false, true);

        string tmpVoiceBoxWavPath = PP.Combine(tmpVoiceBoxDir, $"{safeSeriesName} - {safeStoryTitle} - {speakerIdStr}.wav");

        string tagTitle = $"{safeSeriesName} - {safeStoryTitle} - {speakerIdStr}";

        FfMpegParsedList parsed;

        await using (var vv = new AiUtilVoiceVoxEngine(this.Settings, this.FfMpeg))
        {
            if (tagTitle._IsEmpty()) tagTitle = storyTitle._TruncStrEx(16);

            parsed = await vv.TextToWavAsync(srcText, speakerIdList, tmpVoiceBoxWavPath, tagTitle, true, cancel);
        }

        string tmpVoiceWavPath = PP.Combine(tmpVoiceWavDir, $"{safeSeriesName} - {safeStoryTitle} - {safeVoiceTitle} - {speakerIdStr}.wav");

        tagTitle = $"{safeSeriesName} - {safeStoryTitle} - {safeVoiceTitle} - {speakerIdStr}";

        AvUtilSeedVcMetaData vcMetaData;

        await using (var seedvc = new AiUtilSeedVcEngine(this.Settings, this.FfMpeg))
        {
            vcMetaData = await seedvc.ConvertAsync(tmpVoiceBoxWavPath, tmpVoiceWavPath, srcSampleVoicePath, diffusionSteps, tagTitle, true, parsed.Options_VoiceSegmentsList, cancel: cancel);
        }

        foreach (int speed in speedPercentList)
        {
            string speedStr = "x" + ((double)speed / 100.0).ToString(".00");

            MediaMetaData meta = new MediaMetaData
            {
                Album = safeSeriesName + $" - {speedStr}",
                Title = $"{safeStoryTitle} - {safeVoiceTitle} - {speakerIdStr} - {speedStr}",
                Artist = $"{safeSeriesName} - {safeVoiceTitle} - {speedStr}",
            };

            string tailStr = "";
            if (speakerIdStr._IsDiffi("mixed"))
            {
                tailStr = " - " + speakerIdStr;
            }

            string dstVoiceFlacPath = PP.Combine(dstVoiceDirPath, safeSeriesName, $"{speedStr} - {safeStoryTitle} - {safeVoiceTitle}{tailStr}.flac");

            await FfMpeg.EncodeAudioAsync(tmpVoiceWavPath, dstVoiceFlacPath, FfMpegAudioCodec.Flac, 0, speed, meta, tagTitle, true, voiceSegments: vcMetaData.VoiceSegments, cancel: cancel);
        }
    }

    public async Task<FfMpegParsedList> ReplaceSongVoiceAsync(string srcMusicWavPath, string srcVocalWavPath, string sampleVoicePath, string dstWavPath, string tmpDir, int diffusionSteps, CancellationToken cancel = default)
    {
        string digest = $"x:{srcMusicWavPath}:{srcVocalWavPath}:{sampleVoicePath}:{dstWavPath}:{tmpDir}:{diffusionSteps}"._Digest();
        var okRead = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstWavPath, digest, AiUtilVersion.CurrentVersion, cancel: cancel);
        if (okRead.IsOk && okRead.Value != null) return okRead.Value;

        string normalizedVocalWavPath = PP.Combine(tmpDir, "adjusted_vocal_cache", PP.MakeSafeParentDirAndFilenameWithoutExtension(srcVocalWavPath)) + ".wav";
        string normalizedMusicWavPath = PP.Combine(tmpDir, "adjusted_music_cache", PP.MakeSafeParentDirAndFilenameWithoutExtension(srcMusicWavPath)) + ".wav";
        string changedVocalCachePath = PP.Combine(tmpDir, "changed_vocal_cache", PP.MakeSafeParentDirAndFilenameWithoutExtension(srcVocalWavPath) + "_to_" + PP.GetFileNameWithoutExtension(sampleVoicePath)) + ".wav";

        var srcVocalParsed = await FfMpeg.AdjustAudioVolumeAsync(
            srcVocalWavPath, normalizedVocalWavPath, CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMaxVolume, CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMeanVolume,
            FfmpegAdjustVolumeOptiono.MeanOnly, PP.GetFileNameWithoutExtension(srcVocalWavPath), true, cancel: cancel);

        var srcMusicParsed = await FfMpeg.AdjustAudioVolumeAsync(
            srcMusicWavPath, normalizedMusicWavPath, CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMaxVolume, CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMeanVolume,
            FfmpegAdjustVolumeOptiono.MeanOnly, PP.GetFileNameWithoutExtension(srcMusicWavPath), true, cancel: cancel);

        // vocal と music の dB 差分を計算
        double dbDelta = srcMusicParsed.Item1.VolumeDetect_MeanVolume - srcVocalParsed.Item1.VolumeDetect_MeanVolume;

        AvUtilSeedVcMetaData vcMetaData;

        await using (var seedvc = new AiUtilSeedVcEngine(this.Settings, this.FfMpeg, true))
        {
            vcMetaData = await seedvc.ConvertAsync(normalizedVocalWavPath, changedVocalCachePath, sampleVoicePath, diffusionSteps, PP.GetFileNameWithoutExtension(srcVocalWavPath), true, null, cancel: cancel);
        }

        double targetVocalVolume = srcMusicParsed.Item2.VolumeDetect_MeanVolume - dbDelta;

        string adjustedChangedVoiceWavTmpPath = await Lfs.GenerateUniqueTempFilePathAsync("adj", ".wav", cancel: cancel);

        await FfMpeg.AdjustAudioVolumeAsync(changedVocalCachePath, adjustedChangedVoiceWavTmpPath, 0, targetVocalVolume, FfmpegAdjustVolumeOptiono.MeanOnly,
            PP.GetFileNameWithoutExtension(srcVocalWavPath), true, cancel: cancel);

        string dstTmpWavPath = await Lfs.GenerateUniqueTempFilePathAsync("adj2", ".wav", cancel: cancel);

        await FfMpeg.AddBgmToVoiceFileAsync(adjustedChangedVoiceWavTmpPath, normalizedMusicWavPath, dstTmpWavPath, false, true, cancel: cancel);

        var result = await FfMpeg.AdjustAudioVolumeAsync(dstTmpWavPath, dstWavPath, CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMaxVolume, CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMeanVolume,
            FfmpegAdjustVolumeOptiono.MeanOnly, PP.GetFileNameWithoutExtension(srcMusicWavPath), true, cancel: cancel);

        await Lfs.WriteOkFileAsync(dstWavPath, result.Item2, digest, AiUtilVersion.CurrentVersion, cancel: cancel);

        return result.Item2;
    }

    public async Task EncodeAndNormalizeAllMusicAsync(string srcDirPath, string dstMusicDirPath, string tmpBaseDir, string albumName, CancellationToken cancel = default)
    {
        string tmpMusicDirPath = PP.Combine(tmpBaseDir, "0_MusicRelease_TMP");

        var artistsDirList = await Lfs.EnumDirectoryAsync(srcDirPath, cancel: cancel);

        foreach (var artistDir in artistsDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false).OrderBy(x => x.Name, StrCmpi))
        {
            string artistName = artistDir.Name._NormalizeSoftEther(true);
            string safeArtistName = PPWin.MakeSafeFileName(artistName, true, true, true);

            var srcMusicList = await Lfs.EnumDirectoryAsync(artistDir.FullPath, true, cancel: cancel);

            foreach (var srcMusicFile in srcMusicList.Where(x => x.IsFile && x.Name._IsExtensionMatch(Consts.Extensions.Filter_MusicFiles)).OrderBy(x => x.Name, StrCmpi)._Shuffle().ToList())
            {
                try
                {
                    string songTitle = PPWin.GetFileNameWithoutExtension(srcMusicFile.Name)._NormalizeSoftEther(true);
                    string safeSongTitle = PPWin.MakeSafeFileName(songTitle, true, true, true);

                    string tmpOriginalSongWavPath = PP.Combine(tmpMusicDirPath, $"MusicRelease - {safeArtistName} - {safeSongTitle}.wav");

                    var result = await EncodeAndNormalizeMusicAsync(srcMusicFile.FullPath, tmpOriginalSongWavPath, safeSongTitle, cancel: cancel);

                    if (dstMusicDirPath._IsFilled())
                    {
                        string formalSongTitle = (result?.Meta?.Title)._NonNullTrimSe();
                        if (formalSongTitle._IsEmpty())
                        {
                            formalSongTitle = songTitle;
                        }

                        formalSongTitle = PPWin.MakeSafeFileName(formalSongTitle, false, true, true);

                        await Lfs.CreateDirectoryAsync(dstMusicDirPath, cancel: cancel);

                        var currentDstDirFiles = await Lfs.EnumDirectoryAsync(dstMusicDirPath, cancel: cancel);
                        var currentDstAacFiles = currentDstDirFiles.Where(x => x.IsFile && x.Name._IsExtensionMatch(".m4a"));

                        string albumName2;

                        for (int i = 1; ; i++)
                        {
                            string tmp1 = albumName._RemoveQuotation('[', ']');
                            bool f1 = albumName.StartsWith('[') && albumName.EndsWith(']');
                            tmp1 += "_p" + i.ToString();
                            if (f1)
                            {
                                tmp1 = "[" + tmp1 + "]";
                            }

                            if (currentDstAacFiles.Where(x => x.Name.StartsWith(tmp1 + " - ", StrCmpi)).Count() < 1000) // 1 つの Album 名は最大 1000 件
                            {
                                albumName2 = tmp1;
                                break;
                            }
                        }

                        MediaMetaData meta = new MediaMetaData
                        {
                            Album = albumName2,
                            Title = formalSongTitle + " - " + albumName2,
                            Artist = albumName2 + " - " + artistName,
                        };

                        string dstMusicAacPath = PP.Combine(dstMusicDirPath, $"{albumName2} - {safeArtistName} - {formalSongTitle._TruncStr(48)}.m4a");

                        await FfMpeg.EncodeAudioAsync(tmpOriginalSongWavPath, dstMusicAacPath, FfMpegAudioCodec.Aac, 0, 100, meta, safeSongTitle, cancel: cancel);
                    }
                }
                catch (Exception ex)
                {
                    srcMusicFile.FullPath._Error();
                    ex._Error();
                }
            }
        }
    }

    public async Task<FfMpegParsedList> EncodeAndNormalizeMusicAsync(string srcFilePath, string dstWavPath, string tagTitle, bool useOkFile = true, CancellationToken cancel = default)
    {
        if (tagTitle._IsEmpty()) tagTitle = PP.GetFileNameWithoutExtension(srcFilePath);

        if (dstWavPath._IsEmpty()) throw new CoresLibException("dstWavPath is empty.");

        if (useOkFile)
        {
            var okFileCached = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstWavPath, "", AiUtilVersion.CurrentVersion, cancel);
            if (okFileCached.IsOk && okFileCached.Value != null)
            {
                return okFileCached.Value;
            }
        }

        // 音量調整
        string adjustedWavFile = await Lfs.GenerateUniqueTempFilePathAsync(srcFilePath, cancel: cancel);
        var result = await FfMpeg.AdjustAudioVolumeAsync(srcFilePath, adjustedWavFile, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, FfmpegAdjustVolumeOptiono.MeanOnly /* ! */, tagTitle, false, cancel);

        await Lfs.CopyFileAsync(adjustedWavFile, dstWavPath, param: new(flags: FileFlags.AutoCreateDirectory), cancel: cancel);

        if (useOkFile)
        {
            await Lfs.WriteOkFileAsync(dstWavPath, result.Item1, "", AiUtilVersion.CurrentVersion, cancel);
        }

        return result.Item1;
    }

    public async Task ExtractAllMusicAndVocalAsync(string srcDirPath, string dstMusicDirPath, string tmpBaseDir, string musicOnlyAlbumName, CancellationToken cancel = default)
    {
        string tmpMusicDirPath = PP.Combine(tmpBaseDir, "1_MusicOnly_TMP");
        string tmpVocalDirPath = PP.Combine(tmpBaseDir, "2_VocalOnly_TMP");

        var artistsDirList = await Lfs.EnumDirectoryAsync(srcDirPath, cancel: cancel);

        foreach (var artistDir in artistsDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false).OrderBy(x => x.Name, StrCmpi))
        {
            string artistName = artistDir.Name._NormalizeSoftEther(true);
            string safeArtistName = PPWin.MakeSafeFileName(artistName, true, true, true);

            var srcMusicList = await Lfs.EnumDirectoryAsync(artistDir.FullPath, true, cancel: cancel);

            foreach (var srcMusicFile in srcMusicList.Where(x => x.IsFile && x.Name._IsExtensionMatch(Consts.Extensions.Filter_MusicFiles)).OrderBy(x => x.Name, StrCmpi)._Shuffle().ToList())
            {
                try
                {
                    string songTitle = PPWin.GetFileNameWithoutExtension(srcMusicFile.Name)._NormalizeSoftEther(true);
                    string safeSongTitle = PPWin.MakeSafeFileName(songTitle, true, true, true);

                    string tmpMusicWavPath = PP.Combine(tmpMusicDirPath, $"MusicOnly - {safeArtistName} - {safeSongTitle}.wav");
                    string tmpVocalWavPath = PP.Combine(tmpVocalDirPath, $"VocalOnly - {safeArtistName} - {safeSongTitle}.wav");

                    var result = await ExtractMusicAndVocalAsync(srcMusicFile.FullPath, tmpMusicWavPath, tmpVocalWavPath, safeSongTitle, cancel: cancel);

                    if (dstMusicDirPath._IsFilled())
                    {
                        string formalSongTitle = (result?.Meta?.Title)._NonNullTrimSe();
                        if (formalSongTitle._IsEmpty())
                        {
                            formalSongTitle = songTitle;
                        }

                        formalSongTitle = PPWin.MakeSafeFileName(formalSongTitle, false, true, true);

                        await Lfs.CreateDirectoryAsync(dstMusicDirPath, cancel: cancel);

                        var currentDstDirFiles = await Lfs.EnumDirectoryAsync(dstMusicDirPath, cancel: cancel);
                        var currentDstAacFiles = currentDstDirFiles.Where(x => x.IsFile && x.Name._IsExtensionMatch(".m4a"));

                        string musicOnlyAlbumName2;

                        for (int i = 1; ; i++)
                        {
                            string tmp1 = musicOnlyAlbumName._RemoveQuotation('[', ']');
                            bool f1 = musicOnlyAlbumName.StartsWith('[') && musicOnlyAlbumName.EndsWith(']');
                            tmp1 += "_p" + i.ToString();
                            if (f1)
                            {
                                tmp1 = "[" + tmp1 + "]";
                            }

                            if (currentDstAacFiles.Where(x => x.Name.StartsWith(tmp1 + " - ", StrCmpi)).Count() < 1000) // 1 つの Album 名は最大 1000 件
                            {
                                musicOnlyAlbumName2 = tmp1;
                                break;
                            }
                        }

                        MediaMetaData meta = new MediaMetaData
                        {
                            Album = musicOnlyAlbumName2,
                            Title = formalSongTitle + " - " + musicOnlyAlbumName2,
                            Artist = musicOnlyAlbumName2 + " - " + artistName,
                        };

                        string dstMusicAacPath = PP.Combine(dstMusicDirPath, $"{musicOnlyAlbumName2} - {safeArtistName} - {formalSongTitle._TruncStr(48)}.m4a");

                        await FfMpeg.EncodeAudioAsync(tmpMusicWavPath, dstMusicAacPath, FfMpegAudioCodec.Aac, 0, 100, meta, safeSongTitle, cancel: cancel);
                    }
                }
                catch (Exception ex)
                {
                    srcMusicFile.FullPath._Error();
                    ex._Error();
                }
            }
        }
    }


    public async Task<FfMpegParsedList> ExtractMusicAndVocalAsync(string srcFilePath, string dstMusicWavPath, string dstVocalWavPath, string tagTitle, bool useOkFile = true, CancellationToken cancel = default)
    {
        FfMpegParsedList ret = await TaskUtil.RetryAsync(async c =>
        {
            await using var uvr = new AiUtilUvrEngine(this.Settings, this.FfMpeg);

            return await uvr.ExtractAsync(srcFilePath, dstMusicWavPath, dstVocalWavPath, tagTitle, useOkFile, cancel);
        },
        1000,
        3,
        cancel,
        true);

        return ret;
    }

    public async Task<FfMpegParsedList> CompositAudioFileByAcxBcxTagsWithManyWavMaterialsAsync(string targetAudioFilePath, string dstAudioFilePath, FfMpegAudioCodec codec, int kbps,
        AiCompositWaveSettings settings,
        double targetSrcWavSpeed, MediaMetaData? metaData = null,
        CancellationToken cancel = default)
    {
        string digest = $"{targetAudioFilePath}:{metaData._ObjectToJson()}:{dstAudioFilePath}:{codec}:{targetSrcWavSpeed}"._Digest();

        var okCached = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstAudioFilePath, digest, AiUtilVersion.CurrentVersion, cancel: cancel);
        if (okCached.IsOk && okCached.Value != null)
        {
            return okCached.Value;
        }

        string tmpWavPath = await Lfs.GenerateUniqueTempFilePathAsync("a", "wav", cancel: cancel);

        await this.FfMpeg.EncodeAudioAsync(targetAudioFilePath, tmpWavPath, FfMpegAudioCodec.Wav, 0, useOkFile: false, cancel: cancel);

        var okRead = await Lfs.ReadOkFileAsync<FfMpegParsedList>(targetAudioFilePath, cancel: cancel);

        okRead.ThrowIfError();

        var usedFiles = await CompositWaveFileByAcxBcxTagsWithManyWavMaterialsAsync(tmpWavPath, okRead.Value._NullCheck(), tmpWavPath, settings, targetSrcWavSpeed, cancel);

        var parsed = await this.FfMpeg.EncodeAudioAsync(tmpWavPath, dstAudioFilePath, codec, kbps, metaData: metaData, useOkFile: false, cancel: cancel);

        parsed.Options_UsedBgmSrcMusicList = okRead.Value.Options_UsedBgmSrcMusicList._CloneDeep();
        parsed.Options_UsedOverwriteSrcMusicList = okRead.Value.Options_UsedOverwriteSrcMusicList._CloneDeep();
        parsed.Options_VoiceSegmentsList = okRead.Value.Options_VoiceSegmentsList._CloneDeep();
        parsed.Options_UsedMaterialsList = usedFiles.Options_UsedMaterialsList._CloneDeep();

        await Lfs.WriteOkFileAsync(dstAudioFilePath, parsed, digest, AiUtilVersion.CurrentVersion, cancel: cancel);

        return parsed;
    }

    public async Task<FfMpegParsedList> CompositWaveFileByAcxBcxTagsWithManyWavMaterialsAsync(string targetSrcWavPath, FfMpegParsedList targetSrcMetaData, string dstWavPath,
        AiCompositWaveSettings settings,
        double targetSrcWavSpeed,
        CancellationToken cancel = default)
    {
        FfMpegParsedList ret = targetSrcMetaData._CloneDeep();

        ret.Options_UsedMaterialsList = new();

        List<AiTaskOperationDesc> opList = new();

        var segments = targetSrcMetaData.Options_VoiceSegmentsList;
        segments._NullCheck();

        foreach (var rule in settings.RulesList)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];

                if (rule.EndTagStr._IsFilled())
                {
                    // 開始と終了の両方のタグがあるルール
                    if (seg.IsTag && rule.StartTagStr._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, "|").Where(x => x._IsSamei(seg.TagStr)).Any())
                    {
                        for (int j = i + 1; j < segments.Count; j++)
                        {
                            var seg2 = segments[j];
                            if (seg2.IsTag && rule.EndTagStr._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, "|").Where(x => x._IsSamei(seg2.TagStr)).Any())
                            {
                                if (rule.RuleData.MultipleMode_Count >= 2)
                                {
                                    for (int k = 0; k < rule.RuleData.MultipleMode_Count; k++)
                                    {
                                        double originalLen = seg2.TimePosition - seg.TimePosition;
                                        double thisLen = Math.Min(originalLen * Util.GenRandInterval(rule.RuleData.Multiplemode_SpanRatio._ToTimeSpanSecs()).TotalSeconds, originalLen);
                                        double thisStart = seg.TimePosition + (originalLen - thisLen) * Util.RandDouble0To1();

                                        opList.Add(new AiTaskOperationDesc { StartPosition = thisStart, EndPosition = thisStart + thisLen, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
                                    }
                                }
                                else
                                {
                                    opList.Add(new AiTaskOperationDesc { StartPosition = seg.TimePosition, EndPosition = seg2.TimePosition, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
                                }
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // 開始タグしかないルール
                    if (seg.IsTag && rule.StartTagStr._Split(StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries, "|").Where(x => x._IsSamei(seg.TagStr)).Any())
                    {
                        var seg2 = seg._CloneDeep();
                        seg2.TimePosition = seg.TimePosition + Util.GenRandInterval(rule.RuleData.Param.DurationWhenEndTagIsEmpty._ToTimeSpanSecs(), 60).TotalSeconds;
                        if (rule.RuleData.MultipleMode_Count >= 2)
                        {
                            for (int k = 0; k < rule.RuleData.MultipleMode_Count; k++)
                            {
                                double originalLen = seg2.TimePosition - seg.TimePosition;
                                double thisLen = Math.Min(originalLen * Util.GenRandInterval(rule.RuleData.Multiplemode_SpanRatio._ToTimeSpanSecs()).TotalSeconds, originalLen);
                                double thisStart = seg.TimePosition + (originalLen - thisLen) * Util.RandDouble0To1();

                                opList.Add(new AiTaskOperationDesc { StartPosition = thisStart, EndPosition = thisStart + thisLen, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
                            }
                        }
                        else
                        {
                            opList.Add(new AiTaskOperationDesc { StartPosition = seg.TimePosition, EndPosition = seg2.TimePosition, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
                        }
                    }
                }
            }
        }

        HashSet<string> alreadyUsedList = new HashSet<string>(StrCmpi);

        foreach (var op in opList)
        {
            try
            {
                var param = op.Rule.RuleData.Param;

                double wantLength = op.EndPosition / targetSrcWavSpeed - op.StartPosition / targetSrcWavSpeed;
                double minLength = param.MarginSecs * 2 + wantLength + (param.StdFadeInSecs + param.StdFadeOutSecs) * 1.5 + param.MaxRandAfterLengthSecs + param.MaxRandBeforeLengthSecs + 3.0;

                string wavFilePath = "";
                double wavFileLength = 0.0;

                for (int j = 0; j < 10000; j++)
                {
                    wavFilePath = op.MatFilesQueue.Dequeue();
                    wavFileLength = await GetWavFileLengthSecsAsync(wavFilePath, cancel: cancel);
                    if (wavFileLength >= minLength)
                    {
                        break;
                    }
                    wavFilePath = "";
                }

                if (wavFilePath._IsEmpty())
                {
                    // もう候補なし
                    break;
                }

                MediaUsedMaterialsSegment used = new MediaUsedMaterialsSegment
                {
                    WavPath = wavFilePath,
                    LengthSecs = op.StartPosition,
                    StartSecs = wantLength,
                };

                if (settings.CreateAudioEffectFilter != null)
                {
                    var filter = settings.CreateAudioEffectFilter(op, cancel);
                    if (filter != null)
                    {
                        op.Filter = filter;

                        used.FilterName = filter.FilterName;
                        used.FilterSettings = filter.EffectSettings._ToJObject();
                        used.FilterSpeedType = filter.FilterSpeedType;
                    }
                }

                ret.Options_UsedMaterialsList.Add(used);

                double before_length = param.MaxRandBeforeLengthSecs * Util.RandDouble0To1();
                double after_length = param.MaxRandAfterLengthSecs * Util.RandDouble0To1();

                double len = wantLength + before_length + after_length;

                double matStartPos = Util.RandDouble0To1() * (wavFileLength - len - param.MarginSecs * 2) + param.MarginSecs;

                double fadeIn = Util.GenRandInterval(param.StdFadeInSecs._ToTimeSpanSecs()).TotalSeconds;
                double fadeOut = Util.GenRandInterval(param.StdFadeOutSecs._ToTimeSpanSecs()).TotalSeconds;

                double targetStartPos = Math.Max(op.StartPosition / targetSrcWavSpeed - before_length, 0.0);

                op.Calced_MaterialWavPath = wavFilePath;
                op.Calced_TargetPositionSecs = targetStartPos;
                op.Calced_MeterialPositionSecs = matStartPos;
                op.Calced_LengthSecs = len;
                op.Calced_FadeInSecs = fadeIn;
                op.Calced_FadeOutSecs = fadeOut;

                double randomValueLeft = (Util.RandDouble0To1() - 0.5) * 2 * param.VolumeDeltaRandomRange;
                double randomValueRight = (Util.RandDouble0To1() - 0.5) * 2 * param.VolumeDeltaRandomRange;

                op.Calced_VolumeDelta_Left = param.VolumeDelta + randomValueLeft;
                op.Calced_VolumeDelta_Right = param.VolumeDelta + randomValueRight;
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }

        await CompositWaveWithFadeAsync(targetSrcWavPath, dstWavPath, opList, cancel: cancel);

        return ret;
    }

    public async Task<double> GetWavFileLengthSecsAsync(string wavPath, CancellationToken cancel = default)
    {
        await using (var targetSrcFileStream = File.Open(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await using var reader = new WaveFileReader(targetSrcFileStream);

            CheckWavFormat(reader.WaveFormat);

            return reader.TotalTime.TotalSeconds;
        }
    }

    public static void CheckWavFormat(WaveFormat format)
    {
        if (format.SampleRate != 44100 ||
                      format.Channels != 2 ||
                      format.BitsPerSample != 16 ||
                      format.Encoding != WaveFormatEncoding.Pcm)
        {
            throw new CoresLibException(
                $"指定されたファイルは " +
                $"44.1 kHz / 2 チャンネル / 16 ビット / 無圧縮 PCM ではありません。"
            );
        }
    }

    // By ChatGPT
    /// <summary>
    /// targetWav の position 秒目から sourceWav の長さ分を差し替えます。
    /// 差し替え区間の頭を fadeIn 秒かけてフェードイン（元音 → 新音へクロスフェード）、
    /// 尾を fadeOut 秒かけてフェードアウト（新音 → 元音へクロスフェード）します。
    /// </summary>
    /// <param name="targetWav">差し替え対象の PCM バイト列（ヘッダ除く）</param>
    /// <param name="sourceWav">挿入する PCM バイト列（ヘッダ除く）</param>
    /// <param name="position">差し替え開始位置（秒）</param>
    /// <param name="fadeIn">頭のクロスフェード時間（秒）</param>
    /// <param name="fadeOut">尾のクロスフェード時間（秒）</param>
    public static void ReplaceWavDataWithFadeInOut(
        Memory<byte> targetWav,
        ReadOnlyMemory<byte> sourceWav,
        double position,
        double fadeIn,
        double fadeOut)
    {
        const int sampleRate = 44100;
        const int channels = 2;
        const int bytesPerSample = 2;               // 16bit
        int blockAlign = channels * bytesPerSample; // 4 bytes/frame

        var targetSpan = targetWav.Span;
        var sourceSpan = sourceWav.Span;

        int totalTargetFrames = targetSpan.Length / blockAlign;
        int totalSourceFrames = sourceSpan.Length / blockAlign;

        // フレーム単位の位置・フェード長
        int posFrame = (int)Math.Round(position * sampleRate);
        int fadeInFrames = (int)Math.Round(fadeIn * sampleRate);
        int fadeOutFrames = (int)Math.Round(fadeOut * sampleRate);
        int endFrame = posFrame + totalSourceFrames;

        // ループ範囲を target の外にはみ出さないようにクリップ
        int start = Math.Max(0, posFrame);
        int end = Math.Min(totalTargetFrames, endFrame);

        for (int frame = start; frame < end; frame++)
        {
            int srcFrame = frame - posFrame;
            if (srcFrame < 0 || srcFrame >= totalSourceFrames)
                continue;

            // ゲイン計算 (0..1)
            double gain;
            if (fadeInFrames > 0 && srcFrame < fadeInFrames)
            {
                gain = (double)srcFrame / fadeInFrames;
            }
            else if (fadeOutFrames > 0 && srcFrame >= totalSourceFrames - fadeOutFrames)
            {
                gain = (double)(totalSourceFrames - srcFrame) / fadeOutFrames;
            }
            else
            {
                gain = 1.0;
            }

            // 安全に 0..1 にクランプ
            if (gain < 0.0) gain = 0.0;
            if (gain > 1.0) gain = 1.0;

            // 各チャンネルごとに読み書き
            for (int ch = 0; ch < channels; ch++)
            {
                int tgtOffset = frame * blockAlign + ch * bytesPerSample;
                int srcOffset = srcFrame * blockAlign + ch * bytesPerSample;

                // リトルエンディアン 16bit サンプル取得
                short orig = (short)(targetSpan[tgtOffset] | (targetSpan[tgtOffset + 1] << 8));
                short src = (short)(sourceSpan[srcOffset] | (sourceSpan[srcOffset + 1] << 8));

                // クロスフェード
                double mixed = src * gain + orig * (1.0 - gain);
                short result = (short)Math.Clamp((int)Math.Round(mixed), short.MinValue, short.MaxValue);

                // 書き戻し（リトルエンディアン）
                targetSpan[tgtOffset] = (byte)(result & 0xff);
                targetSpan[tgtOffset + 1] = (byte)((result >> 8) & 0xff);
            }
        }
    }



    /// <summary>
    /// 指定された時刻 currentTimeSec におけるフェードイン・フェードアウトの総合ゲイン値を計算する
    /// </summary>
    private static float ComputeFadeFactor(
        double currentTimeSec,
        double fadeInStartSec, double fadeInEndSec,
        double fadeOutStartSec, double fadeOutEndSec)
    {
        // フェードイン係数
        float fadeInFactor = 1.0f;
        // フェードイン区間が実質的にある場合のみ計算
        if (fadeInEndSec > fadeInStartSec)
        {
            if (currentTimeSec < fadeInStartSec)
            {
                // フェードイン開始前はゲイン 0
                fadeInFactor = 0.0f;
            }
            else if (currentTimeSec > fadeInEndSec)
            {
                // フェードイン完了後はゲイン 1
                fadeInFactor = 1.0f;
            }
            else
            {
                // その間は線形に 0->1 へ変化
                double length = fadeInEndSec - fadeInStartSec;
                fadeInFactor = (float)((currentTimeSec - fadeInStartSec) / length);
            }
        }

        // フェードアウト係数
        float fadeOutFactor = 1.0f;
        // フェードアウト区間が実質的にある場合のみ計算
        if (fadeOutEndSec > fadeOutStartSec)
        {
            if (currentTimeSec < fadeOutStartSec)
            {
                // フェードアウト開始前はゲイン 1
                fadeOutFactor = 1.0f;
            }
            else if (currentTimeSec > fadeOutEndSec)
            {
                // フェードアウト完了後はゲイン 0
                fadeOutFactor = 0.0f;
            }
            else
            {
                // その間は線形に 1->0 へ変化
                double length = fadeOutEndSec - fadeOutStartSec;
                double ratio = (currentTimeSec - fadeOutStartSec) / length;
                fadeOutFactor = (float)(1.0 - ratio);
            }
        }

        // 最終ゲインはフェードインとフェードアウトを掛け合わせる
        float amp = fadeInFactor * fadeOutFactor;

        // 安全のためクリップ（本来は 0～1 に収まるはず）
        if (amp < 0.0f) amp = 0.0f;
        if (amp > 1.0f) amp = 1.0f;

        return amp;
    }

    async Task CompositWaveWithFadeAsync(string targetSrcWavPath, string dstWavPath,
        IEnumerable<AiTaskOperationDesc> operations, CancellationToken cancel = default)
    {
        Memory<byte> targetSrcData;
        WaveFormat targetSrcWaveFormat;

        await using (var targetSrcFileStream = File.Open(targetSrcWavPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await using var targetSrcReader = new WaveFileReader(targetSrcFileStream);
            targetSrcWaveFormat = targetSrcReader.WaveFormat;

            CheckWavFormat(targetSrcWaveFormat);

            targetSrcData = targetSrcReader._ReadToEnd().AsMemory();
        }

        foreach (var op in operations)
        {
            if (op.Calced_MaterialWavPath._IsFilled())
            {
                cancel.ThrowIfCancellationRequested();

                try
                {
                    Memory<byte> matSrcData;

                    //Console.WriteLine(op.MeterialWavPath);
                    await using (var matFileStream = File.Open(op.Calced_MaterialWavPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        await using var matReader = new WaveFileReader(matFileStream);
                        WaveFormat matWaveFormat = matReader.WaveFormat;

                        CheckWavFormat(targetSrcWaveFormat);

                        // 波形フォーマット前提値
                        const int sampleRate = 44100;
                        const int channels = 2;
                        const int bitsPerSample = 16;
                        // 1サンプル(1ch)あたりのバイト数
                        const int bytesPerSample = bitsPerSample / 8; // = 2
                                                                      // 1フレーム(全ch合計)あたりのバイト数(2ch前提)
                        const int blockAlign = channels * bytesPerSample; // 4バイト
                                                                          // 合成を開始するフレーム位置(整数)
                        long sourceStartFrame = (long)(op.Calced_MeterialPositionSecs * sampleRate);

                        // 合成するフレーム数(整数)
                        long framesToProcess = (long)(op.Calced_LengthSecs * sampleRate);
                        if (framesToProcess <= 0)
                        {
                            return;
                        }

                        // バイト単位での開始位置
                        long sourceStartByte = sourceStartFrame * blockAlign;
                        int bytesToProcess = (int)(framesToProcess * blockAlign);

                        matReader.Seek(sourceStartByte, SeekOrigin.Begin);

                        matSrcData = await matReader._ReadAllAsync(bytesToProcess, cancel: cancel);

                        if (op.Filter != null)
                        {
                            // フィルタ適用
                            //Where();
                            op.Filter.PerformFilterFunc(matSrcData, cancel);
                        }
                    }

                    AiWaveMixUtil.MixWaveData(targetSrcData, matSrcData, op.Calced_TargetPositionSecs, 0, op.Calced_LengthSecs, op.Calced_VolumeDelta_Left, op.Calced_VolumeDelta_Right, op.Calced_FadeInSecs, op.Calced_FadeOutSecs);
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }
        }

        cancel.ThrowIfCancellationRequested();

        await Lfs.CreateDirectoryAsync(PP.GetDirectoryName(dstWavPath), cancel: cancel);

        await using (var dstFileStream = File.Create(dstWavPath))
        {
            await using var writer = new WaveFileWriter(dstFileStream, targetSrcWaveFormat);

            await writer.WriteAsync(targetSrcData, cancel);
        }
    }

    public async Task AddRandomBgmToAllVoiceFilesAsync(string srcVoiceDirRoot, string dstDirRoot, string srcMusicWavsDirPath, string replaceWavsDirPath, FfMpegAudioCodec codec, AiRandomBgmSettings settings, bool smoothMode, int kbps = 0, int fadeOutSecs = AiTask.DefaultFadeoutSecs, string? oldTagStr = null, string? newTagStr = null, double? adjustDeltaForConstant = null, CancellationToken cancel = default)
    {
        var srcFiles = await Lfs.EnumDirectoryAsync(srcVoiceDirRoot, true, cancel: cancel);

        foreach (var srcFile in srcFiles.Where(x => x.IsFile && x.Name._IsExtensionMatch(Consts.Extensions.Filter_MusicFiles) && x.Name._InStri("bgm") == false && PP.GetFileName(PP.GetDirectoryName(x.FullPath)).StartsWith("_") == false)
            .OrderBy(x => x.FullPath, StrCmpi)._Shuffle().ToList())
        {
            string relativeDirPth = PP.GetRelativeDirectoryName(PP.GetDirectoryName(srcFile.FullPath), srcVoiceDirRoot);

            string dstDirPath = PP.Combine(dstDirRoot, relativeDirPth);

            Con.WriteLine($"Add BGM: '{srcFile.FullPath}' -> '{dstDirPath}'");

            var result = await AddRandomBgmToVoiceFileAsync(srcFile.FullPath, dstDirPath, srcMusicWavsDirPath, replaceWavsDirPath, codec, settings, smoothMode, kbps, fadeOutSecs, true, oldTagStr, newTagStr, adjustDeltaForConstant, cancel);
        }
    }

    public async Task<(FfMpegParsedList Parsed, string DestFileName)> AddRandomBgmToVoiceFileAsync(string srcVoiceFilePath, string dstDir, string srcMusicWavsDirPath, string replaceWavsDirPath, FfMpegAudioCodec codec, AiRandomBgmSettings settings, bool smoothMode, int kbps = 0, int fadeOutSecs = AiTask.DefaultFadeoutSecs, bool useOkFile = true, string? oldTagStr = null, string? newTagStr = null, double? adjustDeltaForConstant = null, CancellationToken cancel = default)
    {
        List<AiRandomBgpReplaceRanges> replaceRanges = new();

        var srcVoiceFileMetaData = await FfMpeg.ReadMetaDataWithFfProbeAsync(srcVoiceFilePath, cancel: cancel);

        srcVoiceFileMetaData.ReParseMain();

        int srcDurationMsecs = srcVoiceFileMetaData.Input?.GetDurationMsecs() ?? -1;

        if (srcDurationMsecs < 0)
        {
            throw new CoresLibException($"Failed to get duration '{srcVoiceFilePath}'");
        }

        MediaMetaData srcMeta = srcVoiceFileMetaData.Meta;
        if (srcMeta == null) srcMeta = new MediaMetaData();

        var okFileMeta = await Lfs.ReadOkFileAsync<FfMpegParsedList>(srcVoiceFilePath, cancel: cancel);

        if (okFileMeta.IsOk && okFileMeta.Value != null && okFileMeta.Value.Meta != null)
        {
            okFileMeta.Value.ReParseMain();

            if (okFileMeta.Value.Meta.HasValue())
            {
                srcMeta = okFileMeta.Value.Meta;
            }

            var voiceSegList = okFileMeta.Value?.Options_VoiceSegmentsList;
            if (voiceSegList != null && voiceSegList.Count >= 1 && replaceWavsDirPath._IsFilled())
            {
                int currentLevel = 0;

                for (int i = 0; i < voiceSegList.Count; i++)
                {
                    var seg1 = voiceSegList[i];

                    if (seg1.TagStr._IsSamei("<ACX_START>"))
                    {
                        currentLevel += 1;
                    }
                    else if (seg1.TagStr._IsSamei("<ACX_END>"))
                    {
                        currentLevel -= 1;
                    }
                    else if (seg1.TagStr._IsSamei("<BCX_START>"))
                    {
                        currentLevel += 2;
                    }
                    else if (seg1.TagStr._IsSamei("<BCX_FINAL_START>"))
                    {
                        currentLevel += 2;
                    }
                    else if (seg1.TagStr._IsSamei("<XCSSTART>"))
                    {
                        currentLevel += 1;
                    }
                    else if (seg1.TagStr._IsSamei("<XCSEND>"))
                    {
                        currentLevel -= 1;
                    }
                    else if (seg1.TagStr._IsSamei("<BCX_END>") || seg1.TagStr._IsSamei("<BXC_END>"))
                    {
                        currentLevel -= 2;
                    }
                    else if (seg1.TagStr._IsSamei("<BCX_FINAL_END>") || seg1.TagStr._IsSamei("<BXC_FINAL_END>"))
                    {
                        currentLevel -= 2;
                    }

                    seg1.Level = currentLevel;

                    if (seg1.TagStr._IsSamei("<BCX_START>"))
                    {
                        for (int j = i; j < voiceSegList.Count; j++)
                        {
                            var seg2 = voiceSegList[j];
                            if (seg2.TagStr._IsSamei("<BCX_END>") || seg2.TagStr._IsSamei("<BXC_END>"))
                            {
                                replaceRanges.Add(new AiRandomBgpReplaceRanges
                                {
                                    Margin = 30,
                                    Length = seg2.TimePosition - seg1.TimePosition,
                                    FadeIn = 4,
                                    FadeOut = 4,
                                    StartPosition = seg1.TimePosition - 4,
                                });
                                break;
                            }
                        }
                    }
                    if (seg1.TagStr._IsSamei("<BCX_FINAL_START>"))
                    {
                        for (int j = i; j < voiceSegList.Count; j++)
                        {
                            var seg2 = voiceSegList[j];
                            if (seg2.TagStr._IsSamei("<BCX_FINAL_END>") || seg2.TagStr._IsSamei("<BXC_FINAL_END>"))
                            {
                                replaceRanges.Add(new AiRandomBgpReplaceRanges
                                {
                                    Margin = 30,
                                    Length = seg2.TimePosition - seg1.TimePosition,
                                    FadeIn = 4,
                                    FadeOut = 4,
                                    StartPosition = seg1.TimePosition - 4,
                                });
                                break;
                            }
                        }
                    }
                }
            }
        }

        MediaMetaData newMeta = srcMeta._CloneDeep();

        if (oldTagStr._IsFilled() && newTagStr._IsFilled())
        {
            newMeta.Album = newMeta.Album._ReplaceStr(oldTagStr, newTagStr);
            newMeta.AlbumArtist = newMeta.AlbumArtist._ReplaceStr(oldTagStr, newTagStr);
            newMeta.Title = newMeta.Title._ReplaceStr(oldTagStr, newTagStr);
            newMeta.Artist = newMeta.Artist._ReplaceStr(oldTagStr, newTagStr);
        }
        else
        {
            newMeta.Album = newMeta.Album._ReplaceStr(" - x", " - bgm_x");
            newMeta.AlbumArtist = newMeta.AlbumArtist._ReplaceStr(" - x", " - bgm_x");
            newMeta.Title = newMeta.Title._ReplaceStr(" - x", " - bgm_x");
            newMeta.Artist = newMeta.Artist._ReplaceStr(" - x", " - bgm_x");
        }

        string dstFileName;
        string dstExtension = FfMpegUtil.GetExtensionFromCodec(codec);

        if (oldTagStr._IsFilled() && newTagStr._IsFilled())
        {
            dstFileName = PP.GetFileNameWithoutExtension(srcVoiceFilePath)._ReplaceStr(oldTagStr, newTagStr) + dstExtension;
        }
        else
        {
            string tmp1 = PP.GetFileNameWithoutExtension(srcVoiceFilePath);
            string tmp2 = "bgm_" + tmp1;

            dstFileName = tmp2 + dstExtension;
        }

        string dstFilePath = PP.Combine(dstDir, dstFileName);

        if (dstFilePath._IsSamei(srcVoiceFilePath))
        {
            throw new CoresLibException($"dstFilePath == srcVoiceFilePath: '{srcVoiceFilePath}'");
        }

        double adjustDelta = smoothMode ? AiTask.BgmVolumeDeltaForSmooth : (adjustDeltaForConstant ?? AiTask.BgmVolumeDeltaForConstant);

        string digest = $"srcDurationMsecs={srcDurationMsecs},fadeOutSecs={fadeOutSecs},smoothMode={smoothMode},adjustDelta ={adjustDelta},codec={codec},kbps={kbps},meta={newMeta._ObjectToJson()._Digest()}";

        if (useOkFile)
        {
            var okParsed = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstFilePath, digest, AiUtilVersion.CurrentVersion, cancel);
            if (okParsed.IsOk && okParsed.Value != null)
            {
                return (okParsed.Value, dstFilePath);
            }
        }

        await Lfs.DeleteFileIfExistsAsync(dstFilePath, cancel: cancel);
        await Lfs.EnsureCreateDirectoryForFileAsync(dstFilePath, cancel: cancel);

        string voiceWavTmpPath = await Lfs.GenerateUniqueTempFilePathAsync("voicefile", ".wav", cancel: cancel);

        await FfMpeg.EncodeAudioAsync(srcVoiceFilePath, voiceWavTmpPath, FfMpegAudioCodec.Wav, useOkFile: false, cancel: cancel);

        string bgmWavTmpPath = await Lfs.GenerateUniqueTempFilePathAsync("bgmfile", ".wav", cancel: cancel);

        var retSrcList = await CreateRandomBgmFileAsync(srcMusicWavsDirPath, bgmWavTmpPath, srcDurationMsecs, settings, fadeOutSecs, adjustDelta, cancel);

        List<AiWaveConcatenatedSrcWavList> overwriteSrcList = new();

        // 音楽の一部をリプレース処理する
        if (replaceRanges != null && replaceRanges.Any())
        {
            Memory<byte> targetData;
            WaveFormat targetWaveFormat;
            await using (var targetWavStream = File.Open(bgmWavTmpPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await using var targetWavReader = new WaveFileReader(targetWavStream);
                targetWaveFormat = targetWavReader.WaveFormat;
                CheckWavFormat(targetWaveFormat);

                targetData = await targetWavStream._ReadToEndAsync();
            }

            var replaceSrcWavFiles = (await Lfs.EnumDirectoryAsync(replaceWavsDirPath, true, cancel: cancel)).Where(x => x.IsFile && x.Name._IsExtensionMatch(".wav"));
            ShuffledEndlessQueue<string> q = new(replaceSrcWavFiles.Select(x => x.FullPath));

            foreach (var range in replaceRanges)
            {
                double requireLength = range.Length + range.FadeIn;

                string srcWavPath = "";
                double srcWavLength = 0;

                for (int fail = 0; fail <= 1000; fail++)
                {
                    string tmp1 = q.Dequeue();

                    srcWavLength = await GetWavFileLengthSecsAsync(tmp1, cancel: cancel);
                    if (srcWavLength >= requireLength)
                    {
                        srcWavPath = tmp1;
                        break;
                    }
                }

                if (srcWavPath._IsEmpty())
                {
                    continue;
                }

                var item = new AiWaveConcatenatedSrcWavList
                {
                    WavFilePath = srcWavPath,
                };

                // 置換 wav ファイルの読み込み
                double srcStartPosition = (srcWavLength - requireLength) * Util.RandDouble0To1();

                Memory<byte> srcMemory;

                await using (var srcWavStream = File.Open(srcWavPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await using var srcWavReader = new WaveFileReader(srcWavStream);

                    CheckWavFormat(srcWavReader.WaveFormat);

                    // 波形フォーマット前提値
                    const int sampleRate = 44100;
                    const int channels = 2;
                    const int bitsPerSample = 16;
                    // 1サンプル(1ch)あたりのバイト数
                    const int bytesPerSample = bitsPerSample / 8; // = 2
                                                                  // 1フレーム(全ch合計)あたりのバイト数(2ch前提)
                    const int blockAlign = channels * bytesPerSample; // 4バイト
                                                                      // 合成を開始するフレーム位置(整数)
                    long sourceStartFrame = (long)(srcStartPosition * sampleRate);

                    // 合成するフレーム数(整数)
                    long framesToProcess = (long)(requireLength * sampleRate);
                    if (framesToProcess <= 0)
                    {
                        continue;
                    }

                    // バイト単位での開始位置
                    long sourceStartByte = sourceStartFrame * blockAlign;
                    int bytesToProcess = (int)(framesToProcess * blockAlign);

                    srcWavReader.Seek(sourceStartByte, SeekOrigin.Begin);

                    srcMemory = await srcWavReader._ReadAllAsync(bytesToProcess, cancel: cancel);
                }

                if (settings.CreateAudioEffectFilterForOverwriteMusicPartProc != null)
                {
                    var filter = settings.CreateAudioEffectFilterForOverwriteMusicPartProc(cancel);
                    if (filter != null)
                    {
                        filter.PerformFilterFunc(srcMemory, cancel);
                        item.FilterName = filter.FilterName;
                        item.FilterSpeedType = filter.FilterSpeedType;
                        item.FilterSettings = filter.EffectSettings._ToJObject();
                        item.TargetStartMsec = (int)(range.StartPosition * 1000);
                        item.TargetEndMsec = (int)((range.StartPosition + range.Length) * 1000);
                        item.SourceStartMsec = (int)(srcStartPosition * 1000);
                        item.SourceEndMsec = (int)((srcStartPosition + requireLength) * 1000);
                    }
                }

                // 合成処理
                ReplaceWavDataWithFadeInOut(targetData, srcMemory, range.StartPosition, range.FadeIn, range.FadeOut);

                overwriteSrcList.Add(item);
            }

            string bgmWavTmpPath2 = await Lfs.GenerateUniqueTempFilePathAsync("tmppath", ".wav", cancel: cancel);

            // 結果を wav に書き出す
            await using (var targetWavStream = File.Create(bgmWavTmpPath2))
            {
                await using var writer = new WaveFileWriter(targetWavStream, targetWaveFormat);

                await writer.WriteAsync(targetData, cancellationToken: cancel);
            }

            bgmWavTmpPath = bgmWavTmpPath2;
        }

        string voiceWavTmpPath2 = await Lfs.GenerateUniqueTempFilePathAsync("voiceWavTmpPath2", ".wav");

        // voiceWavTmpPath の後処理
        {
            Memory<byte> voiceWavData;
            WaveFormat voiceWavFormat;
            await using (var voiceWavStream = File.Open(voiceWavTmpPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await using var voiceWavReader = new WaveFileReader(voiceWavStream);
                voiceWavFormat = voiceWavReader.WaveFormat;
                CheckWavFormat(voiceWavFormat);

                voiceWavData = await voiceWavStream._ReadToEndAsync();
            }

            foreach (var seg in okFileMeta.Value?.Options_VoiceSegmentsList!)
            {
                if (seg.DataLength != 0 && seg.IsBlank == false && seg.IsTag == false)
                {
                    if (seg.Level >= 1)
                    {
                        AiAudioEffectSpeedType speedType = AiAudioEffectSpeedType.Light;

                        if (seg.Level == 2)
                        {
                            speedType = AiAudioEffectSpeedType.Normal;
                        }
                        else if (seg.Level >= 3)
                        {
                            speedType = AiAudioEffectSpeedType.Heavy;
                        }

                        var effect = AiAudioEffectCollection.AllCollectionRandomQueue.Dequeue();

                        AiAudioEffectFilter filter = new AiAudioEffectFilter(effect, speedType, 1.5 + (double)seg.Level, cancel: cancel);

                        int dataStartPositionInBytes = (int)AiWaveUtil.GetWavDataPositionInByteFromTime(seg.TimePosition, voiceWavFormat);
                        int dataEndPositionInBytes = (int)AiWaveUtil.GetWavDataPositionInByteFromTime(seg.TimePosition + seg.TimeLength, voiceWavFormat);
                        int dataLengthInBytes = dataEndPositionInBytes - dataStartPositionInBytes;

                        if (voiceWavData.Length < (dataStartPositionInBytes + dataLengthInBytes))
                        {
                        }
                        else
                        {
                            var voiceProcessTarget = voiceWavData.Slice(dataStartPositionInBytes, dataLengthInBytes);

                            filter.PerformFilterFunc(voiceProcessTarget, cancel);

                            seg.FilterName = filter.FilterName;
                            seg.FilterSettings = filter.EffectSettings._ToJObject();
                            seg.FilterSpeedType = filter.FilterSpeedType;
                        }
                    }
                }
            }

            // 結果を wav に書き出す
            await using (var voiceWavTmpPath2Stream = File.Create(voiceWavTmpPath2))
            {
                await using var writer = new WaveFileWriter(voiceWavTmpPath2Stream, voiceWavFormat);

                await writer.WriteAsync(voiceWavData, cancellationToken: cancel);
            }
        }

        string outWavTmpPath = await Lfs.GenerateUniqueTempFilePathAsync("bgmadded_file", ".wav", cancel: cancel);

        await FfMpeg.AddBgmToVoiceFileAsync(voiceWavTmpPath2, bgmWavTmpPath, outWavTmpPath, smoothMode: smoothMode, cancel: cancel);

        var parsed = await FfMpeg.EncodeAudioAsync(outWavTmpPath, dstFilePath, codec, kbps, useOkFile: false, metaData: newMeta, cancel: cancel);

        parsed.Options_UsedBgmSrcMusicList = retSrcList;
        parsed.Options_UsedOverwriteSrcMusicList = overwriteSrcList;
        parsed.Options_VoiceSegmentsList = okFileMeta.Value?.Options_VoiceSegmentsList;

        if (useOkFile)
        {
            await Lfs.WriteOkFileAsync(dstFilePath, parsed, digest, AiUtilVersion.CurrentVersion, cancel);
        }

        await Lfs.DeleteFileIfExistsAsync(voiceWavTmpPath, cancel: cancel);
        await Lfs.DeleteFileIfExistsAsync(bgmWavTmpPath, cancel: cancel);
        await Lfs.DeleteFileIfExistsAsync(outWavTmpPath, cancel: cancel);

        return (parsed, dstFilePath);
    }



    public async Task AddRandomMaterialsToAllVoiceAndAudioFilesAsync(string srcVoiceAudioFilePath, string dstDirRoot, string srcMaterialsDirPath, FfMpegAudioCodec codec,
        AiCompositWaveSettings settings,
        int kbps = 0, string? oldTagStr = null, string? newTagStr = null, CancellationToken cancel = default)
    {
        var srcFiles = await Lfs.EnumDirectoryAsync(srcVoiceAudioFilePath, true, cancel: cancel);

        foreach (var srcFile in srcFiles.Where(x => x.IsFile && x.Name._IsExtensionMatch(Consts.Extensions.Filter_MusicFiles) && x.Name._InStri("bgm_x1.00") && PP.GetFileName(PP.GetDirectoryName(x.FullPath)).StartsWith("_") == false)
            .OrderBy(x => x.FullPath, StrCmpi)._Shuffle().ToList())
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                string relativeDirPth = PP.GetRelativeDirectoryName(PP.GetDirectoryName(srcFile.FullPath), srcVoiceAudioFilePath);

                string dstDirPath = PP.Combine(dstDirRoot, relativeDirPth);

                Con.WriteLine($"Add Random Materials: '{srcFile.FullPath}' -> '{dstDirPath}'");

                var result = await AddRandomMaterialsToVoiceAndAudioFileAsync(srcFile.FullPath, dstDirPath,
                    settings, codec, kbps, oldTagStr, newTagStr, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
    }

    public async Task<(FfMpegParsedList Parsed, string DestFileName)> AddRandomMaterialsToVoiceAndAudioFileAsync(
        string srcVoiceAudioFilePath, string dstDir, AiCompositWaveSettings settings, FfMpegAudioCodec codec,
        int kbps = 0, string? oldTagStr = null, string? newTagStr = null, CancellationToken cancel = default)
    {
        var srcVoiceAudioFileMetaData = await FfMpeg.ReadMetaDataWithFfProbeAsync(srcVoiceAudioFilePath, cancel: cancel);

        srcVoiceAudioFileMetaData.ReParseMain();

        int srcDurationMsecs = srcVoiceAudioFileMetaData.Input?.GetDurationMsecs() ?? -1;

        if (srcDurationMsecs < 0)
        {
            throw new CoresLibException($"Failed to get duration '{srcVoiceAudioFilePath}'");
        }

        MediaMetaData srcMeta = srcVoiceAudioFileMetaData.Meta;
        if (srcMeta == null) srcMeta = new MediaMetaData();

        var okFileMeta = await Lfs.ReadOkFileAsync<FfMpegParsedList>(srcVoiceAudioFilePath, cancel: cancel);

        if (okFileMeta.IsError || okFileMeta.Value == null || okFileMeta.Value.Options_VoiceSegmentsList == null)
        {
            // Unsupported. Skip;
            return default;
        }

        if (okFileMeta.IsOk && okFileMeta.Value != null && okFileMeta.Value.Meta != null)
        {
            okFileMeta.Value.ReParseMain();

            if (okFileMeta.Value.Meta.HasValue())
            {
                srcMeta = okFileMeta.Value.Meta;
            }
        }

        MediaMetaData newMeta = srcMeta._CloneDeep();

        if (oldTagStr._IsFilled() && newTagStr._IsFilled())
        {
            newMeta.Album = newMeta.Album._ReplaceStr(oldTagStr, newTagStr, true);
            newMeta.AlbumArtist = newMeta.AlbumArtist._ReplaceStr(oldTagStr, newTagStr, true);
            newMeta.Title = newMeta.Title._ReplaceStr(oldTagStr, newTagStr, true);
            newMeta.Artist = newMeta.Artist._ReplaceStr(oldTagStr, newTagStr, true);
        }
        else
        {
            newMeta.Album = newMeta.Album._ReplaceStr(" - bgm_x", " - eff_x", true);
            newMeta.AlbumArtist = newMeta.AlbumArtist._ReplaceStr(" - bgm_x", " - eff_x", true);
            newMeta.Title = newMeta.Title._ReplaceStr(" - bgm_x", " - eff_x", true);
            newMeta.Artist = newMeta.Artist._ReplaceStr(" - bgm_x", " - eff_x", true);
        }

        string dstFileName;
        string dstExtension = FfMpegUtil.GetExtensionFromCodec(codec);

        if (oldTagStr._IsFilled() && newTagStr._IsFilled())
        {
            dstFileName = PP.GetFileNameWithoutExtension(srcVoiceAudioFilePath)._ReplaceStr(oldTagStr, newTagStr) + dstExtension;
        }
        else
        {
            string tmp1 = PP.GetFileNameWithoutExtension(srcVoiceAudioFilePath);
            if (tmp1.StartsWith("bgm_", StrCmpi)) tmp1 = tmp1.Substring(4); // ファイル名から "bgm_" を消す
            string tmp2 = "eff_" + tmp1;

            dstFileName = tmp2 + dstExtension;
        }

        string dstFilePath = PP.Combine(dstDir, dstFileName);

        if (dstFilePath._IsSamei(srcVoiceAudioFilePath))
        {
            throw new CoresLibException($"dstFilePath == srcVoiceFilePath: '{srcVoiceAudioFilePath}'");
        }

        var parsed = await this.CompositAudioFileByAcxBcxTagsWithManyWavMaterialsAsync(srcVoiceAudioFilePath, dstFilePath, codec, kbps, settings, 1.0, newMeta, cancel: cancel);

        return (parsed, dstFilePath);
    }

    public async Task<List<string>> CreateManyMusicMixAsync(DateTimeOffset timeStamp, IEnumerable<string> srcWavFilesPathList, string destDirPath, string albumName, string artist, AiRandomBgmSettings settings, FfMpegAudioCodec codec = FfMpegAudioCodec.Aac, int kbps = 0, int numRotate = 1, int minTracks = 1, int durationOfSingleFileMsecs = 3 * 60 * 60 * 1000, int fadeOutSecs = AiTask.DefaultFadeoutSecs, CancellationToken cancel = default)
    {
        List<string> ret = new List<string>();

        if (numRotate <= 0) numRotate = 1;
        if (minTracks <= 0) minTracks = 1;

        ShuffledEndlessQueue<string> q = new ShuffledEndlessQueue<string>(srcWavFilesPathList);

        string timestampStr = "[" + timeStamp._ToYymmddStr(yearTwoDigits: true) + "_" + timeStamp._ToHhmmssStr().Substring(0, 4) + "]";

        string secstr = $"{(settings.Medley_SingleFilePartMSecs / 1000)}s";

        await Lfs.CreateDirectoryAsync(destDirPath, cancel: cancel);

        for (int i = 0; i < 999; i++)
        {
            if (q.NumRotate >= numRotate && i >= minTracks)
            {
                break;
            }

            int rotateForDisplay = q.NumRotate + 1;
            if (rotateForDisplay <= 0) rotateForDisplay = 1;

            string dstFilePath = PP.Combine(destDirPath, $"{albumName} - {artist} - {timestampStr} - Track_{(i + 1).ToString("D3")} - r{rotateForDisplay} - {secstr}{FfMpegUtil.GetExtensionFromCodec(codec)}");
            if (await Lfs.IsFileExistsAsync(dstFilePath, cancel))
            {
                Con.WriteLine($"File '{dstFilePath}' already exists. Skip this artist at all.");
                return new();
            }

            Con.WriteLine($"--- Concat {albumName} - {artist} Track #{(i + 1).ToString("D3")} (NumRotate = {q.NumRotate})");

            string dstTmpFileName = await Lfs.GenerateUniqueTempFilePathAsync("concat3", ".wav", cancel: cancel);

            var srcFilesList = await ConcatWavFileFromRandomDirAsync(q, dstTmpFileName, durationOfSingleFileMsecs, settings, fadeOutSecs, cancel);

            MediaMetaData meta = new MediaMetaData
            {
                Track = i + 1,
                TrackTotal = 999,
                Album = $"{albumName} - {timestampStr}",
                Artist = $"{albumName} - {artist} - {timestampStr} - {secstr}",
                Title = $"{albumName} - {artist} - Track_{(i + 1).ToString("D3")} - {secstr} - r{rotateForDisplay}",
            };

            Con.WriteLine($"--- Concat {albumName} - {artist} Track #{(i + 1).ToString("D3")} (NumRotate = {q.NumRotate})");

            string encodeDstTmpFilePath = await Lfs.GenerateUniqueTempFilePathAsync("encode3", FfMpegUtil.GetExtensionFromCodec(codec), cancel: cancel);

            var res = await FfMpeg.EncodeAudioAsync(dstTmpFileName, encodeDstTmpFilePath, codec, kbps, 100, meta, $"{artist} - Track_{(i + 1).ToString("D3")}", sourceFilePathList: srcFilesList, useOkFile: false, cancel: cancel);

            await Lfs.CopyFileAsync(encodeDstTmpFilePath, dstFilePath, cancel: cancel);

            await Lfs.WriteOkFileAsync(dstFilePath, res, version: AiUtilVersion.CurrentVersion, cancel: cancel);

            ret.Add(dstFilePath);

            await Lfs.DeleteFileIfExistsAsync(dstTmpFileName, cancel: cancel);
            await Lfs.DeleteFileIfExistsAsync(encodeDstTmpFilePath, cancel: cancel);
        }

        return ret;
    }


    public async Task<List<AiWaveConcatenatedSrcWavList>> CreateRandomBgmFileAsync(string srcMusicWavsDirPath, string dstWavFilePath, int totalDurationMsecs, AiRandomBgmSettings settings, int fadeOutSecs = AiTask.DefaultFadeoutSecs, double adjustDelta = AiTask.BgmVolumeDeltaForConstant, CancellationToken cancel = default)
    {
        string dstTmpFileName = await Lfs.GenerateUniqueTempFilePathAsync("concat2", ".wav", cancel: cancel);

        List<AiWaveConcatenatedSrcWavList> retSrcList = await ConcatWavFileFromRandomDirAsync(srcMusicWavsDirPath, dstTmpFileName, totalDurationMsecs, settings, fadeOutSecs, cancel);

        await FfMpeg.AdjustAudioVolumeAsync(dstTmpFileName, dstWavFilePath, adjustDelta, cancel);

        return retSrcList;
    }

    public async Task<List<AiWaveConcatenatedSrcWavList>> ConcatWavFileFromRandomDirAsync(string srcMusicWavsDirPath, string dstWavFilePath, int totalDurationMsecs, AiRandomBgmSettings settings, int fadeOutSecs = AiTask.DefaultFadeoutSecs, CancellationToken cancel = default)
    {
        var srcWavList = await Lfs.EnumDirectoryAsync(srcMusicWavsDirPath, true, wildcard: "*.wav", cancel: cancel);

        List<string> fileNamesList = new List<string>();

        foreach (var srcWav in srcWavList.Where(x => x.IsFile).OrderBy(x => x.FullPath, StrCmpi))
        {
            if (await Lfs.IsOkFileExists(srcWav.FullPath, cancel: cancel))
            {
                fileNamesList.Add(srcWav.FullPath);
            }
        }

        ShuffledEndlessQueue<string> srcQueue = new ShuffledEndlessQueue<string>(fileNamesList);

        return await ConcatWavFileFromRandomDirAsync(srcQueue, dstWavFilePath, totalDurationMsecs, settings, fadeOutSecs, cancel);
    }

    public async Task<List<AiWaveConcatenatedSrcWavList>> ConcatWavFileFromRandomDirAsync(ShuffledEndlessQueue<string> srcMusicWavsFilePathQueue, string dstWavFilePath, int totalDurationMsecs, AiRandomBgmSettings settings, int fadeOutSecs = AiTask.DefaultFadeoutSecs, CancellationToken cancel = default)
    {
        List<AiWaveConcatenatedSrcWavList> retSrcList;

        string dstTmpFileName;

        if (fadeOutSecs <= 0)
        {
            dstTmpFileName = dstWavFilePath;
        }
        else
        {
            dstTmpFileName = await Lfs.GenerateUniqueTempFilePathAsync("concat", ".wav", cancel: cancel);
        }

        if (settings.Medley == false)
        {
            retSrcList = await ConcatWavFileFromQueueAsync(srcMusicWavsFilePathQueue, totalDurationMsecs, dstTmpFileName, cancel);
        }
        else
        {
            retSrcList = await AiWaveConcatenateWithCrossFadeUtil.ConcatenateAsync(srcMusicWavsFilePathQueue, totalDurationMsecs, settings, dstTmpFileName, cancel);
        }

        if (fadeOutSecs > 0)
        {
            await Lfs.DeleteFileIfExistsAsync(dstWavFilePath, cancel: cancel);
            await Lfs.EnsureCreateDirectoryForFileAsync(dstWavFilePath, cancel: cancel);

            int totalSecs = totalDurationMsecs / 1000;
            int startSecs = Math.Max(totalSecs - fadeOutSecs, 0);
            int fadeSecs = Math.Min(fadeOutSecs, totalSecs - startSecs);

            await FfMpeg.RunFfMpegAsync($"-y -i {dstTmpFileName._EnsureQuotation()} -vn -reset_timestamps 1 -ar 44100 -ac 2 -c:a pcm_s16le " +
                $"-map_metadata -1 -f wav -af \"afade=t=out:st={startSecs}:d={fadeSecs}\" {dstWavFilePath._EnsureQuotation()}",
                cancel: cancel);

            await Lfs.DeleteFileIfExistsAsync(dstTmpFileName, cancel: cancel);
        }

        return retSrcList;
    }

    public static async Task<List<AiWaveConcatenatedSrcWavList>> ConcatWavFileFromQueueAsync(
        ShuffledEndlessQueue<string> srcWavFilesQueue,
        int totalDurationMsecs,
        string dstWavFilePath, CancellationToken cancel = default)
    {
        List<AiWaveConcatenatedSrcWavList> retSrcList = new List<AiWaveConcatenatedSrcWavList>();

        await Lfs.DeleteFileIfExistsAsync(dstWavFilePath, cancel: cancel);
        await Lfs.EnsureCreateDirectoryForFileAsync(dstWavFilePath, cancel: cancel);

        // 1. キューが空なら何もできないので無音の WAV を生成するか、そのまま return
        if (srcWavFilesQueue.Count == 0)
        {
            // 無音で書き出したい場合は下記のように処理する
            // （フォーマットがわからないので、ここでは 44.1kHz / 16bit / stereo の例として無音を作る）
            await CreateSilentWavAsync(44100, 16, 2, totalDurationMsecs, dstWavFilePath, cancel);
            // return;

            // 何もしない場合
            return retSrcList;
        }

        // 2. まずキューの先頭を覗いてフォーマットを取得する
        string firstWavPath = srcWavFilesQueue.Peek();

        // File.Open は async メソッドがあるのでそちらを使う（NAudio ではコンストラクタが同期的だが、可能な部分のみ async 化）
        await using var firstFileStream = File.Open(firstWavPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var firstReader = new WaveFileReader(firstFileStream);
        WaveFormat waveFormat = firstReader.WaveFormat;

        // 3. 書き出し先 WaveFileWriter を準備
        //    WaveFileWriter はコンストラクタが同期的だが、FileStream は async で開く。
        await using var destFileStream = File.Create(dstWavFilePath);
        using var writer = new WaveFileWriter(destFileStream, waveFormat);

        // 4. 書き出すべき合計バイト数を計算する
        //    (サンプリング数 = サンプルレート * (totalDurationMsecs / 1000.0) を丸める)
        long totalSamples = (long)Math.Round(waveFormat.SampleRate * (totalDurationMsecs / 1000.0));
        long totalBytesToWrite = totalSamples * waveFormat.BlockAlign;
        long writtenBytes = 0;

        // 5. ソース WAV ファイルを順番に読み込み、合計サイズに達するまで書き込む
        var buffer = new byte[65536];
        while (srcWavFilesQueue.Count > 0 && writtenBytes < totalBytesToWrite)
        {
            string currentWavPath = srcWavFilesQueue.Dequeue();

            await using var sourceStream = File.Open(currentWavPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var readerWav = new WaveFileReader(sourceStream);

            retSrcList.Add(new AiWaveConcatenatedSrcWavList { WavFilePath = currentWavPath });

            int bytesRead;
            // WaveFileReader は通常同期的な Read しか提供しないが、Stream としての ReadAsync は呼び出せる場合がある。
            // ただし内部実装次第で実質同期になる可能性あり。
            while ((bytesRead = await readerWav.ReadAsync(buffer, 0, buffer.Length, cancel)) > 0
                   && writtenBytes < totalBytesToWrite)
            {
                // まだ書けるバイト数
                long bytesRemaining = totalBytesToWrite - writtenBytes;

                // 今回読み込んだ分が書き込める残り容量を超える場合は切り詰め
                if (bytesRead > bytesRemaining)
                {
                    bytesRead = (int)bytesRemaining;
                }

                // dst へ書き込み (これは async メソッド)
                await writer.WriteAsync(buffer, 0, bytesRead, cancel);

                writtenBytes += bytesRead;
            }
        }

        // 6. まだ必要なバイト数が残っていたら無音(0)で埋める
        if (writtenBytes < totalBytesToWrite)
        {
            long bytesToFill = totalBytesToWrite - writtenBytes;
            var silenceBuffer = new byte[8192];

            while (bytesToFill > 0)
            {
                int writeSize = (int)Math.Min(silenceBuffer.Length, bytesToFill);

                await writer.WriteAsync(silenceBuffer, 0, writeSize, cancel);

                bytesToFill -= writeSize;
            }
        }

        // WaveFileWriter / Stream を using により閉じる (Disposeされる) ことで
        //  WAV ヘッダが正しく書き込まれる。
        return retSrcList;
    }

    private static async Task CreateSilentWavAsync(int sampleRate, int bitsPerSample, int channels,
        int durationMsecs, string filePath, CancellationToken cancel = default)
    {
        var waveFormat = new WaveFormat(sampleRate, bitsPerSample, channels);
        long totalSamples = (long)Math.Round(waveFormat.SampleRate * (durationMsecs / 1000.0));
        long totalBytesToWrite = totalSamples * waveFormat.BlockAlign;

        await using var fs = File.Create(filePath);
        await using var writer = new WaveFileWriter(fs, waveFormat);

        var silenceBuffer = new byte[65536];
        long written = 0;
        while (written < totalBytesToWrite)
        {
            int toWrite = (int)Math.Min(silenceBuffer.Length, totalBytesToWrite - written);
            await writer.WriteAsync(silenceBuffer, 0, toWrite, cancel);
            written += toWrite;
        }
    }

}

public class AiUtilBasicSettings
{
    public string AiTest_UvrCli_BaseDir = "";
    public string AiTest_VoiceBox_ExePath = "";
    public string AiTest_VoiceBox_ExeArgs = "";
    public string AiTest_SeedVc_BaseDir = "";
    public double AdjustAudioTargetMaxVolume = CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMaxVolume;
    public double AdjustAudioTargetMeanVolume = CoresConfig.DefaultAiUtilSettings.AdjustAudioTargetMeanVolume;
    public int VoiceBoxLocalhostPort = Consts.Ports.VoiceVox;
}

public class AvUtilSeedVcMetaData
{
    public List<MediaVoiceSegment>? VoiceSegments = null;
};

public class AiUtilSeedVcEngine : AiUtilBasicEngine
{
    public FfMpegUtil FfMpeg { get; }
    public bool SingMode { get; }

    public AiUtilSeedVcEngine(AiUtilBasicSettings settings, FfMpegUtil ffMpeg, bool singMode = false) : base(settings, "SEED-VC", settings.AiTest_SeedVc_BaseDir)
    {
        this.SingMode = singMode;
        this.FfMpeg = ffMpeg;
    }

    class SilentRange
    {
        public double StartTime;
        public double Duration;
    }

    public async Task<AvUtilSeedVcMetaData> ConvertAsync(string srcWavPath, string dstWavPath, string voiceSamplePath, int diffusionSteps, string tagTitle = "", bool useOkFile = true, List<MediaVoiceSegment>? voiceSegments = null, CancellationToken cancel = default)
    {
        if (tagTitle._IsEmpty()) tagTitle = PP.GetFileNameWithoutExtension(srcWavPath);

        string digest = $"voiceSamplePath={voiceSamplePath},diffusionSteps={diffusionSteps},targetMaxVolume={Settings.AdjustAudioTargetMaxVolume},targetMeanVolume={Settings.AdjustAudioTargetMeanVolume}";

        if (useOkFile)
        {
            var metaRet = await Lfs.ReadOkFileAsync<AvUtilSeedVcMetaData>(dstWavPath, digest, AiUtilVersion.CurrentVersion, cancel: cancel);
            if (metaRet.IsOk)
            {
                return metaRet.Value!;
            }
        }

        List<SilentRange> silentRangeList = new();

        if (voiceSegments != null)
        {
            var tmpList = voiceSegments.ToArray().Reverse().ToList();
            foreach (var item in tmpList)
            {
                if (item.IsBlank || item.IsSleep)
                {
                    if (item.TimeLength >= 0.01)
                    {
                        silentRangeList.Add(new SilentRange { StartTime = item.TimePosition, Duration = item.TimeLength, });
                    }
                }
            }
        }

        await TaskUtil.RetryAsync(async c =>
        {
            await ConvertInternalAsync(srcWavPath, dstWavPath, voiceSamplePath, diffusionSteps, silentRangeList, tagTitle, cancel);

            return true;
        },
        5, 200, cancel, true);

        var meta = new AvUtilSeedVcMetaData
        {
            VoiceSegments = voiceSegments,
        };

        if (useOkFile)
        {
            await Lfs.WriteOkFileAsync(dstWavPath, meta, digest, AiUtilVersion.CurrentVersion, cancel: cancel);
        }

        return meta;
    }

    async Task ConvertInternalAsync(string srcWavPath, string dstWavPath, string voiceSamplePath, int diffusionSteps, IEnumerable<SilentRange>? silentRanges = null, string tagTitle = "", CancellationToken cancel = default)
    {
        string aiSrcPath = BaseDirPath._CombinePath("test_in_data", "_aiutil_src.wav");

        string aiSamplePath = BaseDirPath._CombinePath("test_in_data", "_aiutil_sample.wav");

        await Lfs.DeleteFileIfExistsAsync(aiSrcPath, cancel: cancel);

        await Lfs.DeleteFileIfExistsAsync(aiSamplePath, cancel: cancel);
        await FfMpeg.AdjustAudioVolumeAsync(voiceSamplePath, aiSamplePath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, FfmpegAdjustVolumeOptiono.MeanOnly, voiceSamplePath, false, cancel);

        await Lfs.DeleteFileIfExistsAsync(aiSrcPath, cancel: cancel);
        await FfMpeg.AdjustAudioVolumeAsync(srcWavPath, aiSrcPath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, FfmpegAdjustVolumeOptiono.MeanOnly, tagTitle, false, cancel);

        await DeleteAllAiProcessOutputFilesAsync(cancel);

        if (tagTitle._IsEmpty())
        {
            tagTitle = srcWavPath._GetFileNameWithoutExtension();
        }
        string tag = $"Convert ('{tagTitle._TruncStrEx(16)}')";

        int srcWavFileLen = await AiTask.GetWavFileLengthMSecAsync(srcWavPath);
        int timeout = (srcWavFileLen * 2) + (60 * 1000);

        long startTick = TickHighresNow;

        EasyExecResult result;

        if (this.SingMode == false)
        {
            result = await this.RunVEnvPythonCommandsAsync(
                $"python inference.py --source test_in_data/_aiutil_src.wav --target test_in_data/_aiutil_sample.wav " +
                $"--output test_out_dir --diffusion-steps {diffusionSteps} --length-adjust 1.0 --inference-cfg-rate 1.0 --f0-condition False " +
                $"--auto-f0-adjust False --semi-tone-shift 0 --config runs/test02/config_dit_mel_seed_uvit_whisper_small_wavenet.yml " +
                $"--fp16 True", timeout, printTag: tag, cancel: cancel);
        }
        else
        {
            result = await this.RunVEnvPythonCommandsAsync(
                $"python inference.py --source test_in_data/_aiutil_src.wav --target test_in_data/_aiutil_sample.wav " +
                $"--output test_out_dir --diffusion-steps {diffusionSteps} --length-adjust 1.0 --inference-cfg-rate 1.0 --f0-condition True " +
                $"--auto-f0-adjust True --semi-tone-shift 0 --config runs/test02/config_dit_mel_seed_uvit_whisper_base_f0_44k.yml " +
                $"--fp16 True", timeout, printTag: tag, cancel: cancel);
        }

        long endTick = TickHighresNow;
        AiTask.TotalGpuProcessTimeMsecs.Add(endTick - startTick);

        if (result.OutputAndErrorStr._GetLines().Where(x => x.StartsWith("RTF: ")).Any() == false)
        {
            throw new CoresLibException($"{tag} failed.");
        }

        string aiOutDir = PP.Combine(this.BaseDirPath, "test_out_dir");

        var files = await Lfs.EnumDirectoryAsync(aiOutDir, wildcard: "*.wav", cancel: cancel);
        var aiDstFile = files.Single();

        string tmpSrcPath = aiDstFile.FullPath;

        // 出力結果の最後の部分を指定した秒数分無音で埋める (Seed-VC の不具合があり、無音部分からゴーストが発生するため)
        if (silentRanges != null && silentRanges.Any())
        {
            Memory<byte> voiceWavData;
            WaveFormat voiceWavFormat;
            await using (var voiceWavStream = File.Open(aiDstFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await using var voiceWavReader = new WaveFileReader(voiceWavStream);
                voiceWavFormat = voiceWavReader.WaveFormat;
                voiceWavData = await voiceWavStream._ReadToEndAsync();
            }

            double srcLen = AiWaveUtil.CalcWavDurationFromSizeInByte(voiceWavData.Length, voiceWavFormat);

            foreach (var range in silentRanges)
            {
                if (range.Duration >= 0.1)
                {
                    int zeroStartInByte = (int)AiWaveUtil.GetWavDataPositionInByteFromTime(range.StartTime, voiceWavFormat);

                    if (zeroStartInByte < voiceWavData.Length)
                    {
                        Memory<byte> silentData = AiWaveUtil.GenerateSilentNoiseData(range.Duration, voiceWavFormat);

                        silentData.CopyTo(voiceWavData.Slice(zeroStartInByte, Math.Min(silentData.Length, voiceWavData.Length - zeroStartInByte)));
                    }
                }
            }

            tmpSrcPath = await Lfs.GenerateUniqueTempFilePathAsync("silent", ".wav", cancel: cancel);

            // 結果を wav に書き出す
            await using (var voiceWavTmpPath2Stream = File.Create(tmpSrcPath))
            {
                await using var writer = new WaveFileWriter(voiceWavTmpPath2Stream, voiceWavFormat);

                await writer.WriteAsync(voiceWavData, cancellationToken: cancel);
            }
        }

        await Lfs.DeleteFileIfExistsAsync(dstWavPath, cancel: cancel);

        await FfMpeg.AdjustAudioVolumeAsync(tmpSrcPath, dstWavPath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, FfmpegAdjustVolumeOptiono.MeanOnly, tagTitle, false, cancel);
    }

    async Task DeleteAllAiProcessOutputFilesAsync(CancellationToken cancel = default)
    {
        string aiOutDir = PP.Combine(this.BaseDirPath, "test_out_dir");

        await Lfs.CreateDirectoryAsync(aiOutDir, cancel: cancel);

        var list = await Lfs.EnumDirectoryAsync(aiOutDir, false, wildcard: "*.wav", cancel: cancel);

        foreach (var f in list)
        {
            await Lfs.DeleteFileIfExistsAsync(f.FullPath, raiseException: true, cancel: cancel);
        }
    }
}

public class AiUtilVoiceVoxEngine : AiUtilBasicEngine
{
    // アルゴリズムの参考元: https://qiita.com/BB-KING777/items/34c3cbb3b4ecc5043a2a BB-KING777
    public FfMpegUtil FfMpeg { get; }

    public AiUtilVoiceVoxEngine(AiUtilBasicSettings settings, FfMpegUtil ffMpeg) : base(settings, "VoiceBox", settings.AiTest_UvrCli_BaseDir)
    {
        this.FfMpeg = ffMpeg;
    }

    public async Task<FfMpegParsedList> TextToWavAsync(string text, IEnumerable<int> speakerIdList /* 0 ～ 98 */, string dstWavPath, string tagTitle, bool useOkFile = true, CancellationToken cancel = default)
    {
        return await TaskUtil.RetryAsync(async c =>
        {
            return await TextToWavMainAsync(text, speakerIdList, dstWavPath, tagTitle, useOkFile, cancel);
        },
        200, 5, cancel, true);
    }

    async Task<FfMpegParsedList> TextToWavMainAsync(string text, IEnumerable<int> speakerIdList /* 0 ～ 98 */, string dstWavPath, string tagTitle = "", bool useOkFile = true, CancellationToken cancel = default)
    {
        if (tagTitle._IsEmpty()) tagTitle = "voicetext";

        text = PreProcessText(text);

        var textBlockList = SplitText(text);

        string digest = $"text={textBlockList._ObjectToJson()._Digest()},speakerId={speakerIdList.Select(x => x.ToString())._Combine("+")},targetMaxVolume={Settings.AdjustAudioTargetMaxVolume},targetMeanVolume={Settings.AdjustAudioTargetMeanVolume}";

        if (useOkFile)
        {
            var okResult = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstWavPath, digest, AiUtilVersion.CurrentVersion, cancel);
            if (okResult.IsOk && okResult.Value != null) return okResult.Value;
        }

        ShuffledEndlessQueue<int> speakerIdShuffleQueue = new ShuffledEndlessQueue<int>(speakerIdList, 3);

        await using var exec = await TaskUtil.RetryAsync(async c =>
        {
            ExecInstance exec = new ExecInstance(new ExecOptions(Settings.AiTest_VoiceBox_ExePath, Settings.AiTest_VoiceBox_ExeArgs, PP.GetDirectoryName(Settings.AiTest_VoiceBox_ExePath)));
            try
            {
                await TaskUtil.RetryAsync(async c2 =>
                {
                    await using var http = new WebApi(new WebApiOptions(new WebApiSettings { DoNotThrowHttpResultError = true }, doNotUseTcpStack: true));

                    string url1 = $"http://127.0.0.1:{this.Settings.VoiceBoxLocalhostPort}/";

                    await http.SimpleQueryAsync(WebMethods.GET, url1, c);

                    return true;
                },
                200, 5, c, true);

                return exec;
            }
            catch
            {
                await exec._DisposeSafeAsync();
                throw;
            }

        },
        1000, 5, cancel, true);

        List<string> blockWavFileNameList = new List<string>();

        long totalFileSize = 0;

        Con.WriteLine($"{SimpleAiName}: {tagTitle}: Start text to wav");

        List<MediaVoiceSegment> segmentsList = new List<MediaVoiceSegment>();
        Dictionary<int, (MediaVoiceSegment SegmentIndexForVoice, MediaVoiceSegment SegmentIndexForBlank)> wavFileNameIndexToSegmentMappingTable = new();
        HashSetDictionary<int, MediaVoiceSegment> wavFileNameIndexToTagSegmentMappindTable = new();

        for (int i = 0; i < textBlockList.Count; i++)
        {
            if (textBlockList[i].Value == false)
            {
                string block = textBlockList[i].Key;
                int speakerId = speakerIdShuffleQueue.Dequeue();

                byte[] blockWavData = await TextBlockToWavAsync(block, speakerId);

                var tmpPath = await Lfs.GenerateUniqueTempFilePathAsync($"{tagTitle}_{i:D8}_speaker{speakerId:D3}", ".wav", cancel: cancel);

                await Lfs.WriteDataToFileAsync(tmpPath, blockWavData, FileFlags.AutoCreateDirectory, cancel: cancel);

                blockWavFileNameList.Add(tmpPath);
                int wavFileNameIndex = blockWavFileNameList.Count - 1;

                totalFileSize += blockWavData.LongLength;

                Con.WriteLine($"{SimpleAiName}: {tagTitle}: Text to Wav: {(i + 1)._ToString3()}/{textBlockList.Count._ToString3()}, Size: {totalFileSize._ToString3()} bytes, Speaker: {speakerId:D3}");

                var segmentForVoice = new MediaVoiceSegment
                {
                    VoiceText = block,
                    SpeakerId = speakerId,
                };

                var segmentForBlank = new MediaVoiceSegment
                {
                    IsBlank = true,
                    SpeakerId = speakerId,
                    BlankDuration = Util.GenRandInterval((0.75)._ToTimeSpanSecs()).TotalSeconds, // 0.75 秒 +/- 30%
                };

                wavFileNameIndexToSegmentMappingTable[wavFileNameIndex] = new(segmentForVoice, segmentForBlank);

                segmentsList.Add(segmentForVoice);

                segmentsList.Add(segmentForBlank);
            }
            else if (textBlockList[i].Key.StartsWith("<SLEEP:", StrCmp))
            {
                // 特別タグ: <SLEEP:xxx>
                string innerText = textBlockList[i].Key._RemoveQuotation('<', '>');
                if (innerText._IsFilled())
                {
                    if (innerText._GetKeyAndValue(out var sleepTagStr, out var durationStr, ":"))
                    {
                        double durationOriginal = Math.Min(durationStr._ToDouble(), 3600);
                        double duration = Math.Max(durationOriginal - 0.1, 0.11);
                        WaveFormat waveFormat = new WaveFormat(24000, 16, 1);

                        int speakerId = 0;
                        byte[] blockWavData = AiWaveUtil.GenerateSilentNoiseData(duration, waveFormat);

                        var tmpPath = await Lfs.GenerateUniqueTempFilePathAsync($"{tagTitle}_{i:D8}_speaker{speakerId:D3}", ".wav", cancel: cancel);

                        MemoryStream waveMs = new();
                        await using (WaveFileWriter writer = new(waveMs, waveFormat))
                        {
                            await writer.WriteAsync(blockWavData, cancel);
                            await writer.FlushAsync();
                        }

                        await Lfs.WriteDataToFileAsync(tmpPath, waveMs.ToArray(), FileFlags.AutoCreateDirectory, cancel: cancel);

                        blockWavFileNameList.Add(tmpPath);
                        int wavFileNameIndex = blockWavFileNameList.Count - 1;

                        totalFileSize += blockWavData.LongLength;

                        Con.WriteLine($"{SimpleAiName}: {tagTitle}: Text to Wav (Sleep): {(i + 1)._ToString3()}/{textBlockList.Count._ToString3()}, Size: {totalFileSize._ToString3()} bytes, Speaker: {speakerId:D3}");

                        var segmentForVoice = new MediaVoiceSegment
                        {
                            VoiceText = "",
                            SpeakerId = speakerId,
                            IsSleep = true,
                            SleepDuration = duration,
                        };

                        var segmentForBlank = new MediaVoiceSegment
                        {
                            IsBlank = true,
                            SpeakerId = speakerId,
                            BlankDuration = 0.1,
                        };

                        wavFileNameIndexToSegmentMappingTable[wavFileNameIndex] = new(segmentForVoice, segmentForBlank);

                        segmentsList.Add(segmentForVoice);

                        segmentsList.Add(segmentForBlank);
                    }
                }
            }
            else
            {
                var segmentForTag = new MediaVoiceSegment
                {
                    IsTag = true,
                    TagStr = textBlockList[i].Key,
                };

                segmentsList.Add(segmentForTag);

                int wavFileNameIndex = blockWavFileNameList.Count;

                wavFileNameIndexToTagSegmentMappindTable.Add(wavFileNameIndex, segmentForTag);
            }
        }

        Con.WriteLine($"{SimpleAiName}: {tagTitle}: Combining...");

        string concatFile = await Lfs.GenerateUniqueTempFilePathAsync($"{tagTitle}_concat", ".wav", cancel: cancel);

        if (blockWavFileNameList.Any() == false)
        {
            WaveFormat waveFormat = new WaveFormat(24000, 16, 1);

            await using var writer = new WaveFileWriter(concatFile, waveFormat);

            int bytesPerSample = waveFormat.BitsPerSample / 8;
            int bytesPerSecond = waveFormat.SampleRate * waveFormat.Channels * bytesPerSample;
            double silenceDurationSeconds = 2.0; // 2.0 秒
            int silenceBytes = (int)(bytesPerSecond * silenceDurationSeconds);

            var silenceBuffer = new byte[silenceBytes];
            writer.Write(silenceBuffer, 0, silenceBuffer.Length);
        }
        else
        {
            WaveFormat? waveFormat = null;
            using (var reader = new WaveFileReader(blockWavFileNameList.First()))
            {
                waveFormat = reader.WaveFormat;
            }

            if (waveFormat == null)
            {
                throw new CoresLibException($"{SimpleAiName}: {tagTitle}: WaveFormat not found");
            }

            await using var writer = new WaveFileWriter(concatFile, waveFormat);

            for (int i = 0; i < blockWavFileNameList.Count; i++)
            {
                string srcFileName = blockWavFileNameList[i];

                await using (var reader = new WaveFileReader(srcFileName))
                {
                    var segmentForVoice = wavFileNameIndexToSegmentMappingTable[i].SegmentIndexForVoice;
                    var segmentForBlank = wavFileNameIndexToSegmentMappingTable[i].SegmentIndexForBlank;

                    segmentForVoice.DataPosition = writer.Position;

                    var buffer = new byte[reader.WaveFormat.AverageBytesPerSecond * 4];
                    int bytesRead;
                    while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        writer.Write(buffer, 0, bytesRead);
                    }

                    segmentForVoice.DataLength = writer.Position - segmentForVoice.DataPosition;


                    int bytesPerSample = waveFormat.BitsPerSample / 8;
                    int bytesPerSecond = waveFormat.SampleRate * waveFormat.Channels * bytesPerSample;

                    segmentForVoice.TimePosition = (double)segmentForVoice.DataPosition / (double)bytesPerSecond;
                    segmentForVoice.TimeLength = (double)segmentForVoice.DataLength / (double)bytesPerSecond;


                    if (wavFileNameIndexToTagSegmentMappindTable.TryGetValue(i, out var tagSegments))
                    {
                        foreach (var seg in tagSegments)
                        {
                            seg.DataPosition = segmentForVoice.DataPosition;
                            seg.TimePosition = segmentForVoice.TimePosition;
                        }
                    }

                    double silenceDurationSeconds = segmentForBlank.BlankDuration;
                    if (silenceDurationSeconds < 0.00001)
                    {
                        silenceDurationSeconds = 0.5; // 未設定の場合は 0.5 秒
                    }

                    var silenceBuffer = AiWaveUtil.GenerateSilentNoiseData(silenceDurationSeconds, waveFormat);

                    segmentForBlank.DataPosition = writer.Position;

                    writer.Write(silenceBuffer, 0, silenceBuffer.Length);

                    segmentForBlank.DataLength = writer.Position - segmentForBlank.DataPosition;

                    segmentForBlank.TimePosition = (double)segmentForBlank.DataPosition / (double)bytesPerSecond;
                    segmentForBlank.TimeLength = (double)segmentForBlank.DataLength / (double)bytesPerSecond;
                }

                Con.WriteLine($"{SimpleAiName}: {tagTitle}: Concat wav: {(i + 1)._ToString3()}/{textBlockList.Count._ToString3()}, Size: {writer.Length._ToString3()} bytes");
            }
        }

        var results = await this.FfMpeg.AdjustAudioVolumeAsync(concatFile, dstWavPath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, FfmpegAdjustVolumeOptiono.MeanOnly, tagTitle, false, cancel);

        results.Item2.Options_VoiceSegmentsList = segmentsList;

        if (useOkFile)
        {
            await Lfs.WriteOkFileAsync(dstWavPath, results.Item2, digest, AiUtilVersion.CurrentVersion, cancel);
        }

        return results.Item2;
    }

    // 音声合成
    async Task<byte[]> TextBlockToWavAsync(string text, int speakerId, CancellationToken cancel = default)
    {
        await using var http = new WebApi(new WebApiOptions(doNotUseTcpStack: true));

        string url1 = $"http://127.0.0.1:{this.Settings.VoiceBoxLocalhostPort}/audio_query?text={Uri.EscapeDataString(text)}&speaker={speakerId}";

        var result1 = await http.SimplePostDataAsync(url1, new byte[0], cancel);

        string queryJsonStr = result1.ToString();

        string url2 = $"http://127.0.0.1:{this.Settings.VoiceBoxLocalhostPort}/synthesis?speaker={speakerId}";

        long startTick = TickHighresNow;

        var result2 = await http.SimplePostDataAsync(url2, queryJsonStr._GetBytes_UTF8(), cancel, "application/json");

        long endTick = TickHighresNow;
        AiTask.TotalGpuProcessTimeMsecs.Add(endTick - startTick);

        return result2.Data;
    }

    // テキスト文字列からタグとそれ以外を分離
    public static KeyValueList<string, bool> SplitTextToNormalAndTag(string text)
    {
        // 前処理: 1 行特別タグ ( < > なし) の変換
        var lines = text._GetLines();
        StringWriter w = new();
        foreach (var line in lines)
        {
            // "SLEEP:xxx" タグ -> "<SLEEP:xxx>" タグ
            string line2 = line;
            if (line.StartsWith("SLEEP:", StrCmp) && line._IsAscii())
            {
                line2 = $"<{line}>";
            }
            w.WriteLine(line2);
        }

        text = w.ToString();

        StringBuilder b = new StringBuilder();

        KeyValueList<string, bool> ret = new();

        int mode = 0;

        int i;
        for (i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '<')
            {
                if (mode == 0)
                {
                    mode = 1;

                    if (b.Length >= 1)
                    {
                        ret.Add(b.ToString(), false);
                        b.Clear();
                    }
                }

                b.Append(c);
            }
            else if (c == '>')
            {
                b.Append(c);

                if (mode == 1)
                {
                    mode = 0;

                    ret.Add(b.ToString(), true);
                    b.Clear();
                }
            }
            else
            {
                b.Append(c);
            }
        }

        if (b.Length >= 1)
        {
            ret.Add(b.ToString(), mode != 0);
        }

        return ret;
    }

    // テキスト分割 (タグも分離)
    public static KeyValueList<string, bool> SplitText(string text, int maxLen = 100)
    {
        KeyValueList<string, bool> ret = new KeyValueList<string, bool>();

        var textAndTags = SplitTextToNormalAndTag(text);

        foreach (var part in textAndTags)
        {
            if (part.Value == false)
            {
                var a = SplitTextCore(part.Key, maxLen);
                foreach (var s in a)
                {
                    ret.Add(s, false);
                }
            }
            else
            {
                ret.Add(part.Key, true);
            }
        }

        return ret;
    }

    // テキスト分割
    static List<string> SplitTextCore(string text, int maxLen = 100)
    {
        var sentences = Regex.Split(text, @"(?<=[。！？])");
        var chunks = new List<string>();
        var current = "";

        foreach (var sentence in sentences)
        {
            if (current.Length + sentence.Length > maxLen)
            {
                if (current._IsFilled())
                {
                    chunks.Add(current);
                }
                current = sentence;
            }
            else
            {
                current += sentence;
            }
        }

        if (current._IsFilled())
        {
            chunks.Add(current);
        }

        return chunks;
    }

    // テキスト前処理
    static string PreProcessText(string text)
    {
        text = text._ReplaceStr("　", " ")._ReplaceStr("\t", " ");

        return text;
    }
}

public class AiUtilUvrEngine : AiUtilBasicEngine
{
    public FfMpegUtil FfMpeg { get; }

    public AiUtilUvrEngine(AiUtilBasicSettings settings, FfMpegUtil ffMpeg) : base(settings, "UVR", settings.AiTest_UvrCli_BaseDir)
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
                var okFileForDstMusicWavPath = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstMusicWavPath, "", AiUtilVersion.CurrentVersion, cancel);
                if (okFileForDstMusicWavPath.IsOk)
                {
                    dstMusicWavPath = null;
                    savedResult = okFileForDstMusicWavPath;
                }
            }

            if (dstVocalWavPath._IsFilled())
            {
                var okFileForDstVocalWavPath = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstVocalWavPath, "", AiUtilVersion.CurrentVersion, cancel);
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
        var result = await FfMpeg.AdjustAudioVolumeAsync(srcFilePath, adjustedWavFile, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, FfmpegAdjustVolumeOptiono.MeanOnly /* ! */, tagTitle, false, cancel);

        if (dstMusicWavPath._IsFilled())
        {
            // 音楽分離実行
            await ExtractInternalAsync(adjustedWavFile, dstMusicWavPath, true, tagTitle, cancel);

            if (useOkFile)
            {
                await Lfs.WriteOkFileAsync(dstMusicWavPath, result.Item1, "", AiUtilVersion.CurrentVersion, cancel);
            }
        }

        if (dstVocalWavPath._IsFilled())
        {
            // ボーカル分離実行
            await ExtractInternalAsync(adjustedWavFile, dstVocalWavPath, false, tagTitle, cancel);

            if (useOkFile)
            {
                await Lfs.WriteOkFileAsync(dstVocalWavPath, result.Item1, "", AiUtilVersion.CurrentVersion, cancel);
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

        int srcWavFileLen = await AiTask.GetWavFileLengthMSecAsync(srcWavPath);
        int timeout = (srcWavFileLen * 2) + (60 * 1000);

        long startTick = TickHighresNow;
        var result = await this.RunVEnvPythonCommandsAsync($"python {(music ? "get_music" : "get_vocal")}_wav.py", timeout, printTag: tag, cancel: cancel);
        long endTick = TickHighresNow;
        AiTask.TotalGpuProcessTimeMsecs.Add(endTick - startTick);

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

    public AiUtilBasicEngine(AiUtilBasicSettings settings, string simpleAiName, string baseDirPath)
    {
        this.SimpleAiName = simpleAiName._NonNullTrim()._NotEmptyOrDefault("AI");
        this.Settings = settings;
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

public class AiRandomBgpReplaceRanges
{
    public double StartPosition;
    public double Length;
    public double FadeIn;
    public double FadeOut;
    public double Margin;
}

public class AiRandomBgmSettings
{
    public bool Medley = false;
    public int Medley_SingleFilePartMSecs = 100 * 1000;
    public int Medley_FadeInOutMsecs = 5 * 1000;
    public int Medley_PlusMinusPercentage = 44;
    public int Medley_MarginMsecs = 15 * 1000;

    [JsonIgnore]
    public Func<CancellationToken, AiAudioEffectFilter?>? CreateAudioEffectFilterForNormalMusicPartProc = null!;

    [JsonIgnore]
    public Func<CancellationToken, AiAudioEffectFilter?>? CreateAudioEffectFilterForOverwriteMusicPartProc = null!;
}

// 以下のコード By ChatGPT
public static class AiWaveMixUtil
{
    /// <summary>
    /// 44.1kHz・2ch・16bit PCM データを前提とした、メモリ上での波形合成処理を行う。
    /// targetWav に、sourceWav をミキシングした結果を書き込む。
    /// </summary>
    /// <param name="targetWav">編集対象の Wave 生データ (Memory&lt;byte&gt;)</param>
    /// <param name="sourceWav">合成元の Wave 生データ (ReadOnlyMemory&lt;byte&gt;)</param>
    /// <param name="targetPosition">targetWav 内の合成開始位置(秒)</param>
    /// <param name="sourceWavPosition">sourceWav 内の使用開始位置(秒)</param>
    /// <param name="length">合成する長さ(秒)</param>
    /// <param name="delta">dB 単位の音量調整値(正で音量アップ、負でダウン)</param>
    /// <param name="fadein">フェードインの長さ(秒)</param>
    /// <param name="fadeout">フェードアウトの長さ(秒)</param>
    public static void MixWaveData(
        Memory<byte> targetWav,
        ReadOnlyMemory<byte> sourceWav,
        double targetPosition,
        double sourceWavPosition,
        double length,
        double deltaLeft,
        double deltaRight,
        double fadein,
        double fadeout)
    {
        // 波形フォーマット前提値
        const int sampleRate = 44100;
        const int channels = 2;
        const int bitsPerSample = 16;
        // 1サンプル(1ch)あたりのバイト数
        const int bytesPerSample = bitsPerSample / 8; // = 2
                                                      // 1フレーム(全ch合計)あたりのバイト数(2ch前提)
        const int blockAlign = channels * bytesPerSample; // 4バイト

        if (targetPosition < 0 || sourceWavPosition < 0 || length <= 0)
        {
            // 負の位置や長さ0以下なら何もしない
            return;
        }

        // 合成を開始するフレーム位置(整数)
        long targetStartFrame = (long)(targetPosition * sampleRate);
        long sourceStartFrame = (long)(sourceWavPosition * sampleRate);
        if (targetStartFrame < 0 || sourceStartFrame < 0)
        {
            // もし計算結果が負なら処理しない
            return;
        }

        // 合成するフレーム数(整数)
        long framesToProcess = (long)(length * sampleRate);
        if (framesToProcess <= 0)
        {
            return;
        }

        // バイト単位での開始位置
        long targetStartByte = targetStartFrame * blockAlign;
        long sourceStartByte = sourceStartFrame * blockAlign;

        // targetWav / sourceWav の残りバイト数
        long targetRemBytes = targetWav.Length - targetStartByte;
        long sourceRemBytes = sourceWav.Length - sourceStartByte;

        // 範囲外チェック
        if (targetRemBytes <= 0 || sourceRemBytes <= 0)
        {
            // 合成開始位置がどちらか一方でも範囲外なら何もしない
            return;
        }

        // 実際に合成できる最大フレーム数を決定
        long maxTargetFrames = targetRemBytes / blockAlign;
        long maxSourceFrames = sourceRemBytes / blockAlign;
        if (framesToProcess > maxTargetFrames)
            framesToProcess = maxTargetFrames;
        if (framesToProcess > maxSourceFrames)
            framesToProcess = maxSourceFrames;

        if (framesToProcess <= 0)
        {
            return;
        }

        // 実際に処理するバイト数
        int bytesToProcess = (int)(framesToProcess * blockAlign);

        // --------------------------------------------------------------------
        // 1) sourceWav の必要部分だけを一時バッファへコピー
        // --------------------------------------------------------------------
        // メモリコピー(高速化のため必要分のみ)
        ReadOnlyMemory<byte> sourceSlice = sourceWav.Slice((int)sourceStartByte, bytesToProcess);
        // short 単位に処理しやすいよう変換用バッファ
        byte[] sourceBytes = sourceSlice.ToArray();

        // --------------------------------------------------------------------
        // 2) フェードイン・フェードアウト・dBスケーリング適用
        // --------------------------------------------------------------------
        // short[] に変換しておいて、サンプルごとに処理
        short[] sourceSamples = new short[sourceBytes.Length / 2];
        Buffer.BlockCopy(sourceBytes, 0, sourceSamples, 0, sourceBytes.Length);

        float amplitudeFactorLeft = (float)Math.Pow(10.0, deltaLeft / 20.0);  // dB → 倍率
        float amplitudeFactorRight = (float)Math.Pow(10.0, deltaRight / 20.0);  // dB → 倍率
        double totalDurationSec = framesToProcess / (double)sampleRate;

        for (int i = 0; i < framesToProcess; i++)
        {
            // フレーム i に対応する時間(秒)
            double t = i / (double)sampleRate;

            // フェードイン率
            double fi = 1.0;
            if (fadein > 0.0 && t < fadein)
            {
                fi = t / fadein;  // 0→1 に上昇
            }

            // フェードアウト率
            double fo = 1.0;
            double timeFromEnd = totalDurationSec - t; // 終了まで残り秒
            if (fadeout > 0.0 && timeFromEnd < fadeout)
            {
                fo = timeFromEnd / fadeout; // 1→0 に減少
            }

            // フェード率の合成
            float fadeFactor = (float)(fi * fo);

            // 左(0), 右(1) と 2chぶん連続している想定
            int leftIndex = i * 2;
            int rightIndex = i * 2 + 1;

            // スケーリング（dB + フェードイン/アウト）
            float scaledLeft = sourceSamples[leftIndex] * amplitudeFactorLeft * fadeFactor;
            float scaledRight = sourceSamples[rightIndex] * amplitudeFactorRight * fadeFactor;

            // short 範囲にクリップ
            sourceSamples[leftIndex] = (short)Math.Clamp((int)scaledLeft, short.MinValue, short.MaxValue);
            sourceSamples[rightIndex] = (short)Math.Clamp((int)scaledRight, short.MinValue, short.MaxValue);
        }

        // --------------------------------------------------------------------
        // 3) targetWav に合成（ミキシング）
        // --------------------------------------------------------------------
        Span<byte> targetSpan = targetWav.Slice((int)targetStartByte, bytesToProcess).Span;

        for (int i = 0; i < framesToProcess; i++)
        {
            int bytePos = i * blockAlign;

            // target の左 ch
            short targetLeft = BitConverter.ToInt16(targetSpan.Slice(bytePos, 2));
            // target の右 ch
            short targetRight = BitConverter.ToInt16(targetSpan.Slice(bytePos + 2, 2));

            // source の左 ch
            short sourceLeft = sourceSamples[i * 2];
            // source の右 ch
            short sourceRight = sourceSamples[i * 2 + 1];

            // 加算 (オーバーフロー対策で一旦 int へ)
            int mixedLeft = targetLeft + sourceLeft;
            int mixedRight = targetRight + sourceRight;

            // short の範囲にクリップ
            mixedLeft = Math.Clamp(mixedLeft, short.MinValue, short.MaxValue);
            mixedRight = Math.Clamp(mixedRight, short.MinValue, short.MaxValue);

            // 書き戻し
            BitConverter.TryWriteBytes(targetSpan.Slice(bytePos, 2), (short)mixedLeft);
            BitConverter.TryWriteBytes(targetSpan.Slice(bytePos + 2, 2), (short)mixedRight);
        }
    }
}

public static class AiWaveUtil
{
    /// <summary>
    /// 指定された WaveFormat / 長さ(duration秒) / レベル(db dB) の微小ノイズ(生PCMデータ)を生成する
    /// </summary>
    /// <param name="waveFormat">使用したい WaveFormat (サンプリングレート, ビット深度など)</param>
    /// <param name="duration">生成するデータの長さ(秒)</param>
    /// <param name="db">ノイズの音量(dB)。例: -80.0</param>
    /// <returns>wav ヘッダなしのPCMデータ(byte配列)</returns>
    public static byte[] GenerateSilentNoiseData(double durationSeconds, WaveFormat format, double dbFS = -80.0)
    {
        if (dbFS >= 0) throw new ArgumentOutOfRangeException(
            nameof(dbFS), "dbFS は 0 より小さい必要があります (0dBFS がフルスケール)");

        // dB → 振幅係数 (0.0001 ≒ -80 dBFS)
        double amp = Math.Pow(10.0, dbFS / 20.0);

        int totalFrames = (int)Math.Round(format.SampleRate * durationSeconds);
        int bufferSize = totalFrames * format.BlockAlign;
        byte[] buffer = new byte[bufferSize];

        Random rng = new Random();                 // 高度な用途なら RNGCryptoServiceProvider などでも可
        int offset = 0;

        for (int frame = 0; frame < totalFrames; frame++)
        {
            for (int ch = 0; ch < format.Channels; ch++)
            {
                double sample = (rng.NextDouble() * 2.0 - 1.0) * amp; // -1.0〜+1.0 にスケール後、振幅係数を掛ける

                switch (format.Encoding)
                {
                    case WaveFormatEncoding.IeeeFloat:
                        float f = (float)sample;                // +/-1.0 がフルスケール
                        BitConverter.GetBytes(f)
                                    .CopyTo(buffer, offset);
                        offset += 4;
                        break;

                    case WaveFormatEncoding.Pcm:
                        switch (format.BitsPerSample)
                        {
                            case 16:
                                short s16 = (short)(sample * short.MaxValue);
                                BitConverter.GetBytes(s16).CopyTo(buffer, offset);
                                offset += 2;
                                break;

                            case 24:
                                int s24 = (int)(sample * 8_388_607); // 2^23-1
                                buffer[offset++] = (byte)(s24 & 0xFF);
                                buffer[offset++] = (byte)((s24 >> 8) & 0xFF);
                                buffer[offset++] = (byte)((s24 >> 16) & 0xFF);
                                break;

                            case 32:
                                int s32 = (int)(sample * int.MaxValue);
                                BitConverter.GetBytes(s32).CopyTo(buffer, offset);
                                offset += 4;
                                break;

                            case 8:
                                // 8-bit PCM は 0–255 の unsigned。128 が 0。
                                byte s8 = (byte)(128 + sample * 127);
                                buffer[offset++] = s8;
                                break;

                            default:
                                throw new NotSupportedException(
                                    $"PCM {format.BitsPerSample}-bit には未対応です");
                        }
                        break;

                    default:
                        throw new NotSupportedException(
                            $"Encoding {format.Encoding} には未対応です");
                }
            }
        }
        return buffer;
    }
    /// <summary>
    /// Box-Muller 法で N(0,1) のガウシアン乱数を1つ生成する。
    /// </summary>
    private static double NextGaussian(Random rand)
    {
        // 2つの一様乱数 u1, u2 を用意 (0 ~ 1)
        double u1 = 1.0 - rand.NextDouble(); // 0 は引かないようにする (logの発散回避)
        double u2 = 1.0 - rand.NextDouble();
        // Box-Muller transform
        double radius = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        double z = radius * Math.Sin(theta);
        return z;
    }

    /// <summary>
    /// 指定した WaveFormat (PCM) と duration (秒) から、-80dB 程度の微小ノイズで埋めた
    /// RAW PCM データ(byte配列)を返す (WAVヘッダなし)。
    /// dB 値を変えれば -80dB 以外のレベルにも対応可能。
    /// </summary>
    /// <param name="waveFormat">PCM形式の WaveFormat</param>
    /// <param name="duration">生成するノイズの秒数</param>
    /// <param name="db">埋め込むノイズのレベル(dB)。例: -80.0</param>
    /// <returns>生成した生PCMデータ（WAVヘッダ無し）</returns>
    /// <exception cref="NotSupportedException">非対応エンコーディングやビット深度の場合に発生</exception>
    public static byte[] GenerateSilentNoiseData2(double duration, WaveFormat waveFormat, double db = -80.0)
    {
        // エンコーディングが PCM 以外は例外
        if (waveFormat.Encoding != WaveFormatEncoding.Pcm)
        {
            throw new NotSupportedException("WaveFormatEncoding.Pcm 以外はサポートしていません。");
        }

        int sampleRate = waveFormat.SampleRate;    // サンプルレート
        int channels = waveFormat.Channels;      // チャンネル数
        int bits = waveFormat.BitsPerSample; // ビット深度 (8,16,24,32など)

        // duration 秒分のフレーム数を計算
        int totalFrames = (int)Math.Round(sampleRate * duration);
        if (totalFrames < 0) totalFrames = 0;

        // dB -> 振幅（リニア値）に変換 (10^(dB/20))
        double amplitude = Math.Pow(10.0, db / 20.0);

        // サンプルあたりのバイト数 (例: 16bit→2, 24bit→3, 32bit→4)
        int bytesPerSample = bits / 8;

        // 全サンプル数(フレーム数×チャンネル数)から、最終的な配列サイズを求める
        long totalSampleCount = (long)totalFrames * channels;
        long totalBytes = totalSampleCount * bytesPerSample;
        if (totalBytes > int.MaxValue)
        {
            // 常識的には超えないはずだが、一応ガード
            throw new Exception("生成しようとしているデータサイズが大きすぎます。");
        }

        byte[] buffer = new byte[totalBytes];

        // 正規分布用のランダム生成 (Box-Muller 法の一例)
        Random rand = new Random();
        double NextGaussian()
        {
            // Box-Muller 変換
            double u1 = 1.0 - rand.NextDouble();
            double u2 = 1.0 - rand.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // ビット深度に応じて書き込み方法を切り替え
        // （8bitはUnsigned、16bit以上はSignedで小数→整数変換）
        for (int n = 0; n < totalFrames; n++)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                // ガウス分布で[-1,1]程度の乱数を生成したものに振幅を掛ける
                double sample = amplitude * NextGaussian();

                // 書き込む先頭インデックス
                int idx = (int)((n * channels + ch) * bytesPerSample);

                switch (bits)
                {
                    case 8:
                        {
                            // 8bit PCM: 0～255 (Unsigned)
                            // sample=-1.0 → 0, sample=+1.0 → 255 になるよう一応スケーリング
                            double val = sample * 127.5 + 127.5;
                            // 範囲クリップ
                            if (val < 0.0) val = 0.0;
                            if (val > 255.0) val = 255.0;
                            buffer[idx] = (byte)Math.Round(val);
                            break;
                        }
                    case 16:
                        {
                            // 16bit PCM: -32768～32767
                            double val = sample * short.MaxValue;  // short.MaxValue=32767
                            if (val < short.MinValue) val = short.MinValue;
                            if (val > short.MaxValue) val = short.MaxValue;
                            short s = (short)Math.Round(val);
                            buffer[idx] = (byte)(s & 0xFF);
                            buffer[idx + 1] = (byte)((s >> 8) & 0xFF);
                            break;
                        }
                    case 24:
                        {
                            // 24bit PCM: -8388608～8388607
                            const double max24 = 8388607.0;
                            double val = sample * max24;
                            if (val < -8388608.0) val = -8388608.0;
                            if (val > 8388607.0) val = 8388607.0;
                            int i24 = (int)Math.Round(val);
                            buffer[idx] = (byte)(i24 & 0xFF);
                            buffer[idx + 1] = (byte)((i24 >> 8) & 0xFF);
                            buffer[idx + 2] = (byte)((i24 >> 16) & 0xFF);
                            break;
                        }
                    case 32:
                        {
                            // 32bit PCM: -2147483648～2147483647
                            const double max32 = 2147483647.0;
                            double val = sample * max32;
                            if (val < int.MinValue) val = int.MinValue;
                            if (val > int.MaxValue) val = int.MaxValue;
                            int i32 = (int)Math.Round(val);
                            buffer[idx] = (byte)(i32 & 0xFF);
                            buffer[idx + 1] = (byte)((i32 >> 8) & 0xFF);
                            buffer[idx + 2] = (byte)((i32 >> 16) & 0xFF);
                            buffer[idx + 3] = (byte)((i32 >> 24) & 0xFF);
                            break;
                        }
                    default:
                        throw new NotSupportedException($"未対応のビット深度です: {bits}bit PCM");
                }
            }
        }

        return buffer;
    }


    public static double CalcWavDurationFromSizeInByte(long size, WaveFormat format)
    {
        return (double)size / (double)format.AverageBytesPerSecond;
    }

    public static int GetWavDataSizeInByteFromTime(double timeSecs, WaveFormat format)
    {
        double exact = timeSecs * format.AverageBytesPerSecond;

        int byteOffset = (int)Math.Round(exact);
        byteOffset -= byteOffset % format.BlockAlign; // 必ずフレーム境界に
        return byteOffset;
    }

    public static long GetWavDataPositionInByteFromTime(double timeSecs, WaveFormat format)
    {
        double exact = timeSecs * format.AverageBytesPerSecond;

        long byteOffset = (long)Math.Round(exact);
        byteOffset -= byteOffset % format.BlockAlign; // 必ずフレーム境界に
        return byteOffset;
    }

    /// <summary>
    /// 1. float[] -> 16bit PCM (ステレオ 44.1kHz) の生データ (ヘッダなし) を表す Memory<byte> に変換する。
    /// </summary>
    /// <param name="floatBuffer">
    ///     NAudio の ISampleProvider.Read() などで取得したインターリーブ済みステレオの float 配列。
    ///     サンプルレート 44.1kHz, 2ch, フォーマットは -1.0f ～ +1.0f を想定。
    /// </param>
    /// <param name="cancel">巨大なループを回す際のキャンセル用トークン</param>
    /// <returns>16bit PCM (ステレオ) の生バイトデータ (WAV ヘッダを含まない波形部分のみ)</returns>
    public static Memory<byte> ConvertFloatArrayToPcm16Memory(
        float[] floatBuffer,
        CancellationToken cancel
    )
    {
        if (floatBuffer == null) throw new ArgumentNullException(nameof(floatBuffer));

        // 16ビットPCM (2バイト) x サンプル数 だけのバイト配列を確保する。
        // float配列はステレオ(2ch)をインターリーブしているが、変換は単純に
        // 「各 float サンプルを 16bit(short) にする」だけなので、
        // サンプル数 x 2 バイト = バイト数になる。
        // （チャンネル数は気にせず、単純にサンプルの数だけループを回せばよい）
        int totalSamples = floatBuffer.Length;
        int totalBytes = totalSamples * sizeof(short); // short = 2 bytes
        var byteArray = new byte[totalBytes];

        // NAudio.WaveBuffer を使うと、ShortBuffer[i] を簡単に扱える
        // (内部的には GCHandle でバッファを Pin してくれる)。
        var waveBuffer = new WaveBuffer(byteArray);

        for (int i = 0; i < totalSamples; i++)
        {
            // 適宜キャンセル要求をチェック (大きい配列を想定するなら頻度調整)
            if ((i % 10000) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            // float(-1.0～+1.0) を 16bit PCM(-32768～32767) に変換
            // ここでは Math.Round で四捨五入後にキャストする例。
            // (NAudio の規定実装では単純に (short)(floatVal * 32767) などがよく使われる)
            float sampleFloat = floatBuffer[i];

            // クリッピングは適宜必要に応じて実装
            if (sampleFloat > 1.0f) sampleFloat = 1.0f;
            if (sampleFloat < -1.0f) sampleFloat = -1.0f;

            waveBuffer.ShortBuffer[i] = (short)Math.Round(sampleFloat * 32767.0f);
        }

        // 返り値は Memory<byte> にして返す
        return new Memory<byte>(byteArray);
    }

    /// <summary>
    /// 2. 16bit PCM (ステレオ 44.1kHz) の生データ (ヘッダなし) -> float[] に変換する。
    /// </summary>
    /// <param name="pcmData">
    ///     「44.1kHz / 2ch / 16bit PCM」の生データ (WAVヘッダなし)。
    ///     ステレオの場合、各サンプルがインターリーブされている。
    /// </param>
    /// <param name="cancel">巨大なループを回す際のキャンセル用トークン</param>
    /// <returns>変換後の float[] (インターリーブ済みステレオのサンプル列)</returns>
    public static float[] ConvertPcm16ToFloatArray(
        ReadOnlyMemory<byte> pcmData,
        CancellationToken cancel
    )
    {
        // byte -> short に変換するので、2 バイトで 1サンプル
        // ステレオかどうかにかかわらず、単純にトータルサンプル数 = (バイト数 / 2)。
        // たとえば「2ch 16bit」でも、(バイト配列の合計長)/2 が short の個数になる。
        // ステレオなら、左サンプル / 右サンプル が交互に格納されている、というだけ。
        int totalBytes = pcmData.Length;
        if (totalBytes % 2 != 0)
        {
            throw new ArgumentException("PCMデータが 2 バイト単位になっていません。壊れている可能性があります。");
        }
        int totalSamples = totalBytes / sizeof(short);

        // 変換先 float 配列
        float[] floatArray = new float[totalSamples];

        // NAudio.WaveBuffer は ReadOnlyMemory<byte> から直接作れないため、
        // 必要ならば一旦 ToArray() する。サイズが巨大な場合は別途工夫が要る。
        var byteArray = pcmData.ToArray();
        var waveBuffer = new WaveBuffer(byteArray);

        for (int i = 0; i < totalSamples; i++)
        {
            if ((i % 10000) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            // short(-32768～32767) -> float(-1.0～+1.0 近辺)
            short sampleShort = waveBuffer.ShortBuffer[i];
            float sampleFloat = sampleShort / 32768.0f; // -1.0 ～ 約+0.9999
            floatArray[i] = sampleFloat;
        }

        return floatArray;
    }
}

public class AiWaveConcatenatedSrcWavList
{
    public string WavFilePath = null!;
    public string? FilterName;
    [JsonConverter(typeof(StringEnumConverter))]
    public AiAudioEffectSpeedType? FilterSpeedType;
    public JObject? FilterSettings;
    public int TargetStartMsec;
    public int TargetEndMsec;
    public int SourceStartMsec;
    public int SourceEndMsec;
}


public static class AiWaveConcatenateWithCrossFadeUtil
{
    /// <summary>
    /// srcWavFilesQueue に含まれる WAV ファイルを順次読み込み、
    /// 部分的に切り取ってフェードイン・フェードアウトを施し、
    /// クロスフェードしながら合成した WAV ファイルを作成する。
    /// </summary>
    /// <param name="srcWaveFilesList">ソースの wav ファイル パスを格納したキュー</param>
    /// <param name="singleFilePartMSecs">切り出し目安の長さ（ミリ秒）</param>
    /// <param name="totalDurationMsecs">出力 WAV の合計長さ（ミリ秒）</param>
    /// <param name="fadeoutMsecs">フェードイン／フェードアウトの時間（ミリ秒）</param>
    /// <param name="dstWavFilePath">出力先 wav ファイル パス</param>
    /// <param name="cancel">キャンセル用トークン</param>
    /// <returns></returns>
    public static async Task<List<AiWaveConcatenatedSrcWavList>> ConcatenateAsync(
        ShuffledEndlessQueue<string> srcWavFilesQueue,
        int totalDurationMsecs,
        AiRandomBgmSettings settings,
        string dstWavFilePath,
        CancellationToken cancel = default)
    {
        if (srcWavFilesQueue.Count == 0)
        {
            throw new ArgumentException("ソースの WAV ファイル キューが空です。");
        }
        if (settings.Medley_SingleFilePartMSecs <= 0 ||
            totalDurationMsecs <= 0 ||
            settings.Medley_FadeInOutMsecs <= 0)
        {
            throw new ArgumentException("パラメータが不正です。");
        }

        List<AiWaveConcatenatedSrcWavList> retSrcList = new List<AiWaveConcatenatedSrcWavList>();

        //------------------------------------------------------------
        // まずは1つ目の WAV ファイルからフォーマット情報を取得する
        // キューをいったん取り出し、また戻す
        //------------------------------------------------------------
        string firstWav = srcWavFilesQueue.Peek();
        WaveFormat? waveFormat;
        await using (var firstFs = new FileStream(firstWav, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        using (var firstReader = new WaveFileReader(firstFs))
        {
            waveFormat = firstReader.WaveFormat;
        }

        // 以降の処理では、全ファイルが同じ waveFormat である前提

        //------------------------------------------------------------
        // 必要な変数を用意
        //------------------------------------------------------------
        var random = new Random(Secure.RandSInt31());
        int currentTotalLengthMsecs = 0;
        var partialDataList = new List<float[]>();  // 各「部分音声データ」（すでにフェードイン・アウトが施されているもの）を格納

        // fade 時のサンプル数
        int fadeSamples = (int)((long)settings.Medley_FadeInOutMsecs * waveFormat.SampleRate / 1000) * waveFormat.Channels;

        //------------------------------------------------------------
        // ソース WAV のキューを消費しながら部分的に読み取る
        //------------------------------------------------------------
        while (srcWavFilesQueue.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();

            string wavPath = srcWavFilesQueue.Dequeue();

            // WAV の総再生時間などを取得
            // （NAudio は完全な async 読み込みではないが、FileStream を useAsync: true で開く）
            int fileTotalMs;
            await using (var fs = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
            using (var reader = new WaveFileReader(fs))
            {
                fileTotalMs = (int)reader.TotalTime.TotalMilliseconds;

                // ファイルが 2*fadeoutMsecs 未満ならスキップ
                if (fileTotalMs < (settings.Medley_FadeInOutMsecs * 2))
                {
                    continue;
                }

                // singleFilePartMSecs に対して上下30% の乱数を加味して thisFilePartMSecs を求める
                //int variation = (int)((double)singleFilePartMSecs * 0.3);
                int thisFilePartMSecs = Util.GenRandInterval(settings.Medley_SingleFilePartMSecs);
                if (thisFilePartMSecs < 0) thisFilePartMSecs = 0;

                // もし thisFilePartMSecs > fileTotalMs ならクリップ
                if (thisFilePartMSecs > fileTotalMs)
                {
                    thisFilePartMSecs = fileTotalMs;
                }

                if (fileTotalMs < (thisFilePartMSecs + settings.Medley_MarginMsecs * 2))
                {
                    continue;
                }

                // ランダム開始位置 (0 ～ (fileTotalMs - thisFilePartMSecs))
                int maxStartMs = fileTotalMs - thisFilePartMSecs;
                if (maxStartMs < 0) maxStartMs = 0; // 念のため

                int randStartMs = settings.Medley_MarginMsecs;
                int randEndMs = maxStartMs - settings.Medley_MarginMsecs;
                if (randEndMs < 0) randEndMs = 0; // 念のため

                int startMs = random.Next(randStartMs, randEndMs + 1);

                // 必要サンプル数の計算
                int readSamples = MsecToSamples(thisFilePartMSecs, waveFormat);
                int startOffsetSamples = MsecToSamples(startMs, waveFormat);

                // WaveFileReader をサンプルプロバイダ化して読み取る
                // まず、開始位置までシーク
                reader.CurrentTime = TimeSpan.FromMilliseconds(startMs);
                var sampleProvider = reader.ToSampleProvider();  // 32-bit float 変換プロバイダ

                // 部分波形を取得
                float[] partBuffer = new float[readSamples];
                int actuallyRead = 0;
                int totalRead = 0;
                do
                {
                    cancel.ThrowIfCancellationRequested();

                    // sampleProvider.Read は同期的に読み取るが、ファイルストリーム自体は async で開いている
                    actuallyRead = sampleProvider.Read(partBuffer, totalRead, readSamples - totalRead);
                    totalRead += actuallyRead;
                } while (actuallyRead > 0 && totalRead < readSamples);

                if (totalRead < readSamples)
                {
                    // 端数しか読めなかった場合、足りない分は 0 で埋める
                    for (int i = totalRead; i < readSamples; i++)
                    {
                        partBuffer[i] = 0f;
                    }
                }

                AiWaveConcatenatedSrcWavList item = new AiWaveConcatenatedSrcWavList { WavFilePath = wavPath };

                item.SourceStartMsec = startMs;
                item.SourceEndMsec = thisFilePartMSecs;

                if (settings.CreateAudioEffectFilterForNormalMusicPartProc != null)
                {
                    var filter = settings.CreateAudioEffectFilterForNormalMusicPartProc(cancel);
                    if (filter != null)
                    {
                        // オーディオフィルタ関数を適用。ただし、float の波形データは直接扱えないので、いったん byte[] に変換し、フィルタを通し、また float[] に戻す処理が必要
                        var partWaveData = AiWaveUtil.ConvertFloatArrayToPcm16Memory(partBuffer, cancel);

                        filter.PerformFilterFunc(partWaveData, cancel);

                        partBuffer = AiWaveUtil.ConvertPcm16ToFloatArray(partWaveData, cancel);

                        item.FilterName = filter.FilterName;
                        item.FilterSettings = filter.EffectSettings._ToJObject();
                        item.FilterSpeedType = filter.FilterSpeedType;
                    }
                }

                item.TargetStartMsec = currentTotalLengthMsecs;
                item.TargetEndMsec = currentTotalLengthMsecs + thisFilePartMSecs - settings.Medley_FadeInOutMsecs;

                retSrcList.Add(item);

                // フェードイン／フェードアウトを適用
                ApplyFadeInOut(partBuffer, fadeSamples);

                // この部分波形をリストに追加
                partialDataList.Add(partBuffer);

                // 加算
                currentTotalLengthMsecs += (thisFilePartMSecs - settings.Medley_FadeInOutMsecs);
            }

            // もし合計長が totalDurationMsecs を超えたらループを抜ける
            if (currentTotalLengthMsecs >= (totalDurationMsecs + settings.Medley_FadeInOutMsecs * 2))
            {
                break;
            }
        }

        //------------------------------------------------------------
        // partialDataList に「部分音声データ」が集まったので、
        // これをクロスフェードしながら最終波形を作ってファイルに書き込む。
        //------------------------------------------------------------
        int totalDurationSamples = MsecToSamples(totalDurationMsecs, waveFormat);

        // 最終出力用バッファ（float で一度合成）
        float[] finalMixBuffer = new float[totalDurationSamples];
        // ゼロ埋めされていると仮定

        // クロスフェード重ね合わせ用
        // ある部分音声データ i と i+1 のフェードアウト／フェードイン部を overlap する。
        int overlapSamples = fadeSamples; // fadeoutMsecs に相当するサンプル数

        // 現在の書き込み先位置（サンプル単位）
        int currentPos = 0;

        for (int i = 0; i < partialDataList.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();

            float[] partData = partialDataList[i];
            int partLen = partData.Length;

            if (i == 0)
            {
                // 最初の部分音声データはそのまま貼り付け開始
                WriteOverlapAdd(
                    source: partData,
                    sourceOffset: 0,
                    sourceCount: partLen,
                    dest: finalMixBuffer,
                    destOffset: 0);

                currentPos = partLen;  // 貼り付け終わりの次のサンプル位置
            }
            else
            {
                // 2 個目以降は overlapSamples だけ前に重ね合わせる
                int startPos = currentPos - overlapSamples;
                if (startPos < 0) startPos = 0;

                // 重ね合わせる分（partData の先頭 overlapSamples と、finalMix の後ろ overlapSamples）
                int overlapCount = overlapSamples;
                if (overlapCount > partLen) overlapCount = partLen;

                // オーバーラップ部分
                WriteOverlapAdd(
                    source: partData,
                    sourceOffset: 0,
                    sourceCount: overlapCount,
                    dest: finalMixBuffer,
                    destOffset: startPos);

                // オーバーラップ後の残り部分をコピー
                int remain = partLen - overlapCount;
                if (remain > 0)
                {
                    int writeDestPos = startPos + overlapCount;
                    // finalMixBuffer が残り書ける長さ
                    int canWrite = finalMixBuffer.Length - writeDestPos;
                    if (canWrite < 0) canWrite = 0;
                    if (remain > canWrite)
                    {
                        remain = canWrite;
                    }

                    if (remain > 0)
                    {
                        WriteOverlapAdd(
                            source: partData,
                            sourceOffset: overlapCount,
                            sourceCount: remain,
                            dest: finalMixBuffer,
                            destOffset: writeDestPos);
                    }
                }

                // 次の書き込み先位置は「(前回の終端) + (今回パート長) - overlap」
                currentPos += (partLen - overlapSamples);
            }

            // もし finalMixBuffer の長さを超える場合、そこで打ち切る
            if (currentPos >= totalDurationSamples)
            {
                break;
            }
        }

        // もしまだ totalDurationSamples より短ければ、残りは無音(0)ですでに埋まっているので OK

        //------------------------------------------------------------
        // finalMixBuffer を waveFormat のフォーマット(例: 16bit PCM) でファイルに書き込む
        //------------------------------------------------------------
        await using (var outFs = new FileStream(dstWavFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        using (var writer = new WaveFileWriter(outFs, waveFormat))
        {
            // フォーマットが 16bit PCM の場合を想定した例。
            // （もしオリジナルが IEEE Float だった場合はそのまま float -> バイト列変換で書き込んでも良いです。
            //   あるいは NAudio の SampleProvider 系を駆使して WaveFileWriter に書くアプローチも可能です。）
            if (waveFormat.Encoding == WaveFormatEncoding.Pcm && waveFormat.BitsPerSample == 16)
            {
                // float => short に変換しながら書き込み
                short[] shortBuffer = new short[finalMixBuffer.Length];
                for (int i = 0; i < finalMixBuffer.Length; i++)
                {
                    // float(-1.0～1.0) -> short(-32768～32767)
                    float val = finalMixBuffer[i];
                    if (val > 1.0f) val = 1.0f;
                    if (val < -1.0f) val = -1.0f;
                    shortBuffer[i] = (short)(val * short.MaxValue);
                }

                // バイナリデータとして書く
                // WriteSamples は 16-bit の場合、配列長の 2倍バイト数が書き込まれる
                writer.WriteSamples(shortBuffer, 0, shortBuffer.Length);
            }
            else
            {
                // PCM 16bit 以外の場合、適宜実装を変更
                // 例: 32-bit float の WaveFormat を作って書く、など。
                throw new NotImplementedException("16bit PCM 以外の形式は未対応のサンプルです。");
            }
        }

        // 完了
        return retSrcList;
    }

    /// <summary>
    /// ミリ秒をサンプル数に変換するヘルパー関数
    /// </summary>
    private static int MsecToSamples(int msec, WaveFormat format)
    {
        long samples = (long)format.SampleRate * msec / 1000 * format.Channels;
        if (samples > int.MaxValue) return int.MaxValue;
        return (int)samples;
    }

    /// <summary>
    /// 部分波形データにフェードイン／フェードアウトを掛ける（In/Out の長さは同じ）
    /// </summary>
    /// <param name="data">処理対象データ（float配列、-1～1 程度を想定）</param>
    /// <param name="fadeSamples">フェードに要するサンプル数</param>
    private static void ApplyFadeInOut(float[] data, int fadeSamples)
    {
        if (data.Length < fadeSamples * 2) return; // 既にこの時点で短すぎる場合は何もしない

        // フェードイン
        for (int i = 0; i < fadeSamples; i++)
        {
            float mul = (float)i / fadeSamples; // 0 ～ 1
            data[i] *= mul;
        }
        // フェードアウト
        for (int i = 0; i < fadeSamples; i++)
        {
            int idx = data.Length - fadeSamples + i;
            float mul = 1.0f - (float)i / fadeSamples; // 1 ～ 0
            data[idx] *= mul;
        }
    }

    /// <summary>
    /// source を dest に加算書き込み（OverlapAdd）するヘルパー
    /// </summary>
    private static void WriteOverlapAdd(
        float[] source,
        int sourceOffset,
        int sourceCount,
        float[] dest,
        int destOffset)
    {
        int sMax = sourceOffset + sourceCount;
        int dMax = destOffset + sourceCount;
        if (sMax > source.Length) sMax = source.Length;
        if (dMax > dest.Length) dMax = dest.Length;

        int length = Math.Min(sMax - sourceOffset, dMax - destOffset);
        for (int i = 0; i < length; i++)
        {
            dest[destOffset + i] += source[sourceOffset + i];
        }
    }
}

public class ProcessAudioEffect1Setting
{
    /// <summary>
    /// ステレオ揺れを生むLFOの基本周波数(Hz)
    /// 例: 0.25 ならば約4秒周期の揺れ
    /// </summary>
    public double LfoFrequency = 0.25;

    /// <summary>
    /// 揺れの深さ(0.0～1.0程度を推奨)
    /// 1.0 に近いほど左右の振り幅が大きくなる
    /// </summary>
    public double Depth = 0.5;

    /// <summary>
    /// 不規則なランダム揺らぎを混ぜる比率(0.0～1.0)
    /// 0.0で完全に規則的(純粋なLFO)、1.0で強いランダム揺らぎ
    /// </summary>
    public double RandomFactor = 0.3;

    /// <summary>
    /// LFOの波形の位相進行スピードに掛ける倍率
    /// (LfoFrequency が小さくても、この値を大きくすれば速く変調される)
    /// </summary>
    public double LfoSpeedMultiplier = 1.0;

    /// <summary>
    /// 不規則変動(Random Walk)のステップサイズ
    /// 数値を大きくすると、より激しく不規則に変動する
    /// </summary>
    public double RandomStepSize = 0.0005;

    /// <summary>
    /// 不規則変動を制限する(振幅が大きくなりすぎない)ための最大値
    /// </summary>
    public double RandomWalkLimit = 1.0;
}

public class ProcessAudioEffect2Setting
{
    /// <summary>
    /// LFO等でボリュームを変動させる際の振幅 (0.0～1.0 程度)
    /// </summary>
    public double Depth { get; set; } = 0.5;

    /// <summary>
    /// ゆらぎの最小周波数(Hz)。UseRandomFrequency が true の場合、これと FrequencyMax の範囲内でランダムに変化する
    /// </summary>
    public double FrequencyMin { get; set; } = 0.2;

    /// <summary>
    /// ゆらぎの最大周波数(Hz)。UseRandomFrequency が true の場合、これと FrequencyMin の範囲内でランダムに変化する
    /// </summary>
    public double FrequencyMax { get; set; } = 1.0;

    /// <summary>
    /// 周波数をリアルタイムにランダム変化させるかどうか
    /// </summary>
    public bool UseRandomFrequency { get; set; } = true;

    /// <summary>
    /// ボリュームゆらぎ以外に位相差(微小ディレイ)による効果を与えるかどうか
    /// </summary>
    public bool EnablePhaseShift { get; set; } = true;

    /// <summary>
    /// 位相差(サンプル数)。EnablePhaseShift=true のとき適用
    /// 例えば 10 サンプル程度のディレイを片側チャネルにだけ付加し、頭の上下や左右に動く錯覚をより強調する
    /// </summary>
    public int PhaseShiftSamples { get; set; } = 10;

    /// <summary>
    /// 効果の種類を表すサンプル enum
    /// </summary>
    public EffectType EffectMode { get; set; } = EffectType.Basic;

    [Flags]
    public enum EffectType
    {
        Basic,
        Extended,
        Crazy
    }
}



public class ProcessAudioEffect3Setting
{
    /// <summary>
    /// 1サイクル(遠方→接近→通過→離脱→遠方に戻る)に要する時間(秒)。
    /// 例: 2.0 なら2秒で1往復する。
    /// </summary>
    public double CycleSeconds = 2.0;

    /// <summary>
    /// 最遠時の音量(距離感の演出)。0.0～1.0程度で設定。
    /// 例: 0.2 くらいにすると、最遠時は音が小さくなる。
    /// </summary>
    public double VolumeFar = 0.2;

    /// <summary>
    /// 最接近時の音量(距離感の演出)。0.0～1.0程度で設定。
    /// 例: 1.0 なら最接近時は原音量(最大)になる。
    /// </summary>
    public double VolumeNear = 1.0;

    /// <summary>
    /// パン幅。-1.0(完全左)～+1.0(完全右)を振り切るなら 1.0 等。
    /// 数値を小さくすると、左右の振れ幅が抑えられる。
    /// </summary>
    public double PanWidth = 1.0;

    /// <summary>
    /// パン方向を反転するかのフラグ。true なら左右を反転して演出。
    /// </summary>
    public bool ReversePan = false;

    /// <summary>
    /// ドップラー効果のようなピッチ変化をシミュレートする場合の強度。
    /// 0.0 ならピッチ変化なし。1.0 ならサイクルに応じたある程度のピッチ変化。
    /// 大きくしすぎると不自然な音程になる場合がある。
    /// </summary>
    public double DopplerFactor = 0.0;

    // 必要に応じて他のパラメータ(フランジャーの深さやLFOの形状など)を追加してもよい。
}

public static class AudioEffectProcessor
{
    /// <summary>
    /// 44.1kHz/16bit/ステレオ の WAV PCM データ(ヘッダ抜き)をメモリ上で加工する。
    /// targetWav 内のデータを直接書き換える。
    /// </summary>
    /// <param name="targetWav">加工対象の WAV PCM データ(ヘッダなし、44.1kHz/16bit/stereo)</param>
    /// <param name="setting">効果パラメータ</param>
    public static void ProcessAudioEffect3(Memory<byte> targetWav, ProcessAudioEffect3Setting setting)
    {
        // 16bitステレオなので、
        //  - 1サンプル = 左チャネル(short) + 右チャネル(short) = 4バイト
        // 全サンプル数 = targetWav.Length / 4

        const int sampleRate = 44100;
        int totalBytes = targetWav.Length;
        int totalSamples = totalBytes / 2;     // 16bitごと(=2バイト単位)の数
        int totalFrames = totalSamples / 2;   // ステレオ(Left+Right)で1フレームなので /2

        // スパンを取得
        var span = targetWav.Span;

        // ドップラー効果(ピッチ変化)を行う場合のために、読み出し位置(実サンプル位置)と書き込み位置を別管理
        // ただし、ここでは簡易的に同じバッファに上書きするので、実際には大掛かりなバッファの再配置が必要になる場合がある。
        // 今回のデモではあまり厳密にはやらず、単純に「読み書き同じ位置だが、一応サイクルに合わせてピッチを少し揺らす」イメージ。
        double dopplerFactor = setting.DopplerFactor;
        double pitchPos = 0.0;   // ドップラー時の「サンプル再生位置」の仮想値
        double pitchStep = 1.0;  // 1サンプルごとの前進量(=1.0が等速、>1.0で再生が早くなりピッチ↑、<1.0で遅くなりピッチ↓)

        for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            // 時間(t)を計算 (単位: 秒)
            // ドップラーでサンプル読み出し位置がずれる場合は本来 pitchPos / sampleRate などだが、
            // ここでは frameIndex を使った「実処理中のサンプル時刻」として計算(簡易版)。
            double t = (double)frameIndex / sampleRate;

            // 1サイクルあたり CycleSeconds なので、その位相を 0～1 で算出
            double phase = (t % setting.CycleSeconds) / setting.CycleSeconds;

            // 距離感(音量)の変化: ここでは 0～π のコサイン波を使って [VolumeFar..VolumeNear] を往復させるイメージ
            // (好みに応じてサイン波でも三角波でも可)
            // 例: コサイン波で 1→-1 に変化させ、そこから音量をマッピング
            double cosVal = Math.Cos(phase * Math.PI * 2.0); // -1.0 ～ +1.0
            // -1.0～+1.0 を 0.0～1.0 に変換 => (cosVal + 1.0)/2.0
            double distLerp = (cosVal + 1.0) / 2.0; // 0～1
            // VolumeFar～VolumeNear に線形補間
            double volume = setting.VolumeFar + (setting.VolumeNear - setting.VolumeFar) * distLerp;

            // 左右パン(サイン波で -1.0～+1.0 に振る)
            double panSign = setting.ReversePan ? -1.0 : 1.0;
            double panVal = Math.Sin(phase * Math.PI * 2.0) * panSign; // -1.0 ～ +1.0
            // 左チャンネルと右チャンネルのそれぞれに掛ける係数を決定
            // 通常は pan=-1.0 で L=1.0,R=0.0、pan=+1.0 で L=0.0,R=1.0 のように計算するが、
            // ここでは stereo balance の簡易式として採用
            double leftFactor = (1.0 - panVal) * 0.5;   // panVal=-1 => leftFactor=1.0, panVal=+1 => leftFactor=0.0
            double rightFactor = (1.0 + panVal) * 0.5;  // panVal=-1 => rightFactor=0.0, panVal=+1 => rightFactor=1.0

            // PanWidth を加味(±PanWidthの範囲に縮める・広げる)
            // 例: PanWidth=1.0 => -1～+1 をそのまま, PanWidth=0.5 => -0.5～+0.5 に
            //   => その結果、leftFactor, rightFactor も中央寄りになる
            double effectivePanVal = panVal * setting.PanWidth;
            leftFactor = (1.0 - effectivePanVal) * 0.5;
            rightFactor = (1.0 + effectivePanVal) * 0.5;

            // 実際の読み書き位置を求める
            // 簡易的に frameIndex をそのまま使うが、ドップラーFactor を考慮してピッチステップを変動させる例
            // (あまり精密ではないが効果の雰囲気を出すためのデモ)
            double dopplerPhase = (phase * 2.0 - 1.0); // -1.0～+1.0
            // 正接近時(phase~0.5あたり)にピッチが上がり、離れる時にピッチが下がるようなイメージ
            // (好きな数式にしてOK)
            pitchStep = 1.0 + dopplerFactor * dopplerPhase * 0.5;
            pitchPos += pitchStep; // 次回に向けて進める

            // ピッチ変化を適用した「読み出しサンプル位置」
            int sampleReadIndex = (int)pitchPos;

            // バッファの外を読まないようにクリップ
            if (sampleReadIndex < 0) sampleReadIndex = 0;
            if (sampleReadIndex >= totalFrames) sampleReadIndex = totalFrames - 1;

            // 実際の書き込み先 frameIndex に対し、読み出し元は sampleReadIndex を参照
            // WAV PCM データは [L, R, L, R, ...] の順で short (2バイト) が交互に並んでいる
            int readOffset = sampleReadIndex * 2 * sizeof(short);
            short sampleL = MemoryMarshal.Read<short>(span.Slice(readOffset, 2));
            short sampleR = MemoryMarshal.Read<short>(span.Slice(readOffset + 2, 2));

            // volume (距離感) とパン係数 を掛け合わせて出力サンプルを計算
            double outL = sampleL * volume * leftFactor;
            double outR = sampleR * volume * rightFactor;

            // 16bitの範囲にクリップ(-32768～32767)
            short finalL = (short)Math.Clamp((int)outL, short.MinValue, short.MaxValue);
            short finalR = (short)Math.Clamp((int)outR, short.MinValue, short.MaxValue);

            // 書き込み先
            int writeOffset = frameIndex * 2 * sizeof(short);
            MemoryMarshal.Write(span.Slice(writeOffset, 2), ref finalL);
            MemoryMarshal.Write(span.Slice(writeOffset + 2, 2), ref finalR);
        }
    }

    /// <summary>
    /// targetWav(44.1kHz/16bit/ステレオ)のPCMデータに対して
    /// 「ゆらゆら動くような幻想的効果」を与えるサンプル処理
    /// </summary>
    /// <param name="targetWav">
    /// 加工対象の WAVファイル音声データ (ヘッダ無し)。44.1kHz/2ch/16bit PCM
    /// </param>
    /// <param name="setting">
    /// 効果設定パラメータ
    /// </param>
    public static void ProcessAudioEffect2(Memory<byte> targetWav, ProcessAudioEffect2Setting setting)
    {
        // 44.1kHz, 2ch, 16bit => 1サンプル(フレーム)あたり4バイト
        const int BytesPerSample = 2;   // 16bit
        const int Channels = 2;        // ステレオ
        const int BytesPerFrame = BytesPerSample * Channels;
        const int SampleRate = 44100;

        // PCMデータ全体のフレーム数(ステレオフレーム数)
        int totalFrames = targetWav.Length / BytesPerFrame;
        if (totalFrames == 0) return;

        // メモリから配列を取り出す (Span<>を利用するサンプル)
        Span<byte> dataSpan = targetWav.Span;

        // -- パラメータを取得 --
        double depth = setting.Depth;               // ボリューム変動の振幅 (0.0～1.0想定)
        double freqMin = Math.Max(0.0, setting.FrequencyMin);
        double freqMax = Math.Max(freqMin, setting.FrequencyMax);
        bool useRandomFreq = setting.UseRandomFrequency;
        bool enablePhaseShift = setting.EnablePhaseShift;
        int phaseShiftSamples = Math.Max(0, setting.PhaseShiftSamples);

        // LFO用の状態管理変数 (ランダム周波数など)
        Random rand = new Random();
        double currentFreq = freqMin + (freqMax - freqMin) * rand.NextDouble(); // 初期周波数
        double phase = 0.0;   // LFOの位相(0～2πを想定してループさせる)
        double twoPi = 2.0 * Math.PI;

        // 周期的に周波数を変化させる (サンプル実装として数千フレームおきに変化)
        int framesUntilNextFreqChange = 2000;

        // 片チャネルへのディレイ用に、ディレイバッファを準備（Rightチャネルを遅らせる例）
        // ディレイバッファ = phaseShiftSamples 個のステレオフレーム分を取る
        // ただし Right チャンネルだけずらすので、右チャネル分だけあればよい
        short[] delayBuffer = new short[phaseShiftSamples];

        // EffectMode によって処理を変えても良い (ここでは簡単にswitchだけ用意)
        switch (setting.EffectMode)
        {
            case ProcessAudioEffect2Setting.EffectType.Basic:
                // 何もしない特別ケース or 下記の標準処理と同一にする
                break;
            case ProcessAudioEffect2Setting.EffectType.Extended:
                // もう少し激しく LFO をかける等
                depth *= 1.2;
                break;
            case ProcessAudioEffect2Setting.EffectType.Crazy:
                // さらに大きく、かつ位相ディレイも絶対ONにしてしまう等
                depth *= 1.5;
                enablePhaseShift = true;
                break;
        }

        // -- メインループ --
        for (int i = 0; i < totalFrames; i++)
        {
            // ランダム周波数変化
            if (useRandomFreq)
            {
                if (--framesUntilNextFreqChange <= 0)
                {
                    // 周波数を (freqMin ～ freqMax) の範囲で新しくランダムに決める
                    currentFreq = freqMin + (freqMax - freqMin) * rand.NextDouble();
                    framesUntilNextFreqChange = 2000 + rand.Next(2000); // 次の変化までをまた適当に決める
                }
            }

            // 現在の LFO位相に基づいてボリューム係数を計算する ( -1.0 ～ +1.0 )
            double lfo = Math.Sin(phase);

            // この lfo を 0.0～1.0 にマッピングして左右に振るようなイメージ
            // 例：Left = 1 - (depth * lfo), Right = 1 + (depth * lfo)
            // ただし -1.0～+1.0 の範囲 → depth分だけ縮めて左右を変調
            double leftGain = 1.0 - (depth * lfo);
            double rightGain = 1.0 + (depth * lfo);

            // 16bitステレオフレームの先頭バイト位置
            int frameIndex = i * BytesPerFrame;

            // 左チャネルサンプル (16bit = 2byte, little endian)
            short leftSample = (short)((dataSpan[frameIndex + 1] << 8) | dataSpan[frameIndex]);

            // 右チャネルサンプル
            short rightSample = (short)((dataSpan[frameIndex + 3] << 8) | dataSpan[frameIndex + 2]);

            // 位相シフトをかける場合、まず右チャネルの過去サンプルを使う
            short delayedRightSample = rightSample;
            if (enablePhaseShift && phaseShiftSamples > 0)
            {
                // delayBuffer[0]に最も古いサンプル、末尾に最も新しいサンプルを突っ込むイメージ
                // まず取り出し
                delayedRightSample = delayBuffer[0];

                // シフト
                Buffer.BlockCopy(delayBuffer, sizeof(short), delayBuffer, 0, (phaseShiftSamples - 1) * sizeof(short));
                // 新しいサンプルを末尾へ
                delayBuffer[phaseShiftSamples - 1] = rightSample;
            }

            // LFOボリュームを適用
            double newLeft = leftSample * leftGain;
            double newRight = delayedRightSample * rightGain;

            // オーバーフローしないように short 範囲(-32768～32767)へクリップ
            short outLeft = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, newLeft));
            short outRight = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, newRight));

            // メモリに書き戻し (little endian)
            dataSpan[frameIndex] = (byte)(outLeft & 0xFF);
            dataSpan[frameIndex + 1] = (byte)((outLeft >> 8) & 0xFF);
            dataSpan[frameIndex + 2] = (byte)(outRight & 0xFF);
            dataSpan[frameIndex + 3] = (byte)((outRight >> 8) & 0xFF);

            // 位相を進める (LFOの 1サンプル刻み)
            // currentFreq [Hz] -> 1秒間に currentFreq [周期] 進行 -> 1サンプル (1/44100秒) で位相は 2π * (currentFreq / 44100)
            phase += (twoPi * currentFreq / SampleRate);
            // 位相が 2π を超えたら折り返し
            if (phase > twoPi) phase -= twoPi;
        }
    }

    /// <summary>
    /// ステレオPCM(44.1kHz/16bit/2ch, WAVヘッダなし)のデータを不規則なステレオ回転効果で加工する。
    /// </summary>
    /// <param name="targetWav">対象のPCMデータ(ステレオ, 16bit, 44.1kHz)</param>
    /// <param name="setting">効果パラメータ</param>
    public static void ProcessAudioEffect1(Memory<byte> targetWav, ProcessAudioEffect1Setting setting)
    {
        // サンプルレート、チャンネル数、ビット深度は固定
        const int sampleRate = 44100;
        const int channels = 2;
        const int bytesPerSample = 2; // 16bit = 2bytes
        int frameSize = channels * bytesPerSample; // 1フレーム(左右2ch分)あたり4バイト

        // targetWav のサイズから総フレーム数を求める
        int totalFrames = targetWav.Length / frameSize;
        if (totalFrames == 0) return; // データが不十分なら何もしない

        // スパンを取得（in-place 処理）
        Span<byte> dataSpan = targetWav.Span;

        // LFO用の位相、RandomWalk用の現在値
        double phase = 0.0;
        double randomWalkValue = 0.0;

        // ランダム生成器
        Random rand = new Random();

        // 1サンプルあたりの位相進行量(2π * 周波数 / サンプルレート)に速度倍率を掛ける
        double phaseIncrement = 2.0 * Math.PI * setting.LfoFrequency / sampleRate * setting.LfoSpeedMultiplier;

        for (int i = 0; i < totalFrames; i++)
        {
            // 現在のLFO値( -1.0 ～ +1.0 )
            double lfo = Math.Sin(phase);

            // ランダム揺らぎの計算: ランダムウォークで -step ～ +step 変動
            double step = (rand.NextDouble() - 0.5) * 2.0 * setting.RandomStepSize;
            randomWalkValue += step;
            // 振幅制限(±RandomWalkLimit)
            if (randomWalkValue > setting.RandomWalkLimit) randomWalkValue = setting.RandomWalkLimit;
            if (randomWalkValue < -setting.RandomWalkLimit) randomWalkValue = -setting.RandomWalkLimit;

            // LFO値とランダムウォーク値を、RandomFactor の比率でミックス
            // RandomFactor=0なら純粋LFO、1なら純粋RandomWalk
            double mixedValue = (1.0 - setting.RandomFactor) * lfo
                              + setting.RandomFactor * randomWalkValue;

            // 混合値にDepthを掛ける( -Depth ～ +Depth )
            double pan = setting.Depth * mixedValue;

            // 左右ゲイン計算
            // pan>0 のとき Right が大きく、pan<0 のとき Left が大きくなるイメージ
            // ここでは「中央1.0 ± pan」で単純に左右をシフト
            double leftGain = 1.0 - pan;
            double rightGain = 1.0 + pan;

            // 左右チャンネルのサンプルを取り出して、ゲインを掛けて書き戻す
            int baseIndex = i * frameSize;

            // 左サンプル(16bit)取得
            short leftSample = (short)(dataSpan[baseIndex] | (dataSpan[baseIndex + 1] << 8));
            // 右サンプル(16bit)取得
            short rightSample = (short)(dataSpan[baseIndex + 2] | (dataSpan[baseIndex + 3] << 8));

            // 倍率適用(オーバーフローを避けるためにintで計算してからClamp)
            int newLeft = (int)Math.Round(leftSample * leftGain);
            int newRight = (int)Math.Round(rightSample * rightGain);

            // 16bitの範囲にクリップ(-32768～32767)
            newLeft = Math.Clamp(newLeft, short.MinValue, short.MaxValue);
            newRight = Math.Clamp(newRight, short.MinValue, short.MaxValue);

            // 書き戻し(リトルエンディアン)
            dataSpan[baseIndex + 0] = (byte)(newLeft & 0xFF);
            dataSpan[baseIndex + 1] = (byte)((newLeft >> 8) & 0xFF);
            dataSpan[baseIndex + 2] = (byte)(newRight & 0xFF);
            dataSpan[baseIndex + 3] = (byte)((newRight >> 8) & 0xFF);

            // 位相を進める
            phase += phaseIncrement;
            if (phase > 2.0 * Math.PI)
            {
                phase -= 2.0 * Math.PI; // 周期を越えたら折り返し
            }
        }
    }
}



public static class AiWaveVolumeUtils
{
    /// <summary>
    /// 1フレーム(ステレオ2サンプル)当たりの無音とみなす振幅の目安 (dB)
    /// </summary>
    private const double GateThresholdDb = -50.0; // -50 dB
    /// <summary>
    /// 線形振幅に変換した際のしきい値 (± 振幅)
    /// </summary>
    private static readonly double GateThresholdAmplitude = Math.Pow(10.0, GateThresholdDb / 20.0); // 約 0.00316

    /// <summary>
    /// (関数1) WAV のステレオ波形データ(16ビット, L/R)から、ゲート付きの平均ボリューム(dB)を返す。
    /// </summary>
    /// <param name="waveFileIn">
    ///     ステレオ WAV(44.1 kHz, 16bit, 2ch)の生 PCM データ(ヘッダ抜き)
    ///     [L, R, L, R, ...] の順番で交互に 16 ビット値が並ぶ。
    /// </param>
    /// <param name="cancel">中断用トークン</param>
    /// <returns>左右チャンネルを合成した RMS の平均値(ゲート除外あり)を dB 表記した値</returns>
    public static double CalcMeanVolume(Memory<byte> waveFileIn, CancellationToken cancel)
    {
        // 1サンプル(16bit) = 2バイト
        // ステレオなので1フレーム = 4バイト (L 2byte + R 2byte)
        // 全サンプル数(左右合計) = waveFileIn.Length / 2
        // フレーム数(ステレオ)  = waveFileIn.Length / 4
        int frameCount = waveFileIn.Length / 4;
        if (frameCount == 0)
        {
            // PCM データが無い場合は -∞ に近い値として返す (例: -90 dB)
            return -90.0;
        }

        var span = waveFileIn.Span;

        double sumOfSquares = 0.0;  // RMS 用(振幅^2 の合計)
        long validCount = 0;        // ゲートしきい値を超えたフレーム数

        for (int i = 0; i < frameCount; i++)
        {
            cancel.ThrowIfCancellationRequested();

            // ステレオ L/R の 16bit サンプル値を取得 (リトルエンディアン)
            short left = (short)((span[4 * i + 1] << 8) | span[4 * i + 0]);
            short right = (short)((span[4 * i + 3] << 8) | span[4 * i + 2]);

            // [-32768, 32767] → [-1.0, +1.0] へ正規化
            double leftFloat = left / 32768.0;
            double rightFloat = right / 32768.0;

            // ステレオのフレームRMS: sqrt( (L^2 + R^2) / 2 ) 
            double frameAmplitude = Math.Sqrt((leftFloat * leftFloat + rightFloat * rightFloat) / 2.0);

            // ゲートを超えるか判定 (-50 dB 相当)
            if (frameAmplitude > GateThresholdAmplitude)
            {
                // フレームRMS を二乗したものを積算 (RMS計算用)
                sumOfSquares += (frameAmplitude * frameAmplitude);
                validCount++;
            }
        }

        if (validCount == 0)
        {
            // 全てがほぼ無音ゲート以下の場合
            return -90.0; // 仮に -90 dB とする
        }

        // 有効フレームの平均平方値
        double meanSquare = sumOfSquares / validCount;
        // RMS に戻す
        double rms = Math.Sqrt(meanSquare);

        // dB化 (20 * log10(rms))
        // rms が非常に小さい場合は -∞ に行かないようクリップ
        if (rms < 1.0e-10)
        {
            return -90.0;
        }

        double volumeDb = 20.0 * Math.Log10(rms);
        return volumeDb;
    }

    /// <summary>
    /// (関数2) 指定された平均ボリューム(dB)になるように、WAV のステレオ波形データ(16ビット, L/R)をスケールする。
    /// </summary>
    /// <param name="waveFileInOut">
    ///     ステレオ WAV(44.1 kHz, 16bit, 2ch)の生 PCM データ(ヘッダ抜き)。
    ///     音量調整処理を施した結果はこの配列を直接上書きする。
    /// </param>
    /// <param name="targetVolume">目指す平均ボリューム(dB)。CalcMeanVolume の戻り値と同じ定義。</param>
    /// <param name="cancel">中断用トークン</param>
    public static void AdjustVolume(Memory<byte> waveFileInOut, double targetVolume, CancellationToken cancel)
    {
        // まず現在の平均ボリューム(dB)を測る
        double currentVolumeDb = CalcMeanVolume(waveFileInOut, cancel);

        // dB 差分
        double deltaDb = targetVolume - currentVolumeDb;
        // 線形スケール倍率 = 10^(deltaDb/20)
        double scale = Math.Pow(10.0, deltaDb / 20.0);

        // スケールが 1.0 とほぼ同じなら何もしない
        if (Math.Abs(scale - 1.0) < 1.0e-8)
        {
            return;
        }

        // 全サンプルにスケールをかけて書き戻す
        int frameCount = waveFileInOut.Length / 4;
        var span = waveFileInOut.Span;

        for (int i = 0; i < frameCount; i++)
        {
            cancel.ThrowIfCancellationRequested();

            short left = (short)((span[4 * i + 1] << 8) | span[4 * i + 0]);
            short right = (short)((span[4 * i + 3] << 8) | span[4 * i + 2]);

            // スケーリング (float化して乗算し、clamp)
            double newLeft = left * scale;
            double newRight = right * scale;

            short leftScaled = (short)Math.Clamp((int)Math.Round(newLeft), short.MinValue, short.MaxValue);
            short rightScaled = (short)Math.Clamp((int)Math.Round(newRight), short.MinValue, short.MaxValue);

            // 書き戻し (リトルエンディアン)
            span[4 * i + 0] = (byte)(leftScaled & 0xFF);
            span[4 * i + 1] = (byte)((leftScaled >> 8) & 0xFF);
            span[4 * i + 2] = (byte)(rightScaled & 0xFF);
            span[4 * i + 3] = (byte)((rightScaled >> 8) & 0xFF);
        }
    }
}

[Flags]
public enum AiAudioEffectSpeedType
{
    Light = 0,
    Normal = 1,
    Heavy = 2,
}

public static class AiAudioEffectCollection
{
    public static IReadOnlyList<AiAudioEffectBase> AllCollectionList { get; }
    public static ShuffledEndlessQueue<AiAudioEffectBase> AllCollectionRandomQueue { get; }

    static AiAudioEffectCollection()
    {
        List<AiAudioEffectBase> allc = new();

        allc.Add(new AiAudioEffect_02_NearFar());
        allc.Add(new AiAudioEffect_02_NearFar());
        allc.Add(new AiAudioEffect_04_FreeFall());
        allc.Add(new AiAudioEffect_05_Flap());
        allc.Add(new AiAudioEffect_07_Tremolo());
        allc.Add(new AiAudioEffect_07_Tremolo());

        AllCollectionList = allc;

        AllCollectionRandomQueue = new(allc);
    }
}

public interface IAiAudioEffectSettings
{
}

public abstract class AiAudioEffectBase
{
    protected abstract IAiAudioEffectSettings NewSettingsFactoryImpl();
    protected abstract IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type);
    protected abstract void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel);

    public IAiAudioEffectSettings NewSettingsFactory() => NewSettingsFactoryImpl();
    public IAiAudioEffectSettings NewSettingsFactoryWithRandom(AiAudioEffectSpeedType type = AiAudioEffectSpeedType.Normal)
    {
        if (type == AiAudioEffectSpeedType.Heavy) type = AiAudioEffectSpeedType.Normal; // 当面
        return NewSettingsFactoryWithRandomImpl(type);
    }
    public void ProcessFilter(Memory<byte> waveFileInOut, IAiAudioEffectSettings settings, CancellationToken cancel = default) => ProcessFilterImpl(waveFileInOut, settings, cancel);
    public void ProcessFilterRandom(Memory<byte> waveFileInOut, AiAudioEffectSpeedType type, CancellationToken cancel = default) => ProcessFilter(waveFileInOut, NewSettingsFactoryWithRandom(type), cancel);
}


#region AiAudioEffect_02_NearFar_Settings
// このクラスが、処理の内容 (効果の程度や挙動の変化) を決定する枢要なパラメータ指定部分である。
public class AiAudioEffect_02_NearFar_Settings : IAiAudioEffectSettings
{
    /*
     * 【各パラメータの説明】
     * 
     * ■ MaxSpeed
     *    - 音源がランダムに移動する際の、速度ベクトルの最大値 (単位: 「リスナーを中心とする仮想空間上」での 1秒あたりの移動量)。
     *    - 値を大きくすると、音源の動きが激しくなり、高速で近づいてきたり去っていく挙動になる。
     *    - デフォルト値 3.0 は「そこそこの速度感」を想定した値。
     */
    public double MaxSpeed = 3.0;

    /*
     * ■ MovementChangeInterval
     *    - 音源の移動速度 (方向・大きさ) をランダムに変更する周期 (秒単位)。
     *    - 値を小さくすると、より頻繁に方向転換したり、ランダムさが増して落ち着かない感じになる。
     *    - デフォルト値 0.5 は「1秒に2回ほどの頻度でランダムに動きが変化する」ことを想定。
     */
    public double MovementChangeInterval = 0.5;

    /*
     * ■ MinDistance
     *    - 音源がリスナー (中心) に近づいてくるときの最小距離。これ以上は近づきすぎないようにする。
     *    - 値を小さくすると、音源がリスナーの真横をかすめるような迫力を大きくできるが、0 に近すぎると
     *      計算で無限大に音量が上がるなどの問題が起きやすい。
     *    - デフォルト値 0.5 は「そこそこ近くまで接近して大きな音量変化を感じる」想定。
     */
    public double MinDistance = 0.5;

    /*
     * ■ MaxDistance
     *    - 音源がリスナー (中心) から遠ざかるときの最大距離。これ以上遠くには行かないようにする。
     *    - 値を大きくすると、音源が遠方に離れる時間が増えるため、音量が小さめになる時間が長くなる。
     *    - デフォルト値 5.0 は「ある程度遠くまで離れていく」想定。
     */
    public double MaxDistance = 5.0;

    /*
     * ■ EnablePitchMod
     *    - ドップラー効果のような、微妙なピッチ変化を擬似的に入れるかどうかのフラグ。
     *    - true なら、近づく際にピッチが上がり、遠ざかる際にピッチが下がる簡易的効果を入れる。
     *    - デフォルト値は true。不要な場合は false。
     */
    public bool EnablePitchMod = true;

    /*
     * ■ PitchModIntensity
     *    - EnablePitchMod=true の時に、有効となるピッチ変化の強度 (±何%までピッチを変化させるか) を決める値。
     *    - たとえば 0.03 なら、±3% 程度のピッチ変化をランダムに加える。
     *    - デフォルト値 0.03。
     */
    public double PitchModIntensity = 0.03;
}

// このクラスが、処理の内容を実装するクラスである。
public class AiAudioEffect_02_NearFar : AiAudioEffectBase
{
    // この関数は、そのまま、書き写すこと。
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl()
        => new AiAudioEffect_02_NearFar_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定を、おおまかな、「強・注・弱」の効果の強さの程度の指定をある程度もとにして、助ける関数
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_02_NearFar_Settings();

        /*
         * ここでは、type の値に従って、Random.Shared を使いながらランダムパラメータを設定している。
         * 「強」「中」「弱」に応じておおまかに以下のように変化させている：
         *   - Heavy: 激しく音源が動き回り、近接や遠方の変化も大きめ
         *   - Normal: 中程度
         *   - Light: ゆったりした移動と近接・遠方変化は控えめ
         */
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                ret.MaxSpeed = Random.Shared.NextDouble() * (10.0 - 5.0) + 5.0;         // 5.0 ～ 10.0
                ret.MovementChangeInterval = Random.Shared.NextDouble() * (0.7 - 0.2) + 0.2; // 0.2 ～ 0.7
                ret.MinDistance = Random.Shared.NextDouble() * (0.7 - 0.2) + 0.2;    // 0.2 ～ 0.7
                ret.MaxDistance = Random.Shared.NextDouble() * (7.0 - 4.0) + 4.0;    // 4.0 ～ 7.0
                ret.EnablePitchMod = (Random.Shared.NextDouble() < 0.9); // 90% の確率で有効に
                ret.PitchModIntensity = Random.Shared.NextDouble() * (0.07 - 0.03) + 0.03; // 0.03 ～ 0.07
                break;

            case AiAudioEffectSpeedType.Normal:
                ret.MaxSpeed = Random.Shared.NextDouble() * (5.0 - 2.0) + 2.0;        // 2.0 ～ 5.0
                ret.MovementChangeInterval = Random.Shared.NextDouble() * (1.0 - 0.5) + 0.5; // 0.5 ～ 1.0
                ret.MinDistance = Random.Shared.NextDouble() * (1.0 - 0.3) + 0.3;     // 0.3 ～ 1.0
                ret.MaxDistance = Random.Shared.NextDouble() * (6.0 - 3.0) + 3.0;     // 3.0 ～ 6.0
                ret.EnablePitchMod = (Random.Shared.NextDouble() < 0.6); // 60% の確率で有効に
                ret.PitchModIntensity = Random.Shared.NextDouble() * (0.05 - 0.01) + 0.01; // 0.01 ～ 0.05
                break;

            case AiAudioEffectSpeedType.Light:
                ret.MaxSpeed = Random.Shared.NextDouble() * (2.0 - 0.3) + 0.3;        // 0.3 ～ 2.0
                ret.MovementChangeInterval = Random.Shared.NextDouble() * (1.5 - 0.8) + 0.8; // 0.8 ～ 1.5
                ret.MinDistance = Random.Shared.NextDouble() * (1.2 - 0.5) + 0.5;     // 0.5 ～ 1.2
                ret.MaxDistance = Random.Shared.NextDouble() * (3.0 - 1.5) + 1.5;     // 1.5 ～ 3.0
                ret.EnablePitchMod = (Random.Shared.NextDouble() < 0.3); // 30% の確率で有効に
                ret.PitchModIntensity = Random.Shared.NextDouble() * (0.03 - 0.005) + 0.005; // 0.005 ～ 0.03
                break;
        }

        return ret;
    }

    // この関数が、「効果」を生み出す処理を実際に行なう枢要部分である。
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容を、扱いやすいようにキャストする。
        AiAudioEffect_02_NearFar_Settings settings = (AiAudioEffect_02_NearFar_Settings)effectSettings;

        /* 
         * 実装部分ここから 
         *
         * WAV データ (44.1kHz, 16bit, ステレオ, ヘッダなし) に対して、
         * 「高速で音源が近付いてきて、左右にかすめたり、ランダムな動きでびっくりさせるようなエフェクト」を付与する。
         * 具体的には：
         *   1) リスナー (中心) を原点 (0,0) とする仮想空間で、音源がランダムな位置に存在し、
         *      ランダムなベクトルで動き回るのをシミュレーションし、サンプルごとに位置を更新。
         *   2) 位置 (x,y) から求めた「距離」に応じて音量を変化 (近づくと大きく、遠ざかると小さく)。
         *   3) 位置 (x) に応じてステレオの左右バランスを変化 (左にあるほど Left チャンネルを強め、Right を弱める)。
         *   4) 必要であれば、ドップラー効果っぽいピッチ変化も導入 (EnablePitchMod が true の場合)。
         *   5) 一定間隔 (MovementChangeInterval 秒) ごとに、速度ベクトルをランダムに変更して予測不能な移動を演出。
         *   6) MinDistance、MaxDistance の範囲を超えないように位置を補正 (または簡易的に反転など)。
         * 
         * 下記では、計算量が多くなるため、巨大なループ処理中に cancel.ThrowIfCancellationRequested() を適宜挿入し、
         * 中断要求に応じられるようにしている。
         */

        // PCM 16bit ステレオの場合、1サンプルあたり4バイト (L, R 各16bit)
        int bytesPerSampleFrame = 4;
        int totalSampleFrames = waveFileInOut.Length / bytesPerSampleFrame;

        // サンプリングレート (44.1kHz)
        double sampleRate = 44100.0;

        // 仮想空間上の音源位置と速度を用意する
        //   とりあえず初期位置は (0, settings.MinDistance) あたりに置いておく(必ずしもここである必要はない)。
        //   速度ベクトルも、最初はランダムに決める。
        double x = 0.0;
        double y = settings.MinDistance + 0.1; // 少しだけ離れたところから始める
        Random rand = new Random();

        // 速度ベクトルをランダム生成するヘルパー関数
        Func<(double vx, double vy)> getRandomVelocity = () =>
        {
            // -MaxSpeed ～ MaxSpeed の範囲でランダムな速度を生成
            double vx = (rand.NextDouble() * 2.0 - 1.0) * settings.MaxSpeed;
            double vy = (rand.NextDouble() * 2.0 - 1.0) * settings.MaxSpeed;
            return (vx, vy);
        };

        (double vx, double vy) = getRandomVelocity();

        // ピッチ変化用のサンプル再生位置管理 (ナイーブなピッチシフタ)
        //   実際のドップラー計算を簡略化し、「接近時は再生速度を少し上げ、離れるときは下げる」といった実装例。
        //   ただし、高品位なピッチシフタではないため、音質の劣化はあり得る。
        double playbackIndex = 0.0; // 現在再生するサンプルフレームの浮動小数位置
        double pitchDelta = 0.0;    // ドップラーによるピッチ変化量

        // 次に速度を変化させる目標時刻 (秒)
        double nextChangeTime = settings.MovementChangeInterval;
        double currentTime = 0.0; // 処理経過時間(秒)
        double dt = 1.0 / sampleRate; // サンプルフレーム間の経過秒数

        // 波形アクセス用の Span
        var span = waveFileInOut.Span;

        // 入力波形を 16bit ステレオとして読み取りながら、書き込みも行うため、元のサンプルを
        // 一時バッファに退避しておき、それを都度読み出す手法を取る。
        // → 大きなメモリ使用になる可能性があるので注意。(ファイルが大きい場合は別手段も検討)
        //   ここではサンプルとして、単純実装のためにすべてロードする。
        short[] originalLeft = new short[totalSampleFrames];
        short[] originalRight = new short[totalSampleFrames];

        // まず元データをコピー
        for (int i = 0; i < totalSampleFrames; i++)
        {
            short l = BitConverter.ToInt16(span.Slice(i * bytesPerSampleFrame, 2));
            short r = BitConverter.ToInt16(span.Slice(i * bytesPerSampleFrame + 2, 2));
            originalLeft[i] = l;
            originalRight[i] = r;
        }

        // 出力先のバッファを 0 クリアしておく (あとで上書き)
        for (int i = 0; i < totalSampleFrames; i++)
        {
            span[i * bytesPerSampleFrame + 0] = 0;
            span[i * bytesPerSampleFrame + 1] = 0;
            span[i * bytesPerSampleFrame + 2] = 0;
            span[i * bytesPerSampleFrame + 3] = 0;
        }

        // メインループ: 出力フレームを 0 から totalSampleFrames-1 まで書き込む
        //   ただし、ピッチ変更 (再生速度変更) を伴うため、
        //   再生する「元データの位置」(playbackIndex) は必ずしも i と等しくない。
        for (int i = 0; i < totalSampleFrames; i++)
        {
            // 中断要求があればここで例外をスロー
            cancel.ThrowIfCancellationRequested();

            // 現在時刻が次の変更時刻を超えたら、速度を再度ランダムに変化させる
            if (currentTime >= nextChangeTime)
            {
                (vx, vy) = getRandomVelocity();
                nextChangeTime += settings.MovementChangeInterval;
            }

            // (1) 位置 x,y を更新
            x += vx * dt;
            y += vy * dt;

            // (2) MinDistance ～ MaxDistance の範囲を超えないように補正 (単純に境界で反射するなど)
            double dist = Math.Sqrt(x * x + y * y);
            if (dist < settings.MinDistance)
            {
                // 小さすぎるので、境界へ押し戻す + 速度ベクトル反転
                double ratio = settings.MinDistance / dist;
                x *= ratio;
                y *= ratio;
                vx = -vx;
                vy = -vy;
            }
            else if (dist > settings.MaxDistance)
            {
                // 大きすぎるので、境界へ押し戻す + 速度ベクトル反転
                double ratio = settings.MaxDistance / dist;
                x *= ratio;
                y *= ratio;
                vx = -vx;
                vy = -vy;
            }
            dist = Math.Sqrt(x * x + y * y); // 再計算

            // (3) ボリュームを計算 (1 / 距離) 程度 (極端になり過ぎないよう、適宜スケール調整もあり)
            double volume = 1.0 / (dist + 0.01); // dist が 0 に近づいた時に無限大にならぬよう微小値を足す

            // (4) ステレオパンを計算: x が負→左側、x が正→右側。
            //     -1.0 ～ +1.0 の範囲のパン値 (pan) としてみる
            //     左ボリューム = volume * (1 - pan)/2
            //     右ボリューム = volume * (1 + pan)/2
            //   pan を x / dist とし、範囲 -1～1 を確保
            double pan = 0.0;
            if (dist > 0.00001)
            {
                pan = Math.Max(-1.0, Math.Min(1.0, x / dist));
            }
            double leftGain = volume * (1.0 - pan) * 0.5;
            double rightGain = volume * (1.0 + pan) * 0.5;

            // (5) ピッチ変化
            //     接近中 (vx*x + vy*y < 0) ならピッチを高く、遠ざかり中なら低くする簡易演算。
            //     vx*x + vy*y は速度ベクトルと位置ベクトルの内積 (正なら遠ざかり, 負なら接近)。
            if (settings.EnablePitchMod)
            {
                double dot = vx * x + vy * y; // 内積
                                              // dot < 0 → 接近, dot > 0 → 遠ざかり
                                              // 接近ならピッチを上げる (playbackIndex を進めるスピードを少し速める)
                                              // 遠ざかりならピッチを下げる
                double sign = (dot < 0.0) ? -1.0 : 1.0;
                // ± settings.PitchModIntensity 程度のピッチ変化を与える
                pitchDelta = 1.0 + sign * settings.PitchModIntensity;
            }
            else
            {
                pitchDelta = 1.0; // ピッチ変化なし
            }

            // (6) 元データのサンプルを再生 (pitchDelta の分だけ読み取り速度を変える)
            //     playbackIndex は浮動小数なので補間が必要だが、ここでは最も単純な nearest neighbor
            //     (または floor) で実装する例を示す。
            int srcIndex = (int)Math.Floor(playbackIndex);
            if (srcIndex < 0) srcIndex = 0;
            if (srcIndex >= totalSampleFrames)
            {
                // もし再生位置が元データを超えてしまったら、ここではループ終了とする。
                // (あるいはループ再生したければ modulo 演算などを使う)
                break;
            }

            short srcL = originalLeft[srcIndex];
            short srcR = originalRight[srcIndex];

            // (7) ボリューム・パン適用
            double outL = srcL * leftGain;
            double outR = srcR * rightGain;

            // 16bitにクリップ
            short finalL = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, (int)(outL)));
            short finalR = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, (int)(outR)));

            // (8) waveFileInOut (span) に書き込む (リトルエンディアン)
            BitConverter.TryWriteBytes(span.Slice(i * bytesPerSampleFrame, 2), finalL);
            BitConverter.TryWriteBytes(span.Slice(i * bytesPerSampleFrame + 2, 2), finalR);

            // (9) 次のサンプルへ向けて時刻とピッチ付き再生位置を更新
            currentTime += dt;
            playbackIndex += pitchDelta; // シンプルにピッチ倍率を足していく (これだと加速度的にずれていくので注意)
        }

        /* 実装部分ここまで */
    }
}

#endregion



#region AiAudioEffect_03_JetCoaster
// このクラスが、処理の内容 (効果の程度や挙動の変化) を決定する枢要なパラメータ指定部分である。
public class AiAudioEffect_03_JetCoaster_Settings : IAiAudioEffectSettings
{
    /* 実装部分ここから */
    /// <summary>
    /// 音像が左右に行き来する際の基本的な周波数(Hz)。
    /// 例: 1.5 は、1.5Hz(約0.66秒周期)で左右を往復するイメージになる。
    /// デフォルト: 1.5
    /// </summary>
    public double PanFrequency = 1.5;

    /// <summary>
    /// 左右振り幅の強度(0.0～1.0程度推奨)。
    /// 0.0 は左右差なし、1.0 は最大振幅(片側が完全に小さくなりもう片側が最大になるイメージ)。
    /// デフォルト: 0.7
    /// </summary>
    public double PanIntensity = 0.7;

    /// <summary>
    /// 「上下に急加速・急落下する」ような振動感(ジェットコースター感)を与える周波数(Hz)。
    /// 例: 2.0 にすると、2Hz(0.5秒周期)で振動が上下に繰り返される。
    /// デフォルト: 2.0
    /// </summary>
    public double RollerCoasterFrequency = 2.0;

    /// <summary>
    /// ジェットコースター感の振動の強度(0.0～1.0程度推奨)。
    /// 振動が音量や左右差にどの程度影響を与えるかを制御する。
    /// デフォルト: 0.5
    /// </summary>
    public double RollerCoasterIntensity = 0.5;

    /// <summary>
    /// ある程度の「予測不能性」を与えるために導入する乱数変動の大きさ(0.0以上)。
    /// 値が大きいほどランダム変動が激しくなる。デフォルト: 0.3
    /// </summary>
    public double RandomJerkiness = 0.3;

    /// <summary>
    /// ステレオの左右チャンネルをあえて逆相(フェーズ反転)にするかどうかのフラグ。
    /// 真の場合は、左右の音を逆位相にして不思議な広がり感を出す。
    /// デフォルト: false
    /// </summary>
    public bool ReversePhase = false;
    /* 実装部分ここまで */
}

// このクラスが、処理の内容を実装するクラスである。
public class AiAudioEffect_03_JetCoaster : AiAudioEffectBase
{
    // この関数は、そのまま、書き写すこと。
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl() => new AiAudioEffect_03_JetCoaster_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定を、おおまかな、「強・注・弱」の効果の強さの程度の指定をある程度もとにして、助ける関数
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_03_JetCoaster_Settings();

        /* 実装部分ここから */
        // type の値に応じて、パラメータをランダムに設定する。
        // Heavy: 強い効果・振り幅が大きくランダムも派手
        // Normal: 中程度
        // Light: 穏やか
        // サンプルとして PanFrequency, PanIntensity, RollerCoasterFrequency, RollerCoasterIntensity, RandomJerkiness の範囲を調整
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                // 左右パンの揺れ(周波数と強度)を広めに乱数設定
                ret.PanFrequency = Random.Shared.NextDouble() * (2.0 - 0.5) + 0.5;  // 0.5 ～ 2.0
                ret.PanIntensity = Random.Shared.NextDouble() * (0.8 - 0.4) + 0.4; // 0.4 ～ 0.8
                                                                                   // ジェットコースター振動
                ret.RollerCoasterFrequency = Random.Shared.NextDouble() * (2.5 - 1.0) + 1.0; // 1.0 ～ 2.5
                ret.RollerCoasterIntensity = Random.Shared.NextDouble() * (0.7 - 0.3) + 0.3; // 0.3 ～ 0.7
                                                                                             // 乱数変動
                ret.RandomJerkiness = Random.Shared.NextDouble() * (0.5 - 0.1) + 0.1; // 0.1 ～ 0.5
                                                                                      // ReversePhase は 30% くらい
                ret.ReversePhase = (Random.Shared.NextDouble() < 0.3);
                break;

            case AiAudioEffectSpeedType.Normal:
                // 左右パン
                ret.PanFrequency = Random.Shared.NextDouble() * (1.5 - 0.4) + 0.4;  // 0.5 ～ 2.0
                ret.PanIntensity = Random.Shared.NextDouble() * (0.6 - 0.3) + 0.3; // 0.4 ～ 0.8
                                                                                   // ジェットコースター振動
                ret.RollerCoasterFrequency = Random.Shared.NextDouble() * (2.0 - 0.7) + 0.7; // 1.0 ～ 2.5
                ret.RollerCoasterIntensity = Random.Shared.NextDouble() * (0.6 - 0.2) + 0.2; // 0.3 ～ 0.7
                                                                                             // 乱数変動
                ret.RandomJerkiness = Random.Shared.NextDouble() * (0.4 - 0.05) + 0.05; // 0.1 ～ 0.5
                                                                                        // ReversePhase は 30% くらい
                ret.ReversePhase = (Random.Shared.NextDouble() < 0.2);
                break;

            case AiAudioEffectSpeedType.Light:
                // 左右パン
                ret.PanFrequency = Random.Shared.NextDouble() * (1.2 - 0.2) + 0.2; // 0.2 ～ 1.2
                ret.PanIntensity = Random.Shared.NextDouble() * (0.5 - 0.2) + 0.2; // 0.2 ～ 0.5
                                                                                   // ジェットコースター振動
                ret.RollerCoasterFrequency = Random.Shared.NextDouble() * (1.5 - 0.5) + 0.5; // 0.5 ～ 1.5
                ret.RollerCoasterIntensity = Random.Shared.NextDouble() * (0.5 - 0.1) + 0.1; // 0.1 ～ 0.5
                                                                                             // 乱数変動
                ret.RandomJerkiness = Random.Shared.NextDouble() * (0.3 - 0.0) + 0.0; // 0.0 ～ 0.3
                                                                                      // ReversePhase は 10% くらい
                ret.ReversePhase = (Random.Shared.NextDouble() < 0.1);
                break;
        }
        /* 実装部分ここまで */

        return ret;
    }

    // この関数が、「効果」を生み出す処理を実際に行なう枢要部分である。
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容を、扱いやすいように、AiAudioEffect_03_JetCoaster_Settings にキャストする。
        AiAudioEffect_03_JetCoaster_Settings settings = (AiAudioEffect_03_JetCoaster_Settings)effectSettings;

        /* 実装部分ここから */
        // 44.1kHz, 16bit, ステレオPCMデータと仮定
        // 1サンプル(フレーム)あたり4バイト(Left 2byte + Right 2byte)
        // NAudio.Wave を使ってもよいし、BitConverter でもよい。

        // メモ:
        //   short 左チャンネル = BitConverter.ToInt16(data, index);
        //   short 右チャンネル = BitConverter.ToInt16(data, index + 2);
        //
        // waveFileInOut.Length は波形PCM部分(ヘッダを除いた実データ)のバイト数
        // サンプル数は waveFileInOut.Length / 4 になる(ステレオなので左+右で4バイト)。

        var data = waveFileInOut.Span;
        int totalBytes = data.Length;
        int sampleCount = totalBytes / 4; // ステレオ1フレームあたり4バイト

        // サンプリングレート(44.1kHz)
        const double sampleRate = 44100.0;

        // ここで時間を進めながら、左右の音をジェットコースター風に動かす。
        // 下記では簡単に:
        //   t: 現在の時間(秒) = i / 44100.0
        //   左右パン(左右の振り幅)と、ジェットコースター(上下の揺れ)を混合。
        //   さらに乱数的な揺らぎを加えて、予測不能性を演出する。
        //
        //   - PanLFO = sin(2π * PanFrequency * t)
        //   - CoasterLFO = sin(2π * RollerCoasterFrequency * t)
        //   - それぞれに Intensity (強度) をかける。
        //   - RandomJerkiness 分だけ、一定サンプルごとにランダム値を加算して揺らぎを出す。
        //
        //   出音レベル(振幅) = 元のサンプル値 * [1 + (PanIntensity * PanLFO * panning係数)]
        //   ただし、Left と Right で panning係数 を異なる符号にして左右差を出す。
        //   さらに全体の音量に (1 + RollerCoasterIntensity * CoasterLFO + ランダム揺らぎ) を乗算。
        //   ReversePhase が true の場合、左右の位相を反転(Leftに+をかけるならRightに-をかける 等)して不思議な効果を追加。
        //
        //   具体的には:
        //     leftFactor  = volumeBase * (1 - panFactor)
        //     rightFactor = volumeBase * (1 + panFactor)
        //   などとしてもよい。(あるいは PanIntensity 次第でスケールする)
        //
        //   ループが巨大なので、適宜 cancel.ThrowIfCancellationRequested() を挟む。

        // ランダム揺らぎを加えるための変数
        // 「ある程度のサンプル間隔ごと」に突然変動させる実装例
        double randomOffsetPan = 0.0;
        double randomOffsetCoaster = 0.0;
        int nextRandomizeCount = 0; // 0になると新しい乱数を発生させる

        // データ処理ループ
        for (int i = 0; i < sampleCount; i++)
        {
            // 中断要求が来ていないかチェック
            if (i % 1024 == 0) // 処理負荷を考慮して適度な頻度でチェック
            {
                cancel.ThrowIfCancellationRequested();
            }

            // 現在時間(秒)
            double t = i / sampleRate;

            // もし「次に乱数を振るタイミング」に達したら、ランダムな揺らぎ値を更新
            if (nextRandomizeCount <= 0)
            {
                // 左右パン揺らぎ
                randomOffsetPan = (Random.Shared.NextDouble() - 0.5) * settings.RandomJerkiness * 2.0;
                // 上下Coaster揺らぎ
                randomOffsetCoaster = (Random.Shared.NextDouble() - 0.5) * settings.RandomJerkiness * 2.0;
                // 次の更新タイミング(乱数で 100～1000サンプル後)
                nextRandomizeCount = Random.Shared.Next(100, 1000);
            }
            nextRandomizeCount--;

            // LFO計算
            double panLFO = Math.Sin(2.0 * Math.PI * settings.PanFrequency * t) + randomOffsetPan;
            double coasterLFO = Math.Sin(2.0 * Math.PI * settings.RollerCoasterFrequency * t) + randomOffsetCoaster;

            // PanIntensity分だけパンを乗算
            panLFO *= settings.PanIntensity;
            // RollerCoasterIntensity分だけ上下(音量)を乗算
            coasterLFO *= settings.RollerCoasterIntensity;

            // 全体音量変動(1 + coasterLFO) で上下振動
            // ただし、coasterLFO が負の場合は若干音量が下がる。
            double volumeBase = 1.0 + coasterLFO;
            if (volumeBase < 0.0) volumeBase = 0.0; // 音量がマイナスにならないようにクリップ

            // 左右パン: -panLFO ～ +panLFO の範囲
            // 例: panLFO = +0.8 => 左チャンネルは小さく(1 - 0.8=0.2倍)、右チャンネルは大きく(1 + 0.8=1.8倍)
            //     panLFO = -0.8 => 逆
            double leftFactor = volumeBase * (1.0 - panLFO);
            double rightFactor = volumeBase * (1.0 + panLFO);

            // フェーズ反転を考慮
            // ReversePhase が true の場合、左右で位相を逆にしてみる
            // (ここでは簡単に、右チャンネルの符号を反転させる例を示す)
            if (settings.ReversePhase)
            {
                rightFactor = -rightFactor;
            }

            // 元のサンプルを取得 (16bit, ステレオ)
            int byteIndex = i * 4;
            short leftSample = BitConverter.ToInt16(data.Slice(byteIndex, 2));
            short rightSample = BitConverter.ToInt16(data.Slice(byteIndex + 2, 2));

            // 計算結果を反映
            double newLeft = leftSample * leftFactor;
            double newRight = rightSample * rightFactor;

            // 16bit の範囲にクリップ
            short outLeft = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(newLeft)));
            short outRight = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(newRight)));

            // 書き戻し
            BitConverter.TryWriteBytes(data.Slice(byteIndex, 2), outLeft);
            BitConverter.TryWriteBytes(data.Slice(byteIndex + 2, 2), outRight);
        }
        /* 実装部分ここまで */
    }
}

#endregion



#region AiAudioEffect_04_FreeFall_Settings
// このクラスが、処理の内容 (効果の程度や挙動の変化) を決定する枢要なパラメータ指定部分である。
public class AiAudioEffect_04_FreeFall_Settings : IAiAudioEffectSettings
{
    /// <summary>
    /// 自由落下のメイン時間(秒)。
    /// ここで設定した時間で「落下エンベロープ」を形成する。大きいほど長い時間かけて落下するイメージになる。
    /// </summary>
    public double FallDurationSec = 2.0;

    /// <summary>
    /// LFO(低周波数振動)の周波数(Hz)。音量やパンに揺らぎを与える。
    /// 値を大きくすると短い周期で揺れるため、より細かくビブラート的な揺れを感じる。
    /// </summary>
    public double LfoFrequency = 1.0;

    /// <summary>
    /// LFOの深さ(0～1程度が目安)。
    /// 音量/パン変化の振幅をどの程度にするかを決める。大きいほど、揺れ幅が大きい。
    /// </summary>
    public double LfoDepth = 0.3;

    /// <summary>
    /// タービュランス(乱流)を有効にするかどうか。true であれば、ランダムな急変動が時々発生する。
    /// </summary>
    public bool EnableTurbulence = true;

    /// <summary>
    /// タービュランスの強度(0～1程度が目安)。
    /// 乱流発生時に、どの程度の変化幅が加わるかを決める。
    /// </summary>
    public double TurbulenceIntensity = 0.4;
}
#endregion

#region AiAudioEffect_04_FreeFall
public class AiAudioEffect_04_FreeFall : AiAudioEffectBase
{
    // そのまま書き写す
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl()
        => new AiAudioEffect_04_FreeFall_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_04_FreeFall_Settings();

        /* 実装部分ここから */
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                // Heavy: より長く激しい落下、深いLFO、強い乱流
                ret.FallDurationSec = Random.Shared.NextDouble() * (5.0 - 2.0) + 2.0; // 2.0 ～ 5.0
                ret.LfoFrequency = Random.Shared.NextDouble() * (2.0 - 0.5) + 0.5;   // 0.5 ～ 2.0
                ret.LfoDepth = Random.Shared.NextDouble() * (1.0 - 0.5) + 0.5;       // 0.5 ～ 1.0
                ret.EnableTurbulence = Random.Shared.NextDouble() < 0.7;            // 約70%で乱流ON
                ret.TurbulenceIntensity = Random.Shared.NextDouble() * (0.8 - 0.3) + 0.3; // 0.3～0.8
                break;

            case AiAudioEffectSpeedType.Normal:
                // Normal: 中程度の落下と揺れ、そこそこの乱流
                ret.FallDurationSec = Random.Shared.NextDouble() * (3.0 - 1.0) + 1.0; // 1.0 ～ 3.0
                ret.LfoFrequency = Random.Shared.NextDouble() * (1.0 - 0.2) + 0.2;    // 0.2 ～ 1.0
                ret.LfoDepth = Random.Shared.NextDouble() * (0.6 - 0.3) + 0.3;        // 0.3 ～ 0.6
                ret.EnableTurbulence = Random.Shared.NextDouble() < 0.4;             // 約40%で乱流ON
                ret.TurbulenceIntensity = Random.Shared.NextDouble() * (0.5 - 0.2) + 0.2; // 0.2～0.5
                break;

            case AiAudioEffectSpeedType.Light:
                // Light: 短い落下と穏やかな揺れ、乱流はあまり期待しない
                ret.FallDurationSec = Random.Shared.NextDouble() * (1.5 - 0.5) + 0.5;  // 0.5 ～ 1.5
                ret.LfoFrequency = Random.Shared.NextDouble() * (0.5 - 0.1) + 0.1;     // 0.1 ～ 0.5
                ret.LfoDepth = Random.Shared.NextDouble() * (0.3 - 0.1) + 0.1;         // 0.1 ～ 0.3
                ret.EnableTurbulence = Random.Shared.NextDouble() < 0.2;              // 約20%で乱流ON
                ret.TurbulenceIntensity = Random.Shared.NextDouble() * (0.3 - 0.1) + 0.1; // 0.1～0.3
                break;
        }
        /* 実装部分ここまで */

        return ret;
    }

    // WAVデータ(44.1kHz, 16bit, ステレオの波形部分)に「自由落下」効果を与える
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容をキャスト
        AiAudioEffect_04_FreeFall_Settings settings = (AiAudioEffect_04_FreeFall_Settings)effectSettings;

        /* 実装部分ここから */

        // パラメータ取得
        double fallDuration = settings.FallDurationSec;   // 自由落下の周期(片道)
        double lfoFreq = settings.LfoFrequency;           // LFO周波数
        double lfoDepth = settings.LfoDepth;              // LFO深さ
        bool enableTurb = settings.EnableTurbulence;      // 乱流フラグ
        double turbIntensity = settings.TurbulenceIntensity; // 乱流強度

        // WAVのサンプリングレート(本タスクでは44.1kHzと仮定)
        const int sampleRate = 44100;
        // ステレオ16bitPCM(Left, Right) = 4バイト/フレーム
        int bytesPerFrame = 4;
        int totalBytes = waveFileInOut.Length;
        int totalFrames = totalBytes / bytesPerFrame; // サンプルフレーム数

        // バイト列を Span で扱う
        var span = waveFileInOut.Span;

        // 乱流イベント関連の制御変数
        //  - 一定時間ごとにランダムで「衝撃」(Amplitude/Pan変化)を起こす例
        //  - nextTurbEventTime: 次の乱流発生予定時刻(秒)
        //  - currentTurbEffect: 乱流が発生している間の係数(瞬間的に加算してだんだん消えるなど)
        double nextTurbEventTime = 0.5;  // 0.5秒後に初回発生を狙う
        double lastTurbTimeSec = -1.0;   // 乱流が起きた時の時刻。-1ならまだ起きていない
        double turbDecayDuration = 0.2;  // 乱流が0.2秒ぐらいかけて減衰するイメージ
        double turbRandomAmp = 0.0;      // 乱流による振幅変化量(ランダム生成)
        double turbRandomPan = 0.0;      // 乱流によるパン変化量(ランダム生成)

        for (int i = 0; i < totalFrames; i++)
        {
            // キャンセル指示が来ていないか、時々チェック
            if (i % 10000 == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            // 現在の時刻(秒)
            double timeSec = (double)i / sampleRate;

            // ----------------
            // 1) 自由落下エンベロープを求める
            //    - 2*fallDuration(往復)でひとつのサイクルとし、繰り返す。
            double cyclePeriod = fallDuration * 2.0;
            double cycleTime = timeSec % cyclePeriod; // 現在のサイクル内での経過時間(0～2*fallDuration)
            // 片道: 0～fallDuration の間は落下(1.0→0.3へ線形に減衰するイメージ)、
            // 戻り: fallDuration～2*fallDuration で 0.3→1.0 に戻るイメージ。
            double minAmp = 0.3; // 落下時の最低振幅(ここの値は好みに合わせて調整)
            double baseEnv;
            if (cycleTime <= fallDuration)
            {
                // 落下フェーズ
                double ratio = cycleTime / fallDuration; // 0.0→1.0
                baseEnv = 1.0 - ratio * (1.0 - minAmp);
            }
            else
            {
                // 戻りフェーズ
                double ratio = (cycleTime - fallDuration) / fallDuration; // 0.0→1.0
                baseEnv = minAmp + ratio * (1.0 - minAmp);
            }

            // ----------------
            // 2) LFO による振幅変調 & パン変調
            //    - LFOは sin() を用いて揺らす。
            double lfoPhase = 2.0 * Math.PI * lfoFreq * timeSec;  // LFOの位相
            double lfoVal = Math.Sin(lfoPhase);                   // -1.0～+1.0

            // amplitudeFactor: baseEnv をさらに揺らす。 (1.0±lfoDepth/2 ではなく、ここでは直接掛け合わせる例)
            // ただし揺れすぎを防ぐため、(1 + lfoDepth*lfoVal) のようにする
            double amplitudeFactor = baseEnv * (1.0 + lfoDepth * lfoVal);

            // panningFactor: 左右で -1.0～+1.0 の揺れを作る
            // たとえば lfoVal=-1～+1 をそのまま利用し、深さを掛け算
            double panVal = lfoDepth * lfoVal;  // -lfoDepth ～ +lfoDepth
            // leftGain = 1 - panVal, rightGain = 1 + panVal をベースに正規化する例
            double leftGain = 1.0 - panVal;
            double rightGain = 1.0 + panVal;
            // このままだと左右とも最大が 1+(lfoDepth) になり得るので、ここでは
            // ざっくり 1 / (1 + lfoDepth) で均等化しておく。
            double norm = 1.0 / (1.0 + lfoDepth);
            leftGain *= norm;
            rightGain *= norm;

            // ----------------
            // 3) 乱流(タービュランス)の発生
            //    - 一定時間(乱数)ごとに、急激な衝撃(ampとpanが変化)が短時間起こる
            if (enableTurb)
            {
                if (timeSec >= nextTurbEventTime)
                {
                    // ここで乱流開始
                    lastTurbTimeSec = timeSec;
                    // 乱流の幅をランダム生成
                    turbRandomAmp = (Random.Shared.NextDouble() * 2.0 - 1.0) * turbIntensity; // -turbIntensity ～ +turbIntensity
                    turbRandomPan = (Random.Shared.NextDouble() * 2.0 - 1.0) * turbIntensity; // 同上
                    // 次の乱流イベントを適当な時間後に設定
                    // (例: 0.3秒後～1.5秒後のどこか)
                    double interval = Random.Shared.NextDouble() * (1.5 - 0.3) + 0.3;
                    nextTurbEventTime = timeSec + interval;
                }

                // 乱流が発動してから turbDecayDuration 秒以内なら効果を適用
                if (lastTurbTimeSec >= 0)
                {
                    double dt = timeSec - lastTurbTimeSec;
                    if (dt <= turbDecayDuration)
                    {
                        double decayRatio = 1.0 - (dt / turbDecayDuration); // 1.0→0.0
                        // 振幅に加算
                        amplitudeFactor += turbRandomAmp * decayRatio;
                        // パンにも加算(左右ゲインに影響させる例)
                        double turbPanOffset = turbRandomPan * decayRatio;
                        leftGain += -turbPanOffset;
                        rightGain += turbPanOffset;
                    }
                    else
                    {
                        // 乱流時間が過ぎたら終了
                        lastTurbTimeSec = -1;
                    }
                }
            }

            // ----------------
            // 4) 実際のPCMデータに反映 (16bitステレオ)
            //    - Byte配列 -> short(左), short(右) に変換して読み書き
            int baseIndex = i * bytesPerFrame;
            short leftSample = BitConverter.ToInt16(span.Slice(baseIndex, 2));
            short rightSample = BitConverter.ToInt16(span.Slice(baseIndex + 2, 2));

            // floatやdouble計算に変換
            double left = leftSample;
            double right = rightSample;

            // 音量・パンの適用
            left *= amplitudeFactor * leftGain;
            right *= amplitudeFactor * rightGain;

            // 16bit範囲にクリップ
            if (left > short.MaxValue) left = short.MaxValue;
            if (left < short.MinValue) left = short.MinValue;
            if (right > short.MaxValue) right = short.MaxValue;
            if (right < short.MinValue) right = short.MinValue;

            // 書き戻し
            short newLeftSample = (short)left;
            short newRightSample = (short)right;
            BitConverter.TryWriteBytes(span.Slice(baseIndex, 2), newLeftSample);
            BitConverter.TryWriteBytes(span.Slice(baseIndex + 2, 2), newRightSample);
        }

        /* 実装部分ここまで */
    }
}
#endregion




#region AiAudioEffect_05_Flap
public class AiAudioEffect_05_Flap_Settings : IAiAudioEffectSettings
{
    /* 実装部分ここから
     * 
     * 【パラメータ例】
     *   1) LfoRateHz:
     *      1秒間に何回「左右移動の変化（ランダムパン）」を行うかを指定する。大きいほど素早い変化となり、効果が激しくなる。
     *      デフォルト: 6.0 (1秒間に6回変化)
     *
     *   2) Depth:
     *      パンの強度(振れ幅)を示す。0.0～1.0程度で扱う想定。
     *      1.0に近いほど、左右への振り切りが大きくなる。
     *      デフォルト: 0.8
     *
     *   3) ReturnToCenterProbability:
     *      新しいパン値を決定するときに、センター(=0)に戻す確率(0.0～1.0)を指定する。
     *      大きいほどセンターに戻りやすく、左右に振り切る時間が少なくなる。
     *      デフォルト: 0.1
     *
     *   4) TransitionLengthSec:
     *      次のパン値に移行するときの「なめらかに移行する時間(秒)」を指定する。0.0なら瞬時切り替え。
     *      デフォルト: 0.01 (10msかけて徐々に次のパン値へ移行する)
     *
     *   5) SmoothedTransition:
     *      true の場合、パン値の切り替えを「TransitionLengthSec」にしたがって滑らかに補間する。
     *      false の場合、瞬時にパン値が切り替わり、より激しい効果になる。
     *      デフォルト: true
     */

    public double LfoRateHz = 6.0;
    public double Depth = 0.8;
    public double ReturnToCenterProbability = 0.1;
    public double TransitionLengthSec = 0.01;
    public bool SmoothedTransition = true;

    /* 実装部分ここまで */
}

// このクラスが、処理の内容を実装するクラスである。
public class AiAudioEffect_05_Flap : AiAudioEffectBase
{
    // この関数は、そのまま、書き写すこと。
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl() => new AiAudioEffect_05_Flap_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定を、おおまかな、「強・注・弱」の効果の強さの程度の指定をある程度もとにして、助ける関数
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_05_Flap_Settings();

        /* 実装部分ここから
         *   type の値に従って、ランダムパラメータの設定をする。
         *   Heavy の場合: 強く激しい効果が出るよう値を大きめに
         *   Normal の場合: 中間的な値
         *   Light の場合: 弱めの値
         */
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                ret.LfoRateHz = 2 * Random.Shared.NextDouble() * (12.0 - 5.0) + 5.0; // 5.0 ～ 12.0
                ret.Depth = Random.Shared.NextDouble() * (1.0 - 0.7) + 0.7;       // 0.7 ～ 1.0
                ret.ReturnToCenterProbability = Random.Shared.NextDouble() * (0.4 - 0.2) + 0.2; // 0.2～0.4
                ret.TransitionLengthSec = Random.Shared.NextDouble() * (0.03 - 0.005) + 0.005;   // 0.005～0.03
                ret.SmoothedTransition = (Random.Shared.NextDouble() < 0.8); // 80%の確率でスムーズ切り替え
                break;

            case AiAudioEffectSpeedType.Normal:
                ret.LfoRateHz = 2 * Random.Shared.NextDouble() * (8.0 - 3.0) + 3.0; // 3.0 ～ 8.0
                ret.Depth = Random.Shared.NextDouble() * (0.8 - 0.4) + 0.4;      // 0.4 ～ 0.8
                ret.ReturnToCenterProbability = Random.Shared.NextDouble() * (0.2 - 0.05) + 0.05; // 0.05～0.2
                ret.TransitionLengthSec = Random.Shared.NextDouble() * (0.02 - 0.005) + 0.005;     // 0.005～0.02
                ret.SmoothedTransition = (Random.Shared.NextDouble() < 0.7);
                break;

            case AiAudioEffectSpeedType.Light:
                ret.LfoRateHz = 2 * Random.Shared.NextDouble() * (3.0 - 1.0) + 1.0; // 1.0 ～ 3.0
                ret.Depth = Random.Shared.NextDouble() * (0.4 - 0.1) + 0.1;      // 0.1 ～ 0.4
                ret.ReturnToCenterProbability = Random.Shared.NextDouble() * (0.1 - 0.0) + 0.0; // 0.0～0.1
                ret.TransitionLengthSec = Random.Shared.NextDouble() * (0.02 - 0.0) + 0.0;       // 0.0～0.02
                ret.SmoothedTransition = (Random.Shared.NextDouble() < 0.5);
                break;
        }
        /* 実装部分ここまで */

        return ret;
    }

    // この関数が、「効果」を生み出す処理を実際に行なう枢要部分である。
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容を、扱いやすいように、AiAudioEffect_05_Flap_Settings にキャストする。
        AiAudioEffect_05_Flap_Settings settings = (AiAudioEffect_05_Flap_Settings)effectSettings;

        /* 実装部分ここから */

        // ■ 概要: 
        //   - ステレオ16bit(左右1サンプルあたり2byte計4byte)の PCM データに対して、
        //     ランダムにパン値(panning factor)を変化させ、左右の音量比を変化させることで
        //     頭の中を左右に激しく行き来するようなサイケデリック効果を与える。
        //   - LfoRateHz 回/秒の周期でパン値を更新し、Depth の値に従って左右への振れ幅を調整する。
        //   - ReturnToCenterProbability の確率で、次のパン値を 0(センター) にする。
        //   - SmoothedTransition が true の場合は、TransitionLengthSec 秒かけて
        //     前回のパン値から線形補間して移動する。
        //   - 定期的に cancel.ThrowIfCancellationRequested(); を呼び出すことで中断要求を処理する。

        var span = waveFileInOut.Span;

        // WAV は 44100Hz, 16bit, Stereo(2ch) と仮定
        int bytesPerSample = 2 * 2;  // 2ch × 16bit(2byte)
        int totalSamples = waveFileInOut.Length / bytesPerSample;
        int sampleRate = 44100;

        double lfoRateHz = settings.LfoRateHz;       // パンを切り替える頻度(Hz)
        double depth = Math.Clamp(settings.Depth, 0.0, 1.0); // 左右振れ幅(0.0～1.0)
        double returnCenterProb = Math.Clamp(settings.ReturnToCenterProbability, 0.0, 1.0);
        double transitionSec = Math.Max(0.0, settings.TransitionLengthSec);
        bool smooth = settings.SmoothedTransition;

        // パン値 p は -1.0～+1.0 で扱い、p>0 なら右寄り, p<0 なら左寄り, p=0 ならセンター。
        // ただし、実際の音量への反映は Depth を掛ける。

        // 何サンプルごとにパンを切り替えるか
        // ※連続的に変化し続けるのではなく、サンプル＆ホールド的に一定のパン値を保ち、
        //   次の区間で新しいパン値をランダムに設定するイメージ
        int samplesPerLfo = (lfoRateHz <= 0.0001)
            ? int.MaxValue
            : (int)(sampleRate / lfoRateHz);

        // 直前区間のパン値と、現在区間での目標パン値を保持
        double currentPan = 0.0;
        double nextPan = 0.0;
        // 線形補間用に区間開始サンプル位置、区間終了サンプル位置を保持
        int regionStartSample = 0;
        int regionEndSample = samplesPerLfo;

        // ランダムパン値を求めるサブ関数
        double GetRandomPan()
        {
            // ReturnToCenterProbabilityの確率で、pan=0を返す
            if (Random.Shared.NextDouble() < returnCenterProb)
            {
                return 0.0; // センター
            }
            // そうでなければ -1.0～+1.0 をランダムに返す
            double p = (Random.Shared.NextDouble() * 2.0) - 1.0; // -1～+1
            return p;
        }

        // 最初の次Panを決める
        nextPan = GetRandomPan();

        // メインループ
        for (int i = 0; i < totalSamples; i++)
        {
            cancel.ThrowIfCancellationRequested();

            // 現在のパン値を算出(線形補間または瞬時切替)
            double pan;
            if (smooth)
            {
                // regionStartSample ～ regionStartSample+transitionSamples の間は補間
                // それ以降は nextPan で一定
                int transitionSamples = (int)(transitionSec * sampleRate);
                transitionSamples = Math.Min(transitionSamples, (regionEndSample - regionStartSample));

                if (i < regionStartSample + transitionSamples)
                {
                    // まだ補間中
                    double t = (double)(i - regionStartSample) / Math.Max(1, transitionSamples);
                    pan = currentPan + (nextPan - currentPan) * t;
                }
                else
                {
                    // 補間完了後は nextPan を維持
                    pan = nextPan;
                }
            }
            else
            {
                // 瞬時に切り替え(最初のサンプルで nextPan にセット)
                pan = nextPan;
            }

            // 音声データ読み込み
            int offset = i * bytesPerSample;
            short leftSample = BitConverter.ToInt16(span.Slice(offset, 2));
            short rightSample = BitConverter.ToInt16(span.Slice(offset + 2, 2));

            // short → float計算用 ( -32768～32767 の範囲 )
            float leftF = leftSample;
            float rightF = rightSample;

            // panFactor: 実際に左右にかけるゲイン(Depth考慮)
            //   pan>0 => 右が大きく, 左が小さく
            //   pan<0 => 左が大きく, 右が小さく
            //   pan=0 => 左右比は変更なし(Depthが0でなければ、ちょっとでも変わる実装にすることもできるが、ここではセンター維持)
            // ここではシンプルに以下の式を用いる：
            //   gainL = 1 - depth * max(0,  pan)
            //   gainR = 1 - depth * max(0, -pan)
            double p = Math.Clamp(pan, -1.0, 1.0) * depth;
            double gainL = 1.0 - Math.Max(0.0, p);
            double gainR = 1.0 - Math.Max(0.0, -p);

            // 左右へパンを適用
            float newLeftF = (float)(leftF * gainL);
            float newRightF = (float)(rightF * gainR);

            // 16bit範囲にクリップして書き戻し
            short newLeft = (short)Math.Clamp(Math.Round(newLeftF), short.MinValue, short.MaxValue);
            short newRight = (short)Math.Clamp(Math.Round(newRightF), short.MinValue, short.MaxValue);

            BitConverter.TryWriteBytes(span.Slice(offset, 2), newLeft);
            BitConverter.TryWriteBytes(span.Slice(offset + 2, 2), newRight);

            // 次の LFO 区間に到達したらパン値を更新
            if (i >= regionEndSample - 1)
            {
                // 新しい区間の始まりとしてパン値更新
                currentPan = nextPan;
                nextPan = GetRandomPan();

                regionStartSample = regionEndSample;
                regionEndSample = regionStartSample + samplesPerLfo;

                // 万一、regionEndSample が総サンプル数を超えても特に問題はないが、ここでは超えたままでもOK
                // (ループ終了間際に新パン値が更新されるだけ)
            }
        }

        /* 実装部分ここまで */
    }
}
#endregion

#region AiAudioEffect_06_Playing

// このクラスが、処理の内容 (効果の程度や挙動の変化) を決定する枢要なパラメータ指定部分である。
/// <summary>
/// AiAudioEffect_06_Playing 用の設定パラメータクラス。<br/>
/// ・ <see cref="EffectRate"/>: 1 秒あたりに「変化」(左右や両方のチャンネル) が何回発生するかの平均回数を指定。<br/>
/// ・ <see cref="EffectDurationMs"/>: 1 回の「変化」が何ミリ秒続くかを指定。<br/>
/// ・ <see cref="BothChannelsProbability"/>: 変化時に左右同時に発生する確率。残りは左右いずれか片方のみ。<br/>
/// </summary>
public class AiAudioEffect_06_Playing_Settings : IAiAudioEffectSettings
{
    /* 実装部分ここから */

    /// <summary>
    /// 1 秒あたりに「変化」が何回発生するかの平均回数を指定するパラメータ。<br/>
    /// 例: 2.0 にすると、1 秒あたり約 2 回の変化が入るようになる。
    /// </summary>
    public double EffectRate = 2.0;

    /// <summary>
    /// 1 回の「変化」の長さ(ミリ秒)。<br/>
    /// 例: 100.0 にすると、100ms (0.1秒) 間、左右いずれか、または両方を強調して効果を与える。
    /// </summary>
    public double EffectDurationMs = 100.0;

    /// <summary>
    /// 変化時に「左右同時」に発生する確率 (0.0～1.0)。<br/>
    /// 例: 0.3 にすると、約 30% の確率で左右同時、残り 70% は左右いずれか片方のみになる。
    /// </summary>
    public double BothChannelsProbability = 0.3;

    /* 実装部分ここまで */
}

// このクラスが、処理の内容を実装するクラスである。
public class AiAudioEffect_06_Playing : AiAudioEffectBase
{
    // この関数は、そのまま、書き写すこと。
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl() => new AiAudioEffect_06_Playing_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定を、おおまかな、「強・注・弱」の効果の強さの程度の指定をある程度もとにして、助ける関数
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_06_Playing_Settings();

        /* 実装部分ここから */
        // type に応じて、「変化」がより激しいか穏やかか、発生頻度や持続時間、左右同時発生率を変化させるサンプル実装
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                // Heavy: 1 秒あたり 5～6 回ほど「変化」が起きるように
                ret.EffectRate = Random.Shared.NextDouble() * (6.0 - 5.0) + 5.0; // 5.0～6.0
                                                                                 // 変化時間は短めに (50～80ms) として素早く連続するように
                ret.EffectDurationMs = Random.Shared.NextDouble() * (80.0 - 50.0) + 50.0; // 50.0～80.0
                                                                                          // 両方チャンネル同時が起こりやすい (0.5～1.0)
                ret.BothChannelsProbability = Random.Shared.NextDouble() * (1.0 - 0.5) + 0.5;
                break;
            case AiAudioEffectSpeedType.Normal:
                // Normal: 1 秒あたり 2～3 回くらい
                ret.EffectRate = Random.Shared.NextDouble() * (3.0 - 2.0) + 2.0; // 2.0～3.0
                                                                                 // 変化時間は中程度 (80～150ms)
                ret.EffectDurationMs = Random.Shared.NextDouble() * (150.0 - 80.0) + 80.0; // 80.0～150.0
                                                                                           // 両方チャンネル同時はそこそこ起こる (0.2～0.6)
                ret.BothChannelsProbability = Random.Shared.NextDouble() * (0.6 - 0.2) + 0.2;
                break;
            case AiAudioEffectSpeedType.Light:
                // Light: 1 秒あたり 0.5～1 回くらい
                ret.EffectRate = Random.Shared.NextDouble() * (1.0 - 0.5) + 0.5; // 0.5～1.0
                                                                                 // 変化時間はやや長め (150～250ms)
                ret.EffectDurationMs = Random.Shared.NextDouble() * (250.0 - 150.0) + 150.0; // 150.0～250.0
                                                                                             // 両方チャンネル同時はあまり起こらない (0.0～0.3)
                ret.BothChannelsProbability = Random.Shared.NextDouble() * 0.3;
                break;
        }
        /* 実装部分ここまで */

        return ret;
    }

    // この関数が、「効果」を生み出す処理を実際に行なう枢要部分である。
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容を、扱いやすいように、AiAudioEffect_06_Playing_Settings にキャストする。
        AiAudioEffect_06_Playing_Settings settings = (AiAudioEffect_06_Playing_Settings)effectSettings;

        /* 実装部分ここから */

        // waveFileInOut には、44.1 kHz / 16-bit / 2ch (ステレオ) の PCM データ(ヘッダ除く) が含まれている。
        // 1 サンプル (L, R のセット) あたり、4 バイト (16ビット×2ch)。
        // このデータに対して、「変化」効果を与えるイメージ:
        //  - 1 秒あたり settings.Rate 回ほどランダムタイミングで「変化」(左右片方 or 両方) を行う
        //  - 1 回の変化は settings.DurationMs ミリ秒程度
        //  - 変化が発生しているチャンネルは音量をブーストし、他方チャンネルは少しアテニュエート (減衰)
        //  - フェードイン・フェードアウトで自然につままれているように感じさせる
        //  - ランダムな間隔・ランダムなチャンネル (左右 or 両方) で発生
        //
        // 実装方針:
        //  1) サンプル数を計算
        //  2) 変化をどのタイミングでどのチャンネルに発生させるかの「イベントリスト」を構築
        //  3) イベントリストをもとに、サンプルごとの「左チャンネルの増幅率」「右チャンネルの増幅率」を計算
        //  4) waveFileInOut のサンプルを順次読み書きして、増幅率を掛け合わせて書き戻す
        //
        // メモ: 巨大なループでは適宜 cancel.ThrowIfCancellationRequested() を呼んで中断を検知すること。

        // 1) サンプル数を計算
        var waveData = waveFileInOut.Span;
        int totalBytes = waveData.Length;
        // 16bit(2 bytes) x 2ch = 4 bytes per frame
        // フレーム(ステレオ1サンプル) 数
        int totalFrames = totalBytes / 4;
        if (totalFrames <= 0) return;

        int sampleRate = 44100; // 本タスク前提: 44.1 kHz

        // 2) 変化イベントのリストを構築
        //    - 平均的には Rate 回/秒 の頻度
        //    - 1 回の持続は DurationMs ミリ秒
        //    - イベント開始～終了まで音量を変更
        //    - イベントの間隔は (1 / Rate) 秒を中心にランダムとし、実際には ±50% ほどブレさせる例にする
        List<EffectEvent> events = new();

        double averageIntervalSec = (settings.EffectRate <= 0.0)
            ? 999999.0 // 万が一 Rate=0 のときは実質発生しない
            : (1.0 / settings.EffectRate);

        double currentTimeSec = 0.0;
        double totalDurationSec = (double)totalFrames / sampleRate;

        Random rng = new Random();

        while (currentTimeSec < totalDurationSec)
        {
            // 変化イベントの「開始時刻」を決定
            // 平均は averageIntervalSec、±50% 程度に振れ幅をもたせる
            // (例えば Heavy なら 1/5～1/6秒程度がベース)
            double randFactor = rng.NextDouble() * 1.0 + 0.5; // 0.5～1.5
            double intervalSec = averageIntervalSec * randFactor;

            currentTimeSec += intervalSec;
            if (currentTimeSec >= totalDurationSec) break; // 全体長を超えれば終了

            double startSec = currentTimeSec;
            double durSec = settings.EffectDurationMs / 1000.0;
            double endSec = startSec + durSec;
            if (endSec > totalDurationSec) endSec = totalDurationSec;

            // 変化するチャンネルをランダムに決定
            bool both = (rng.NextDouble() < settings.BothChannelsProbability);
            bool left = both ? true : (rng.NextDouble() < 0.5);
            bool right = both ? true : !left; // 両方でない限り、片方は left なら right=false, left=false なら right=true

            // Frames (サンプル) 単位に変換
            int startFrame = (int)(startSec * sampleRate);
            int endFrame = (int)(endSec * sampleRate);

            var eventObj = new EffectEvent
            {
                StartFrame = startFrame,
                EndFrame = endFrame,
                ChannelLeft = left,
                ChannelRight = right
            };
            events.Add(eventObj);
        }

        // 3) イベントをもとに、サンプルごとの「左・右増幅率」を計算する
        //    ここでは 1.0 (何も変化なし) から、変化中はブースト・他チャンネルをアテニュエートするような
        //    変化をフェードイン・フェードアウトしながら付与する。
        //    メモリを大量に消費する可能性があるため、応答速度を優先して double[] で確保しても良い。
        //    ここではサンプル数分の配列を用意する実装例。
        double[] leftGains = new double[totalFrames];
        double[] rightGains = new double[totalFrames];
        // 全サンプル初期値 = 1.0
        for (int i = 0; i < totalFrames; i++)
        {
            leftGains[i] = 1.0;
            rightGains[i] = 1.0;
        }

        // フェードイン・アウトに使う時間(サンプル数) ここでは 10ms 程度 (441 サンプル) にしてみる
        int fadeSamples = (int)(0.01 * sampleRate);

        // イベントごとに、対応する範囲の leftGains, rightGains を上書き
        foreach (var ev in events)
        {
            int evLen = ev.EndFrame - ev.StartFrame;
            if (evLen <= 0) continue;

            // 変化中のピーク増幅率 (たとえば 1.8 倍)
            // 変化中でないチャンネルのアテニュエート率 (たとえば 0.5 倍)
            // 適宜好みで調整可能
            double effectBoost = 1.8;
            double unsetAttenuation = 0.5;

            for (int f = ev.StartFrame; f < ev.EndFrame; f++)
            {
                if (f < 0 || f >= totalFrames) continue;

                // cancel チェック (大きいループなのでたまに確認)
                if ((f % 10000) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                }

                // フェードイン・アウトによる乗算係数 (0.0～1.0)
                double fadeFactor = 1.0;
                int posInEvent = f - ev.StartFrame;
                int remainInEvent = ev.EndFrame - f - 1;

                // フェードイン: 開始～fadeSamples にかけて線形で 0→1
                if (posInEvent < fadeSamples)
                {
                    fadeFactor *= (double)posInEvent / fadeSamples;
                }
                // フェードアウト: 終了手前 fadeSamples にかけて線形で 1→0
                if (remainInEvent < fadeSamples)
                {
                    fadeFactor *= (double)remainInEvent / fadeSamples;
                }

                // 左チャンネル
                if (ev.ChannelLeft)
                {
                    // 変化対象 => ブースト
                    double current = leftGains[f];
                    current *= (1.0 + (effectBoost - 1.0) * fadeFactor);
                    leftGains[f] = current;
                }
                else
                {
                    // 変化対象外 => アテニュエート
                    double current = leftGains[f];
                    current *= (1.0 - (1.0 - unsetAttenuation) * fadeFactor);
                    leftGains[f] = current;
                }

                // 右チャンネル
                if (ev.ChannelRight)
                {
                    // 変化対象 => ブースト
                    double current = rightGains[f];
                    current *= (1.0 + (effectBoost - 1.0) * fadeFactor);
                    rightGains[f] = current;
                }
                else
                {
                    // 変化対象外 => アテニュエート
                    double current = rightGains[f];
                    current *= (1.0 - (1.0 - unsetAttenuation) * fadeFactor);
                    rightGains[f] = current;
                }
            }
        }

        // 4) 実際に waveFileInOut の各サンプルに対して上記の増幅率を掛け合わせて書き戻す
        //    16bit short の範囲内にクリップする
        const short minVal = short.MinValue; // -32768
        const short maxVal = short.MaxValue; // 32767

        for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            cancel.ThrowIfCancellationRequested();

            // waveFileInOut は (L16bit, R16bit) 順で交互に入っている
            int byteIndex = frameIndex * 4;
            // 左チャンネルの short 値を取得 (リトルエンディアン)
            short sampleLeft = (short)(waveData[byteIndex] | (waveData[byteIndex + 1] << 8));
            // 右チャンネル
            short sampleRight = (short)(waveData[byteIndex + 2] | (waveData[byteIndex + 3] << 8));

            // 増幅率を掛ける
            double outLeft = sampleLeft * leftGains[frameIndex];
            double outRight = sampleRight * rightGains[frameIndex];

            // 16bit にクリップ
            short finalLeft = (short)Math.Clamp((int)Math.Round(outLeft), minVal, maxVal);
            short finalRight = (short)Math.Clamp((int)Math.Round(outRight), minVal, maxVal);

            // 書き戻し
            waveData[byteIndex] = (byte)(finalLeft & 0xFF);
            waveData[byteIndex + 1] = (byte)((finalLeft >> 8) & 0xFF);

            waveData[byteIndex + 2] = (byte)(finalRight & 0xFF);
            waveData[byteIndex + 3] = (byte)((finalRight >> 8) & 0xFF);
        }

        /* 実装部分ここまで */
    }

    /// <summary>
    /// 変化イベント(開始フレーム、終了フレーム、左右チャンネルフラグ) の情報を保持する簡易クラス
    /// </summary>
    private class EffectEvent
    {
        public int StartFrame;
        public int EndFrame;
        public bool ChannelLeft;
        public bool ChannelRight;
    }
}
#endregion



#region AiAudioEffect_07_Tremolo

/// <summary>
/// トレモロおよびオートパン(左右振動)エフェクトを付与するための設定値クラス。
/// </summary>
public class AiAudioEffect_07_Tremolo_Settings : IAiAudioEffectSettings
{
    /// <summary>
    /// LFO (音量変調を行う低周波オシレータ) の周波数 [Hz]。
    /// 例: 5.0 ならば、1秒に5回の振幅変化を行う。
    /// デフォルト: 5.0
    /// </summary>
    public double LfoRateHz = 5.0;

    /// <summary>
    /// トレモロの深さ (変調幅) [0.0～1.0]。1.0 だと音量がゼロまで下がる可能性がある。
    /// 0.0 で変化なし、0.5 で変化量は半分程度。
    /// デフォルト: 0.5
    /// </summary>
    public double TremoloDepth = 0.5;

    /// <summary>
    /// オートパンの深さ [0.0～1.0]。1.0 にすると左右が最大限に振れ、0.0 でオートパンなし。
    /// デフォルト: 0.3
    /// </summary>
    public double PanDepth = 0.3;

    /// <summary>
    /// LFO波形の種類。Sine, Triangle, Square。
    /// デフォルト: Sine
    /// </summary>
    public LfoWaveShape WaveShape = LfoWaveShape.Sine;

    /// <summary>
    /// LFOのランダム変動量 [0.0～1.0]。
    /// 大きいほどLFOの周波数や変調がランダムに揺らぎ、予測不能なエフェクトとなる。
    /// デフォルト: 0.2
    /// </summary>
    public double RandomModIntensity = 0.2;

    /// <summary>
    /// LFOの初期位相をランダムにするかどうか。
    /// true であれば、処理開始時のLFO位相が毎回ランダムに変わる。
    /// デフォルト: true
    /// </summary>
    public bool EnableRandomPhase = true;
}

/// <summary>
/// トレモロおよびオートパン(左右振動)エフェクトを付与するクラス。
/// </summary>
public class AiAudioEffect_07_Tremolo : AiAudioEffectBase
{
    /// <summary>
    /// 設定を生成するための基本ファクトリ。実装そのまま。
    /// </summary>
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl()
        => new AiAudioEffect_07_Tremolo_Settings();

    /// <summary>
    /// 効果のバリエーションを増すためのパラメータをランダム生成する。
    /// Heavy/Normal/Light の指定により効果の強さを調整した例示的実装。
    /// </summary>
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_07_Tremolo_Settings();

        // 例示的に switch-case でパラメータの範囲を変化させる
        // （トレモロ周波数・深さ・オートパン・ランダム変動などを調整）
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
            /*                // 強烈な変化を想定
                            ret.LfoRateHz = Random.Shared.NextDouble() * (12.0 - 4.0) + 4.0;       // 4.0～12.0
                            ret.TremoloDepth = Random.Shared.NextDouble() * (1.0 - 0.7) + 0.7;  // 0.7～1.0
                            ret.PanDepth = Random.Shared.NextDouble() * (1.0 - 0.5) + 0.5;      // 0.5～1.0
                            ret.RandomModIntensity = Random.Shared.NextDouble() * (0.8 - 0.3) + 0.3; // 0.3～0.8
                            ret.WaveShape = (LfoWaveShape)Random.Shared.Next(0, 3); // 0,1,2 のいずれか (Sine/Triangle/Square)
                            ret.EnableRandomPhase = (Random.Shared.NextDouble() < 0.8); // 80%程度で位相ランダム
                            break;
            */
            case AiAudioEffectSpeedType.Normal:
                // 標準的な変化を想定
                ret.LfoRateHz = Random.Shared.NextDouble() * (8.0 - 2.0) + 2.0;      // 2.0～8.0
                ret.TremoloDepth = Random.Shared.NextDouble() * (0.7 - 0.3) + 0.3;  // 0.3～0.7
                ret.PanDepth = Random.Shared.NextDouble() * (0.6 - 0.2) + 0.2;      // 0.2～0.6
                ret.RandomModIntensity = Random.Shared.NextDouble() * (0.5 - 0.1) + 0.1; // 0.1～0.5
                ret.WaveShape = (LfoWaveShape)Random.Shared.Next(0, 3);
                ret.EnableRandomPhase = (Random.Shared.NextDouble() < 0.5); // 50%で位相ランダム
                break;

            case AiAudioEffectSpeedType.Light:
                // 弱い変化を想定
                ret.LfoRateHz = Random.Shared.NextDouble() * (3.0 - 0.5) + 0.5;      // 0.5～3.0
                ret.TremoloDepth = Random.Shared.NextDouble() * (0.4 - 0.05) + 0.05; // 0.05～0.4
                ret.PanDepth = Random.Shared.NextDouble() * (0.3 - 0.0) + 0.0;       // 0.0～0.3
                ret.RandomModIntensity = Random.Shared.NextDouble() * (0.3 - 0.0) + 0.0; // 0.0～0.3
                ret.WaveShape = (LfoWaveShape)Random.Shared.Next(0, 3);
                ret.EnableRandomPhase = (Random.Shared.NextDouble() < 0.3); // 30%で位相ランダム
                break;
        }

        return ret;
    }

    /// <summary>
    /// 実際に WAV データを操作してトレモロ＆オートパンエフェクトを付与するメイン処理。
    /// </summary>
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // 設定をキャスト
        AiAudioEffect_07_Tremolo_Settings settings = (AiAudioEffect_07_Tremolo_Settings)effectSettings;

        // 44.1 kHz, 16bit, ステレオ(2ch)を想定
        const int bytesPerSample = 2;   // 16bit = 2 bytes
        const int channels = 2;         // ステレオ
        const int sampleRate = 44100;   // 44.1 kHz

        // 波形データ部分(PCM)を操作するためにSpanを取得
        Span<byte> audioSpan = waveFileInOut.Span;

        // 全サンプルフレーム数（1フレーム = 左右2chぶんのサンプル）
        int frameCount = audioSpan.Length / (bytesPerSample * channels);

        // LFOの初期位相を設定
        double phase = 0.0;
        if (settings.EnableRandomPhase)
        {
            phase = Random.Shared.NextDouble() * Math.PI * 2.0;
        }

        // LFO位相の増分 (基本周波数に相当)
        // 後に、ランダムドリフトを加味して変動させる
        double basePhaseIncrement = 2.0 * Math.PI * settings.LfoRateHz / sampleRate;

        // ランダムドリフトを適用する区間(秒)とそのサンプル数
        // 例: 0.25秒ごとにLFO周波数を少し変動させる
        double driftCycleSec = 0.25;
        int driftCycleSamples = (int)(driftCycleSec * sampleRate);

        // 現在のランダムドリフト値 (周波数追加分, Hz)
        double currentDriftFreq = 0.0;
        // 周波数ドリフトを位相インクリメントに変換した値
        double currentDriftIncrement = 0.0;

        // LFO波形生成用関数
        // waveShapeに応じて [-1, +1] の範囲の値を返す
        double LfoWaveValue(double ph, LfoWaveShape shape)
        {
            switch (shape)
            {
                case LfoWaveShape.Sine:
                    return Math.Sin(ph);
                case LfoWaveShape.Triangle:
                    {
                        // 三角波: 位相を [0, 2π) -> [-1,1] に変換
                        // 一般的には|2*(ph/(2π) - floor(0.5 + ph/(2π)))|*2-1 等で表せるが、
                        // ここでは簡易的に実装
                        // 0～π で上昇, π～2π で下降
                        double t = (ph % (2.0 * Math.PI)) / (Math.PI);
                        // t: 0～2
                        if (t < 1.0)
                        {
                            // 0～1: 上昇
                            return (t * 2.0) - 1.0; // -1～+1
                        }
                        else
                        {
                            // 1～2: 下降
                            return 1.0 - ((t - 1.0) * 2.0); // +1～-1
                        }
                    }
                case LfoWaveShape.Square:
                    return (Math.Sin(ph) >= 0.0) ? 1.0 : -1.0;
                default:
                    return Math.Sin(ph);
            }
        }

        // 周波数ランダムドリフトを更新するローカル関数
        void UpdateRandomDrift()
        {
            // settings.RandomModIntensity = 0.2 なら、 周波数(LfoRateHz)の ±20% 程度をランダムで上下させるようなイメージ
            double maxDriftHz = settings.LfoRateHz * settings.RandomModIntensity;
            currentDriftFreq = (Random.Shared.NextDouble() * 2.0 - 1.0) * maxDriftHz;
            // basePhaseIncrement + drift の合計をサンプルレートで割った値に
            currentDriftIncrement = 2.0 * Math.PI * (settings.LfoRateHz + currentDriftFreq) / sampleRate;
        }

        // RandomModIntensity > 0 であればランダムドリフトを利用
        bool useRandomDrift = (settings.RandomModIntensity > 0.0);

        int nextDriftUpdate = 0;
        if (useRandomDrift)
        {
            UpdateRandomDrift();
            nextDriftUpdate = driftCycleSamples;
        }
        else
        {
            // ランダムドリフトを使わない場合
            currentDriftIncrement = basePhaseIncrement;
        }

        // 実処理ループ
        for (int i = 0; i < frameCount; i++)
        {
            // 中断要求があれば例外をスロー (外側でキャッチする想定)
            if ((i % 2048) == 0) // 適当な頻度でチェック
            {
                cancel.ThrowIfCancellationRequested();
            }

            // LFO計算
            double lfoVal = LfoWaveValue(phase, settings.WaveShape); // [-1, +1]

            // トレモロ計算
            // (1 - depth) + depth * (lfoValの正規化)
            // lfoVal: -1～+1 -> 0～+1 にシフトするには (lfoVal + 1.0)/2
            double amplitude = (1.0 - settings.TremoloDepth)
                               + settings.TremoloDepth * ((lfoVal + 1.0) * 0.5);
            // ただし 0～1.0 を想定

            // オートパン計算
            // lfoVal: -1～+1 をそのままパン変化に使う場合、PanDepthを乗じる
            // 左右のボリューム比率が [ (1 - pan), (1 + pan) ] になるように
            // pan は [-PanDepth, +PanDepth]
            double pan = lfoVal * settings.PanDepth; // [-PanDepth, +PanDepth]

            // 左右それぞれの最終ゲインを計算
            double leftGain = amplitude * (1.0 - 0.5 * pan);  // pan>0 なら右を大きく、左を小さく
            double rightGain = amplitude * (1.0 + 0.5 * pan); // pan<0 なら左を大きく、右を小さく

            // PCMデータを読み取り (16bit, little-endian)
            int idx = i * channels * bytesPerSample;
            short leftSample = (short)(audioSpan[idx] | (audioSpan[idx + 1] << 8));
            short rightSample = (short)(audioSpan[idx + 2] | (audioSpan[idx + 3] << 8));

            // floatに変換してゲインをかける
            double leftFloat = leftSample / 32768.0;
            double rightFloat = rightSample / 32768.0;

            leftFloat *= leftGain;
            rightFloat *= rightGain;

            // クリッピングしないように -1.0～+1.0 の範囲に収めてshortに変換
            int newLeft = (int)(leftFloat * 32767.0);
            int newRight = (int)(rightFloat * 32767.0);

            if (newLeft > short.MaxValue) newLeft = short.MaxValue;
            if (newLeft < short.MinValue) newLeft = short.MinValue;
            if (newRight > short.MaxValue) newRight = short.MaxValue;
            if (newRight < short.MinValue) newRight = short.MinValue;

            // メモリに書き戻し
            audioSpan[idx] = (byte)(newLeft & 0xFF);
            audioSpan[idx + 1] = (byte)((newLeft >> 8) & 0xFF);
            audioSpan[idx + 2] = (byte)(newRight & 0xFF);
            audioSpan[idx + 3] = (byte)((newRight >> 8) & 0xFF);

            // 次のサンプル向けに位相を進める (ドリフト含め)
            phase += currentDriftIncrement;

            // ランダムドリフトの更新タイミング
            if (useRandomDrift)
            {
                if (i == nextDriftUpdate)
                {
                    UpdateRandomDrift();
                    nextDriftUpdate += driftCycleSamples;
                }
            }
        }
    }
}

/// <summary>
/// LFOの波形を表す列挙型。
/// </summary>
[Flags]
public enum LfoWaveShape
{
    Sine = 0,
    Triangle = 1,
    Square = 2
}

#endregion



#region AiAudioEffect_09_Flanger
// このクラスが、処理の内容 (効果の程度や挙動の変化) を決定する枢要なパラメータ指定部分である。
public class AiAudioEffect_09_Flanger_Settings : IAiAudioEffectSettings
{
    /*
     * フランジャーエフェクト用のパラメータ例。すべてのパラメータに、
     * 標準的な効果が得られる値をデフォルト値として代入する。
     */

    /// <summary>
    /// フランジャーの基本ディレイ時間 (ミリ秒)。
    /// 値が大きいほど、原音に対する遅延音のずれが大きくなる。
    /// 一般的には 1～5 ms 程度が多い。
    /// ここでは標準的な 2 ms をデフォルト値とする。
    /// </summary>
    public double BaseDelayMs = 2.0;

    /// <summary>
    /// ディレイ変調の深さ (ミリ秒)。
    /// LFO によるディレイ時間変化の最大幅。
    /// 値が大きいほど、うねりが強くなる。
    /// ここでは標準的な 2 ms をデフォルト値とする。
    /// </summary>
    public double DepthMs = 2.0;

    /// <summary>
    /// 変調周波数 (Hz)。
    /// フランジャーが 1 秒間に何回うねりのサイクルを作るか。
    /// 値が大きいと LFO が速く、うねりの速度が速い。
    /// ここでは標準的な 0.5 Hz をデフォルト値とする。
    /// </summary>
    public double RateHz = 0.5;

    /// <summary>
    /// フィードバック率 (0.0～0.95 程度)。
    /// 遅延音をどの程度リングバッファに再帰させるか。
    /// 値が大きいほど効果が強調され、深いうなりが得られるが、
    /// ホウリングに近づくこともある。
    /// ここでは 0.3 をデフォルト値とする。
    /// </summary>
    public double Feedback = 0.3;

    /// <summary>
    /// エフェクトのウェット成分とドライ成分のミックス比 (0.0～1.0)。
    /// 0 なら原音のみ、1 ならエフェクト音のみ。
    /// ここでは 0.5 をデフォルト値とし、半々にミックスする。
    /// </summary>
    public double Mix = 0.5;

    /// <summary>
    /// ランダム変調を追加的に行うかのフラグ。
    /// true にすると、LFO 周波数やディレイ時間を
    /// ランダムに揺らす動作が加わり、予測不能感を高める。
    /// ここでは false をデフォルト値とする。
    /// </summary>
    public bool RandomizeLFO = false;
}

// このクラスが、処理の内容を実装するクラスである。
public class AiAudioEffect_09_Flanger : AiAudioEffectBase
{
    // この関数は、そのまま、書き写すこと。
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl() => new AiAudioEffect_09_Flanger_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定を、
    // おおまかな、「強・中・弱」の効果の強さの程度の指定をある程度もとにして行う関数。
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_09_Flanger_Settings();

        /* 実装部分ここから */
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                // 「強い」フランジャー効果を狙う: 深い遅延、速めまたは中程度の LFO、フィードバックも強め
                ret.BaseDelayMs = Random.Shared.NextDouble() * (5.0 - 2.0) + 2.0;      // 2.0 ～ 5.0 ms
                ret.DepthMs = Random.Shared.NextDouble() * (4.0 - 2.0) + 2.0;      // 2.0 ～ 4.0 ms
                ret.RateHz = Random.Shared.NextDouble() * (2.0 - 0.2) + 0.2;      // 0.2 ～ 2.0 Hz
                ret.Feedback = Random.Shared.NextDouble() * (0.9 - 0.5) + 0.5;      // 0.5 ～ 0.9
                ret.Mix = Random.Shared.NextDouble() * (1.0 - 0.7) + 0.7;      // 0.7 ～ 1.0
                ret.RandomizeLFO = (Random.Shared.NextDouble() < 0.8);                 // 80% でランダム変調を付加
                break;

            case AiAudioEffectSpeedType.Normal:
                // 「中程度」フランジャー効果: 適度な遅延と深さ、フィードバックは中程度
                ret.BaseDelayMs = Random.Shared.NextDouble() * (3.0 - 1.0) + 1.0;      // 1.0 ～ 3.0 ms
                ret.DepthMs = Random.Shared.NextDouble() * (3.0 - 1.0) + 1.0;      // 1.0 ～ 3.0 ms
                ret.RateHz = Random.Shared.NextDouble() * (1.0 - 0.2) + 0.2;      // 0.2 ～ 1.0 Hz
                ret.Feedback = Random.Shared.NextDouble() * (0.5 - 0.2) + 0.2;      // 0.2 ～ 0.5
                ret.Mix = Random.Shared.NextDouble() * (0.7 - 0.3) + 0.3;      // 0.3 ～ 0.7
                ret.RandomizeLFO = (Random.Shared.NextDouble() < 0.5);                 // 50% でランダム変調を付加
                break;

            case AiAudioEffectSpeedType.Light:
                // 「弱い」フランジャー効果: 小さい遅延、浅めの深さ、穏やかな変調
                ret.BaseDelayMs = Random.Shared.NextDouble() * (2.0 - 0.5) + 0.5;      // 0.5 ～ 2.0 ms
                ret.DepthMs = Random.Shared.NextDouble() * (2.0 - 0.5) + 0.5;      // 0.5 ～ 2.0 ms
                ret.RateHz = Random.Shared.NextDouble() * (0.5 - 0.05) + 0.05;    // 0.05 ～ 0.5 Hz
                ret.Feedback = Random.Shared.NextDouble() * (0.3 - 0.0) + 0.0;      // 0.0 ～ 0.3
                ret.Mix = Random.Shared.NextDouble() * (0.5 - 0.2) + 0.2;      // 0.2 ～ 0.5
                ret.RandomizeLFO = (Random.Shared.NextDouble() < 0.3);                 // 30% でランダム変調を付加
                break;
        }
        /* 実装部分ここまで */

        return ret;
    }

    // この関数が、「効果」を生み出す処理を実際に行なう枢要部分である。
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容を、扱いやすいように、AiAudioEffect_09_Flanger_Settings にキャストする。
        AiAudioEffect_09_Flanger_Settings settings = (AiAudioEffect_09_Flanger_Settings)effectSettings;

        /* 実装部分ここから */

        // waveFileInOut は、44.1 kHz / 2チャンネル / 16ビット PCM の波形データ (ヘッダなし) である。
        // 1サンプルあたり2バイト、ステレオなので1フレーム4バイト。

        var span = waveFileInOut.Span;
        int totalBytes = span.Length;
        // ステレオ 16ビット = 4バイト/フレーム
        int totalFrames = totalBytes / 4;
        if (totalFrames == 0) return;

        // フランジャー実装に使用するリングバッファを用意する。
        // 最大でも 10 ms ほどあれば一般的なフランジャーには十分だが、
        // ユーザーがもっと大きい値を指定する場合を考慮して安全に確保する。
        // 例として 50 ms ぶんを確保 (44100Hzで約2205サンプル) にしておく。
        int maxBufferSamples = 44100 / 20; // 50ms → 2205サンプル程度
        if (maxBufferSamples < 1) maxBufferSamples = 1;
        float[] ringBufferL = new float[maxBufferSamples];
        float[] ringBufferR = new float[maxBufferSamples];

        // 書き込み位置
        int writePos = 0;

        // LFO 用のフェーズ
        double lfoPhase = 0.0;

        // サンプリングレート
        const double sampleRate = 44100.0;

        // ランダム変調用のパラメータ
        // RandomizeLFO が true の時に、一定間隔で少しずつ変化させる
        double randomLfoRateOffset = 0.0;    // LFO周波数をランダムにずらす
        double randomDepthOffset = 0.0;    // DepthMsをランダムに揺らす
        int randomLfoCounter = 0;
        // 何フレーム毎にランダム変動を行うか (ここでは 0.1 秒ごと = 4410 フレームごと)
        int randomLfoInterval = 4410;

        // 各フレームを順に処理
        for (int i = 0; i < totalFrames; i++)
        {
            // 中断要求チェック (頻繁にやると遅いので 1024フレームごと等で良い)
            if ((i & 1023) == 0) cancel.ThrowIfCancellationRequested();

            // 入力波形読み取り (16ビット、ステレオ)
            int byteIndex = i * 4;
            short sampleLeft = (short)(span[byteIndex + 0] | (span[byteIndex + 1] << 8));
            short sampleRight = (short)(span[byteIndex + 2] | (span[byteIndex + 3] << 8));

            // float に変換(-1.0～1.0)
            float inL = sampleLeft / 32768.0f;
            float inR = sampleRight / 32768.0f;

            // LFO変調周波数とDepthにランダム揺らぎを付与 (RandomizeLFO == true の場合)
            if (settings.RandomizeLFO && randomLfoCounter++ >= randomLfoInterval)
            {
                randomLfoCounter = 0;
                // 周波数を ±(0.1Hz 程度) で揺らす
                double rnd1 = (Random.Shared.NextDouble() - 0.5) * 0.2;
                // Depth を ±(1.0ms 程度) で揺らす
                double rnd2 = (Random.Shared.NextDouble() - 0.5) * 2.0;

                // 反映 (過度にパラメータがおかしくならないよう軽減して加算)
                randomLfoRateOffset += rnd1;
                randomDepthOffset += rnd2;

                // クリップ (負の周波数や極端な数値にならないよう制限)
                if (settings.RateHz + randomLfoRateOffset < 0.01) randomLfoRateOffset = 0.01 - settings.RateHz;
                if (settings.RateHz + randomLfoRateOffset > 5.0) randomLfoRateOffset = 5.0 - settings.RateHz;
                if (settings.DepthMs + randomDepthOffset < 0.1) randomDepthOffset = 0.1 - settings.DepthMs;
                if (settings.DepthMs + randomDepthOffset > 20.0) randomDepthOffset = 20.0 - settings.DepthMs;
            }

            // 現在の LFO 周波数
            double currentRateHz = settings.RateHz + (settings.RandomizeLFO ? randomLfoRateOffset : 0.0);
            // 現在の変調深さ (ms)
            double currentDepthMs = settings.DepthMs + (settings.RandomizeLFO ? randomDepthOffset : 0.0);

            // LFO位相の計算
            double lfoValue = Math.Sin(2.0 * Math.PI * lfoPhase);

            // 次のサンプルに進める前に LFO位相を更新
            // 1フレームあたりの位相増分 = (RateHz / sampleRate)
            lfoPhase += currentRateHz / sampleRate;
            // LFO位相を 0～1 の範囲でループさせる
            if (lfoPhase >= 1.0) lfoPhase -= 1.0;

            // 実効遅延時間 (ms) = BaseDelayMs + (LFOによる変調)
            // LFO を -1～+1 で変調させるため、DepthMs * 0.5 * lfoValue
            double delayMs = settings.BaseDelayMs + (currentDepthMs * 0.5 * lfoValue);

            // サンプル数に変換
            double delaySamples = (delayMs * sampleRate) / 1000.0;

            // リングバッファ読み出し位置 (writePos - 遅延サンプル)
            // フラクショナル遅延を実現するために補間を行う
            double readPos = (double)writePos - delaySamples;
            // リングバッファ範囲内に収める (モジュロ演算)
            // 負になったら末尾へ回す
            while (readPos < 0.0)
            {
                readPos += maxBufferSamples;
            }
            while (readPos >= maxBufferSamples)
            {
                readPos -= maxBufferSamples;
            }

            // 補間用にフロアと小数部を求める
            int basePos = (int)readPos;
            double frac = readPos - basePos;
            int basePos1 = basePos + 1;
            if (basePos1 >= maxBufferSamples) basePos1 -= maxBufferSamples;

            // リングバッファから遅延サンプルを取り出す (線形補間)
            float dL0 = ringBufferL[basePos];
            float dL1 = ringBufferL[basePos1];
            float delayedL = (float)(dL0 * (1.0 - frac) + dL1 * frac);

            float dR0 = ringBufferR[basePos];
            float dR1 = ringBufferR[basePos1];
            float delayedR = (float)(dR0 * (1.0 - frac) + dR1 * frac);

            // 出力サンプル (Wet / Dry ミックス)
            float outL = inL * (float)(1.0 - settings.Mix) + delayedL * (float)settings.Mix;
            float outR = inR * (float)(1.0 - settings.Mix) + delayedR * (float)settings.Mix;

            // リングバッファへの書き込み (フィードバックも加味)
            float fbL = inL + delayedL * (float)settings.Feedback;
            float fbR = inR + delayedR * (float)settings.Feedback;

            ringBufferL[writePos] = fbL;
            ringBufferR[writePos] = fbR;

            // 書き込み位置を進める
            writePos++;
            if (writePos >= maxBufferSamples) writePos = 0;

            // 出力を short に戻してメモリへ書き込む (クリッピングに注意)
            short outSampleL = (short)Math.Clamp((int)(outL * 32767.0f), short.MinValue, short.MaxValue);
            short outSampleR = (short)Math.Clamp((int)(outR * 32767.0f), short.MinValue, short.MaxValue);

            span[byteIndex + 0] = (byte)(outSampleL & 0xFF);
            span[byteIndex + 1] = (byte)((outSampleL >> 8) & 0xFF);
            span[byteIndex + 2] = (byte)(outSampleR & 0xFF);
            span[byteIndex + 3] = (byte)((outSampleR >> 8) & 0xFF);
        }

        /* 実装部分ここまで */
    }
}
#endregion



#region AiAudioEffect_12_DelayEcho
// このクラスが、処理の内容 (効果の程度や挙動の変化) を決定する枢要なパラメータ指定部分である。
public class AiAudioEffect_12_DelayEcho_Settings : IAiAudioEffectSettings
{
    /* 実装部分ここから
     *
     * ★ ディレイ・エコー効果を調整するためのパラメータを設定。
     *   - DelayTimeMs    : 遅延時間（ミリ秒）
     *   - Feedback       : 反復回数を決めるフィードバック量（0.0～1.0）
     *   - DryMix         : 原音(ドライ音)の音量比率（0.0～1.0）
     *   - WetMix         : エコー音(ウェット音)の音量比率（0.0～1.0）
     *   - PingPong       : ピンポン・ディレイを行うかどうかのフラグ
     *
     * ※ デフォルト値は、基本的なエコー効果が聴こえるよう設定。
     */

    /// <summary>
    /// 遅延時間（ミリ秒）。たとえば 300ms 程度にするとほどよい残響。
    /// 短すぎる（50ms 以下）とスラップバックに近く、200～400ms 程度で一般的なエコー感、
    /// 500ms 以上にすると空間がかなり広く感じられる。
    /// </summary>
    public double DelayTimeMs = 300.0;

    /// <summary>
    /// フィードバック量（0.0～1.0）。0.0 なら1回だけ遅延音が鳴り、
    /// 1.0 に近いほどディレイ音が何度も反復して長く残る。
    /// </summary>
    public double Feedback = 0.5;

    /// <summary>
    /// 原音(ドライ音)の音量比率（0.0～1.0）。0.0 にすると完全にエコー音だけ、
    /// 1.0 に近いほど元の音声の存在感が大きい。
    /// </summary>
    public double DryMix = 0.8;

    /// <summary>
    /// エコー音(ウェット音)の音量比率（0.0～1.0）。0.0 にするとエコーが聞こえず、
    /// 1.0 に近いほどエコー音が大きくなる。
    /// </summary>
    public double WetMix = 0.4;

    /// <summary>
    /// ピンポン・ディレイを有効にするかどうかのフラグ。
    /// true にすると左右のチャンネルが交互にエコー音を反復し、不思議な広がりが得られる。
    /// </summary>
    public bool PingPong = false;

    /* 実装部分ここまで */
}

// このクラスが、処理の内容を実装するクラスである。
public class AiAudioEffect_12_DelayEcho : AiAudioEffectBase
{
    // この関数は、そのまま、書き写すこと。
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl() => new AiAudioEffect_12_DelayEcho_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定を、おおまかな、「強・注・弱」の効果の強さの程度の指定をある程度もとにして、助ける関数
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_12_DelayEcho_Settings();

        /* 実装部分ここから
         *
         * type の値に従って、ランダムパラメータを設定し、
         * Heavy -> 強めのエコー（長めのディレイ、高めのフィードバック）、
         * Normal -> 中程度、
         * Light -> 短めのディレイ、フィードバックも控えめ、
         * といった範囲内で乱数を使って設定する例。
         */

        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                // ディレイタイム 400～800ms
                ret.DelayTimeMs = Random.Shared.NextDouble() * (800.0 - 400.0) + 400.0;
                // フィードバック量 0.6～0.9
                ret.Feedback = Random.Shared.NextDouble() * (0.9 - 0.6) + 0.6;
                // ドライ音量 0.5～0.8
                ret.DryMix = Random.Shared.NextDouble() * (0.8 - 0.5) + 0.5;
                // ウェット音量 0.6～1.0
                ret.WetMix = Random.Shared.NextDouble() * (1.0 - 0.6) + 0.6;
                // ピンポン確率 50%
                ret.PingPong = Random.Shared.NextDouble() < 0.5;
                break;

            case AiAudioEffectSpeedType.Normal:
                // ディレイタイム 200～400ms
                ret.DelayTimeMs = Random.Shared.NextDouble() * (400.0 - 200.0) + 200.0;
                // フィードバック量 0.3～0.6
                ret.Feedback = Random.Shared.NextDouble() * (0.6 - 0.3) + 0.3;
                // ドライ音量 0.7～0.9
                ret.DryMix = Random.Shared.NextDouble() * (0.9 - 0.7) + 0.7;
                // ウェット音量 0.3～0.6
                ret.WetMix = Random.Shared.NextDouble() * (0.6 - 0.3) + 0.3;
                // ピンポン確率 30%
                ret.PingPong = Random.Shared.NextDouble() < 0.3;
                break;

            case AiAudioEffectSpeedType.Light:
                // ディレイタイム 80～200ms
                ret.DelayTimeMs = Random.Shared.NextDouble() * (200.0 - 80.0) + 80.0;
                // フィードバック量 0.1～0.3
                ret.Feedback = Random.Shared.NextDouble() * (0.3 - 0.1) + 0.1;
                // ドライ音量 0.9～1.0
                ret.DryMix = Random.Shared.NextDouble() * (1.0 - 0.9) + 0.9;
                // ウェット音量 0.1～0.3
                ret.WetMix = Random.Shared.NextDouble() * (0.3 - 0.1) + 0.1;
                // ピンポン確率 10%
                ret.PingPong = Random.Shared.NextDouble() < 0.1;
                break;
        }

        /* 実装部分ここまで */

        return ret;
    }

    // この関数が、「効果」を生み出す処理を実際に行なう枢要部分である。
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容を、扱いやすいように、AiAudioEffect_12_DelayEcho_Settings にキャストする。
        AiAudioEffect_12_DelayEcho_Settings settings = (AiAudioEffect_12_DelayEcho_Settings)effectSettings;

        /* 実装部分ここから */

        // ---- コードの枢要をここに実装してください。----

        // 44.1 kHz / 16ビット / ステレオの WAV データであることを前提にする。
        // waveFileInOut のサイズ (バイト数) / 4 = ステレオのサンプルフレーム数 となる。
        // (左チャンネル 2バイト + 右チャンネル 2バイト = 4バイトで1フレーム)
        int totalBytes = waveFileInOut.Length;
        int totalFrames = totalBytes / 4; // ステレオのフレーム数

        // 16ビット整数(short)配列へ変換
        // （NAudio.Waveなどでラップせず、直接バイナリ操作するため）
        short[] sampleData = new short[totalBytes / 2];
        Buffer.BlockCopy(waveFileInOut.ToArray(), 0, sampleData, 0, totalBytes);

        // ディレイタイム（サンプル単位）
        // たとえば 300ms なら、44100 * 0.300 = 13230 サンプル分ほどの遅延となる。
        int delaySamples = (int)(44100.0 * (settings.DelayTimeMs / 1000.0));

        // 安全のため、最低1サンプル以上確保
        if (delaySamples < 1)
        {
            delaySamples = 1;
        }

        // ピンポンディレイなどで最大 2秒 ほどを想定したバッファを確保
        // （必要に応じて拡大しても良い）
        int maxDelaySamples = 44100 * 2;
        // 左右それぞれにリングバッファを用意
        float[] delayBufferLeft = new float[maxDelaySamples];
        float[] delayBufferRight = new float[maxDelaySamples];

        // リングバッファ読み書きの開始位置
        // writePos を 0、readPos を (0 - delaySamples) mod maxDelaySamples などで開始すると
        // 最初のほうの読出しは0が返ってくる形になる。
        int writePos = 0;
        int readPos = (maxDelaySamples - delaySamples) % maxDelaySamples;

        // パラメータの取得
        double feedback = settings.Feedback;  // 0.0～1.0
        double dryMix = settings.DryMix;       // 0.0～1.0
        double wetMix = settings.WetMix;       // 0.0～1.0
        bool pingPong = settings.PingPong;

        // ★ ここではランダムな変化を少し与える例として、
        //    一定間隔ごとに feedback や wetMix を微調整する実装を簡易で入れてみる。
        //    （ランダム変化を大きくしたい場合はここを工夫してください）
        Random rnd = new Random();
        int nextParamChangeFrame = 0; // 次にパラメータを変化させるサンプルフレーム

        for (int i = 0; i < totalFrames; i++)
        {
            // 一定周期でキャンセルをチェック（大きなループのとき）
            if ((i & 0xFFF) == 0) // 4096フレームごと
            {
                cancel.ThrowIfCancellationRequested();
            }

            // 左右のサンプルを short -> float(-1.0～1.0) に変換
            short leftS = sampleData[i * 2 + 0];
            short rightS = sampleData[i * 2 + 1];
            float left = leftS / 32768.0f;
            float right = rightS / 32768.0f;

            // リングバッファからディレイ音を取得
            float delayedLeft = delayBufferLeft[readPos];
            float delayedRight = delayBufferRight[readPos];

            // 出力サンプルを計算
            float outLeft;
            float outRight;

            if (!pingPong)
            {
                // 通常のステレオディレイ：LはLを遅延、RはRを遅延
                outLeft = (float)(left * dryMix) + delayedLeft * (float)wetMix;
                outRight = (float)(right * dryMix) + delayedRight * (float)wetMix;

                // 次のエコーのためにリングバッファに書き込み
                delayBufferLeft[writePos] = outLeft * (float)feedback;
                delayBufferRight[writePos] = outRight * (float)feedback;
            }
            else
            {
                // ピンポンディレイ：L ← Rの遅延音、R ← Lの遅延音 を混合
                outLeft = (float)(left * dryMix) + delayedRight * (float)wetMix;
                outRight = (float)(right * dryMix) + delayedLeft * (float)wetMix;

                // 次のエコーのためにリングバッファに書き込み（左右逆にフィードバック）
                delayBufferLeft[writePos] = outRight * (float)feedback;
                delayBufferRight[writePos] = outLeft * (float)feedback;
            }

            // 計算結果を short に戻す前にクリッピング
            short outLeftS = (short)Math.Clamp(outLeft * 32767.0f, -32768, 32767);
            short outRightS = (short)Math.Clamp(outRight * 32767.0f, -32768, 32767);

            // 計算結果を書き戻し（In/Outバッファ）
            sampleData[i * 2 + 0] = outLeftS;
            sampleData[i * 2 + 1] = outRightS;

            // リングバッファの読み書き位置を進める
            readPos = (readPos + 1) % maxDelaySamples;
            writePos = (writePos + 1) % maxDelaySamples;

            // ★ ランダム変化例：ある程度のフレーム間隔で feedback, wetMix を小幅に揺らす
            if (i == nextParamChangeFrame)
            {
                // ±0.05 の範囲で微調整（0.0～1.0 にクリップ）
                double fbVariation = feedback + (rnd.NextDouble() * 0.1 - 0.05);
                double wmVariation = wetMix + (rnd.NextDouble() * 0.1 - 0.05);
                feedback = Math.Min(Math.Max(fbVariation, 0.0), 1.0);
                wetMix = Math.Min(Math.Max(wmVariation, 0.0), 1.0);

                // 次の変化タイミングを 20000～40000 フレーム後（約0.45秒～0.90秒後）にセット
                // （44100Hz換算で）
                int interval = rnd.Next(20000, 40000);
                nextParamChangeFrame = i + interval;
            }
        }

        // 処理後のデータを waveFileInOut に書き戻す
        sampleData.CopyTo(waveFileInOut.Span._AsSInt16Span());

        /* 実装部分ここまで */
    }
}
#endregion





#region AiAudioEffect_15_SidechainPumping
// このクラスが、処理の内容 (効果の程度や挙動の変化) を決定する枢要なパラメータ指定部分である。
public class AiAudioEffect_15_SidechainPumping_Settings : IAiAudioEffectSettings
{
    /// <summary>
    /// ポンピングの深さを指定する。0.0 なら音量ディップなし（効果オフ相当）、1.0 なら最大ディップ。
    /// デフォルト値 0.7 は比較的はっきりとしたポンピングが得られる標準的な設定。
    /// </summary>
    public double PumpDepth = 0.7;

    /// <summary>
    /// 1秒あたりのポンピング回数。例えば 2.0 なら1秒に2回のディップ（= 2Hz = 1分間に120BPMの4拍とみなせる）。
    /// デフォルト値 2.0 はやや速いダンスビート的なポンピング。
    /// </summary>
    public double PumpFrequency = 2.0;

    /// <summary>
    /// アタックタイム (ms)。ディップ開始時、音量が最大から最小へ下がるのに要する時間。
    /// デフォルト 10.0ms は比較的早い立ち上がりで、明瞭なディップを生む。
    /// </summary>
    public double AttackTimeMs = 10.0;

    /// <summary>
    /// リリースタイム (ms)。ディップの後、音量が最小から元に戻るまでの時間。
    /// デフォルト 300.0ms はゆっくりとした回復で、EDM系にありがちな息をするようなうねりが得られる。
    /// </summary>
    public double ReleaseTimeMs = 300.0;

    /// <summary>
    /// ポンピング周波数や深さをどの程度ランダムに変動させるかを示す強度。
    /// 0.0 なら変動なし、1.0 なら大きく変化する。デフォルト 0.2。
    /// </summary>
    public double VariationIntensity = 0.2;

    /// <summary>
    /// 何秒ごとにランダム変動を行うかの間隔を指定する (秒)。
    /// デフォルト 1.0 は1秒に1回程度変動が入り、程よい予測不能感を得られる。
    /// </summary>
    public double VariationSpeed = 1.0;

    /// <summary>
    /// ランダム変動を行うかどうかのフラグ。true なら行う。false なら行わない。
    /// デフォルト false。
    /// </summary>
    public bool UseRandomVariation = false;
}

// このクラスが、処理の内容を実装するクラスである。
public class AiAudioEffect_15_SidechainPumping : AiAudioEffectBase
{
    // この関数は、そのまま、書き写すこと。
    protected override IAiAudioEffectSettings NewSettingsFactoryImpl() => new AiAudioEffect_15_SidechainPumping_Settings();

    // 効果のバリエーションを増すためのパラメータのランダム設定を、おおまかな、「強・注・弱」の効果の強さの程度の指定をある程度もとにして、助ける関数
    protected override IAiAudioEffectSettings NewSettingsFactoryWithRandomImpl(AiAudioEffectSpeedType type)
    {
        var ret = new AiAudioEffect_15_SidechainPumping_Settings();

        /* 実装部分ここから */
        switch (type)
        {
            case AiAudioEffectSpeedType.Heavy:
                // Deepかつ速いポンピングを想定
                ret.PumpDepth = Random.Shared.NextDouble() * (1.0 - 0.6) + 0.6;    // 0.6～1.0
                ret.PumpFrequency = Random.Shared.NextDouble() * (4.0 - 1.5) + 1.5;  // 1.5～4.0 (1秒あたり1.5～4回のディップ)
                ret.AttackTimeMs = Random.Shared.NextDouble() * (30.0 - 5.0) + 5.0; // 5～30ms
                ret.ReleaseTimeMs = Random.Shared.NextDouble() * (400.0 - 100.0) + 100.0; // 100～400ms
                ret.VariationIntensity = Random.Shared.NextDouble() * (0.5 - 0.2) + 0.2;  // 0.2～0.5
                ret.VariationSpeed = Random.Shared.NextDouble() * (1.0 - 0.3) + 0.3;  // 0.3～1.0秒ごとに変動
                // だいたい 70% くらいの確率でランダム変動を有効化
                ret.UseRandomVariation = (Random.Shared.NextDouble() < 0.7);
                break;

            case AiAudioEffectSpeedType.Normal:
                // 中程度のポンピング
                ret.PumpDepth = Random.Shared.NextDouble() * (0.8 - 0.4) + 0.4;   // 0.4～0.8
                ret.PumpFrequency = Random.Shared.NextDouble() * (3.0 - 1.0) + 1.0;  // 1.0～3.0 (1秒あたり1～3回のディップ)
                ret.AttackTimeMs = Random.Shared.NextDouble() * (50.0 - 10.0) + 10.0; // 10～50ms
                ret.ReleaseTimeMs = Random.Shared.NextDouble() * (600.0 - 200.0) + 200.0; // 200～600ms
                ret.VariationIntensity = Random.Shared.NextDouble() * (0.3 - 0.1) + 0.1;  // 0.1～0.3
                ret.VariationSpeed = Random.Shared.NextDouble() * (2.0 - 1.0) + 1.0;  // 1.0～2.0秒ごとに変動
                // だいたい 50% くらいの確率でランダム変動を有効化
                ret.UseRandomVariation = (Random.Shared.NextDouble() < 0.5);
                break;

            case AiAudioEffectSpeedType.Light:
                // ゆるめ・控えめのポンピング
                ret.PumpDepth = Random.Shared.NextDouble() * (0.5 - 0.2) + 0.2;  // 0.2～0.5
                ret.PumpFrequency = Random.Shared.NextDouble() * (2.0 - 0.5) + 0.5;  // 0.5～2.0 (1秒あたり0.5～2回)
                ret.AttackTimeMs = Random.Shared.NextDouble() * (80.0 - 20.0) + 20.0; // 20～80ms
                ret.ReleaseTimeMs = Random.Shared.NextDouble() * (800.0 - 300.0) + 300.0; // 300～800ms
                ret.VariationIntensity = Random.Shared.NextDouble() * (0.2 - 0.05) + 0.05; // 0.05～0.2
                ret.VariationSpeed = Random.Shared.NextDouble() * (4.0 - 2.0) + 2.0;  // 2.0～4.0秒ごとに変動
                // だいたい 30% くらいの確率でランダム変動を有効化
                ret.UseRandomVariation = (Random.Shared.NextDouble() < 0.3);
                break;
        }
        /* 実装部分ここまで */

        return ret;
    }

    // この関数が、「効果」を生み出す処理を実際に行なう枢要部分である。
    protected override void ProcessFilterImpl(Memory<byte> waveFileInOut, IAiAudioEffectSettings effectSettings, CancellationToken cancel)
    {
        // まず、effectSettings の内容を、扱いやすいように、AiAudioEffect_15_SidechainPumping_Settings にキャストする。
        AiAudioEffect_15_SidechainPumping_Settings settings = (AiAudioEffect_15_SidechainPumping_Settings)effectSettings;

        /* 実装部分ここから */

        // この実装例では、次のような「擬似サイドチェイン」手法を用いる:
        //   1. waveFileInOut には 44.1kHz / 16bit / ステレオ のPCMデータが入っている。
        //   2. 1サンプル(フレーム)につき 4バイト(左ch 2byte + 右ch 2byte)。
        //   3. 指定されたポンピング周波数 PumpFrequency(Hz) = 1秒あたりのディップ回数。
        //   4. アタック(AttackTimeMs)とリリース(ReleaseTimeMs)でゲインを急激に変化させないよう補間。
        //   5. 一定間隔(VariationSpeed秒ごと)にランダム要素を加味し、PumpFrequencyやPumpDepthを揺らす(UseRandomVariation が true の場合)。
        //   6. ゲインが変化しすぎてクリップノイズが生じないように注意。
        //
        // ポンピングの波形: 1周期を0～1に正規化した位相(phaseInCycle)を用い、
        //   [0        ~ attackFrac)      : 1 から 1 - PumpDepth へ線形に下げる(アタック)
        //   [attackFrac ~ attackFrac+releaseFrac) : 1 - PumpDepth から 1 へ線形に戻す(リリース)
        //   [その他                   ] : 1 (ゲイン最大)
        //
        // attackFrac, releaseFrac は PumpFrequency に応じて調整する。周期は (1.0 / PumpFrequency)秒。
        // ただし attackFrac + releaseFrac が1を超える場合は適宜クリップ（または比率再計算）する。
        //
        // ※ 実際のサイドチェインでは別トラックのキックをトリガーにコンプレッサをかけるが、
        //   ここではトリガーなしのLFO的アプローチでポンピング感を再現する。

        Span<byte> span = waveFileInOut.Span;
        int totalBytes = span.Length;
        // 1サンプル(ステレオ1フレーム)あたり4バイト
        int totalFrames = totalBytes / 4;
        if (totalFrames == 0) return; // 音声データがない場合は処理終了

        int sampleRate = 44100; // 44.1kHz固定を想定

        // 周期 (秒)
        double cycleDuration = (settings.PumpFrequency <= 0.0) ? double.MaxValue : (1.0 / settings.PumpFrequency);
        // Attack と Release を周期(0～1に正規化)で表す
        double attackFrac = 0.0;
        double releaseFrac = 0.0;
        if (cycleDuration < double.MaxValue)
        {
            // attackFrac = AttackTimeMs / (周期秒 * 1000)
            attackFrac = settings.AttackTimeMs / (cycleDuration * 1000.0);
            // releaseFrac = ReleaseTimeMs / (周期秒 * 1000)
            releaseFrac = settings.ReleaseTimeMs / (cycleDuration * 1000.0);

            // アタック＋リリースが1を超えるときは、全体を比率でスケールダウンして1に収める
            double sumAR = attackFrac + releaseFrac;
            if (sumAR > 1.0)
            {
                attackFrac /= sumAR;
                releaseFrac /= sumAR;
            }
        }

        // 現在の位相(0～1の小数でカウントし、超えたら折り返す)
        double phaseInCycle = 0.0;

        // ランダム変動用の次回変動発生時刻(秒)
        double nextVariationTime = (settings.UseRandomVariation) ? settings.VariationSpeed : double.MaxValue;
        // 現在の「実際に使用している」PumpFrequency, PumpDepth
        double currentFrequency = settings.PumpFrequency;
        double currentDepth = settings.PumpDepth;

        // 変動などで周期が変わった場合に再計算する関数
        void RecalcCycle()
        {
            cycleDuration = (currentFrequency <= 0.0) ? double.MaxValue : (1.0 / currentFrequency);
            attackFrac = settings.AttackTimeMs / (cycleDuration * 1000.0);
            releaseFrac = settings.ReleaseTimeMs / (cycleDuration * 1000.0);
            double sumAR = attackFrac + releaseFrac;
            if (sumAR > 1.0)
            {
                attackFrac /= sumAR;
                releaseFrac /= sumAR;
            }
        }

        // メインループ
        double currentTimeSec = 0.0;               // 経過時間(秒)
        double deltaTimeSec = 1.0 / sampleRate;  // 1フレーム(ステレオ)あたりの時間(秒)

        for (int i = 0; i < totalFrames; i++)
        {
            // キャンセル要求が来ていないか一定間隔ごとにチェック（負荷を下げるため適宜）
            if ((i & 0xFFF) == 0) // 4096サンプルごとにチェック
            {
                cancel.ThrowIfCancellationRequested();
            }

            // ポンピング・ゲイン計算
            double envelope = 1.0; // ゲイン(0～1)

            // phaseInCycle: 0～1を周期として移動
            //   0.0 で周期開始点、1.0 で次の周期に折り返し
            if (attackFrac + releaseFrac > 0.0)
            {
                if (phaseInCycle < attackFrac)
                {
                    // アタック段階（音量が 1 から (1 - depth) へ線形に下がる）
                    double r = phaseInCycle / attackFrac;
                    envelope = 1.0 - currentDepth * r;
                }
                else if (phaseInCycle < (attackFrac + releaseFrac))
                {
                    // リリース段階（音量が (1 - depth) から 1 へ線形に上がる）
                    double r = (phaseInCycle - attackFrac) / releaseFrac;
                    envelope = (1.0 - currentDepth) + currentDepth * r;
                }
                else
                {
                    // それ以降は 1.0 で一定（最大音量に復帰した状態）
                    envelope = 1.0;
                }
            }

            // 16bitステレオなので、左チャンネル, 右チャンネルの順
            int byteIndex = i * 4;
            short sampleLeft = BitConverter.ToInt16(span.Slice(byteIndex, 2));
            short sampleRight = BitConverter.ToInt16(span.Slice(byteIndex + 2, 2));

            // -32768～32767 の short を -1.0～+1.0 に変換
            double left = sampleLeft / 32768.0;
            double right = sampleRight / 32768.0;

            // エンベロープを適用
            left *= envelope;
            right *= envelope;

            // short に戻す（クリッピングに注意）
            short outLeft = (short)Math.Clamp((int)(left * 32768.0), -32768, 32767);
            short outRight = (short)Math.Clamp((int)(right * 32768.0), -32768, 32767);

            // 書き戻し
            BitConverter.TryWriteBytes(span.Slice(byteIndex, 2), outLeft);
            BitConverter.TryWriteBytes(span.Slice(byteIndex + 2, 2), outRight);

            // 位相を進める
            if (cycleDuration < double.MaxValue)
            {
                phaseInCycle += deltaTimeSec / cycleDuration;
                if (phaseInCycle >= 1.0) phaseInCycle -= 1.0;
            }

            // 時間を進める
            currentTimeSec += deltaTimeSec;

            // ランダム変動
            if (settings.UseRandomVariation && currentTimeSec >= nextVariationTime)
            {
                // 次回変動を設定
                nextVariationTime += settings.VariationSpeed;

                // VariationIntensity を元に、周波数・深さを少し揺らす
                double freqFactor = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * settings.VariationIntensity;
                double depthFactor = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * settings.VariationIntensity;
                // 周波数や深さが極端になりすぎないようクランプする
                currentFrequency = Math.Clamp(currentFrequency * freqFactor, 0.01, 20.0);
                currentDepth = Math.Clamp(currentDepth * depthFactor, 0.0, 1.0);

                // 再計算
                RecalcCycle();
            }
        }

        /* 実装部分ここまで */
    }
}
#endregion






#endif

