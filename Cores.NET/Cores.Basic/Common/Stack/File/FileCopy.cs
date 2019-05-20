// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.AccessControl;

namespace IPA.Cores.Basic
{
    [Flags]
    enum CopyDirectoryFlags : long
    {
        None = 0,
        BackupMode = 1,
        CopyDirectoryCompressionFlag = 2,
        CopyFileCompressionFlag = 4,
        CopyFileSparseFlag = 8,
        AsyncCopy = 16,
        Overwrite = 32,
        Recursive = 64,
        SetAclProtectionFlagOnRootDir = 128,
        IgnoreReadError = 256,

        Default = CopyDirectoryCompressionFlag | CopyFileCompressionFlag | CopyFileSparseFlag | AsyncCopy | Overwrite | Recursive | SetAclProtectionFlagOnRootDir,
    }

    class CopyDirectoryStatus
    {
        public object State { get; set; }
        public long StartTick { get; set; }
        public long EndTick { get; set; }
        public long SpentTime => EndTick - StartTick;
        public long SizeTotal { get; set; }
        public long SizeOk { get; set; }
        public long SizeError => SizeTotal - SizeOk;
        public long NumFilesTotal { get; set; }
        public long NumFilesOk { get; set; }
        public long NumFilesError => NumFilesTotal - NumFilesOk;
        public long NumDirectoriesTotal { get; set; }
        public long NumDirectoriesOk { get; set; }
        public long NumDirectoriesError => NumDirectoriesTotal - NumDirectoriesOk;
        public List<string> IgnoreReadErrorFileNameList { get; } = new List<string>();

        public bool IsAllOk => (NumFilesTotal == NumFilesOk && NumDirectoriesTotal == NumDirectoriesOk);

        public void Clear()
        {
            this.State = null;
            StartTick = EndTick = SizeTotal = SizeOk = NumFilesTotal = NumFilesOk = NumDirectoriesTotal = NumDirectoriesOk = 0;
        }

        public readonly CriticalSection LockObj = new CriticalSection();
    }

    class CopyDirectoryParams
    {
        public static FileMetadataCopier DefaultDirectoryMetadataCopier { get; } = new FileMetadataCopier(FileMetadataCopyMode.Default);

        public CopyDirectoryFlags CopyDirFlags { get; }
        public FileOperationFlags CopyFileFlags { get; }
        public FileMetadataCopier DirectoryMetadataCopier { get; }
        public FileMetadataCopier FileMetadataCopier { get; }
        public int BufferSize { get; }
        public int IgnoreReadErrorSectorSize { get; }

        public ProgressReporterFactoryBase EntireProgressReporterFactory { get; }
        public ProgressReporterFactoryBase FileProgressReporterFactory { get; }

        public static ProgressReporterFactoryBase NullReporterFactory { get; } = CopyFileParams.NullReporterFactory;
        public static ProgressReporterFactoryBase ConsoleReporterFactory { get; } = CopyFileParams.ConsoleReporterFactory;
        public static ProgressReporterFactoryBase DebugReporterFactory { get; } = CopyFileParams.DebugReporterFactory;

        public CopyFileParams GenerateCopyFileParams(FileOperationFlags additionalFlags = FileOperationFlags.None)
        {
            FileOperationFlags fileFlags = this.CopyFileFlags | additionalFlags;

            return new CopyFileParams(
                this.CopyDirFlags.Bit(CopyDirectoryFlags.Overwrite), fileFlags,
                this.FileMetadataCopier,
                this.BufferSize,
                this.CopyDirFlags.Bit(CopyDirectoryFlags.AsyncCopy),
                this.CopyDirFlags.Bit(CopyDirectoryFlags.IgnoreReadError),
                this.IgnoreReadErrorSectorSize,
                this.FileProgressReporterFactory);
        }

        public delegate Task<bool> ProgressCallback(CopyDirectoryStatus status, FileSystemEntity entity);
        public delegate Task<bool> ExceptionCallback(CopyDirectoryStatus status, FileSystemEntity entity, Exception exception);

        async Task<bool> DefaultProgressCallback(CopyDirectoryStatus status, FileSystemEntity entity)
        {
            Con.WriteError($"Copying: '{entity.FullPath}'");

            await Task.CompletedTask;
            return true;
        }

