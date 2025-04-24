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

using NAudio.Wave;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Text.RegularExpressions;
using System.Buffers.Binary;

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
    public Func<AiCompositRuleData> GetRuleProc = null!;

    public AiCompositRuleData RuleData => _RuleDataCreated;
    readonly CachedProperty<AiCompositRuleData> _RuleDataCreated;

    public AiCompositRule()
    {
        this._RuleDataCreated = new(getter: () => this.GetRuleProc());
    }
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

        parsed.Options_UsedBgmSrcFileList = okRead.Value.Options_UsedBgmSrcFileList._CloneDeep();
        parsed.Options_VoiceSegmentsList = okRead.Value.Options_VoiceSegmentsList._CloneDeep();
        parsed.Options_UsedMaterials = usedFiles.Options_UsedMaterials._CloneDeep();

        await Lfs.WriteOkFileAsync(dstAudioFilePath, parsed, digest, AiUtilVersion.CurrentVersion, cancel: cancel);

        return parsed;
    }

    private class OperationDesc
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
    }

    public async Task<FfMpegParsedList> CompositWaveFileByAcxBcxTagsWithManyWavMaterialsAsync(string targetSrcWavPath, FfMpegParsedList targetSrcMetaData, string dstWavPath,
        AiCompositWaveSettings settings,
        double targetSrcWavSpeed,
        CancellationToken cancel = default)
    {
        FfMpegParsedList ret = targetSrcMetaData._CloneDeep();

        ret.Options_UsedMaterials = new();

        List<OperationDesc> opList = new();

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

                                        opList.Add(new OperationDesc { StartPosition = thisStart, EndPosition = thisStart + thisLen, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
                                    }
                                }
                                else
                                {
                                    opList.Add(new OperationDesc { StartPosition = seg.TimePosition, EndPosition = seg2.TimePosition, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
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

                                opList.Add(new OperationDesc { StartPosition = thisStart, EndPosition = thisStart + thisLen, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
                            }
                        }
                        else
                        {
                            opList.Add(new OperationDesc { StartPosition = seg.TimePosition, EndPosition = seg2.TimePosition, Rule = rule, MatFilesQueue = rule.RuleData.MaterialsWavPathQueue });
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

                ret.Options_UsedMaterials.Add(new(wavFilePath, op.StartPosition, wantLength));

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

    public void CheckWavFormat(WaveFormat format)
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
        IEnumerable<OperationDesc> operations, CancellationToken cancel = default)
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
                    ReadOnlyMemory<byte> matSrcData;

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
                for (int i = 0; i < voiceSegList.Count; i++)
                {
                    var seg1 = voiceSegList[i];
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

                for (int fail = 0;fail <= 1000;fail++)
                {
                    string tmp1 = q.Dequeue();

                    srcWavLength = await GetWavFileLengthSecsAsync(tmp1, cancel: cancel);
                    if (srcWavLength  >= requireLength)
                    {
                        srcWavPath = tmp1;
                        break;
                    }
                }

                if (srcWavPath._IsEmpty())
                {
                    continue;
                }

                // 置換 wav ファイルの読み込み
                double srcStartPosition = (srcWavLength - requireLength) * Util.RandDouble0To1();

                ReadOnlyMemory<byte> srcMemory;

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

                // 合成処理
                ReplaceWavDataWithFadeInOut(targetData, srcMemory, range.StartPosition, range.FadeIn, range.FadeOut);
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

        string outWavTmpPath = await Lfs.GenerateUniqueTempFilePathAsync("bgmadded_file", ".wav", cancel: cancel);

        await FfMpeg.AddBgmToVoiceFileAsync(voiceWavTmpPath, bgmWavTmpPath, outWavTmpPath, smoothMode: smoothMode, cancel: cancel);

        var parsed = await FfMpeg.EncodeAudioAsync(outWavTmpPath, dstFilePath, codec, kbps, useOkFile: false, metaData: newMeta, cancel: cancel);

        parsed.Options_UsedBgmSrcFileList = retSrcList;
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
            newMeta.Album = newMeta.Album._ReplaceStr(" - bgm_x", " - mat_x", true);
            newMeta.AlbumArtist = newMeta.AlbumArtist._ReplaceStr(" - bgm_x", " - mat_x", true);
            newMeta.Title = newMeta.Title._ReplaceStr(" - bgm_x", " - mat_x", true);
            newMeta.Artist = newMeta.Artist._ReplaceStr(" - bgm_x", " - mat_x", true);
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
            string tmp2 = "mat_" + tmp1;

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


    public async Task<List<string>> CreateRandomBgmFileAsync(string srcMusicWavsDirPath, string dstWavFilePath, int totalDurationMsecs, AiRandomBgmSettings settings, int fadeOutSecs = AiTask.DefaultFadeoutSecs, double adjustDelta = AiTask.BgmVolumeDeltaForConstant, CancellationToken cancel = default)
    {
        string dstTmpFileName = await Lfs.GenerateUniqueTempFilePathAsync("concat2", ".wav", cancel: cancel);

        List<string> retSrcList = await ConcatWavFileFromRandomDirAsync(srcMusicWavsDirPath, dstTmpFileName, totalDurationMsecs, settings, fadeOutSecs, cancel);

        await FfMpeg.AdjustAudioVolumeAsync(dstTmpFileName, dstWavFilePath, adjustDelta, cancel);

        return retSrcList;
    }

    public async Task<List<string>> ConcatWavFileFromRandomDirAsync(string srcMusicWavsDirPath, string dstWavFilePath, int totalDurationMsecs, AiRandomBgmSettings settings, int fadeOutSecs = AiTask.DefaultFadeoutSecs, CancellationToken cancel = default)
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

    public async Task<List<string>> ConcatWavFileFromRandomDirAsync(ShuffledEndlessQueue<string> srcMusicWavsFilePathQueue, string dstWavFilePath, int totalDurationMsecs, AiRandomBgmSettings settings, int fadeOutSecs = AiTask.DefaultFadeoutSecs, CancellationToken cancel = default)
    {
        List<string> retSrcList;

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

    public static async Task<List<string>> ConcatWavFileFromQueueAsync(
        ShuffledEndlessQueue<string> srcWavFilesQueue,
        int totalDurationMsecs,
        string dstWavFilePath, CancellationToken cancel = default)
    {
        List<string> retSrcList = new List<string>();

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

            retSrcList.Add(currentWavPath);

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

        await TaskUtil.RetryAsync(async c =>
        {
            await ConvertInternalAsync(srcWavPath, dstWavPath, voiceSamplePath, diffusionSteps, tagTitle, cancel);

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

    async Task ConvertInternalAsync(string srcWavPath, string dstWavPath, string voiceSamplePath, int diffusionSteps, string tagTitle = "", CancellationToken cancel = default)
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
                $"--auto-f0-adjust False --semi-tone-shift 0 --config runs/test01_kuraki/config_dit_mel_seed_uvit_whisper_small_wavenet.yml " +
                $"--fp16 True", timeout, printTag: tag, cancel: cancel);
        }
        else
        {
            result = await this.RunVEnvPythonCommandsAsync(
                $"python inference.py --source test_in_data/_aiutil_src.wav --target test_in_data/_aiutil_sample.wav " +
                $"--output test_out_dir --diffusion-steps {diffusionSteps} --length-adjust 1.0 --inference-cfg-rate 1.0 --f0-condition True " +
                $"--auto-f0-adjust True --semi-tone-shift 0 --config runs/test01_kuraki/config_dit_mel_seed_uvit_whisper_base_f0_44k.yml " +
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

        await Lfs.DeleteFileIfExistsAsync(dstWavPath, cancel: cancel);

        await FfMpeg.AdjustAudioVolumeAsync(aiDstFile.FullPath, dstWavPath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, FfmpegAdjustVolumeOptiono.MeanOnly, tagTitle, false, cancel);
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
                };

                wavFileNameIndexToSegmentMappingTable[wavFileNameIndex] = new(segmentForVoice, segmentForBlank);

                segmentsList.Add(segmentForVoice);

                segmentsList.Add(segmentForBlank);
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

                    double silenceDurationSeconds = 0.5; // 0.5 秒
                    int silenceBytes = (int)(bytesPerSecond * silenceDurationSeconds);

                    var silenceBuffer = new byte[silenceBytes];

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
}

// By ChatGPT
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
    public static async Task<List<string>> ConcatenateAsync(
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

        List<string> retSrcList = new List<string>();

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

                retSrcList.Add(wavPath);

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

#endif

