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
    public const int CurrentVersion = 20250330_03;
}

public class AiTask
{
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

        foreach (var wavFile in wavFilesList.Where(x => x.IsFile).OrderBy(x => x.Name, StrCmpi))
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

        ShuffleQueue<string> sampleVoiceFileNameShuffleQueue;


        List<int> randIntListAll = new();
        for (int i = 0; i <= 98; i++)
        {
            randIntListAll.Add(i);
        }
        ShuffleQueue<int> speakerIdShuffleQueueAll = new ShuffleQueue<int>(randIntListAll);

        List<int> randIntListTokutei = new();
        randIntListTokutei.Add(8);
        randIntListTokutei.Add(4);
        randIntListTokutei.Add(4);
        randIntListTokutei.Add(43);
        randIntListTokutei.Add(43);
        randIntListTokutei.Add(48);
        randIntListTokutei.Add(58);
        randIntListTokutei.Add(58);
        randIntListTokutei.Add(58);
        randIntListTokutei.Add(60);
        randIntListTokutei.Add(60);
        randIntListTokutei.Add(68);
        randIntListTokutei.Add(90);
        randIntListTokutei.Add(90);
        ShuffleQueue<int> speakerIdShuffleQueueTokutei = new ShuffleQueue<int>(randIntListTokutei);


        var randSampleVoiceFilesList = await Lfs.EnumDirectoryAsync(sampleVoiceWavDirName, false, wildcard: "*.wav", cancel: cancel);
        if (randSampleVoiceFilesList.Any())
        {
            sampleVoiceFileNameShuffleQueue = new ShuffleQueue<string>(randSampleVoiceFilesList.Select(x => x.FullPath));
        }
        else
        {
            throw new CoresLibException($"Directory '{sampleVoiceWavDirName}' has no music files.");
        }

