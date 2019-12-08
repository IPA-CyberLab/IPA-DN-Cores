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

#if CORES_BASIC_JSON

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
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    // バックアップした先のディレクトリに保存されるメタデータ
    public class DirSuperBackupMetadata
    {
        public DateTimeOffset TimeStamp;
        public FileMetadata DirMetadata = null!;
        public List<DirSuperBackupMetadataFile> FileList = null!;
    }
    public class DirSuperBackupMetadataFile
    {
        public string FileName = null!;
        public FileMetadata MetaData = null!;
    }

    public class DirSuperBackupOptions
    {
        public FileSystem Fs { get; }
        public string? AllLogFileName { get; }
        public string? ErrorLogFileName { get; }

        public DirSuperBackupOptions(FileSystem? fs = null, string? allLogFileName = null, string? errorLogFileName = null)
        {
            Fs = fs ?? Lfs;
            AllLogFileName = allLogFileName;
            ErrorLogFileName = errorLogFileName;
        }
    }

    [Flags]
    public enum DirSuperBackupLogType
    {
        Info = 1,
        Error = 2,
    }

    public class DirSuperBackupStat
    {
        public long Copy_NumFiles;
        public long Copy_TotalSize;

        public long Skip_NumFiles;
        public long Skip_TotalSize;

        public long Error_Dir;

        public long Error_NumFiles;
        public long Error_TotalSize;
    }

    // ディレクトリ単位の世代対応バックアップユーティリティ
    public class DirSuperBackup : AsyncService
    {
        public DirSuperBackupOptions Options { get; }
        public FileSystem Fs => Options.Fs;
        readonly FileSystem Lfs;
        readonly FileSystem LfsUtf8;

        readonly FileObject? AllLogFileObj;
        readonly FileStream? AllLogFileStream;
        readonly StreamWriter? AllLogWriter;

        readonly FileObject? ErrorLogFileObj;
        readonly FileStream? ErrorLogFileStream;
        readonly StreamWriter? ErrorLogWriter;

        public const string PrefixMetadata = ".super_metadata_";
        public const string SuffixMetadata = ".metadat.json";

        readonly DirSuperBackupStat Stat = new DirSuperBackupStat();

        public DirSuperBackup(DirSuperBackupOptions? options = null)
        {
            try
            {
                this.Options = options ?? new DirSuperBackupOptions();
                this.Lfs = new LargeFileSystem(new LargeFileSystemParams(this.Fs));
                this.LfsUtf8 = new Utf8BomFileSystem(new Utf8BomFileSystemParam(this.Lfs));

                if (Options.AllLogFileName._IsFilled())
                {
                    AllLogFileObj = this.LfsUtf8.OpenOrCreateAppend(Options.AllLogFileName, flags: FileFlags.AutoCreateDirectory | FileFlags.BackupMode | FileFlags.LargeFs_AppendWithoutCrossBorder);
                    AllLogFileStream = AllLogFileObj.GetStream(true);
                    AllLogWriter = new StreamWriter(AllLogFileStream);
                }

                if (Options.ErrorLogFileName._IsFilled())
                {
                    ErrorLogFileObj = this.LfsUtf8.OpenOrCreateAppend(Options.ErrorLogFileName, flags: FileFlags.AutoCreateDirectory | FileFlags.BackupMode | FileFlags.LargeFs_AppendWithoutCrossBorder);
                    ErrorLogFileStream = ErrorLogFileObj.GetStream(true);
                    ErrorLogWriter = new StreamWriter(ErrorLogFileStream);
                }

                WriteLogAsync(DirSuperBackupLogType.Error, "------------------")._GetResult();
                WriteLogAsync(DirSuperBackupLogType.Error, "Start")._GetResult();
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        protected override void DisposeImpl(Exception? ex)
        {
            try
            {
                WriteStatAsync()._TryGetResult();

                WriteLogAsync(DirSuperBackupLogType.Error, "Finish")._TryGetResult();

                AllLogWriter._DisposeSafe();
                AllLogFileStream._DisposeSafe();
                AllLogFileObj._DisposeSafe();

                ErrorLogWriter._DisposeSafe();
                ErrorLogFileStream._DisposeSafe();
                ErrorLogFileObj._DisposeSafe();

                this.LfsUtf8._DisposeSafe();
                this.Lfs._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }

        async Task WriteLogAsync(DirSuperBackupLogType type, string str)
        {
            string line = $"{DateTimeOffset.Now._ToDtStr()},{str}";

            if (type.Bit(DirSuperBackupLogType.Error))
            {
                await WriteLogMainAsync(this.ErrorLogWriter, line);
            }

            line = line._OneLine();

            Console.WriteLine(line);

            await WriteLogMainAsync(this.AllLogWriter, line);
        }

        async Task WriteLogMainAsync(StreamWriter? writer, string line)
        {
            if (writer == null) return;

            await writer.WriteLineAsync(line);
        }

        // Stat をログに書き込む
        public async Task WriteStatAsync()
        {
            await WriteLogAsync(DirSuperBackupLogType.Error, this.Stat._ObjectToJson(compact: true));
        }

        // 1 つのディレクトリをバックアップする
        public async Task DoSingleDirBackupAsync(string srcDir, string destDir, CancellationToken cancel = default)
        {
            DateTimeOffset now = DateTimeOffset.Now;

            FileSystemEntity[]? srcDirEnum = null;

            try
            {
                FileMetadata srcDirMetadata = await Fs.GetDirectoryMetadataAsync(srcDir, cancel: cancel);

                srcDirEnum = (await Fs.EnumDirectoryAsync(srcDir, false, EnumDirectoryFlags.NoGetPhysicalSize, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

                FileSystemEntity[] destDirEnum = new FileSystemEntity[0];

                DirSuperBackupMetadata? destDirOldMetaData = null;

                DirSuperBackupMetadata destDirNewMetaData;

                // 宛先ディレクトリがすでに存在しているかどうか検査する
                if (await Fs.IsDirectoryExistsAsync(destDir, cancel))
                {
                    destDirEnum = await Fs.EnumDirectoryAsync(destDir, false, EnumDirectoryFlags.NoGetPhysicalSize, cancel);

                    // 宛先ディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
                    destDirOldMetaData = await GetLatestMetaDataFileNameAsync(destDir, destDirEnum, cancel);
                }
                else
                {
                    // 宛先ディレクトリがまだ存在していない場合は作成する
                    await Fs.CreateDirectoryAsync(destDir, FileFlags.BackupMode | FileFlags.AutoCreateDirectory, cancel);
                }

                // 新しいメタデータを作成する
                if (destDirOldMetaData == null)
                {
                    destDirNewMetaData = new DirSuperBackupMetadata();
                    destDirNewMetaData.FileList = new List<DirSuperBackupMetadataFile>();
                }
                else
                {
                    destDirNewMetaData = destDirOldMetaData._CloneWithJson();
                }
                destDirNewMetaData.TimeStamp = now;
                destDirNewMetaData.DirMetadata = srcDirMetadata;

                // 元ディレクトリに存在するファイルを 1 つずつバックアップする
                foreach (FileSystemEntity srcFile in srcDirEnum.Where(x => x.IsFile))
                {
                    string destFilePath = Fs.PathParser.Combine(destDir, srcFile.Name);

                    try
                    {
                        FileMetadata srcMetadata = await Fs.GetFileMetadataAsync(srcFile.FullPath, cancel: cancel);

                        // このファイルと同一のファイル名がすでに宛先ディレクトリに物理的に存在するかどうか確認する
                        bool exists = await Fs.IsFileExistsAsync(destFilePath, cancel);

                        bool fileChangedOrNew = false;

                        if (exists)
                        {
                            // すでに宛先ディレクトリに存在する物理的なファイルのメタデータを取得する
                            FileMetadata destExistsMetadata = await Fs.GetFileMetadataAsync(destFilePath, cancel: cancel);

                            // ファイルサイズを比較する
                            if (destExistsMetadata.Size != srcFile.Size)
                            {
                                // ファイルサイズが異なる
                                fileChangedOrNew = true;
                            }

                            // 日付を比較する。ただし宛先ディレクトリの物理的なファイルの日付は信用できないので、メタデータ上のファイルサイズと比較する
                            if (destDirOldMetaData != null)
                            {
                                DirSuperBackupMetadataFile existsFileMetadataFromDirMetadata = destDirOldMetaData.FileList.Where(x => x.FileName._IsSamei(srcFile.Name)).SingleOrDefault();
                                if (existsFileMetadataFromDirMetadata == null)
                                {
                                    // メタデータ上に存在しない
                                    fileChangedOrNew = true;
                                }
                                else
                                {
                                    if (existsFileMetadataFromDirMetadata.MetaData!.LastWriteTime!.Value.Ticks != srcMetadata.LastWriteTime!.Value.Ticks)
                                    {
                                        // 最終更新日時が異なる
                                        fileChangedOrNew = true;
                                    }
                                }
                            }
                            else
                            {
                                // 宛先ディレクトリ上にメタデータがない
                                fileChangedOrNew = true;
                            }
                        }
                        else
                        {
                            // 新しいファイルである
                            fileChangedOrNew = true;
                        }

                        if (fileChangedOrNew)
                        {
                            // ファイルが新しいか、または更新された場合は、そのファイルをバックアップする
                            // ただし、バックアップ先に同名のファイルがすでに存在する場合は、
                            // xxxx.0123.old のような形式でまだ存在しない連番に古いファイル名をリネームする

                            if (exists)
                            {
                                string newOldFileName;

                                // 連番でかつ存在していないファイル名を決定する
                                for (int i = 0; ; i++)
                                {
                                    string newOldFileNameCandidate = $"{srcFile.Name}.{i:D4}.old";

                                    if (srcDirEnum.Where(x => x.Name._IsSamei(newOldFileNameCandidate)).Any() == false)
                                    {
                                        if (await Fs.IsFileExistsAsync(Fs.PathParser.Combine(destDir, newOldFileNameCandidate), cancel) == false)
                                        {
                                            newOldFileName = newOldFileNameCandidate;
                                            break;
                                        }
                                    }
                                }

                                // 変更されたファイル名を .old ファイルにリネーム実行する
                                await Fs.MoveFileAsync(destFilePath, Fs.PathParser.Combine(destDir, newOldFileName), cancel);
                            }

                            // ファイルをコピーする
                            await Fs.CopyFileAsync(srcFile.FullPath, destFilePath, new CopyFileParams(flags: FileFlags.BackupMode, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.None)),
                                cancel: cancel);

                            // コピーしたファイルを元に新しいメタデータを更新する
                            // 同一名の古いメタデータがあれば削除する
                            var existingMetaEntryList = destDirNewMetaData.FileList.Where(x => x.FileName._IsSamei(srcFile.Name)).ToList();
                            foreach (var existingMetaEntry in existingMetaEntryList)
                            {
                                destDirNewMetaData.FileList.Remove(existingMetaEntry);
                            }

                            // 新しいメタデータを書き込む
                            destDirNewMetaData.FileList.Add(new DirSuperBackupMetadataFile() { FileName = srcFile.Name, MetaData = srcMetadata });

                            Stat.Copy_NumFiles++;
                            Stat.Copy_TotalSize += srcFile.Size;
                        }
                        else
                        {
                            Stat.Skip_NumFiles++;
                            Stat.Skip_TotalSize += srcFile.Size;
                        }
                    }
                    catch (Exception ex)
                    {
                        Stat.Error_NumFiles++;
                        Stat.Error_TotalSize += srcFile.Size;

                        // ファイル単位のエラー発生
                        await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileError", srcFile.FullPath, destFilePath, ex.Message));
                    }
                }

                // 新しいメタデータをファイル名でソートする
                destDirNewMetaData.FileList = destDirNewMetaData.FileList.OrderBy(x => x.FileName, StrComparer.IgnoreCaseComparer).ToList();

                // 新しいメタデータを書き込む
                string newMetadataFilePath = Fs.PathParser.Combine(destDir, $"{PrefixMetadata}{Str.DateTimeToStrShortWithMilliSecs(now.UtcDateTime)}{SuffixMetadata}");
                await Fs.WriteJsonToFileAsync(newMetadataFilePath, destDirNewMetaData, FileFlags.BackupMode, cancel: cancel);
            }
            catch (Exception ex)
            {
                Stat.Error_Dir++;

                // ディレクトリ単位のエラー発生
                await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError", srcDir, destDir, ex.Message));
            }

            if (srcDirEnum != null)
            {
                try
                {
                    // ソースディレクトリの列挙に成功した場合は、サブディレクトリに対して再帰的に実行する
                    foreach (var subDir in srcDirEnum.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
                    {
                        await DoSingleDirBackupAsync(Fs.PathParser.Combine(srcDir, subDir.Name), Fs.PathParser.Combine(destDir, subDir.Name), cancel);
                    }
                }
                catch (Exception ex)
                {
                    // 何らかのディレクトリ単位のエラーで catch されていないものが発生
                    Stat.Error_Dir++;

                    // ディレクトリ単位のエラー発生
                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError", srcDir, destDir, ex.Message));
                }
            }
        }

        // 指定されたディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
        async Task<DirSuperBackupMetadata?> GetLatestMetaDataFileNameAsync(string dirPath, FileSystemEntity[] dirList, CancellationToken cancel = default)
        {
            // ディレクトリにあるファイル一覧を列挙し、メタデータファイル名を降順ソートする
            List<string> fileNameCandidates = new List<string>();

            foreach (var e in dirList)
            {
                if (e.Name.StartsWith(PrefixMetadata, StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(SuffixMetadata, StringComparison.OrdinalIgnoreCase))
                {
                    fileNameCandidates.Add(e.Name);
                }
            }

            fileNameCandidates.Sort(StrComparer.IgnoreCaseComparer);

            fileNameCandidates.Reverse();

            foreach (string fileName in fileNameCandidates)
            {
                string fullPath = Fs.PathParser.Combine(dirPath, fileName);

                DirSuperBackupMetadata? ret = await Fs.ReadJsonFromFileAsync<DirSuperBackupMetadata>(fullPath, nullIfError: true, flags: FileFlags.BackupMode);

                if (ret != null) return ret;
            }

            return null;
        }
    }
}

#endif

