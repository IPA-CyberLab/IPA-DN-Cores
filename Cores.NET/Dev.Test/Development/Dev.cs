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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

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
        All = 1,
        Error = 2,
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

            await WriteLogMainAsync(this.AllLogWriter, line);
        }

        async Task WriteLogMainAsync(StreamWriter? writer, string line)
        {
            if (writer == null) return;

            await writer.WriteLineAsync(line._OneLine());
        }

        // 1 つのディレクトリをバックアップする (サブディレクトリの中身はバックアップしないが、サブディレクトリの作成は行なう)
        public async Task DoSingleDirBackupAsync(string srcDir, string destDir, CancellationToken cancel = default)
        {
            DateTimeOffset now = DateTimeOffset.Now;

            FileMetadata srcDirMetadata = await Fs.GetDirectoryMetadataAsync(srcDir, cancel: cancel);

            FileSystemEntity[] srcDirEnum = await Fs.EnumDirectoryAsync(srcDir, false, EnumDirectoryFlags.NoGetPhysicalSize, cancel);

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
                destDirNewMetaData = destDirOldMetaData._CloneDeep();
            }
            destDirNewMetaData.TimeStamp = now;
            destDirNewMetaData.DirMetadata = srcDirMetadata;

            // 元ディレクトリに存在するファイルを 1 つずつバックアップする
            foreach (FileSystemEntity srcFile in srcDirEnum)
            {
                string destFileName = Fs.PathParser.Combine(destDir, srcFile.Name);

                FileMetadata srcMetadata = await Fs.GetFileMetadataAsync(srcFile.FullPath, cancel: cancel);

                // このファイルと同一のファイル名がすでに宛先ディレクトリに物理的に存在するかどうか確認する
                bool exists = await Fs.IsFileExistsAsync(destFileName, cancel);

                bool fileChangedOrNew = false;

                if (exists)
                {
                    // すでに宛先ディレクトリに存在する物理的なファイルのメタデータを取得する
                    FileMetadata destExistsMetadata = await Fs.GetFileMetadataAsync(destFileName, cancel: cancel);

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
                        await Fs.MoveFileAsync(destFileName, newOldFileName, cancel);
                    }

                    // ファイルをコピーする
                    await Fs.CopyFileAsync(srcFile.FullPath, destFileName, new CopyFileParams(flags: FileFlags.BackupMode, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.None)),
                        cancel: cancel);

                    // コピーしたファイルを元に新しいメタデータを更新する
                    // 同一名の古いメタデータがあれば削除する
                    var existingMetaEntryList = destDirNewMetaData.FileList.Where(x => x.FileName._IsSamei(srcFile.Name));
                    foreach (var existingMetaEntry in existingMetaEntryList)
                    {
                        destDirNewMetaData.FileList.Remove(existingMetaEntry);
                    }

                    // 新しいメタデータを書き込む
                    destDirNewMetaData.FileList.Add(new DirSuperBackupMetadataFile() { FileName = srcFile.Name, MetaData = srcMetadata });
                }
            }

            // 新しいメタデータをファイル名でソートする
            destDirNewMetaData.FileList = destDirNewMetaData.FileList.OrderBy(x => x.FileName, StrComparer.IgnoreCaseComparer).ToList();

            // 新しいメタデータを書き込む
            string newMetadataFileName = Fs.PathParser.Combine(destDir, $"{PrefixMetadata}{Str.DateTimeToStrShortWithMilliSecs(now.DateTime)}{SuffixMetadata}");
            await Fs.WriteJsonToFileAsync(newMetadataFileName, destDirNewMetaData, FileFlags.BackupMode, cancel: cancel);
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