        async Task<bool> DefaultExceptionCallback(CopyDirectoryStatus status, FileSystemEntity entity, Exception exception)
        {
            Con.WriteError($"Error: '{entity.FullPath}': {exception.Message}");

            await Task.CompletedTask;

            return true;
        }

        public ExceptionCallback ExceptionCallbackProc { get; }
        public ProgressCallback ProgressCallbackProc { get; }

        public CopyDirectoryParams(CopyDirectoryFlags copyDirFlags = CopyDirectoryFlags.Default, FileOperationFlags copyFileFlags = FileOperationFlags.None,
            FileMetadataCopier dirMetadataCopier = null, FileMetadataCopier fileMetadataCopier = null,
            int bufferSize = 0, int ignoreReadErrorSectorSize = 0,
            ProgressReporterFactoryBase entireReporterFactory = null, ProgressReporterFactoryBase fileReporterFactory = null,
            ProgressCallback progressCallback = null,
            ExceptionCallback exceptionCallback = null)
        {
            if (dirMetadataCopier == null) dirMetadataCopier = CopyDirectoryParams.DefaultDirectoryMetadataCopier;
            if (fileMetadataCopier == null) fileMetadataCopier = CopyFileParams.DefaultFileMetadataCopier;
            if (bufferSize <= 0) bufferSize = CoresConfig.FileUtilSettings.FileCopyBufferSize.Value;
            if (entireReporterFactory == null) entireReporterFactory = NullReporterFactory;
            if (fileReporterFactory == null) fileReporterFactory = NullReporterFactory;
            if (exceptionCallback == null) exceptionCallback = DefaultExceptionCallback;
            if (progressCallback == null) progressCallback = DefaultProgressCallback;

            this.CopyDirFlags = copyDirFlags;
            this.CopyFileFlags = copyFileFlags;
            this.BufferSize = bufferSize;
            this.DirectoryMetadataCopier = dirMetadataCopier;
            this.FileMetadataCopier = fileMetadataCopier;
            this.EntireProgressReporterFactory = entireReporterFactory;
            this.FileProgressReporterFactory = fileReporterFactory;

            if (this.CopyDirFlags.Bit(CopyDirectoryFlags.BackupMode))
                this.CopyFileFlags |= FileOperationFlags.BackupMode;

            if (this.CopyDirFlags.Bit(CopyDirectoryFlags.Overwrite))
                this.CopyFileFlags |= FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed;

            this.CopyFileFlags |= FileOperationFlags.AutoCreateDirectory;

            this.ExceptionCallbackProc = exceptionCallback;
            this.ProgressCallbackProc = progressCallback;

            this.IgnoreReadErrorSectorSize = ignoreReadErrorSectorSize;
        }

    }

    class CopyFileParams
    {
        public static FileMetadataCopier DefaultFileMetadataCopier { get; } = new FileMetadataCopier(FileMetadataCopyMode.Default);

        public bool Overwrite { get; }
        public FileOperationFlags Flags { get; }
        public FileMetadataCopier MetadataCopier { get; }
        public int BufferSize { get; }
        public bool AsyncCopy { get; }
        public bool IgnoreReadError { get; }
        public int IgnoreReadErrorSectorSize { get; }

        public ProgressReporterFactoryBase ProgressReporterFactory { get; }