        for (int i = 0; i < maxTryCount; i++)
        {
            try
            {
                var sampleVoicePath = sampleVoiceFileNameShuffleQueue.GetNext();

                string thisText = srcText.Substring(Secure.RandSInt31() % (srcText.Length - textLengthOfRandomPart), textLengthOfRandomPart);

                int speakerIdToUse = speakerId;

                if (speakerIdToUse < 0)
                {
                    if (speakerIdToUse == -2)
                    {
                        int rand1 = Secure.RandSInt31() % 3;
                        if (rand1 != 0)
                        {
                            speakerIdToUse = speakerIdShuffleQueueTokutei.GetNext();
                        }
                        else
                        {
                            speakerIdToUse = speakerIdShuffleQueueAll.GetNext();
                        }
                    }
                    else
                    {
                        speakerIdToUse = speakerIdShuffleQueueAll.GetNext();
                    }
                }

                if (speakerIdToUse < 0) speakerIdToUse = 58;

                string storyTitle = testName + "_" + i.ToString("D5");

                await ConvertTextToVoiceAsync(thisText, sampleVoicePath, dstVoiceDirPath, tmpVoiceBoxDir, tmpVoiceWavDir, speakerIdToUse._SingleArray(), diffusionSteps, seriesName, storyTitle, new int[] { 100, 125 }, cancel);
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

    public async Task ConvertAllTextToVoiceAsync(string srcDirPath, string srcSampleVoiceFileNameOrRandDir, string speakerIdStrOrListFilePath, bool mixedMode, int diffusionSteps, string dstVoiceDirPath, string tmpVoiceBoxDir, string tmpVoiceWavDir, int[]? speedPercentList = null, CancellationToken cancel = default)
    {
        ShuffleQueue<string>? sampleVoiceFileNameShuffleQueue = null;
        if (await Lfs.IsDirectoryExistsAsync(srcSampleVoiceFileNameOrRandDir, cancel))
        {
            var randSampleVoiceFilesList = await Lfs.EnumDirectoryAsync(srcSampleVoiceFileNameOrRandDir, false, wildcard: "*.wav", cancel: cancel);
            if (randSampleVoiceFilesList.Any())
            {
                sampleVoiceFileNameShuffleQueue = new ShuffleQueue<string>(randSampleVoiceFilesList.Select(x => x.FullPath));
            }
            else
            {
                throw new CoresLibException($"Directory '{srcSampleVoiceFileNameOrRandDir}' has no music files.");
            }
        }

        ShuffleQueue<int>? speakerIdShuffleForRotatin = null;

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
                speakerIdShuffleForRotatin = new ShuffleQueue<int>(await ReadSpeakerIDListAsync(speakerIdStrOrListFilePath, cancel));
            }
            else
            {
                speakerIdListInOneFile = await ReadSpeakerIDListAsync(speakerIdStrOrListFilePath, cancel);
            }
        }

        var seriesDirList = await Lfs.EnumDirectoryAsync(srcDirPath, cancel: cancel);

        foreach (var seriesDir in seriesDirList.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false).OrderBy(x => x.Name, StrCmpi))
        {
            var srcTextList = await Lfs.EnumDirectoryAsync(seriesDir.FullPath, true, cancel: cancel);

            foreach (var srcTextFile in srcTextList.Where(x => x.IsFile && x.Name._IsExtensionMatch(Consts.Extensions.Text) && x.Name.StartsWith("_") == false).OrderBy(x => x.Name, StrCmpi))
            {
                try
                {
                    string srcSampleVoiceFile = srcSampleVoiceFileNameOrRandDir;

                    if (sampleVoiceFileNameShuffleQueue != null)
                    {
                        srcSampleVoiceFile = sampleVoiceFileNameShuffleQueue.GetNext();
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
                        speakerIdListForThisFile = speakerIdShuffleForRotatin!.GetNext()._SingleList();
                    }
                    else
                    {
                        speakerIdListForThisFile = speakerIdListInOneFile.ToList();
                    }

                    await ConvertTextToVoiceAsync(srcText, srcSampleVoiceFile, dstVoiceDirPath, tmpVoiceBoxDir, tmpVoiceWavDir, speakerIdListForThisFile, diffusionSteps, seriesName, storyTitle, speedPercentList, cancel);

                    // テキストファイルの先頭に _ を付ける
                    string newFilePath = PP.Combine(PP.GetDirectoryName(srcTextFile.FullPath), "_" + PP.GetFileName(srcTextFile.FullPath));

                    await Lfs.MoveFileAsync(srcTextFile.FullPath, newFilePath);
                }
                catch (Exception ex)
                {
                    srcTextFile.FullPath._Error();
                    ex._Error();
                }
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

        await using (var vv = new AiUtilVoiceVoxEngine(this.Settings, this.FfMpeg))
        {
            if (tagTitle._IsEmpty()) tagTitle = storyTitle._TruncStrEx(16);

            await vv.TextToWavAsync(srcText, speakerIdList, tmpVoiceBoxWavPath, tagTitle, true, cancel);
        }

        string tmpVoiceWavPath = PP.Combine(tmpVoiceWavDir, $"{safeSeriesName} - {safeStoryTitle} - {safeVoiceTitle} - {speakerIdStr}.wav");

        tagTitle = $"{safeSeriesName} - {safeStoryTitle} - {safeVoiceTitle} - {speakerIdStr}";

        await using (var seedvc = new AiUtilSeedVcEngine(this.Settings, this.FfMpeg))
        {
            await seedvc.ConvertAsync(tmpVoiceBoxWavPath, tmpVoiceWavPath, srcSampleVoicePath, diffusionSteps, tagTitle, true, cancel);
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

            string dstVoiceFlacPath = PP.Combine(dstVoiceDirPath, safeSeriesName, $"{safeSeriesName} - {speedStr} - {safeStoryTitle} - {safeVoiceTitle} - {speakerIdStr}.flac");

            await FfMpeg.EncodeAudioAsync(tmpVoiceWavPath, dstVoiceFlacPath, FfMpegAudioCodec.Flac, 0, speed, meta, tagTitle, true, cancel);
        }
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

            foreach (var srcMusicFile in srcMusicList.Where(x => x.IsFile && x.Name._IsExtensionMatch(Consts.Extensions.Filter_MusicFiles)).OrderBy(x => x.Name, StrCmpi))
            {
                try
                {
                    string songTitle = PPWin.GetFileNameWithoutExtension(srcMusicFile.Name)._NormalizeSoftEther(true);
                    string safeSongTitle = PPWin.MakeSafeFileName(songTitle, true, true, true);

                    string tmpMusicWavPath = PP.Combine(tmpMusicDirPath, $"MusicOnly - {safeArtistName} - {safeSongTitle}.wav");
                    string tmpVocalWavPath = PP.Combine(tmpVocalDirPath, $"VocalOnly - {safeArtistName} - {safeSongTitle}.wav");

                    var result = await ExtractMusicAndVocalAsync(srcMusicFile.FullPath, tmpMusicWavPath, tmpVocalWavPath, safeSongTitle, cancel);

                    string formalSongTitle = (result?.Meta?.Title)._NonNullTrimSe();
                    if (formalSongTitle._IsEmpty())
                    {
                        formalSongTitle = songTitle;
                    }

                    MediaMetaData meta = new MediaMetaData
                    {
                        Album = musicOnlyAlbumName,
                        Title = formalSongTitle + " - " + musicOnlyAlbumName,
                        Artist = musicOnlyAlbumName + " - " + artistName,
                    };

                    string dstMusicAacPath = PP.Combine(dstMusicDirPath, $"{musicOnlyAlbumName} - {safeArtistName} - {safeSongTitle}.m4a");

                    await FfMpeg.EncodeAudioAsync(tmpMusicWavPath, dstMusicAacPath, FfMpegAudioCodec.Aac, 0, 100, meta, safeSongTitle, cancel: cancel);
                }
                catch (Exception ex)
                {
                    srcMusicFile.FullPath._Error();
                    ex._Error();
                }
            }
        }
    }

