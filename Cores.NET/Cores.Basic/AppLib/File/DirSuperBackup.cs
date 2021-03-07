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
        public List<string> DirList = null!;
    }
    public class DirSuperBackupMetadataFile
    {
        public string FileName = null!;
        public FileMetadata MetaData = null!;
    }

    public enum DirSuperBackupFlags
    {
        None = 0,
        RestoreOnlyNewer = 1,
        RestoreExactlySame = 2,
        RestoreMakeBackup = 4,
        RestoreNoAcl = 8,
    }

    public class DirSuperBackupOptions
    {
        public FileSystem Fs { get; }
        public string? InfoLogFileName { get; }
        public string? ErrorLogFileName { get; }
        public DirSuperBackupFlags Flags { get; }

        public DirSuperBackupOptions(FileSystem? fs = null, string? infoLogFileName = null, string? errorLogFileName = null, DirSuperBackupFlags flags = DirSuperBackupFlags.None)
        {
            Fs = fs ?? Lfs;
            InfoLogFileName = infoLogFileName;
            ErrorLogFileName = errorLogFileName;
            Flags = flags;
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

        readonly FileObject? InfoLogFileObj;
        readonly FileStream? InfoLogFileStream;
        readonly StreamWriter? InfoLogWriter;

        readonly FileObject? ErrorLogFileObj;
        readonly FileStream? ErrorLogFileStream;
        readonly StreamWriter? ErrorLogWriter;

        public const string PrefixMetadata = ".super_metadata_";
        public const string SuffixMetadata = ".metadat.json";

        public readonly DirSuperBackupStat Stat = new DirSuperBackupStat();

        public DirSuperBackup(DirSuperBackupOptions? options = null)
        {
            try
            {
                this.Options = options ?? new DirSuperBackupOptions();
                this.Lfs = new LargeFileSystem(new LargeFileSystemParams(this.Fs));
                this.LfsUtf8 = new Utf8BomFileSystem(new Utf8BomFileSystemParam(this.Lfs));

                if (Options.InfoLogFileName._IsFilled())
                {
                    InfoLogFileObj = this.LfsUtf8.OpenOrCreateAppend(Options.InfoLogFileName, flags: FileFlags.AutoCreateDirectory | FileFlags.BackupMode | FileFlags.LargeFs_ProhibitWriteWithCrossBorder);
                    InfoLogFileStream = InfoLogFileObj.GetStream(true);
                    InfoLogWriter = new StreamWriter(InfoLogFileStream);
                }

                if (Options.ErrorLogFileName._IsFilled())
                {
                    ErrorLogFileObj = this.LfsUtf8.OpenOrCreateAppend(Options.ErrorLogFileName, flags: FileFlags.AutoCreateDirectory | FileFlags.BackupMode | FileFlags.LargeFs_ProhibitWriteWithCrossBorder);
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

                InfoLogWriter._DisposeSafe();
                InfoLogFileStream._DisposeSafe();
                InfoLogFileObj._DisposeSafe();

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

        readonly AsyncLock WriteLogLock = new AsyncLock();

        async Task WriteLogAsync(DirSuperBackupLogType type, string str)
        {
            try
            {
                string line = $"{DateTimeOffset.Now._ToDtStr()},{str}";

                line = line._OneLine();

                using (await WriteLogLock.LockWithAwait())
                {
                    lock (Con.ConsoleWriteLock)
                    {
                        Console.WriteLine(line);
                    }

                    if (type.Bit(DirSuperBackupLogType.Error))
                    {
                        await WriteLogMainAsync(this.ErrorLogWriter, line);

                        if (this.ErrorLogWriter != null)
                        {
                            await this.ErrorLogWriter.FlushAsync();
                        }
                    }

                    await WriteLogMainAsync(this.InfoLogWriter, line);
                }
            }
            catch
            {
            }
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

        // 1 つのディレクトリを復元する
        public async Task DoSingleDirRestoreAsync(string srcDir, string destDir, CancellationToken cancel = default, string? ignoreDirNames = null)
        {
            DateTimeOffset now = DateTimeOffset.Now;

            FileSystemEntity[]? srcDirEnum = null;

            string[] ignoreDirNamesList = ignoreDirNames._NonNull()._Split(StringSplitOptions.RemoveEmptyEntries, ",", ";");

            try
            {
                if (srcDir._IsSamei(destDir))
                {
                    throw new CoresException($"srcDir == destDir. Directory path: '{srcDir}'");
                }

                // 元ディレクトリが存在していることを確認する
                if (await Fs.IsDirectoryExistsAsync(srcDir, cancel) == false)
                {
                    throw new CoresException($"The directory '{srcDir}' not found.");
                }

                // 元ディレクトリを列挙する
                srcDirEnum = (await Fs.EnumDirectoryAsync(srcDir, false, EnumDirectoryFlags.NoGetPhysicalSize, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

                // 元ディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
                // なお、メタデータファイルのパースがエラーになったら、必ずエラーを発生し中断する
                DirSuperBackupMetadata? dirMetaData = await GetLatestMetaDataFileNameAsync(srcDir, srcDirEnum, cancel, throwJsonParseError: true)!;
                if (dirMetaData == null)
                {
                    throw new CoresException($"Metadata not found on the directory '{srcDir}'.");
                }

                // 先ディレクトリがまだ存在していない場合は作成をする
                if (await Fs.IsDirectoryExistsAsync(destDir, cancel) == false)
                {
                    FileFlags newDirFlags = FileFlags.None;

                    // ディレクトリの圧縮フラグを適切に設定する
                    if (dirMetaData.DirMetadata.SpecialOperationFlags.Bit(FileSpecialOperationFlags.SetCompressionFlag) || (dirMetaData.DirMetadata.Attributes?.Bit(FileAttributes.Compressed) ?? false))
                    {
                        newDirFlags |= FileFlags.OnCreateSetCompressionFlag;
                    }
                    else
                    {
                        newDirFlags |= FileFlags.OnCreateRemoveCompressionFlag;
                    }

                    try
                    {
                        await Fs.CreateDirectoryAsync(destDir, newDirFlags, cancel);
                    }
                    catch
                    {
                        // ヘンな圧縮フラグの設定に失敗した場合もう一度作成試行する
                        try
                        {
                            await Fs.CreateDirectoryAsync(destDir, FileFlags.None, cancel);
                        }
                        catch
                        {
                            // ディレクトリ作成コマンドでなぜかエラーになっても、結果としてディレクトリが作成されればそれでよい
                            if (await Fs.IsDirectoryExistsAsync(destDir, cancel) == false)
                            {
                                throw;
                            }

                            // やはりディレクトリの作成に失敗したらここでエラーを発生させる
                        }
                    }
                }

                // ディレクトリの属性を設定する
                try
                {
                    await Fs.SetDirectoryMetadataAsync(destDir, dirMetaData.DirMetadata, cancel);
                }
                catch (Exception ex)
                {
                    // ディレクトリの属性の設定に失敗したが、軽微なエラーなのでエラーを出して続行する
                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirAttributeSetError", srcDir, destDir, ex.Message));
                    Stat.Error_Dir++;
                }

                // 元ディレクトリに存在するはずのファイル (メタデータに書いてある) を 1 つずつ復元する
                RefInt concurrentNum = new RefInt();

                AsyncLock SafeLock = new AsyncLock();

                await TaskUtil.ForEachAsync(32, dirMetaData.FileList, async (srcFile, cancel) =>
                {
                    await Task.Yield();

                    string srcFilePath = Fs.PathParser.Combine(srcDir, srcFile.FileName);
                    string destFilePath = Fs.PathParser.Combine(destDir, srcFile.FileName);
                    FileMetadata? srcFileMetadata = null;

                    concurrentNum.Increment();

                    try
                    {
                        srcFileMetadata = srcFile.MetaData;

                        // このファイルと同一のファイル名がすでに宛先ディレクトリに物理的に存在するかどうか確認する
                        bool exists = await Fs.IsFileExistsAsync(destFilePath, cancel);

                        bool restoreThisFile = false;

                        if (exists)
                        {
                            // すでに宛先ディレクトリに存在する物理的なファイルのメタデータを取得する
                            FileMetadata destExistsMetadata = await Fs.GetFileMetadataAsync(destFilePath, cancel: cancel);

                            if (Options.Flags.Bit(DirSuperBackupFlags.RestoreOnlyNewer) == false)
                            {
                                // 古いファイルも復元する
                                if (Options.Flags.Bit(DirSuperBackupFlags.RestoreExactlySame))
                                {
                                    // 必ず上書きする
                                    restoreThisFile = true;
                                }
                                else
                                {
                                    // ファイルサイズを比較する
                                    if (destExistsMetadata.Size != srcFileMetadata.Size)
                                    {
                                        // ファイルサイズが異なる
                                        restoreThisFile = true;
                                    }

                                    // 日付を比較する
                                    if (srcFileMetadata!.LastWriteTime!.Value.Ticks != destExistsMetadata.LastWriteTime!.Value.Ticks)
                                    {
                                        // 最終更新日時が異なる
                                        restoreThisFile = true;
                                    }

                                    if (restoreThisFile == false)
                                    {
                                        // 新旧両方のファイルが存在する場合で、ファイルサイズも日付も同じであれば、復元先ファイル内容がバックアップファイルと同一かチェックし、同一の場合はコピーをスキップする
                                        var sameRet = await FileUtil.CompareFileHashAsync(new FilePath(srcFilePath, Fs, flags: FileFlags.BackupMode), new FilePath(destFilePath, Fs, flags: FileFlags.BackupMode), cancel: cancel);

                                        if (sameRet.IsOk == false || sameRet.Value != 0)
                                        {
                                            restoreThisFile = true;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // バックアップのほうが新しい場合のみ復元するモード
                                // 日付を比較する
                                if (srcFileMetadata!.LastWriteTime!.Value.Ticks > destExistsMetadata.LastWriteTime!.Value.Ticks)
                                {
                                    // 最終更新日時がバックアップのほうが新しい
                                    restoreThisFile = true;
                                }
                            }
                        }
                        else
                        {
                            restoreThisFile = true;
                        }

                        if (restoreThisFile)
                        {
                            // すべての判断に合格したら、このファイルの復元を実施する
                            if (exists)
                            {
                                if (Options.Flags.Bit(DirSuperBackupFlags.RestoreMakeBackup))
                                {
                                    // 復元先に同名のファイルがすでに存在する場合は、
                                    // .original.xxxx.0123.original のような形式でまだ存在しない連番に古いファイル名をリネームする
                                    using (await SafeLock.LockWithAwait(cancel))
                                    {
                                        string newOldFileName;

                                        // 連番でかつ存在していないファイル名を決定する
                                        for (int i = 0; ; i++)
                                        {
                                            string newOldFileNameCandidate = $".original.{srcFile.FileName}.{i:D4}.original";

                                            if (await Fs.IsFileExistsAsync(Fs.PathParser.Combine(destDir, newOldFileNameCandidate), cancel) == false)
                                            {
                                                newOldFileName = newOldFileNameCandidate;
                                                break;
                                            }
                                        }

                                        // 変更されたファイル名を .old ファイルにリネーム実行する
                                        string newOldFilePath = Fs.PathParser.Combine(destDir, newOldFileName);
                                        await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileRename", destFilePath, newOldFilePath));
                                        await Fs.MoveFileAsync(destFilePath, newOldFilePath, cancel);

                                        // 隠しファイルにする
                                        try
                                        {
                                            var meta = await Fs.GetFileMetadataAsync(newOldFilePath, cancel: cancel);
                                            if (meta.Attributes != null)
                                            {
                                                FileMetadata meta2 = new FileMetadata(attributes: meta.Attributes?.BitAdd(FileAttributes.Hidden));

                                                await Fs.SetFileMetadataAsync(newOldFilePath, meta2, cancel);
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }

                            // 復元メイン
                            await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileCopy", srcFilePath, destFilePath));

                            // ファイルをコピーする
                            await Fs.CopyFileAsync(srcFilePath, destFilePath,
                                new CopyFileParams(flags: FileFlags.BackupMode | FileFlags.CopyFile_Verify | FileFlags.Async,
                                metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll)),
                                cancel: cancel,
                                newFileMeatadata: Options.Flags.Bit(DirSuperBackupFlags.RestoreNoAcl) ? null : srcFileMetadata);

                            Stat.Copy_NumFiles++;
                            Stat.Copy_TotalSize += srcFile.MetaData.Size;

                            // メタデータを再度復元
                            try
                            {
                                var meta = Options.Flags.Bit(DirSuperBackupFlags.RestoreNoAcl) ? srcFileMetadata.Clone(FileMetadataCopyMode.TimeAll | FileMetadataCopyMode.Attributes | FileMetadataCopyMode.ReplicateArchiveBit | FileMetadataCopyMode.AlternateStream | FileMetadataCopyMode.Author) : srcFileMetadata;

                                await Fs.SetFileMetadataAsync(destFilePath, meta, cancel);
                            }
                            catch (Exception ex)
                            {
                                FileMetadata? existingFileMetadata = null;
                                try
                                {
                                    existingFileMetadata = await Fs.GetFileMetadataAsync(destFilePath, cancel: cancel);
                                }
                                catch { }
                                if ((existingFileMetadata?.Attributes?.Bit(FileAttributes.ReadOnly) ?? true) == false)
                                {
                                    // メタデータの属性の設定に失敗したが、軽微なエラーなのでエラーを出して続行する
                                    // (宛先ファイルが ReadOnly の場合は、この操作はエラーとなる可能性が高い。このような場合は、予期されている動作なので、エラーは表示しない)
                                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileAttributeSetError", srcFilePath, destFilePath, ex.Message));
                                    Stat.Error_NumFiles++;
                                }
                            }
                        }
                        else
                        {
                            Stat.Skip_NumFiles++;
                            Stat.Skip_TotalSize += srcFile.MetaData.Size;
                        }
                    }
                    catch (Exception ex)
                    {
                        Stat.Error_NumFiles++;
                        Stat.Error_TotalSize += srcFileMetadata?.Size ?? 0;

                        // ファイル単位のエラー発生
                        await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileError", srcFilePath, destFilePath, ex.Message));
                    }
                    finally
                    {
                        concurrentNum.Decrement();
                    }
                });

                // このディレクトリの全ファイルの復元が終わったら、ディレクトリのタイムスタンプ情報を再書き込みする
                // (中のファイルが新しくなったことが原因で、ディレクトリの更新日時が新しくなってしまう可能性があるためである)
                try
                {
                    await Fs.SetFileMetadataAsync(destDir, dirMetaData.DirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                }
                catch
                {
                    // 属性書き込みは失敗してもよい
                }

                try
                {
                    // ソースディレクトリの列挙に成功した場合は、サブディレクトリに対して再帰的に実行する
                    foreach (var subDir in dirMetaData.DirList)
                    {
                        // 無視リストのいずれにも合致しない場合のみ
                        if (ignoreDirNamesList.Where(x => x._IsSamei(subDir)).Any() == false)
                        {
                            await DoSingleDirRestoreAsync(Fs.PathParser.Combine(srcDir, subDir), Fs.PathParser.Combine(destDir, subDir), cancel, ignoreDirNames);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 何らかのディレクトリ単位のエラーで catch されていないものが発生
                    Stat.Error_Dir++;

                    // ディレクトリ単位のエラー発生
                    await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError", srcDir, destDir, ex.Message));
                }

                // 再度 宛先ディレクトリの日付情報のみ属性書き込みする (Linux の場合、中のファイルを更新するとディレクトリの日時が変ってしまうため)
                try
                {
                    if (dirMetaData != null)
                    {
                        await Fs.SetFileMetadataAsync(destDir, dirMetaData.DirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                    }
                }
                catch
                {
                    // 属性書き込みは失敗してもよい
                }
            }
            catch (Exception ex)
            {
                Stat.Error_Dir++;

                // ディレクトリ単位のエラー発生
                await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError", srcDir, destDir, ex.Message));
            }
        }

        // 1 つのディレクトリをバックアップする
        public async Task DoSingleDirBackupAsync(string srcDir, string destDir, CancellationToken cancel = default, string? ignoreDirNames = null)
        {
            DateTimeOffset now = DateTimeOffset.Now;

            FileSystemEntity[]? srcDirEnum = null;

            string[] ignoreDirNamesList = ignoreDirNames._NonNull()._Split(StringSplitOptions.RemoveEmptyEntries, ",", ";");

            FileMetadata? srcDirMetadata = null;

            try
            {
                if (srcDir._IsSamei(destDir))
                {
                    throw new CoresException($"srcDir == destDir. Directory path: '{srcDir}'");
                }

                srcDirMetadata = await Fs.GetDirectoryMetadataAsync(srcDir, cancel: cancel);

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

                // 宛先ディレクトリの日付情報のみ属性書き込みする
                try
                {
                    await Fs.SetFileMetadataAsync(destDir, srcDirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                }
                catch
                {
                    // 属性書き込みは失敗してもよい
                }

                // 新しいメタデータを作成する
                destDirNewMetaData = new DirSuperBackupMetadata();
                destDirNewMetaData.FileList = new List<DirSuperBackupMetadataFile>();
                destDirNewMetaData.TimeStamp = now;
                destDirNewMetaData.DirMetadata = srcDirMetadata;
                destDirNewMetaData.DirList = new List<string>();

                // 元ディレクトリに存在するサブディレクトリ名一覧をメタデータに追記する
                foreach (var subDir in srcDirEnum.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
                {
                    // シンボリックリンクは無視する
                    if (subDir.IsSymbolicLink == false)
                    {
                        // 無視リストのいずれにも合致しない場合のみ
                        if (ignoreDirNamesList.Where(x => x._IsSamei(subDir.Name)).Any() == false)
                        {
                            destDirNewMetaData.DirList.Add(subDir.Name);
                        }
                    }
                }

                // 元ディレクトリに存在するファイルを 1 つずつバックアップする
                var fileEntries = srcDirEnum.Where(x => x.IsFile);

                RefInt concurrentNum = new RefInt();

                AsyncLock SafeLock = new AsyncLock();

                await TaskUtil.ForEachAsync(32, fileEntries, async (srcFile, cancel) =>
                {
                    await Task.Yield();

                    string destFilePath = Fs.PathParser.Combine(destDir, srcFile.Name);
                    FileMetadata? srcFileMetadata = null;

                    concurrentNum.Increment();

                    try
                    {
                        srcFileMetadata = await Fs.GetFileMetadataAsync(srcFile.FullPath, cancel: cancel);

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
                                DirSuperBackupMetadataFile? existsFileMetadataFromDirMetadata = destDirOldMetaData.FileList.Where(x => x.FileName._IsSamei(srcFile.Name)).SingleOrDefault();
                                if (existsFileMetadataFromDirMetadata == null)
                                {
                                    // メタデータ上に存在しない
                                    fileChangedOrNew = true;
                                }
                                else
                                {
                                    if (existsFileMetadataFromDirMetadata.MetaData!.LastWriteTime!.Value.Ticks != srcFileMetadata.LastWriteTime!.Value.Ticks)
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
                                using (await SafeLock.LockWithAwait(cancel))
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
                                    await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileRename", destFilePath, Fs.PathParser.Combine(destDir, newOldFileName)));
                                    await Fs.MoveFileAsync(destFilePath, Fs.PathParser.Combine(destDir, newOldFileName), cancel);
                                }
                            }

                            // ファイルをコピーする
                            // 属性は、ファイルの日付情報のみコピーする
                            await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileCopy", srcFile.FullPath, destFilePath));
                            await Fs.CopyFileAsync(srcFile.FullPath, destFilePath, new CopyFileParams(flags: FileFlags.BackupMode | FileFlags.CopyFile_Verify | FileFlags.Async, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll)),
                                cancel: cancel);

                            Stat.Copy_NumFiles++;
                            Stat.Copy_TotalSize += srcFile.Size;
                        }
                        else
                        {
                            Stat.Skip_NumFiles++;
                            Stat.Skip_TotalSize += srcFile.Size;

                            // ファイルの日付情報のみ更新する
                            try
                            {
                                await Fs.SetFileMetadataAsync(destFilePath, srcFileMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                            }
                            catch
                            {
                                // ファイルの日付情報の更新は失敗してもよい
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Stat.Error_NumFiles++;
                        Stat.Error_TotalSize += srcFile.Size;

                        // ファイル単位のエラー発生
                        await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileError", srcFile.FullPath, destFilePath, ex.Message));
                    }
                    finally
                    {
                        concurrentNum.Decrement();
                    }

                    // このファイルに関するメタデータを追加する
                    if (srcFileMetadata != null)
                    {
                        destDirNewMetaData.FileList.Add(new DirSuperBackupMetadataFile() { FileName = srcFile.Name, MetaData = srcFileMetadata });
                    }
                }, cancel: cancel);

                // 新しいメタデータをファイル名でソートする
                destDirNewMetaData.FileList = destDirNewMetaData.FileList.OrderBy(x => x.FileName, StrComparer.IgnoreCaseComparer).ToList();
                destDirNewMetaData.DirList = destDirNewMetaData.DirList.OrderBy(x => x, StrComparer.IgnoreCaseComparer).ToList();

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

            // 再度 宛先ディレクトリの日付情報のみ属性書き込みする (Linux の場合、中のファイルを更新するとディレクトリの日時が変ってしまうため)
            try
            {
                if (srcDirMetadata != null)
                {
                    await Fs.SetFileMetadataAsync(destDir, srcDirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                }
            }
            catch
            {
                // 属性書き込みは失敗してもよい
            }

            if (srcDirEnum != null)
            {
                try
                {
                    // ソースディレクトリの列挙に成功した場合は、サブディレクトリに対して再帰的に実行する
                    foreach (var subDir in srcDirEnum.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false))
                    {
                        // シンボリックリンクは無視する
                        if (subDir.IsSymbolicLink == false)
                        {
                            // 無視リストのいずれにも合致しない場合のみ
                            if (ignoreDirNamesList.Where(x => x._IsSamei(subDir.Name)).Any() == false)
                            {
                                await DoSingleDirBackupAsync(Fs.PathParser.Combine(srcDir, subDir.Name), Fs.PathParser.Combine(destDir, subDir.Name), cancel, ignoreDirNames);
                            }
                        }
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


            // 再度 宛先ディレクトリの日付情報のみ属性書き込みする (Linux の場合、中のファイルを更新するとディレクトリの日時が変ってしまうため)
            try
            {
                if (srcDirMetadata != null)
                {
                    await Fs.SetFileMetadataAsync(destDir, srcDirMetadata.Clone(FileMetadataCopyMode.TimeAll), cancel);
                }
            }
            catch
            {
                // 属性書き込みは失敗してもよい
            }
        }

        // 指定されたディレクトリに存在するメタデータファイルのうち最新のファイルを取得する
        async Task<DirSuperBackupMetadata?> GetLatestMetaDataFileNameAsync(string dirPath, FileSystemEntity[] dirList, CancellationToken cancel = default, bool throwJsonParseError = false)
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

                DirSuperBackupMetadata? ret = await Fs.ReadJsonFromFileAsync<DirSuperBackupMetadata>(fullPath, nullIfError: !throwJsonParseError, flags: FileFlags.BackupMode, maxSize: long.MaxValue);

                if (ret != null) return ret;
            }

            return null;
        }
    }
}

#endif

