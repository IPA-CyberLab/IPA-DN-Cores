// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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

using System;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.Cryptography;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev;

partial class TestDevCommands
{
    // 指定したディレクトリにあるすべてのファイルの SHA1 チェックサムを表示する
    [ConsoleCommand(
        "ExpandIncludes command",
        "ExpandIncludes <src> /DST:<dst> [/BOM:true|false]",
        "ExpandIncludes command")]
    static int ExpandIncludes(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[src]", ConsoleService.Prompt, "Src: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("DST", ConsoleService.Prompt, "Dst: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("BOM"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string src = vl.DefaultParam.StrValue;
        string dst = vl["DST"].StrValue;
        bool bom = vl["BOM"].BoolValue;

        MiscUtil.ExpandIncludesFileAsync(src, dst, writeBom: true)._GetResult();

        return 0;
    }

    [ConsoleCommand(
        "WriteLargeRandFile command",
        "WriteLargeRandFile <path> /SIZE:<size> [/COUNT:<count=1>]",
        "WriteLargeRandFile command")]
    static int WriteLargeRandFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[path]", ConsoleService.Prompt, "Path: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("SIZE", ConsoleService.Prompt, "Size: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("COUNT"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string basePath = vl.DefaultParam.StrValue;
        long targetOneFileSize = vl["SIZE"].StrValue._ToLong();
        int count = vl["COUNT"].IntValue;

        count = Math.Max(count, 1);

        if (targetOneFileSize < 0) targetOneFileSize = long.MaxValue;

        Con.WriteLine($"TargetSize: {targetOneFileSize._ToString3()} bytes");

        List<string> fileNameList = new List<string>();

        if (count == 1)
        {
            fileNameList.Add(basePath);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                string fn = PP.GetFileName(basePath);
                string fn1 = PP.GetFileNameWithoutExtension(fn);
                string fn2 = PP.GetExtension(fn);
                fn = $"{fn1}.{i:D4}{fn2}";

                fileNameList.Add(PP.Combine(PP.GetDirectoryName(basePath), fn));
            }
        }

        long estimatedRemainSize = targetOneFileSize * count;
        if (targetOneFileSize == long.MaxValue) estimatedRemainSize = long.MaxValue;

        long initialTotalSize = estimatedRemainSize;

        using (ProgressReporterBase reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput,
            reportTimingSetting: new ProgressReportTimingSetting(false, 1000)
            ), null))
        {
            foreach (var filePath in fileNameList)
            {
                Async(async () =>
                {
                    string fn = PP.GetFileName(filePath);

                    await using var file = await Lfs.OpenOrCreateAppendAsync(filePath, flags: FileFlags.AutoCreateDirectory | FileFlags.OnCreateRemoveCompressionFlag);

                    long initialFileSize = await file.GetFileSizeAsync();

                    await file.SeekToEndAsync();

                    if (targetOneFileSize <= initialFileSize)
                    {
                        Con.WriteLine($"{fn}: Data already exists. CurrentSize: {initialFileSize._ToString3()} bytes");
                        estimatedRemainSize -= targetOneFileSize;
                        reporter.ReportProgress(new ProgressData(initialTotalSize - estimatedRemainSize, initialTotalSize, additionalInfo: fn));
                        return;
                    }

                    int bufSize = 1_000_000;

                    Memory<byte> buffer = new byte[bufSize];

                    int flushCount = 0;

                    estimatedRemainSize -= initialFileSize;

                    long currentFileSize = initialFileSize;

                    while (true)
                    {
                        long currentRemainSize = targetOneFileSize - currentFileSize;

                        if (currentRemainSize <= 0) break;

                        int blockSize = (int)Math.Min(bufSize, currentRemainSize);

                        Memory<byte> block = buffer.Slice(0, blockSize);

                        Util.Rand(block.Span);

                        await file.WriteAsync(block);

                        currentFileSize += blockSize;

                        estimatedRemainSize -= blockSize;

                        flushCount++;

                        if ((flushCount % 100) == 0)
                        {
                            await file.FlushAsync();
                        }

                        reporter.ReportProgress(new ProgressData(initialTotalSize - estimatedRemainSize, initialTotalSize, additionalInfo: fn));
                    }
                });
            }
        }

        return 0;
    }

    // 指定したディレクトリにあるすべてのファイルの SHA1 チェックサムを表示する
    [ConsoleCommand(
        "CompareBinaryFile command",
        "CompareBinaryFile <file1> /FILE2:<file2> [/MAXDIFFS:max_diffs=1000]",
        "CompareBinaryFile command")]
    static int CompareBinaryFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[file1]", ConsoleService.Prompt, "File1: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("FILE2", ConsoleService.Prompt, "File2: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("MAXDIFFS"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string path1 = vl.DefaultParam.StrValue;
        string path2 = vl["FILE2"].StrValue;
        long maxDiffs = Math.Max(vl["MAXDIFFS"].StrValue._ToLong(), 0);
        if (maxDiffs == 0) maxDiffs = 1000;

        const int blockSize = 1_000_000;
        const int printBlockSize = 25;

        Async(async () =>
        {
            await using var file1 = await Lfs.OpenAsync(path1);
            await using var file2 = await Lfs.OpenAsync(path2);

            string fn1 = path1._GetFileName()!;
            string fn2 = path2._GetFileName()!;

            long fileSize1 = await file1.GetFileSizeAsync();
            long fileSize2 = await file2.GetFileSizeAsync();

            long compareFileSize = Math.Min(fileSize1, fileSize2);

            if (fileSize1 != fileSize2)
            {
                $"Warning: File size is different. {path1} = {fileSize1._ToString3()}, {path2} = {fileSize2._ToString3()}"._Print();
            }

            long currentFilePos = 0;

            Memory<byte> mem1 = new byte[blockSize];
            Memory<byte> mem2 = new byte[blockSize];

            long diffIndex = 0;

            StringWriter diffResults = new StringWriter();

            using (ProgressReporterBase reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput,
    reportTimingSetting: new ProgressReportTimingSetting(false, 1000)
    ), null))
            {

                while (currentFilePos < compareFileSize)
                {
                    long remainSize = compareFileSize - currentFilePos;
                    int readSize = (int)Math.Min(blockSize, remainSize);

                    Memory<byte> buf1 = mem1._SliceHead(readSize);
                    Memory<byte> buf2 = mem2._SliceHead(readSize);

                    int r1 = await file1.ReadAsync(mem1);
                    if (r1 != readSize)
                    {
                        throw new CoresException($"{path1}: Read Error. Read Size = {readSize}, Pos = {currentFilePos._ToString3()}");
                    }

                    int r2 = await file2.ReadAsync(mem2);
                    if (r2 != readSize)
                    {
                        throw new CoresException($"{path2}: Read Error. Read Size = {readSize}, Pos = {currentFilePos._ToString3()}");
                    }

                    if (buf1._MemEquals(buf2) == false)
                    {
                        // 相違しているようである。相違点を検査する。
                        for (int printPos = 0; printPos < readSize; printPos += printBlockSize)
                        {
                            int thisPrintBlockSize = Math.Min(printBlockSize, readSize - printPos);

                            Memory<byte> pbuf1 = buf1.Slice(printPos, thisPrintBlockSize);
                            Memory<byte> pbuf2 = buf2.Slice(printPos, thisPrintBlockSize);

                            if (pbuf1._MemEquals(pbuf2) == false)
                            {
                                // 相違点発見！
                                long offset = currentFilePos + printPos;
                                StringWriter w = new StringWriter();
                                diffIndex++;
                                w.WriteLine($"{fn1} vs {fn2}:");
                                w.WriteLine($"Diff #{diffIndex}: Offset: {offset._ToString3()}");
                                w.WriteLine(pbuf1._GetHexString(" "));
                                w.WriteLine(pbuf2._GetHexString(" "));
                                w.WriteLine();
                                w.WriteLine();
                                w.ToString()._Print();

                                diffResults.Write(w.ToString());

                                if (diffIndex >= maxDiffs)
                                {
                                    $"Error: Too many diffs."._Print();
                                    goto L_ESCAPE;
                                }
                            }
                        }
                    }

                    currentFilePos += readSize;

                    reporter.ReportProgress(new ProgressData(currentFilePos, compareFileSize, false, (diffIndex == 0 ? "No diffs" : $"Diffs={diffIndex}")));
                }

                L_ESCAPE:

                reporter.ReportProgress(new ProgressData(currentFilePos, currentFilePos, true, (diffIndex == 0 ? "No diffs" : $"Diffs={diffIndex}")));

                ""._Print();
                "------- Finished !!! -------"._Print();

                if (diffIndex == 0)
                {
                    $"Result: No diffs."._Print();
                }
                else
                {
                    $"Total Result: {diffIndex} or more diffs."._Print();
                    ""._Print();
                    diffResults.ToString()._Print();
                    $"Total Result: {diffIndex} or more diffs."._Print();
                }

                if (fileSize1 != fileSize2)
                {
                    $"Warning: File size is different. {path1} = {fileSize1._ToString3()}, {path2} = {fileSize2._ToString3()}"._Print();
                }

                ""._Print();
            }
        });

        return 0;
    }

    // 指定したディレクトリにあるすべてのファイルの SHA1 チェックサムを表示する
    [ConsoleCommand(
    "DirSha1Sum command",
    "DirSha1Sum [dirName]",
    "DirSha1Sum command")]
    static int DirSha1Sum(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[dirName]", ConsoleService.Prompt, "Dir name: ", ConsoleService.EvalNotEmpty, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dirName = vl.DefaultParam.StrValue;

        SortedDictionary<string, string> resultList = new SortedDictionary<string, string>(Lfs.PathParser.PathStringComparer);

        void PrintCurrentResultList(bool onlyConsole)
        {
            StringWriter w = new StringWriter();

            lock (resultList)
            {
                if (resultList.Any() == false)
                {
                    return;
                }

                w.WriteLine();

                w.WriteLine($"=== Begin Current Results ===");
                foreach (var kv in resultList)
                {
                    w.WriteLine($"{kv.Key._GetFileName()}: {kv.Value}");
                }
                w.WriteLine($"=== End Current Results ===");
                w.WriteLine();
                w.WriteLine();
            }

            if (onlyConsole)
            {
                lock (Con.ConsoleWriteLock)
                {
                    Console.WriteLine(w.ToString());
                }
            }
            else
            {
                w.ToString()._Print();
            }
        }

        Async(async () =>
        {
            await using CancelWatcher printIntervalCancel = new CancelWatcher();

            Task printInterval = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                while (printIntervalCancel.IsCancellationRequested == false)
                {
                    await printIntervalCancel.CancelToken._WaitUntilCanceledAsync(60 * 1000);

                    PrintCurrentResultList(true);
                }
            });

            try
            {
                using (ProgressReporterBase reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput,
                    reportTimingSetting: new ProgressReportTimingSetting(false, 1000)
                    ), null))
                {
                    long totalAllFilesReadSize = 0;

                    while (true)
                    {
                        // 次のファイル名を決める
                        var entList = await Lfs.EnumDirectoryAsync(dirName);
                        string targetFilePath;

                        lock (resultList)
                        {
                            targetFilePath = entList.Where(x => x.IsFile).OrderBy(x => x.Name, Lfs.PathParser.PathStringComparer).Where(x => resultList.ContainsKey(x.FullPath) == false).FirstOrDefault()?.FullPath ?? "";
                        }

                        long estimatedAllFileSizeSum = entList.Where(x => x.IsFile).Sum(x => x.Size);
                        if (targetFilePath._IsEmpty())
                        {
                            // 全部のファイルを読み込み完了した
                            reporter.ReportProgress(new ProgressData(totalAllFilesReadSize, totalAllFilesReadSize, true, "ALL"));
                            break;
                        }

                        using SHA1 sha = SHA1.Create();

                        int bufSize = 8 * 1024 * 1024;
                        await using var fs = new FileStream(targetFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufSize, false);
                        long fileSizeEstimated = fs.Length;

                        string fileName = targetFilePath._GetFileName()!;

                        RefLong fileTotalReadSize = new RefLong();

                        $"--- START ---"._Print();
                        $"File Name: '{fileName}'"._Print();
                        $"File Size: {fileSizeEstimated._ToString3()}"._Print();
                        $"File Read Size (Estimated): {fileSizeEstimated._ToString3()}"._Print();

                        string hashStr;
                        try
                        {
                            var hash = await Secure.CalcStreamHashAsync(fs, sha, bufferSize: bufSize, totalReadSize: fileTotalReadSize,
                                progressReporter: reporter,
                                progressReporterCurrentSizeOffset: totalAllFilesReadSize,
                                progressReporterTotalSizeHint: estimatedAllFileSizeSum,
                                progressReporterFinalize: false,
                                progressReporterAdditionalInfo: fileName);

                            totalAllFilesReadSize += fileTotalReadSize;

                            hashStr = hash._GetHexString().ToLowerInvariant();
                        }
                        catch (Exception ex)
                        {
                            hashStr = $"! Read Error Offset = {fileTotalReadSize.Value._ToString3()}, Err = {ex.Message._OneLine()}";

                            ex._Print();

                            totalAllFilesReadSize += fileSizeEstimated;
                        }

                        $"File Name: '{fileName}'"._Print();
                        $"File Read Size (Actual): {fileTotalReadSize.Value._ToString3()}"._Print();
                        $"Total All Files Read Size (Actual): {totalAllFilesReadSize._ToString3()}"._Print();
                        $"Hash: {hashStr}"._Print();
                        $"--- FINISHED ---"._Print();

                        lock (resultList)
                        {
                            resultList.Add(targetFilePath, hashStr);
                        }

                        ""._Print();

                        PrintCurrentResultList(false);
                    }
                }
            }
            finally
            {
                printIntervalCancel.Cancel();
                await printInterval._TryWaitAsync();
            }
        });

        return 0;
    }

    // HDD のテストのため、巨大なテストファイルをディスク容量が枯渇するか指定されたサイズに達するまで連番で書き込みする。
    [ConsoleCommand(
        "WriteDiskTestFiles command",
        "WriteDiskTestFiles [dirPath] [/SIZE:single_file_size=1000000000000 (1TB)] [/TOTALSIZE:total_file_size]",
        "WriteDiskTestFiles command")]
    static int WriteDiskTestFiles(ConsoleService c, string cmdName, string str)
    {
        const int blockSize = 1000 * 1000;
        const int randSeedSize = blockSize * 16;
        const int sizePerFlush = 500 * 1000 * 1000;

        ConsoleParam[] args =
        {
            new ConsoleParam("[dirPath]", ConsoleService.Prompt, "Dir Path: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("SIZE"),
            new ConsoleParam("TOTALSIZE"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dirPath = vl.DefaultParam.StrValue;
        long singleFileSize = vl["SIZE"].StrValue._ToLong();
        if (singleFileSize <= 0) singleFileSize = 1_000_000_000_000;
        singleFileSize = (singleFileSize / (long)blockSize) * (long)blockSize;

        long totalAllFileSize = vl["TOTALSIZE"].StrValue._ToLong();
        if (totalAllFileSize <= 0) totalAllFileSize = long.MaxValue;
        totalAllFileSize = (totalAllFileSize / singleFileSize) * singleFileSize;

        Async(async () =>
        {
            SeedBasedRandomGenerator gen = new SeedBasedRandomGenerator("Hello");

            ReadOnlyMemory<byte> randSeed = gen.GetBytes(randSeedSize);

            ReadOnlyMemory<byte> baseRandData = (new SeedBasedRandomGenerator("World")).GetBytes(blockSize);

            await Lfs.CreateDirectoryAsync(dirPath);

            long currentTotalWriteSize = 0;

            using (ProgressReporterBase reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true,
                options: ProgressReporterOptions.EnableThroughput,
                reportTimingSetting: new ProgressReportTimingSetting(false, 1000)
                ), null))
            {
                for (int index = 0; ; index++)
                {
                    if (currentTotalWriteSize >= totalAllFileSize)
                    {
                        break;
                    }

                    string filePath = dirPath._CombinePath("test_" + index.ToString("D4") + ".dat");
                    long targetBlockCount = singleFileSize / blockSize;

                    $"--- BEGIN: {filePath} ---"._Print();
                    $"Target Size: {singleFileSize._ToString3()} bytes"._Print();
                    $"Target Blocks: {targetBlockCount._ToString3()} blocks"._Print();

                    await using var file = await Lfs.OpenOrCreateAppendAsync(filePath, flags: FileFlags.AutoCreateDirectory);

                    long existingFileSize = await file.GetFileSizeAsync();
                    existingFileSize = (existingFileSize / (long)blockSize) * (long)blockSize;

                    long currentBlockCount = existingFileSize / blockSize;

                    ""._Print();
                    $"Current File Size: {existingFileSize._ToString3()} bytes"._Print();
                    $"Current File Blocks: {currentBlockCount._ToString3()} blocks"._Print();

                    currentTotalWriteSize += existingFileSize;

                    if (existingFileSize >= singleFileSize)
                    {
                        $"Skipping: currentSize ({existingFileSize._ToString3()}) >= targetSize ({singleFileSize._ToString3()})"._Print();

                        continue;
                    }

                    await file.SetFileSizeAsync(existingFileSize);
                    await file.SeekAsync(existingFileSize, SeekOrigin.Begin);

                    long sizeToWrite = singleFileSize - existingFileSize;
                    long blockToWrite = sizeToWrite / blockSize;

                    ""._Print();
                    $"Size to Write: {sizeToWrite._ToString3()} bytes"._Print();
                    $"Blocks to Write: {blockToWrite._ToString3()} blocks"._Print();

                    ""._Print();

                    Memory<byte> tmp = new byte[blockSize];

                    long currentNotYetFlushedSize = 0;

                    for (long currentBlockIndex = currentBlockCount; currentBlockIndex < targetBlockCount; currentBlockIndex++)
                    {
                        int randIndex = (new SeedBasedRandomGenerator(currentBlockIndex.ToString())).GetSInt31() % (randSeedSize - blockSize);

                        tmp.Span._Xor(baseRandData.Span, randSeed.Span.Slice(randIndex, blockSize));

                        await file.WriteAsync(tmp);

                        currentNotYetFlushedSize += blockSize;

                        if (currentNotYetFlushedSize >= sizePerFlush)
                        {
                            currentNotYetFlushedSize = 0;
                            await file.FlushAsync();
                        }

                        currentTotalWriteSize += blockSize;

                        reporter.ReportProgress(new ProgressData(currentTotalWriteSize, totalAllFileSize, additionalInfo: filePath._GetFileName()._NonNullTrim()));
                    }

                    $"--- FINISHED: {filePath} ---"._Print();
                    ""._Print();
                }

                reporter.ReportProgress(new ProgressData(currentTotalWriteSize, totalAllFileSize, additionalInfo: "ALL"));
            }
        });

        return 0;
    }

    // ランダムに見える内容をファイルに書き込む。しかし、実際にはランダムではなく、同一の乱数内容である。
    // ファイルシステムの正常性を確認するために便利である。
    [ConsoleCommand(
        "WriteRandomFile command",
        "WriteRandomFile [fileName] [/size:length]",
        "WriteRandomFile command")]
    static int WriteRandomFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[fileName]", ConsoleService.Prompt, "File name: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("size", ConsoleService.Prompt, "Size (0 to infinite): ", ConsoleService.EvalNotEmpty, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string filePath = vl.DefaultParam.StrValue;
        long targetSize = vl["size"].StrValue._ToLong();

        Async(async () =>
        {
            int blockSize = 1000 * 1000;
            int randSeedSize = blockSize * 16;
            int sizePerFlush = 500 * 1000 * 1000;

            if (targetSize <= 0)
            {
                targetSize = long.MaxValue;
            }

            targetSize = (targetSize / (long)blockSize) * (long)blockSize;

            long targetBlockCount = targetSize / blockSize;

            ""._Print();
            $"Target Size: {targetSize._ToString3()} bytes"._Print();
            $"Target Blocks: {targetBlockCount._ToString3()} blocks"._Print();

            SeedBasedRandomGenerator gen = new SeedBasedRandomGenerator("Hello");

            ReadOnlyMemory<byte> randSeed = gen.GetBytes(randSeedSize);

            ReadOnlyMemory<byte> baseRandData = (new SeedBasedRandomGenerator("World")).GetBytes(blockSize);

            await using var file = await Lfs.OpenOrCreateAppendAsync(filePath, flags: FileFlags.AutoCreateDirectory);

            long currentSize = await file.GetFileSizeAsync();
            currentSize = (currentSize / (long)blockSize) * (long)blockSize;
            await file.SetFileSizeAsync(currentSize);

            long currentBlockCount = currentSize / blockSize;

            ""._Print();
            $"Current File Size: {currentSize._ToString3()} bytes"._Print();
            $"Current File Blocks: {currentBlockCount._ToString3()} blocks"._Print();

            await file.SeekAsync(currentSize, SeekOrigin.Begin);

            if (currentSize >= targetSize)
            {
                $"currentSize ({currentSize._ToString3()}) >= targetSize ({targetSize._ToString3()})"._Print();
                return;
            }

            long sizeToWrite = targetSize - currentSize;
            long blockToWrite = sizeToWrite / blockSize;

            ""._Print();
            $"Size to Write: {sizeToWrite._ToString3()} bytes"._Print();
            $"Blocks to Write: {blockToWrite._ToString3()} blocks"._Print();

            ""._Print();

            using (ProgressReporterBase reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true,
                options: ProgressReporterOptions.EnableThroughput,
                reportTimingSetting: new ProgressReportTimingSetting(false, 1000)
                ), null))
            {
                Memory<byte> tmp = new byte[blockSize];

                long currentNotYetFlushedSize = 0;

                for (long currentBlockIndex = currentBlockCount; currentBlockIndex < targetBlockCount; currentBlockIndex++)
                {
                    int randIndex = (new SeedBasedRandomGenerator(currentBlockIndex.ToString())).GetSInt31() % (randSeedSize - blockSize);

                    tmp.Span._Xor(baseRandData.Span, randSeed.Span.Slice(randIndex, blockSize));

                    await file.WriteAsync(tmp);

                    currentNotYetFlushedSize += blockSize;

                    if (currentNotYetFlushedSize >= sizePerFlush)
                    {
                        currentNotYetFlushedSize = 0;
                        await file.FlushAsync();
                    }

                    reporter.ReportProgress(new ProgressData((currentBlockIndex - currentBlockCount) * blockSize, sizeToWrite));
                }
            }
        });

        return 0;
    }


    [ConsoleCommand(
        "Sha1Sum command",
        "Sha1Sum [fileName] [/START:start_offset] [/SIZE:size]",
        "Sha1Sum command")]
    static int Sha1Sum(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[fileName]", ConsoleService.Prompt, "File name: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("START"),
            new ConsoleParam("SIZE"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string fileName = vl.DefaultParam.StrValue;
        long startOffset = vl["START"].StrValue._ToLong();
        startOffset = Math.Max(startOffset, 0);
        long sizeToRead = vl["SIZE"].StrValue._ToLong();
        sizeToRead = Math.Max(sizeToRead, 0);
        if (sizeToRead == 0)
        {
            sizeToRead = long.MaxValue;
        }

        using (ProgressReporterBase reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput,
            reportTimingSetting: new ProgressReportTimingSetting(false, 1000)
            ), null))
        {
            Async((Func<Task>)(async () =>
            {
                using SHA1 sha = SHA1.Create();

                int bufSize = 8 * 1024 * 1024;
                await using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufSize, false);
                long fileSize = fs.Length;

                startOffset = Math.Min(startOffset, fileSize);
                sizeToRead = Math.Max(fileSize - startOffset, 0);

                fs.Seek(startOffset, SeekOrigin.Begin);

                RefLong totalReadSize = new RefLong();

                $"File Name: '{fileName}'"._Print();
                $"File Size: {fileSize._ToString3()}"._Print();
                $"Read Offset: {startOffset._ToString3()}"._Print();
                $"Total Read Size (Estimated): {sizeToRead._ToString3()}"._Print();

                var hash = await Secure.CalcStreamHashAsync(fs, sha, bufferSize: bufSize, totalReadSize: totalReadSize,
                    progressReporter: reporter,
                    progressReporterTotalSizeHint: sizeToRead);

                $"File Name: '{fileName}'"._Print();
                $"Total Read Size (Actual): {totalReadSize.Value._ToString3()}"._Print();
                $"Hash: {hash._GetHexString().ToLowerInvariant()}"._Print();

                ""._Print();
            }));
        }

        return 0;
    }

    // 指定されたサブディレクトリにあるすべてのファイルを読む
    [ConsoleCommand(
        "ReadAllFiles command",
        "ReadAllFiles [dirName]",
        "ReadAllFiles command")]
    static int ReadAllFiles(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[dirName]", ConsoleService.Prompt, "Directory name: ", ConsoleService.EvalNotEmpty, null),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dirName = vl.DefaultParam.StrValue;

        using (ProgressReporterBase reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: false,
            options: ProgressReporterOptions.EnableThroughput,
            reportTimingSetting: new ProgressReportTimingSetting(false, 1000)
            ), null))
        {
            ReadAllFilesCtx ctx = new ReadAllFilesCtx();

            ctx.DoMainAsync(reporter, dirName)._GetResult();
        }

        return 0;
    }

    class ReadAllFilesCtx
    {
        public long TotalReadSize = 0;
        public long TotalReadNum = 0;
        public long TotalErrorNum = 0;

        readonly Memory<byte> TmpBuffer = new byte[4 * 1024 * 1024];

        public async Task DoMainAsync(ProgressReporterBase r, string dirName)
        {
            await ProcessDirectoryAsync(r, dirName);

            r.ReportProgress(new ProgressData(TotalReadSize));

            Con.WriteLine($"Finished!  TotalReadSize = {TotalReadSize._ToString3()}, TotalReadNum = {TotalReadNum._ToString3()}, TotalErrorNum = {TotalErrorNum._ToString3()}");
        }

        async Task ProcessDirectoryAsync(ProgressReporterBase r, string dirName)
        {
            var entities = await Lfs.EnumDirectoryAsync(dirName, false, EnumDirectoryFlags.None);

            foreach (var file in entities.Where(x => x.IsFile && x.IsSymbolicLink == false))
            {
                TotalReadNum++;

                try
                {
                    //Con.WriteLine($"File '{file.FullPath}'");

                    using (var f = Lfs.Open(file.FullPath))
                    {
                        long fileSize = await f.GetFileSizeAsync();
                        string info = $"File #{TotalReadNum._ToString3()}: '{file.FullPath}' ({fileSize._GetFileSizeStr()})";

                        while (true)
                        {
                            int readSize = await f.ReadAsync(this.TmpBuffer);
                            if (readSize == 0) break;

                            TotalReadSize += readSize;

                            r.ReportProgress(new ProgressData(TotalReadSize, additionalInfo: info));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Con.WriteError($"Reading the file '{file.FullPath}' error. {ex.Message}");
                    TotalErrorNum++;
                }
            }

            foreach (var dir in entities.Where(x => x.IsDirectory && x.Attributes.Bit(FileAttributes.ReparsePoint) == false && x.IsSymbolicLink == false))
            {
                try
                {
                    //Con.WriteLine($"Directory '{dir.FullPath}'");

                    await ProcessDirectoryAsync(r, dir.FullPath);
                }
                catch (Exception ex)
                {
                    Con.WriteError($"Processing the directory '{dir.FullPath}' error. {ex.Message}");
                }
            }
        }
    }

    // Physical Disk Seek Test
    [ConsoleCommand(
        "RawDiskSeekTest command",
        "RawDiskSeekTest [diskName]",
        "RawDiskSeekTest command")]
    static int RawDiskSeekTest(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[diskName]", ConsoleService.Prompt, "Physical disk name: ", ConsoleService.EvalNotEmpty, null),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string diskName = vl.DefaultParam.StrValue;

        using var rawFs = new LocalRawDiskFileSystem();
        using var disk = rawFs.Open($"/{diskName}");

        int size = 4096;
        long diskSize = disk.Size;
        Memory<byte> tmp = new byte[size];

        int numSeek = 0;

        long startTick = Time.HighResTick64;

        long last = 0;

        while (true)
        {
            long pos = (Util.RandSInt63() % (diskSize - (long)size)) / 4096L * 4096L;

            disk.ReadRandom(pos, tmp);
            numSeek++;

            if ((numSeek % 10) == 0)
            {
                long now = Time.HighResTick64;

                if (now > startTick)
                {
                    if (last == 0 || (last + 1000) <= now)
                    {
                        last = now;

                        double secs = (double)(now - startTick) / 1000.0;

                        double averageSeekTime = secs / (double)numSeek;

                        Con.WriteLine(averageSeekTime.ToString("F6"));

                        if (now >= (startTick + (10 * 1000)))
                        {
                            break;
                        }
                    }
                }
            }
        }

        Con.WriteLine();

        return 0;
    }

    // Backup Physical Disk
    [ConsoleCommand(
        "RawDiskBackup command",
        "RawDiskBackup [diskName] /dst:filename [/truncate:size]",
        "RawDiskBackup command")]
    static int RawDiskBackup(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[diskName]", ConsoleService.Prompt, "Physical disk name: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dst", ConsoleService.Prompt, "Destination file name: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("truncate"),
                new ConsoleParam("gzip"),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string diskName = vl.DefaultParam.StrValue;
        string dstFileName = vl["dst"].StrValue;
        long truncate = vl["truncate"].StrValue._ToLong();
        bool gzip = vl["gzip"].BoolValue;
        if (truncate <= 0)
        {
            truncate = -1;
        }
        else
        {
            truncate = (truncate + 4095L) / 4096L * 4096L;
        }

        if (gzip == false)
        {
            // Plain
            using (var rawFs = new LocalRawDiskFileSystem())
            {
                using (var disk = rawFs.Open($"/{diskName}"))
                {
                    using (var file = Lfs.Create(dstFileName, flags: FileFlags.AutoCreateDirectory))
                    {
                        using (var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput), null))
                        {
                            FileUtil.CopyBetweenFileBaseAsync(disk, file, truncateSize: truncate, param: new CopyFileParams(asyncCopy: true, bufferSize: 16 * 1024 * 1024, ensureBufferSize: true), reporter: reporter)._GetResult();
                        }
                    }
                }
            }
        }
        else
        {
            // Gzip
            using (var rawFs = new LocalRawDiskFileSystem())
            {
                using (var disk = rawFs.Open($"/{diskName}"))
                {
                    using var diskStream = disk.GetStream(true);

                    using (var file = Lfs.Create(dstFileName, flags: FileFlags.AutoCreateDirectory))
                    {
                        using var fileStream = file.GetStream(true);
                        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest, false);

                        using (var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput), null))
                        {
                            FileUtil.CopyBetweenStreamAsync(diskStream, gzipStream, truncateSize: truncate, param: new CopyFileParams(asyncCopy: true, bufferSize: 16 * 1024 * 1024, ensureBufferSize: true), reporter: reporter)._GetResult();

                            gzipStream.Flush();
                            fileStream.Flush();

                            gzipStream.Close();
                        }
                    }
                }
            }
        }

        return 0;
    }

    // Restore Physical Disk
    [ConsoleCommand(
        "RawDiskRestore command",
        "RawDiskRestore [diskName] /src:filename [/truncate:size]",
        "RawDiskRestore command")]
    static int RawDiskRestore(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[diskName]", ConsoleService.Prompt, "Physical disk name: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("src", ConsoleService.Prompt, "Source file name: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("truncate"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string diskName = vl.DefaultParam.StrValue;
        string dstFileName = vl["src"].StrValue;
        long truncate = vl["truncate"].StrValue._ToLong();
        if (truncate <= 0)
        {
            truncate = -1;
        }
        else
        {
            truncate = (truncate + 4095L) / 4096L * 4096L;
        }

        using (var rawFs = new LocalRawDiskFileSystem())
        {
            using (var disk = rawFs.Open($"/{diskName}", writeMode: true))
            {
                bool isGZip = false;

                Con.WriteLine("Determining the source file format...");

                using (var file = Lfs.Open(dstFileName))
                {
                    using var fileStream = file.GetStream(true);

                    isGZip = IPA.Cores.Basic.Legacy.GZipUtil.IsGZipStreamAsync(fileStream)._GetResult();
                }

                Con.WriteLine($"isGZip = {isGZip._ToBoolStrLower()}");

                if (isGZip == false)
                {
                    // Plain
                    using (var file = Lfs.Open(dstFileName))
                    {
                        using (var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput), null))
                        {
                            FileUtil.CopyBetweenFileBaseAsync(file, disk, truncateSize: truncate, param: new CopyFileParams(asyncCopy: true, bufferSize: 16 * 1024 * 1024, ensureBufferSize: true), reporter: reporter)._GetResult();

                            disk.Flush();
                        }
                    }
                }
                else
                {
                    // GZip
                    using (var file = Lfs.Open(dstFileName))
                    {
                        using var fileStream = file.GetStream(true);
                        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, false);

                        using var diskStream = disk.GetStream(true);

                        using (var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput), null))
                        {
                            FileUtil.CopyBetweenStreamAsync(gzipStream, diskStream, truncateSize: truncate, param: new CopyFileParams(asyncCopy: true, bufferSize: 16 * 1024 * 1024, ensureBufferSize: true), reporter: reporter)._GetResult();

                            diskStream.Flush();
                            disk.Flush();
                        }
                    }
                }
            }
        }

        return 0;
    }

    // Zero Clear Physical Disk
    [ConsoleCommand(
        "RawDiskZeroClear command",
        "RawDiskZeroClear [diskName] [/size:size]",
        "RawDiskZeroClear command")]
    static int RawDiskZeroClear(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[diskName]", ConsoleService.Prompt, "Physical disk name: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("size"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string diskName = vl.DefaultParam.StrValue;
        long size = vl["size"].StrValue._ToLong();
        if (size <= 0)
        {
            size = -1;
        }
        else
        {
            size = (size + 4095L) / 4096L * 4096L;
        }

        using (var rawFs = new LocalRawDiskFileSystem())
        {
            using (var disk = rawFs.Open($"/{diskName}", writeMode: true))
            {
                long diskSize = disk.GetFileSize();

                if (size < 0) size = diskSize;

                size = Math.Min(size, diskSize);

                // Plain
                using (var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.ConsoleAndDebug, toStr3: true, showEta: true, options: ProgressReporterOptions.EnableThroughput), null))
                {
                    FileUtil.EraseFileBaseAsync(disk, totalSize: size, param: new CopyFileParams(asyncCopy: true, bufferSize: 16 * 1024 * 1024, ensureBufferSize: true), reporter: reporter)._GetResult();

                    disk.Flush();
                }
            }
        }

        return 0;
    }

    // Enum Physical Disks
    [ConsoleCommand(
        "RawDiskEnum command",
        "RawDiskEnum",
        "RawDiskEnum command")]
    static void RawDiskEnum()
    {
        using (var rawFs = new LocalRawDiskFileSystem())
        {
            Con.WriteLine();

            var items = rawFs.EnumDirectory("/");

            foreach (var item in items.Where(x => x.IsFile))
            {
                Con.WriteLine($"{item.Name}    -  {item.Size._ToString3()} bytes");
            }

            Con.WriteLine();
        }
    }

    // 指定されたディレクトリを DirSuperBackup を用いてバックアップする
    // 指定されたディレクトリやサブディレクトリを列挙し結果をファイルに書き出す
    [ConsoleCommand(
        "DirSuperBackup command",
        "DirSuperBackup [src] /dst:dst /options:options1,options1,... [/errorlog:errorlog] [/infolog:infolog] [/password:password] [/numthreads:num] [/ignoredirs:dir1,dir2,...]",
        "DirSuperBackup command")]
    static int DirSuperBackup(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[src]", ConsoleService.Prompt, "Source directory path: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dst", ConsoleService.Prompt, "Destination directory path: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("options", ConsoleService.Prompt, $"Options ({DirSuperBackupFlags.Default._GetDefinedEnumElementsStrList().Where(x=>!x._StartWithi("Restore"))._Combine(",")}) ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("errorlog"),
                new ConsoleParam("infolog"),
                new ConsoleParam("password"),
                new ConsoleParam("numthreads"),
                new ConsoleParam("ignoredirs"),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string src = vl.DefaultParam.StrValue;
        string dst = vl["dst"].StrValue;
        string errorlog = vl["errorlog"].StrValue;
        string infolog = vl["infolog"].StrValue;
        string password = vl["password"].StrValue;
        string ignoredirs = vl["ignoredirs"].StrValue;
        string options = vl["options"].StrValue;
        string numthreads = vl["numthreads"].StrValue;

        bool err = false;

        try
        {
            Lfs.EnableBackupPrivilege();
        }
        catch (Exception ex)
        {
            Con.WriteError(ex);
        }

        var optionsValues = options._ParseEnumBits(DirSuperBackupFlags.Default, ',', '|', ' ');

        using (var b = new DirSuperBackup(new DirSuperBackupOptions(Lfs, infolog, errorlog, flags: optionsValues, encryptPassword: password, numThreads: numthreads._ToInt())))
        {
            Async(async () =>
            {
                await b.DoSingleDirBackupAsync(src, dst, default, ignoredirs);
            });

            if (b.Stat.Error_Dir != 0 || b.Stat.Error_NumFiles != 0)
            {
                err = true;
            }
        }

        if (err)
        {
            Con.WriteError("Error occured.");
        }
        else
        {
            Con.WriteError("All OK!");
        }

        return err ? 1 : 0;
    }

    // 指定されたディレクトリを DirSuperRestore を用いてリストアする
    // 指定されたディレクトリやサブディレクトリを列挙し結果をファイルに書き出す
    [ConsoleCommand(
        "DirSuperRestore command",
        "DirSuperRestore [src] /dst:dst /options:options1,options1,... [/errorlog:errorlog] [/infolog:infolog] [/password:password] [/numthreads:num] [/ignoredirs:dir1,dir2,...]",
        "DirSuperRestore command")]
    static int DirSuperRestore(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[src]", ConsoleService.Prompt, "Source directory path: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dst", ConsoleService.Prompt, "Destination directory path: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("options", ConsoleService.Prompt, $"Options ({DirSuperBackupFlags.Default._GetDefinedEnumElementsStrList().Where(x=>!x._StartWithi("Backup"))._Combine(",")}) ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("errorlog"),
                new ConsoleParam("infolog"),
                new ConsoleParam("password"),
                new ConsoleParam("numthreads"),
                new ConsoleParam("ignoredirs"),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string src = vl.DefaultParam.StrValue;
        string dst = vl["dst"].StrValue;
        string errorlog = vl["errorlog"].StrValue;
        string infolog = vl["infolog"].StrValue;
        string ignoredirs = vl["ignoredirs"].StrValue;
        string password = vl["password"].StrValue;
        string options = vl["options"].StrValue;
        string numthreads = vl["numthreads"].StrValue;

        bool err = false;

        try
        {
            Lfs.EnableBackupPrivilege();
        }
        catch (Exception ex)
        {
            Con.WriteError(ex);
        }

        var optionsValues = options._ParseEnumBits(DirSuperBackupFlags.Default, ',', '|', ' ');

        using (var b = new DirSuperBackup(new DirSuperBackupOptions(Lfs, infolog, errorlog, optionsValues, encryptPassword: password, numThreads: numthreads._ToInt())))
        {
            Async(async () =>
            {
                await b.DoSingleDirRestoreAsync(src, dst, default, ignoredirs);
            });

            if (b.Stat.Error_Dir != 0 || b.Stat.Error_NumFiles != 0)
            {
                err = true;
            }
        }

        if (err)
        {
            Con.WriteError("Error occured.");
        }
        else
        {
            Con.WriteError("All OK!");
        }

        return err ? 1 : 0;
    }

    // 指定されたディレクトリやサブディレクトリを列挙し結果をファイルに書き出す
    [ConsoleCommand(
        "EnumDir command",
        "EnumDir [dir] /out:dest",
        "EnumDir command")]
    static int EnumDir(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[dir]", ConsoleService.Prompt, "Target directory path: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("out", ConsoleService.Prompt, "Destination filename: ", ConsoleService.EvalNotEmpty, null),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dir = vl.DefaultParam.StrValue;
        string dest = vl["out"].StrValue;

        using var outFile = Lfs.Create(dest, flags: FileFlags.AutoCreateDirectory);
        using var outStream = outFile.GetStream(false);
        using var w = new StreamWriter(outStream);

        DirectoryWalker walker = new DirectoryWalker(Lfs, EnumDirectoryFlags.NoGetPhysicalSize);

        walker.WalkDirectory(rootDirectory: dir,
            callback: (pathinfo, entry, cancel) =>
            {
                w.WriteLine(PP.AppendDirectorySeparatorTail(pathinfo.FullPath));
                entry.Where(x => x.IsFile).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer)._DoForEach(file => w.WriteLine(file.FullPath));
                return true;
            },
            exceptionHandler: (pathinfo, exp, cancel) =>
            {
                w.WriteLine($"*** Error: \"{pathinfo.FullPath}\": {exp.Message}");
                return true;
            });

        w.Flush();
        outStream.Flush();

        return 0;
    }

    [ConsoleCommand(
        "CopyErrorFile command",
        "CopyErrorFile src /dest:dest",
        "CopyErrorFile command")]
    static int CopyErrorFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[src]", ConsoleService.Prompt, "Source filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("dest", ConsoleService.Prompt, "Destination filename: ", ConsoleService.EvalNotEmpty, null),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        RefBool ignoredError = new RefBool(false);

        Lfs.CopyFile(vl.DefaultParam.StrValue, vl["dest"].StrValue,
            new CopyFileParams(overwrite: true, flags: FileFlags.AutoCreateDirectory, ignoreReadError: true,
            reporterFactory: new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.ConsoleAndDebug, options: ProgressReporterOptions.EnableThroughput)),
            readErrorIgnored: ignoredError);

        if (ignoredError)
        {
            Con.WriteError("*** Read errors are ignored. ***");
        }

        return 0;
    }

    [ConsoleCommand(
        "RamFile command",
        "RamFile [arg]",
        "RamFile test")]
    static int RamFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string src1 = @"C:\git\IPA-DNP-LabUtil";
        //string dst1 = @"D:\tmp\190428\test2\";
        string dst1 = @"D:\TMP\190428\test2\LabUtil.NET\LabUtil.Basic\Base";
        string dst2 = "/test1/";
        string dst3 = "/test2/";
        string dst4 = @"D:\tmp\190428\test3\";
        for (int i = 0; i < 1; i++)
        {
            using (var ramfs = new VirtualFileSystem(new VirtualFileSystemParams()))
            {
                //Lfs.CopyDir(src1, dst1);

                Lfs.CopyDir(dst1, dst2, ramfs);

                ramfs.CopyDir(dst2, dst3);

                ramfs.CopyDir(dst3, dst4, Lfs);
            }
        }

        return 0;
    }

    [ConsoleCommand(
        "CopyFile command",
        "CopyFile [arg]",
        "CopyFile test")]
    static int CopyFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);


        try
        {
            Lfs.EnableBackupPrivilege();
        }
        catch (Exception ex)
        {
            Con.WriteError(ex);
        }

        if (false)
        {
            var copyParam = new CopyDirectoryParams(copyDirFlags: CopyDirectoryFlags.Default,// | CopyDirectoryFlags.BackupMode,
                copyFileFlags: FileFlags.SparseFile,
            //progressCallback: (x, y) => { return Task.FromResult(true); },
            dirMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default),
            fileMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default)
            );

            var ret1 = FileUtil.CopyDirAsync(Lfs, @"D:\TMP\copy_test2\c1", Lfs, @"D:\TMP\copy_test2\c2", copyParam, null, null)._GetResult();

            return 0;
        }

        if (true)
        {
            //AppConfig.LargeFileSystemSettings.LocalLargeFileSystemParams.Set(new LargeFileSystemParams(10_000));

            string srcDir1 = @"C:\git\IPA-DN-Cores\Cores.NET\DepTest";
            string dstDir1 = @"d:\tmp\copy_test2\01";
            string dstDir2 = @"d:\tmp\copy_test2\02";
            string dstDir3 = @"d:\tmp\copy_test2\03";

            if (true)
            {
                try
                {
                    Lfs.DeleteDirectory(dstDir1, true);
                }
                catch { }

                var copyParam = new CopyDirectoryParams(copyDirFlags: CopyDirectoryFlags.Default,// | CopyDirectoryFlags.BackupMode,
                    copyFileFlags: FileFlags.SparseFile,                  //progressCallback: (x, y) => { return Task.FromResult(true); },
                    dirMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default),
                    fileMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default)
                    );

                var ret1 = FileUtil.CopyDirAsync(Lfs, srcDir1, Lfs, dstDir1, copyParam, null, null)._GetResult();

                Con.WriteLine("Copy Test Completed.");
                ret1._PrintAsJson();
            }

            if (true)
            {
                try
                {
                    Lfs.DeleteDirectory(dstDir2, true);
                }
                catch { }

                var copyParam = new CopyDirectoryParams(copyDirFlags: CopyDirectoryFlags.Default,// | CopyDirectoryFlags.BackupMode,
                                                                                                 //progressCallback: (x, y) => { return Task.FromResult(true); },
                    dirMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default),
                    fileMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default)
                    );

                var ret1 = FileUtil.CopyDirAsync(Lfs, dstDir1, LLfsUtf8, dstDir2, copyParam, null, null)._GetResult();

                Con.WriteLine("Copy Test Completed.");
                ret1._PrintAsJson();
            }

            if (true)
            {
                try
                {
                    Lfs.DeleteDirectory(dstDir3, true);
                }
                catch { }

                var copyParam = new CopyDirectoryParams(copyDirFlags: CopyDirectoryFlags.Default,// | CopyDirectoryFlags.BackupMode,
                    copyFileFlags: FileFlags.SparseFile,                                  //progressCallback: (x, y) => { return Task.FromResult(true); },
                    dirMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default),
                    fileMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.Default)
                    );

                var ret1 = FileUtil.CopyDirAsync(LLfsUtf8, dstDir2, Lfs, dstDir3, copyParam, null, null)._GetResult();

                Con.WriteLine("Copy Test Completed.");
                ret1._PrintAsJson();
            }

            return 0;
        }

        if (false)
        {
            string srcDir1 = @"C:\git\IPA-DN-Cores\Cores.NET";

            string dstDir1 = @"D:\tmp\copy_test\dst2\a";

            Lfs.GetDirectoryMetadata(srcDir1)._PrintAsJson();
            Lfs.GetDirectoryMetadata(dstDir1)._PrintAsJson();

            return 0;
        }

        if (false)
        {
            string srcDir1 = @"C:\git\IPA-DN-Cores\Cores.NET";

            string dstDir1 = @"d:\tmp\copy_test\acld1";

            Lfs.CreateDirectory(dstDir1);
            Lfs.SetDirectoryMetadata(dstDir1, Lfs.GetDirectoryMetadata(srcDir1));

            return 0;
        }

        if (true)
        {
            string srcDir1 = @"C:\tmp\acl_test2";

            string dstDir1 = @"d:\tmp\copy_test\dst27";

            var copyParam = new CopyDirectoryParams(copyDirFlags: CopyDirectoryFlags.Default | CopyDirectoryFlags.BackupMode,
                dirMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.All),
                fileMetadataCopier: new FileMetadataCopier(FileMetadataCopyMode.All)
                );

            var ret1 = FileUtil.CopyDirAsync(Lfs, srcDir1, Lfs, dstDir1, copyParam, null, null)._GetResult();

            Con.WriteLine("Copy Test Completed.");
            ret1._PrintAsJson();

            return 0;
        }

        return 0;
    }

    [ConsoleCommand(
        "LargeFile command",
        "LargeFile [arg]",
        "LargeFile test")]
    static int LargeFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dirPath = @"d:\tmp\large_file_test";

        if (true)
        {
            CoresConfig.LocalLargeFileSystemSettings.MaxSingleFileSize.Set(100);

            var fileSystem = LLfsUtf8;

            try
            {
                fileSystem.DeleteDirectory(dirPath, true);
            }
            catch
            {
            }

            fileSystem.CreateDirectory(dirPath);

            for (int j = 0; j < 2; j++)
            {
                string filePath = LLfsUtf8.PathParser.Combine(dirPath, $"test{j:D2}.txt");

                for (int i = 0; i < 10; i++)
                {
                    string hello = $"Hello World {i:D10}\r\n"; // 24 bytes

                    fileSystem.AppendDataToFile(filePath, hello._GetBytes_Ascii());
                }

                using (var file = fileSystem.Open(filePath, writeMode: true))
                {
                    file.WriteRandom(231, "<12345678>"._GetBytes_Ascii());
                }
            }

            var dirent = fileSystem.EnumDirectory(dirPath);
            dirent._PrintAsJson();

            var meta = fileSystem.GetFileMetadata(dirent.Where(x => x.IsDirectory == false).First().FullPath);
            meta._PrintAsJson();

            fileSystem.CopyFile(@"C:\TMP\large_file_test\test00.txt", @"C:\TMP\large_file_test\plain.txt", destFileSystem: Lfs);

            dirent = fileSystem.EnumDirectory(dirPath);
            dirent._PrintAsJson();

            fileSystem.CopyFile(@"C:\TMP\large_file_test\plain.txt", @"C:\TMP\large_file_test\plain2.txt", destFileSystem: LfsUtf8);

            return 0;
        }


        if (false)
        {
            CoresConfig.LocalLargeFileSystemSettings.MaxSingleFileSize.Set(100);

            // 単純文字列
            string filePath = LLfs.PathParser.Combine(dirPath, @"test.txt");

            for (int i = 0; ; i++)
            {
                string hello = $"Hello World {i:D10}\r\n"; // 24 bytes

                LLfs.AppendDataToFile(filePath, hello._GetBytes_Ascii(), FileFlags.AutoCreateDirectory);
            }
            return 0;
        }

        if (false)
        {
            CoresConfig.LocalLargeFileSystemSettings.MaxSingleFileSize.Set(10_000_000);

            // スパースファイル
            string filePath = LLfs.PathParser.Combine(dirPath, @"test2.txt");
            var handle = LLfs.GetRandomAccessHandle(filePath, true);

            for (int i = 0; i < 100; i++)
            {
                string hello = $"Hello World {i:D10}\r\n"; // 24 bytes

                long position = Util.RandSInt63() % (LLfs.Params.MaxLogicalFileSize - 100);
                handle.WriteRandom(position, hello._GetBytes_Ascii());
            }
            return 0;
        }

        return 0;
    }

    static byte[] SparseFile_GenerateTestData(int size)
    {
        byte[] ret = new byte[size];
        for (int i = 0; i < size; i++)
            ret[i] = (byte)('A' + (i % 26));
        return ret;
    }

    [ConsoleCommand(
        "SparseFile command",
        "SparseFile [arg]",
        "SparseFile test")]
    static int SparseFile(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args = { };
        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        CoresConfig.LocalLargeFileSystemSettings.MaxSingleFileSize.Set(100_000);

        string normalFn = @"D:\TMP\sparse_file_test\normal_file.txt";
        string standardApi = @"D:\TMP\sparse_file_test\standard_api.txt";
        string sparseFn = @"D:\TMP\sparse_file_test\sparse_file.txt";
        string copySparse2Fn = @"D:\TMP\sparse_file_test\sparse_file_2.txt";
        string copySparse3Fn = @"D:\TMP\sparse_file_test\sparse_file_3.txt";

        string largeFn = @"D:\TMP\sparse_file_test\large\large.txt";

        //            string ramFn = @"D:\TMP\sparse_file_test\ram.txt";

        try
        {
            Lfs.EnableBackupPrivilege();
        }
        catch (Exception ex)
        {
            Con.WriteError(ex);
        }

        Lfs.CreateDirectory(@"D:\TMP\sparse_file_test\large\");

        int count = 0;
        while (true)
        {
            count++;

            Lfs.DeleteFile(normalFn);
            Lfs.DeleteFile(sparseFn);
            Lfs.DeleteFile(standardApi);
            LLfsUtf8.DeleteFile(largeFn);

            MemoryBuffer<byte>? ram = new MemoryBuffer<byte>();

            for (int i = 0; i < 3; i++)
            {
                FileFlags flags = FileFlags.AutoCreateDirectory;
                if ((Util.RandSInt31() % 8) == 0) flags |= FileFlags.Async;
                if (Util.RandBool()) flags |= FileFlags.BackupMode;

                using (var normal = Lfs.OpenOrCreate(normalFn, flags: flags))
                {
                    using (var sparse = Lfs.OpenOrCreate(sparseFn, flags: flags | FileFlags.SparseFile))
                    {
                        using (var api = new FileStream(standardApi, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, FileOptions.None))
                        {
                            using (var large = LLfsUtf8.OpenOrCreate(largeFn))
                            {
                                for (int k = 0; k < 3; k++)
                                {
                                    MemoryBuffer<byte> data = new MemoryBuffer<byte>();

                                    int numBlocks = Util.RandSInt31() % 32;

                                    for (int j = 0; j < numBlocks; j++)
                                    {
                                        if (j >= 1 || Util.RandBool())
                                            data.WriteZero(Util.RandSInt31() % 100_000);
                                        data.Write(SparseFile_GenerateTestData(Util.RandSInt31() % 10000));
                                    }

                                    data.Write("Hello World"._GetBytes_Ascii());

                                    if (Util.RandBool())
                                        data.WriteZero(Util.RandSInt31() % 10000);

                                    long pos = Util.RandSInt31() % 10_000_000;
                                    normal.WriteRandom(pos, data);
                                    sparse.WriteRandom(pos, data);
                                    large.WriteRandom(pos, data);

                                    api.Seek(pos, SeekOrigin.Begin);
                                    api.Write(data);

                                    ram.Seek((int)pos, SeekOrigin.Begin, true);
                                    var destSpan = ram.Walk(data.Length);
                                    Debug.Assert(destSpan.Length == data.Span.Length);
                                    data.Span.CopyTo(destSpan);
                                    //Con.WriteLine($"ram size = {ram.Length}, file size = {api.Length}");
                                }
                            }
                        }
                    }
                }

                //Lfs.WriteToFile(ramFn, ram.Memory);
                Lfs.CopyFile(normalFn, copySparse2Fn, new CopyFileParams(flags: flags | FileFlags.SparseFile, overwrite: true));
                Lfs.CopyFile(sparseFn, copySparse3Fn, new CopyFileParams(flags: flags | FileFlags.SparseFile, overwrite: true));
            }

            var largebytes = LLfsUtf8.ReadDataFromFile(largeFn);

            Lfs.WriteDataToFile(@"D:\TMP\sparse_file_test\large_copied.txt", largebytes);

            string hash0 = Secure.HashSHA1(Lfs.ReadDataFromFile(standardApi).Span.ToArray())._GetHexString();
            string hash1 = Secure.HashSHA1(Lfs.ReadDataFromFile(normalFn).Span.ToArray())._GetHexString();
            string hash2 = Secure.HashSHA1(Lfs.ReadDataFromFile(sparseFn).Span.ToArray())._GetHexString();
            string hash3 = Secure.HashSHA1(Lfs.ReadDataFromFile(copySparse2Fn).Span.ToArray())._GetHexString();
            string hash4 = Secure.HashSHA1(Lfs.ReadDataFromFile(copySparse3Fn).Span.ToArray())._GetHexString();
            string hash5 = Secure.HashSHA1(ram.Span.ToArray())._GetHexString();
            string hash6 = Secure.HashSHA1(largebytes.Span.ToArray())._GetHexString();

            if (hash0 != hash1 || hash1 != hash2 || hash1 != hash3 || hash1 != hash4 || hash1 != hash5 || hash1 != hash6)
            {
                Con.WriteLine("Error!!!\n");
                Con.WriteLine($"hash0 = {hash0}");
                Con.WriteLine($"hash1 = {hash1}");
                Con.WriteLine($"hash2 = {hash2}");
                Con.WriteLine($"hash3 = {hash3}");
                Con.WriteLine($"hash4 = {hash4}");
                Con.WriteLine($"hash5 = {hash5}");
                Con.WriteLine($"hash6 = {hash6}");
                return 0;
            }
            else
            {
                Con.WriteLine($"count = {count}");
            }

            ram = null;
        }


        return 0;
    }
}