    public async Task<FfMpegParsedList> ExtractMusicAndVocalAsync(string srcFilePath, string dstMusicWavPath, string dstVocalWavPath, string tagTitle, CancellationToken cancel = default)
    {
        FfMpegParsedList ret = await TaskUtil.RetryAsync(async c =>
        {
            await using var uvr = new AiUtilUvrEngine(this.Settings, this.FfMpeg);

            return await uvr.ExtractAsync(srcFilePath, dstMusicWavPath, dstVocalWavPath, tagTitle, true, cancel);
        },
        1000,
        3,
        cancel,
        true);

        return ret;
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

public class AiUtilSeedVcEngine : AiUtilBasicEngine
{
    public FfMpegUtil FfMpeg { get; }

    public AiUtilSeedVcEngine(AiUtilBasicSettings settings, FfMpegUtil ffMpeg) : base(settings, "SEED-VC", settings.AiTest_SeedVc_BaseDir)
    {
        this.FfMpeg = ffMpeg;
    }

    public async Task ConvertAsync(string srcWavPath, string dstWavPath, string voiceSamplePath, int diffusionSteps, string tagTitle = "", bool useOkFile = true, CancellationToken cancel = default)
    {
        if (tagTitle._IsEmpty()) tagTitle = PP.GetFileNameWithoutExtension(srcWavPath);

        string digest = $"voiceSamplePath={voiceSamplePath},diffusionSteps={diffusionSteps},targetMaxVolume={Settings.AdjustAudioTargetMaxVolume},targetMeanVolume={Settings.AdjustAudioTargetMeanVolume}";

        if (useOkFile)
        {
            if (await Lfs.IsOkFileExists(dstWavPath, digest, AiUtilVersion.CurrentVersion, cancel))
            {
                return;
            }
        }

        await TaskUtil.RetryAsync(async c =>
        {
            await ConvertInternalAsync(srcWavPath, dstWavPath, voiceSamplePath, diffusionSteps, tagTitle, cancel);

            return true;
        },
        5, 200, cancel, true);

        if (useOkFile)
        {
            await Lfs.WriteOkFileAsync(dstWavPath, new OkFileEmptyMetaData(), digest, AiUtilVersion.CurrentVersion, cancel);
        }
    }

    async Task ConvertInternalAsync(string srcWavPath, string dstWavPath, string voiceSamplePath, int diffusionSteps, string tagTitle = "", CancellationToken cancel = default)
    {
        string aiSrcPath = BaseDirPath._CombinePath("test_in_data", "_aiutil_src.wav");

        string aiSamplePath = BaseDirPath._CombinePath("test_in_data", "_aiutil_sample.wav");

        await Lfs.DeleteFileIfExistsAsync(aiSrcPath, cancel: cancel);

        await Lfs.DeleteFileIfExistsAsync(aiSamplePath, cancel: cancel);
        await FfMpeg.AdjustAudioVolumeAsync(voiceSamplePath, aiSamplePath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, voiceSamplePath, false, cancel);

        await Lfs.DeleteFileIfExistsAsync(aiSrcPath, cancel: cancel);
        await FfMpeg.AdjustAudioVolumeAsync(srcWavPath, aiSrcPath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, tagTitle, false, cancel);

        await DeleteAllAiProcessOutputFilesAsync(cancel);

        if (tagTitle._IsEmpty())
        {
            tagTitle = srcWavPath._GetFileNameWithoutExtension();
        }
        string tag = $"Convert ('{tagTitle._TruncStrEx(16)}')";

        int srcWavFileLen = await AiTask.GetWavFileLengthMSecAsync(srcWavPath);
        int timeout = (srcWavFileLen * 2) + (60 * 1000);

        var result = await this.RunVEnvPythonCommandsAsync(
            $"python inference.py --source test_in_data/_aiutil_src.wav --target test_in_data/_aiutil_sample.wav " +
            $"--output test_out_dir --diffusion-steps {diffusionSteps} --length-adjust 1.0 --inference-cfg-rate 1.0 --f0-condition False " +
            $"--auto-f0-adjust False --semi-tone-shift 0 --config runs/test01_kuraki/config_dit_mel_seed_uvit_whisper_small_wavenet.yml " +
            $"--fp16 True", timeout, printTag: tag, cancel: cancel);

        if (result.OutputAndErrorStr._GetLines().Where(x => x.StartsWith("RTF: ")).Any() == false)
        {
            throw new CoresLibException($"{tag} failed.");
        }

        string aiOutDir = PP.Combine(this.BaseDirPath, "test_out_dir");

        var files = await Lfs.EnumDirectoryAsync(aiOutDir, wildcard: "*.wav", cancel: cancel);
        var aiDstFile = files.Single();

        await Lfs.DeleteFileIfExistsAsync(dstWavPath, cancel: cancel);

        await FfMpeg.AdjustAudioVolumeAsync(aiDstFile.FullPath, dstWavPath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, tagTitle, false, cancel);
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

        string digest = $"text={textBlockList._LinesToStr()._Digest()},speakerId={speakerIdList.Select(x => x.ToString())._Combine("+")},targetMaxVolume={Settings.AdjustAudioTargetMaxVolume},targetMeanVolume={Settings.AdjustAudioTargetMeanVolume}";

        if (useOkFile)
        {
            var okResult = await Lfs.ReadOkFileAsync<FfMpegParsedList>(dstWavPath, digest, AiUtilVersion.CurrentVersion, cancel);
            if (okResult.IsOk && okResult.Value != null) return okResult.Value;
        }

        ShuffleQueue<int> speakerIdShuffleQueue = new ShuffleQueue<int>(speakerIdList, 3);

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

        for (int i = 0; i < textBlockList.Count; i++)
        {
            string block = textBlockList[i];
            int speakerId = speakerIdShuffleQueue.GetNext();

            byte[] blockWavData = await TextBlockToWavAsync(block, speakerId);

            var tmpPath = await Lfs.GenerateUniqueTempFilePathAsync($"{tagTitle}_{i:D8}_speaker{speakerId:D3}", ".wav", cancel: cancel);

            await Lfs.WriteDataToFileAsync(tmpPath, blockWavData, FileFlags.AutoCreateDirectory, cancel: cancel);

            blockWavFileNameList.Add(tmpPath);

            totalFileSize += blockWavData.LongLength;

            Con.WriteLine($"{SimpleAiName}: {tagTitle}: Text to Wav: {(i + 1)._ToString3()}/{textBlockList.Count._ToString3()}, Size: {totalFileSize._ToString3()} bytes, Speaker: {speakerId:D3}");
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
                    var buffer = new byte[reader.WaveFormat.AverageBytesPerSecond * 4];
                    int bytesRead;
                    while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        writer.Write(buffer, 0, bytesRead);
                    }

                    int bytesPerSample = waveFormat.BitsPerSample / 8;
                    int bytesPerSecond = waveFormat.SampleRate * waveFormat.Channels * bytesPerSample;
                    double silenceDurationSeconds = 0.5; // 0.5 秒
                    int silenceBytes = (int)(bytesPerSecond * silenceDurationSeconds);

                    var silenceBuffer = new byte[silenceBytes];
                    writer.Write(silenceBuffer, 0, silenceBuffer.Length);
                }

                Con.WriteLine($"{SimpleAiName}: {tagTitle}: Concat wav: {(i + 1)._ToString3()}/{textBlockList.Count._ToString3()}, Size: {writer.Length._ToString3()} bytes");
            }
        }

        var results = await this.FfMpeg.AdjustAudioVolumeAsync(concatFile, dstWavPath, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, tagTitle, false, cancel);

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

        var result2 = await http.SimplePostDataAsync(url2, queryJsonStr._GetBytes_UTF8(), cancel, "application/json");

        return result2.Data;
    }

    // テキスト分割
    static List<string> SplitText(string text, int maxLen = 100)
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
        var result = await FfMpeg.AdjustAudioVolumeAsync(srcFilePath, adjustedWavFile, Settings.AdjustAudioTargetMaxVolume, Settings.AdjustAudioTargetMeanVolume, tagTitle, false, cancel);

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

        var result = await this.RunVEnvPythonCommandsAsync($"python {(music ? "get_music" : "get_vocal")}_wav.py", timeout, printTag: tag, cancel: cancel);

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

#endif