        public static ProgressReporterFactoryBase NullReporterFactory { get; } = new NullReporterFactory();
        public static ProgressReporterFactoryBase ConsoleReporterFactory { get; } = new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.Console);
        public static ProgressReporterFactoryBase DebugReporterFactory { get; } = new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.Debug);

        public CopyFileParams(bool overwrite = true, FileOperationFlags flags = FileOperationFlags.None, FileMetadataCopier metadataCopier = null, int bufferSize = 0, bool asyncCopy = true,
            bool ignoreReadError = false, int ignoreReadErrorSectorSize = 0,
            ProgressReporterFactoryBase reporterFactory = null)
        {
            if (metadataCopier == null) metadataCopier = DefaultFileMetadataCopier;
            if (bufferSize <= 0) bufferSize = CoresConfig.FileUtilSettings.DefaultSectorSize;
            if (reporterFactory == null) reporterFactory = NullReporterFactory;
            if (ignoreReadErrorSectorSize <= 0) ignoreReadErrorSectorSize = CoresConfig.FileUtilSettings.DefaultSectorSize;

            this.Overwrite = overwrite;
            this.Flags = flags;
            this.MetadataCopier = metadataCopier;
            this.BufferSize = bufferSize;
            this.AsyncCopy = asyncCopy;
            this.ProgressReporterFactory = reporterFactory;
            this.IgnoreReadError = ignoreReadError;
            this.IgnoreReadErrorSectorSize = ignoreReadErrorSectorSize;
        }
    }


    abstract partial class FileSystem
    {
        public async Task CopyFileAsync(string srcPath, string destPath, CopyFileParams param = null, object state = null, CancellationToken cancel = default, FileSystem destFileSystem = null, RefBool readErrorIgnored = null)
        {
            if (destFileSystem == null) destFileSystem = this;

            await FileUtil.CopyFileAsync(this, srcPath, destFileSystem, destPath, param, state, cancel, readErrorIgnored);
        }
        public void CopyFile(string srcPath, string destPath, CopyFileParams param = null, object state = null, CancellationToken cancel = default, FileSystem destFileSystem = null, RefBool readErrorIgnored = null)
            => CopyFileAsync(srcPath, destPath, param, state, cancel, destFileSystem, readErrorIgnored)._GetResult();

        public async Task<CopyDirectoryStatus> CopyDirAsync(string srcPath, string destPath, FileSystem destFileSystem = null,
            CopyDirectoryParams param = null, object state = null, CopyDirectoryStatus statusObject = null, CancellationToken cancel = default)
        {
            if (destFileSystem == null) destFileSystem = this;

            return await FileUtil.CopyDirAsync(this, srcPath, destFileSystem, destPath, param, state, statusObject, cancel);
        }
        public CopyDirectoryStatus CopyDir(string srcPath, string destPath, FileSystem destFileSystem = null,
            CopyDirectoryParams param = null, object state = null, CopyDirectoryStatus statusObject = null, CancellationToken cancel = default)
            => CopyDirAsync(srcPath, destPath, destFileSystem, param, state, statusObject, cancel)._GetResult();
    }

    static partial class FileUtil
    {
        public static async Task<CopyDirectoryStatus> CopyDirAsync(FileSystem srcFileSystem, string srcPath, FileSystem destFileSystem, string destPath,
            CopyDirectoryParams param = null, object state = null, CopyDirectoryStatus statusObject = null, CancellationToken cancel = default)
        {
            CopyDirectoryStatus status = statusObject ?? new CopyDirectoryStatus();
            status.Clear();

            status.State = state;
            status.StartTick = Tick64.Now;

            if (param == null)
                param = new CopyDirectoryParams();

            srcPath = await srcFileSystem.NormalizePathAsync(srcPath, cancel);
            destPath = await destFileSystem.NormalizePathAsync(destPath, cancel);

            if (srcFileSystem == destFileSystem)
                if (srcFileSystem.PathParser.PathStringComparer.Equals(srcPath, destPath))
                    throw new FileException(destPath, "Both source and destination is the same directory.");

            using (ProgressReporterBase dirReporter = param.EntireProgressReporterFactory.CreateNewReporter($"CopyDir '{srcFileSystem.PathParser.GetFileName(srcPath)}'", state))
            {
                DirectoryWalker walker = new DirectoryWalker(srcFileSystem);
                bool walkRet = await walker.WalkDirectoryAsync(srcPath,
                    async (dirInfo, entries, c) =>
                    {
                        c.ThrowIfCancellationRequested();

                        foreach (FileSystemEntity entity in entries)
                        {
                            c.ThrowIfCancellationRequested();

                            try
                            {
                                if (await param.ProgressCallbackProc(status, entity) == false)
                                {
                                    throw new OperationCanceledException($"Copying of the file '{entity.FullPath}' is cancaled by the user.");
                                }

                                string entryName = entity.Name;
                                if (entity.IsCurrentDirectory)
                                    entryName = "";

                                string srcFullPath = srcFileSystem.PathParser.Combine(srcPath, dirInfo.RelativePath, entryName);
                                string destFullPath = destFileSystem.PathParser.Combine(destPath, srcFileSystem.PathParser.ConvertDirectorySeparatorToOtherSystem(dirInfo.RelativePath, destFileSystem.PathParser), entryName);

                                if (entity.IsDirectory == false)
                                {
                                    // Copy a file
                                    lock (status.LockObj)
                                        status.NumFilesTotal++;

                                    FileMetadataGetFlags metadataGetFlags = FileMetadataCopier.CalcOptimizedMetadataGetFlags(param.FileMetadataCopier.Mode | FileMetadataCopyMode.Attributes);
                                    FileMetadata srcFileMetadata = await srcFileSystem.GetFileMetadataAsync(srcFullPath, metadataGetFlags, cancel);
                                    FileOperationFlags copyFileAdditionalFlags = FileOperationFlags.None;

                                    lock (status.LockObj)
                                        status.SizeTotal += srcFileMetadata.Size;

                                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.CopyFileCompressionFlag))
                                        if (srcFileMetadata.Attributes is FileAttributes attr)
                                            if (attr.Bit(FileAttributes.Compressed))
                                                copyFileAdditionalFlags |= FileOperationFlags.OnCreateSetCompressionFlag;
                                            else
                                                copyFileAdditionalFlags |= FileOperationFlags.OnCreateRemoveCompressionFlag;

                                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.CopyFileSparseFlag))
                                        if (srcFileMetadata.Attributes is FileAttributes attr)
                                            if (attr.Bit(FileAttributes.SparseFile))
                                                copyFileAdditionalFlags |= FileOperationFlags.SparseFile;

                                    var copyFileParam = param.GenerateCopyFileParams(copyFileAdditionalFlags);

                                    RefBool ignoredReadError = new RefBool(false);

                                    await CopyFileAsync(srcFileSystem, srcFullPath, destFileSystem, destFullPath, copyFileParam, state, cancel, ignoredReadError);

                                    lock (status.LockObj)
                                    {
                                        status.NumFilesOk++;
                                        status.SizeOk += srcFileMetadata.Size;

                                        if (ignoredReadError)
                                        {
                                            status.IgnoreReadErrorFileNameList.Add(srcFullPath);
                                        }
                                    }
                                }
                                else
                                {
                                    // Make a directory
                                    lock (status.LockObj)
                                    {
                                        status.NumDirectoriesTotal++;
                                    }

                                    FileMetadataGetFlags metadataGetFlags = FileMetadataCopier.CalcOptimizedMetadataGetFlags(param.DirectoryMetadataCopier.Mode | FileMetadataCopyMode.Attributes);
                                    FileMetadata srcDirMetadata = await srcFileSystem.GetDirectoryMetadataAsync(srcFullPath, metadataGetFlags, cancel);
                                    FileOperationFlags copyDirFlags = FileOperationFlags.None;

                                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.BackupMode))
                                        copyDirFlags |= FileOperationFlags.BackupMode;

                                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.CopyDirectoryCompressionFlag))
                                        if (srcDirMetadata.Attributes is FileAttributes attr)
                                            if (attr.Bit(FileAttributes.Compressed))
                                                copyDirFlags |= FileOperationFlags.OnCreateSetCompressionFlag;
                                            else
                                                copyDirFlags |= FileOperationFlags.OnCreateRemoveCompressionFlag;

                                    await destFileSystem.CreateDirectoryAsync(destFullPath, copyDirFlags, cancel);

                                    FileMetadata dstDirMetadata = param.DirectoryMetadataCopier.Copy(srcDirMetadata);

                                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.SetAclProtectionFlagOnRootDir))
                                    {
                                        if (dirInfo.IsRoot)
                                        {
                                            if (dstDirMetadata.Security != null)
                                            {
                                                if (dstDirMetadata.Security.Acl != null)
                                                    dstDirMetadata.Security.Acl.Win32AclSddl = "!" + dstDirMetadata.Security.Acl.Win32AclSddl;

                                                if (dstDirMetadata.Security.Audit != null)
                                                    dstDirMetadata.Security.Audit.Win32AuditSddl = "!" + dstDirMetadata.Security.Audit.Win32AuditSddl;
                                            }
                                        }
                                    }

                                    await destFileSystem.SetDirectoryMetadataAsync(destFullPath, dstDirMetadata, cancel);

                                    lock (status.LockObj)
                                        status.NumDirectoriesOk++;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (await param.ExceptionCallbackProc(status, entity, ex) == false)
                                {
                                    throw ex;
                                }
                            }
                        }

                        return true;
                    },
                    async (dirInfo, entries, c) =>
                    {
                        c.ThrowIfCancellationRequested();
                        foreach (FileSystemEntity entity in entries)
                        {
                            c.ThrowIfCancellationRequested();
                            string entryName = entity.Name;
                            if (entity.IsCurrentDirectory)
                                entryName = "";

                            string srcFullPath = srcFileSystem.PathParser.Combine(srcPath, dirInfo.RelativePath, entryName);
                            string destFullPath = destFileSystem.PathParser.Combine(destPath, srcFileSystem.PathParser.ConvertDirectorySeparatorToOtherSystem(dirInfo.RelativePath, destFileSystem.PathParser), entryName);

                            if (entity.IsDirectory)
                            {
                                // Update the directory LastWriteTime metadata after placing all inside files into the directory
                                if (param.DirectoryMetadataCopier.Mode.BitAny(FileMetadataCopyMode.TimeAll))
                                {
                                    FileMetadataGetFlags metadataGetFlags = FileMetadataCopier.CalcOptimizedMetadataGetFlags(param.DirectoryMetadataCopier.Mode & (FileMetadataCopyMode.TimeAll));
                                    FileMetadata srcDirMetadata = await srcFileSystem.GetDirectoryMetadataAsync(srcFullPath, metadataGetFlags, cancel);
                                    FileMetadata dstDirMetadata = param.DirectoryMetadataCopier.Copy(srcDirMetadata);
                                    await destFileSystem.SetDirectoryMetadataAsync(destFullPath, dstDirMetadata, cancel);
                                }
                            }
                        }
                        return true;
                    },
                    async (dirInfo, exception, c) =>
                    {
                        c.ThrowIfCancellationRequested();
                        if (await param.ExceptionCallbackProc(status, dirInfo.Entity, exception) == false)
                        {
                            throw exception;
                        }
                        return true;
                    },
                    param.CopyDirFlags.Bit(CopyDirectoryFlags.Recursive),
                    cancel
                    );
            }

            status.EndTick = Tick64.Now;

            return status;
        }

        public static async Task CopyFileAsync(FileSystem srcFileSystem, string srcPath, FileSystem destFileSystem, string destPath,
            CopyFileParams param = null, object state = null, CancellationToken cancel = default, RefBool readErrorIgnored = null)
        {
            if (readErrorIgnored == null)
                readErrorIgnored = new RefBool(false);

            readErrorIgnored.Set(false);

            if (param == null)
                param = new CopyFileParams();

            srcPath = await srcFileSystem.NormalizePathAsync(srcPath, cancel);
            destPath = await destFileSystem.NormalizePathAsync(destPath, cancel);

            if (srcFileSystem == destFileSystem)
                if (srcFileSystem.PathParser.PathStringComparer.Equals(srcPath, destPath))
                    throw new FileException(destPath, "Both source and destination is the same file.");

            using (ProgressReporterBase reporter = param.ProgressReporterFactory.CreateNewReporter($"CopyFile '{srcFileSystem.PathParser.GetFileName(srcPath)}'", state))
            {
                using (var srcFile = await srcFileSystem.OpenAsync(srcPath, flags: param.Flags, cancel: cancel))
                {
                    try
                    {
                        FileMetadata srcFileMetadata = await srcFileSystem.GetFileMetadataAsync(srcPath, param.MetadataCopier.OptimizedMetadataGetFlags, cancel);

                        bool destFileExists = await destFileSystem.IsFileExistsAsync(destPath, cancel);

                        using (var destFile = await destFileSystem.CreateAsync(destPath, flags: param.Flags, doNotOverwrite: !param.Overwrite, cancel: cancel))
                        {
                            try
                            {
                                reporter.ReportProgress(new ProgressData(0, srcFileMetadata.Size));

                                long copiedSize = await CopyBetweenHandleAsync(srcFile, destFile, param, reporter, srcFileMetadata.Size, cancel, readErrorIgnored);

                                reporter.ReportProgress(new ProgressData(copiedSize, copiedSize, true));

                                await destFile.CloseAsync();

                                try
                                {
                                    await destFileSystem.SetFileMetadataAsync(destPath, param.MetadataCopier.Copy(srcFileMetadata), cancel);
                                }
                                catch (Exception ex)
                                {
                                    Con.WriteDebug($"CopyFileAsync: '{destPath}': SetFileMetadataAsync failed. Error: {ex.Message}");
                                }
                            }
                            catch
                            {
                                if (destFileExists == false)
                                {
                                    try
                                    {
                                        await destFileSystem.DeleteFileAsync(destPath);
                                    }
                                    catch { }
                                }

                                throw;
                            }
                            finally
                            {
                                await destFile.CloseAsync();
                            }
                        }
                    }
                    finally
                    {
                        await srcFile.CloseAsync();
                    }
                }
            }
        }
        public static void CopyFile(FileSystem srcFileSystem, string srcPath, FileSystem destFileSystem, string destPath,
            CopyFileParams param = null, object state = null, CancellationToken cancel = default, RefBool readErrorIgnored = null)
            => CopyFileAsync(srcFileSystem, srcPath, destFileSystem, destPath, param, state, cancel, readErrorIgnored)._GetResult();

        static async Task<long> CopyBetweenHandleAsync(FileBase src, FileBase dest, CopyFileParams param, ProgressReporterBase reporter, long estimatedSize, CancellationToken cancel, RefBool readErrorIgnored)
        {
            readErrorIgnored.Set(false);

            checked
            {
                long currentPosition = 0;

                if (param.IgnoreReadError)
                {
                    long fileSize = src.Size;
                    int errorCounter = 0;

                    // Ignore read error mode
                    using (MemoryHelper.FastAllocMemoryWithUsing(param.IgnoreReadErrorSectorSize, out Memory<byte> buffer))
                    {
                        for (long pos = 0; pos < fileSize; pos += param.IgnoreReadErrorSectorSize)
                        {
                            int size = (int)Math.Min(param.IgnoreReadErrorSectorSize, (fileSize - pos));
                            Memory<byte> buffer2 = buffer.Slice(0, size);

                            int readSize = 0;

                            try
                            {
                                //if (pos >= 10000000 && pos <= (10000000 + 100000)) throw new ApplicationException("*err*");
                                readSize = await src.ReadRandomAsync(pos, buffer2, cancel);
                            }
                            catch (Exception ex)
                            {
                                errorCounter++;
                                if (errorCounter >= 100)
                                {
                                    // Skip error display
                                    if (errorCounter == 100)
                                    {
                                        Con.WriteError($"The read error counter of the file \"{src.FileParams.Path}\" exceeds 100. No more errors will be reported.");
                                    }
                                }
                                else
                                {
                                    // Display the error
                                    Con.WriteError($"Ignoring the read error at the offset {pos} of the file \"{src.FileParams.Path}\". Error: {ex.Message}");
                                }
                                readErrorIgnored.Set(true);
                            }

                            if (readSize >= 1)
                            {
                                await dest.WriteRandomAsync(pos, buffer2.Slice(0, readSize), cancel);
                            }

                            currentPosition = pos + readSize;
                            reporter.ReportProgress(new ProgressData(currentPosition, fileSize));
                        }
                    }
                }
                else if (param.AsyncCopy == false)
                {
                    // Normal copy
                    using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer))
                    {
                        while (true)
                        {
                            int readSize = await src.ReadAsync(buffer, cancel);

                            Debug.Assert(readSize <= buffer.Length);

                            if (readSize <= 0) break;

                            await dest.WriteAsync(buffer.Slice(0, readSize), cancel);

                            currentPosition += readSize;
                            reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                        }
                    }
                }
                else
                {
                    // Async copy
                    using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer1))
                    {
                        using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer2))
                        {
                            Task lastWriteTask = null;
                            int number = 0;
                            int writeSize = 0;

                            Memory<byte>[] buffers = new Memory<byte>[2] { buffer1, buffer2 };

                            while (true)
                            {
                                Memory<byte> buffer = buffers[(number++) % 2];

                                int readSize = await src.ReadAsync(buffer, cancel);

                                Debug.Assert(readSize <= buffer.Length);

                                if (lastWriteTask != null)
                                {
                                    await lastWriteTask;
                                    currentPosition += writeSize;
                                    reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                                }

                                if (readSize <= 0) break;

                                writeSize = readSize;
                                lastWriteTask = dest.WriteAsync(buffer.Slice(0, writeSize), cancel);
                            }

                            reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                        }
                    }
                }

                return currentPosition;
            }
        }
    }
}
