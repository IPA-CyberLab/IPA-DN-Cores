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
using System.Security.Cryptography;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.AccessControl;

namespace IPA.Cores.Basic;

[Flags]
public enum CopyDirectoryFlags : long
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
    SilenceSuccessfulReport = 512,
    DeleteNotExistFiles = 1024,
    DeleteNotExistDirs = 2048,

    Default = CopyDirectoryCompressionFlag | CopyFileCompressionFlag | CopyFileSparseFlag | AsyncCopy | Overwrite | Recursive | SetAclProtectionFlagOnRootDir,
}

public class CopyDirectoryStatus
{
    public object? State { get; set; }
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
    public long NumFilesDeleted { get; set; }
    public long NumDirectoriesDeleted { get; set; }
    public List<string> IgnoreReadErrorFileNameList { get; } = new List<string>();

    public bool IsAllOk => (NumFilesTotal == NumFilesOk && NumDirectoriesTotal == NumDirectoriesOk);

    public void Clear()
    {
        this.State = null;
        StartTick = EndTick = SizeTotal = SizeOk = NumFilesTotal = NumFilesOk = NumDirectoriesTotal = NumDirectoriesOk = 0;
    }

    public readonly CriticalSection LockObj = new CriticalSection<CopyDirectoryStatus>();
}

public class CopyDirectoryParams
{
    public static FileMetadataCopier DefaultDirectoryMetadataCopier { get; } = new FileMetadataCopier(FileMetadataCopyMode.Default);

    public CopyDirectoryFlags CopyDirFlags { get; }
    public FileFlags CopyFileFlags { get; }
    public FileMetadataCopier DirectoryMetadataCopier { get; }
    public FileMetadataCopier FileMetadataCopier { get; }
    public int BufferSize { get; }
    public int IgnoreReadErrorSectorSize { get; }

    public ProgressReporterFactoryBase EntireProgressReporterFactory { get; }
    public ProgressReporterFactoryBase FileProgressReporterFactory { get; }

    public static ProgressReporterFactoryBase NullReporterFactory { get; } = CopyFileParams.NullReporterFactory;
    public static ProgressReporterFactoryBase ConsoleReporterFactory { get; } = CopyFileParams.ConsoleReporterFactory;
    public static ProgressReporterFactoryBase DebugReporterFactory { get; } = CopyFileParams.DebugReporterFactory;

    public CopyFileParams GenerateCopyFileParams(FileFlags additionalFlags = FileFlags.None)
    {
        FileFlags fileFlags = this.CopyFileFlags | additionalFlags;

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
    public delegate bool DetermineToCopyCallback(DirectoryPathInfo dir, FileSystemEntity entity);
    public delegate bool DetermineToDeleteCallback(FileSystemEntity entity);

    async Task<bool> DefaultProgressCallback(CopyDirectoryStatus status, FileSystemEntity entity)
    {
        if (this.CopyDirFlags.Bit(CopyDirectoryFlags.SilenceSuccessfulReport) == false)
        {
            Con.WriteInfo($"Copying: '{entity.FullPath}'");
        }

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
    public DetermineToCopyCallback? DetermineToCopyCallbackProc { get; }
    public DetermineToDeleteCallback? DetermineToDeleteCallbackProc { get; }

    public CopyDirectoryParams(CopyDirectoryFlags copyDirFlags = CopyDirectoryFlags.Default, FileFlags copyFileFlags = FileFlags.None,
        FileMetadataCopier? dirMetadataCopier = null, FileMetadataCopier? fileMetadataCopier = null,
        int bufferSize = 0, int ignoreReadErrorSectorSize = 0,
        ProgressReporterFactoryBase? entireReporterFactory = null, ProgressReporterFactoryBase? fileReporterFactory = null,
        ProgressCallback? progressCallback = null,
        ExceptionCallback? exceptionCallback = null,
        DetermineToCopyCallback? determineToCopyCallback = null,
        DetermineToDeleteCallback? determineToDeleteCallback = null)
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
            this.CopyFileFlags |= FileFlags.BackupMode;

        if (this.CopyDirFlags.Bit(CopyDirectoryFlags.Overwrite))
            this.CopyFileFlags |= FileFlags.ForceClearReadOnlyOrHiddenBitsOnNeed;

        this.CopyFileFlags |= FileFlags.AutoCreateDirectory;

        this.ExceptionCallbackProc = exceptionCallback;
        this.ProgressCallbackProc = progressCallback;
        this.DetermineToCopyCallbackProc = determineToCopyCallback;
        this.DetermineToDeleteCallbackProc = determineToDeleteCallback;

        this.IgnoreReadErrorSectorSize = ignoreReadErrorSectorSize;
    }

}

[Flags]
public enum EncryptOption
{
    None = 0,
    Encrypt = 1,
    Decrypt = 2,
    Compress = 4,
}

public class CopyFileParams
{
    public static FileMetadataCopier DefaultFileMetadataCopier { get; } = new FileMetadataCopier(FileMetadataCopyMode.Default);

    public bool Overwrite { get; }
    public FileFlags Flags { get; }
    public FileMetadataCopier MetadataCopier { get; }
    public int BufferSize { get; }
    public bool AsyncCopy { get; }
    public bool IgnoreReadError { get; }
    public int IgnoreReadErrorSectorSize { get; }
    public EncryptOption EncryptOption { get; }
    public string EncryptPassword { get; }
    public bool DeleteFileIfVerifyFailed { get; }
    public bool EnsureBufferSize { get; }
    public int RetryCount { get; }
    public bool CalcDigest { get; }

    public ProgressReporterFactoryBase ProgressReporterFactory { get; }

    public static ProgressReporterFactoryBase NullReporterFactory { get; } = new NullReporterFactory();
    public static ProgressReporterFactoryBase ConsoleReporterFactory { get; } = new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.Console, options: ProgressReporterOptions.EnableThroughput);
    public static ProgressReporterFactoryBase DebugReporterFactory { get; } = new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.Debug, options: ProgressReporterOptions.EnableThroughput);

    public CopyFileParams(bool overwrite = true, FileFlags flags = FileFlags.None, FileMetadataCopier? metadataCopier = null, int bufferSize = 0, bool asyncCopy = true,
        bool ignoreReadError = false, int ignoreReadErrorSectorSize = 0,
        ProgressReporterFactoryBase? reporterFactory = null, EncryptOption encryptOption = EncryptOption.None, string encryptPassword = "",
        bool deleteFileIfVerifyFailed = false, bool ensureBufferSize = false, int retryCount = 3, bool calcDigest = false)
    {
        if (metadataCopier == null) metadataCopier = DefaultFileMetadataCopier;
        if (bufferSize <= 0) bufferSize = CoresConfig.FileUtilSettings.FileCopyBufferSize;
        if (reporterFactory == null) reporterFactory = NullReporterFactory;
        if (ignoreReadErrorSectorSize <= 0) ignoreReadErrorSectorSize = CoresConfig.FileUtilSettings.DefaultSectorSize;
        if (retryCount < 0) retryCount = 0;

        this.Overwrite = overwrite;
        this.Flags = flags;
        this.MetadataCopier = metadataCopier;
        this.BufferSize = bufferSize;
        this.AsyncCopy = asyncCopy;
        this.ProgressReporterFactory = reporterFactory;
        this.IgnoreReadError = ignoreReadError;
        this.IgnoreReadErrorSectorSize = ignoreReadErrorSectorSize;

        this.EncryptOption = encryptOption;
        this.EncryptPassword = encryptPassword._NonNull();
        this.DeleteFileIfVerifyFailed = deleteFileIfVerifyFailed;
        this.EnsureBufferSize = ensureBufferSize;
        this.RetryCount = retryCount;
        this.CalcDigest = calcDigest;
    }
}


public abstract partial class FileSystem
{
    public async Task CopyFileAsync(string srcPath, string destPath, CopyFileParams? param = null, object? state = null, CancellationToken cancel = default, FileSystem? destFileSystem = null, RefBool? readErrorIgnored = null, FileMetadata? newFileMeatadata = null, RefBool? errorOccuredButRecovered = null, Ref<string>? digest = null)
    {
        if (destFileSystem == null) destFileSystem = this;

        await FileUtil.CopyFileAsync(this, srcPath, destFileSystem, destPath, param, state, cancel, readErrorIgnored, newFileMeatadata, errorOccuredButRecovered, digest);
    }
    public void CopyFile(string srcPath, string destPath, CopyFileParams? param = null, object? state = null, CancellationToken cancel = default, FileSystem? destFileSystem = null, RefBool? readErrorIgnored = null, FileMetadata? newFileMeatadata = null, RefBool? errorOccuredButRecovered = null, Ref<string>? digest = null)
        => CopyFileAsync(srcPath, destPath, param, state, cancel, destFileSystem, readErrorIgnored, newFileMeatadata, errorOccuredButRecovered, digest)._GetResult();

    public async Task<CopyDirectoryStatus> CopyDirAsync(string srcPath, string destPath, FileSystem? destFileSystem = null,
        CopyDirectoryParams? param = null, object? state = null, CopyDirectoryStatus? statusObject = null, CancellationToken cancel = default)
    {
        if (destFileSystem == null) destFileSystem = this;

        return await FileUtil.CopyDirAsync(this, srcPath, destFileSystem, destPath, param, state, statusObject, cancel);
    }
    public CopyDirectoryStatus CopyDir(string srcPath, string destPath, FileSystem? destFileSystem = null,
        CopyDirectoryParams? param = null, object? state = null, CopyDirectoryStatus? statusObject = null, CancellationToken cancel = default)
        => CopyDirAsync(srcPath, destPath, destFileSystem, param, state, statusObject, cancel)._GetResult();
}

public static partial class FileUtil
{
    public static async Task<byte[]> CalcFileHashAsync(FilePath file, HashAlgorithm hash, long truncateSize = long.MaxValue, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? fileSize = null, CancellationToken cancel = default)
    {
        using var f = await file.OpenAsync(cancel: cancel);
        using var stream = f.GetStream();
        return await Secure.CalcStreamHashAsync(stream, hash, truncateSize, bufferSize, fileSize, cancel);
    }

    public static async Task<ResultOrError<int>> CompareFileHashAsync(FilePath file1, FilePath file2, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? fileSize = null, CancellationToken cancel = default, Ref<Exception?>? exception = null, Ref<string>? hashStr1 = null, Ref<string>? hashStr2 = null)
    {
        if (exception == null) exception = new Ref<Exception?>();
        exception.Set(null);

        if (hashStr1 == null) hashStr1 = new Ref<string>();
        if (hashStr2 == null) hashStr2 = new Ref<string>();
        hashStr1.Set("");
        hashStr2.Set("");

        using MD5 md5 = MD5.Create();

        byte[] hash1, hash2;

        try
        {
            if (await file1.IsFileExistsAsync(cancel) == false)
            {
                exception.Set(new CoresException($"File '{file1}' not found"));

                return new ResultOrError<int>(EnsureError.Error);
            }

            hash1 = await CalcFileHashAsync(file1, md5, bufferSize: bufferSize, fileSize: fileSize, cancel: cancel);
        }
        catch (Exception ex)
        {
            exception.Set(new CoresException($"File '{file1}': {ex.Message}"));

            return new ResultOrError<int>(EnsureError.Error);
        }

        try
        {
            if (await file2.IsFileExistsAsync(cancel) == false)
            {
                exception.Set(new CoresException($"File '{file2}' not found"));

                return new ResultOrError<int>(EnsureError.Error);
            }

            hash2 = await CalcFileHashAsync(file2, md5, bufferSize: bufferSize, cancel: cancel);
        }
        catch (Exception ex)
        {
            exception.Set(new CoresException($"File '{file2}': {ex.Message}"));

            return new ResultOrError<int>(EnsureError.Error);
        }

        hashStr1.Set(hash1._GetHexString());
        hashStr2.Set(hash2._GetHexString());

        return hash1._MemCompare(hash2);
    }

    public static async Task<ResultOrError<int>> CompareEncryptedFileHashAsync(string encryptPassword, bool isCompressed, FilePath filePlain, FilePath fileEncrypted, int bufferSize = Consts.Numbers.DefaultLargeBufferSize, RefLong? fileSize = null, CancellationToken cancel = default, Ref<Exception?>? exception = null, Ref<string>? hashStr1 = null, Ref<string>? hashStr2 = null)
    {
        if (exception == null) exception = new Ref<Exception?>();
        exception.Set(null);

        if (hashStr1 == null) hashStr1 = new Ref<string>();
        if (hashStr2 == null) hashStr2 = new Ref<string>();
        hashStr1.Set("");
        hashStr2.Set("");

        using MD5 md5 = MD5.Create();

        byte[] hash1, hash2;

        try
        {
            if (await filePlain.IsFileExistsAsync(cancel) == false)
            {
                exception.Set(new CoresException($"File '{filePlain}' not found"));

                return new ResultOrError<int>(EnsureError.Error);
            }

            hash1 = await CalcFileHashAsync(filePlain, md5, bufferSize: bufferSize, fileSize: fileSize, cancel: cancel);
        }
        catch (Exception ex)
        {
            exception.Set(new CoresException($"File '{filePlain}': {ex.Message}"));

            return new ResultOrError<int>(EnsureError.Error);
        }

        try
        {
            if (await fileEncrypted.IsFileExistsAsync(cancel) == false)
            {
                exception.Set(new CoresException($"File '{fileEncrypted}' not found"));

                return new ResultOrError<int>(EnsureError.Error);
            }

            await using var fileEncryptedObject = await fileEncrypted.OpenAsync(cancel: cancel);
            await using var xts = new XtsAesRandomAccess(fileEncryptedObject, encryptPassword, disposeObject: true);
            await using Stream internalStream = isCompressed == false ? xts.GetStream(disposeTarget: true) : await xts.GetDecompressStreamAsync();

            hash2 = await Secure.CalcStreamHashAsync(internalStream, md5, bufferSize: bufferSize, cancel: cancel);
        }
        catch (Exception ex)
        {
            exception.Set(new CoresException($"File '{fileEncrypted}': {ex.Message}"));

            return new ResultOrError<int>(EnsureError.Error);
        }

        hashStr1.Set(hash1._GetHexString());
        hashStr2.Set(hash2._GetHexString());

        return hash1._MemCompare(hash2);
    }

    public static async Task<CopyDirectoryStatus> CopyDirAsync(FileSystem srcFileSystem, string srcPath, FileSystem destFileSystem, string destPath,
        CopyDirectoryParams? param = null, object? state = null, CopyDirectoryStatus? statusObject = null, CancellationToken cancel = default)
    {
        CopyDirectoryStatus status = statusObject ?? new CopyDirectoryStatus();
        status.Clear();

        status.State = state;
        status.StartTick = Tick64.Now;

        if (param == null)
            param = new CopyDirectoryParams();

        srcPath = await srcFileSystem.NormalizePathAsync(srcPath, cancel: cancel);
        destPath = await destFileSystem.NormalizePathAsync(destPath, cancel: cancel);

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
                                FileFlags copyFileAdditionalFlags = FileFlags.None;

                                lock (status.LockObj)
                                    status.SizeTotal += srcFileMetadata.Size;

                                if (param.CopyDirFlags.Bit(CopyDirectoryFlags.CopyFileCompressionFlag))
                                    if (srcFileMetadata.Attributes is FileAttributes attr)
                                        if (attr.Bit(FileAttributes.Compressed))
                                            copyFileAdditionalFlags |= FileFlags.OnCreateSetCompressionFlag;
                                        else
                                            copyFileAdditionalFlags |= FileFlags.OnCreateRemoveCompressionFlag;

                                if (param.CopyDirFlags.Bit(CopyDirectoryFlags.CopyFileSparseFlag))
                                    if (srcFileMetadata.Attributes is FileAttributes attr)
                                        if (attr.Bit(FileAttributes.SparseFile))
                                            copyFileAdditionalFlags |= FileFlags.SparseFile;

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
                                FileFlags copyDirFlags = FileFlags.None;

                                if (param.CopyDirFlags.Bit(CopyDirectoryFlags.BackupMode))
                                    copyDirFlags |= FileFlags.BackupMode;

                                if (param.CopyDirFlags.Bit(CopyDirectoryFlags.CopyDirectoryCompressionFlag))
                                    if (srcDirMetadata.Attributes is FileAttributes attr)
                                        if (attr.Bit(FileAttributes.Compressed))
                                            copyDirFlags |= FileFlags.OnCreateSetCompressionFlag;
                                        else
                                            copyDirFlags |= FileFlags.OnCreateRemoveCompressionFlag;

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
                                throw;
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

                    string destDirFullPath = destFileSystem.PathParser.Combine(destPath, srcFileSystem.PathParser.ConvertDirectorySeparatorToOtherSystem(dirInfo.RelativePath, destFileSystem.PathParser));

                    FileSystemEntity[] filesAndDirsInDestDir = null!;

                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.DeleteNotExistFiles) || param.CopyDirFlags.Bit(CopyDirectoryFlags.DeleteNotExistDirs))
                    {
                        filesAndDirsInDestDir = await destFileSystem.EnumDirectoryAsync(destDirFullPath, false, cancel: cancel);
                    }

                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.DeleteNotExistFiles))
                    {
                        // src に存在せず dest に存在するファイルをすべて削除する
                        var srcFilesSet = new HashSet<string>(entries.Where(e => e.IsFile).Select(e => e.Name), destFileSystem.PathParser.PathStringComparer);

                        var notExistFiles = filesAndDirsInDestDir.Where(x => x.IsFile && srcFilesSet.Contains(x.Name) == false);

                        foreach (var f in notExistFiles)
                        {
                            if (param.DetermineToDeleteCallbackProc == null || param.DetermineToDeleteCallbackProc(f))
                            {
                                try
                                {
                                    //$"Debug: Deleting non-exist file {f.FullPath}"._Error();
                                    await destFileSystem.DeleteFileAsync(f.FullPath, cancel: cancel);
                                }
                                catch (Exception ex)
                                {
                                    c.ThrowIfCancellationRequested();
                                    if (await param.ExceptionCallbackProc(status, f, ex) == false)
                                    {
                                        throw;
                                    }
                                }
                            }
                        }
                    }

                    if (param.CopyDirFlags.Bit(CopyDirectoryFlags.DeleteNotExistDirs))
                    {
                        // src に存在せず dest に存在するディレクトリをすべて削除する
                        var srcDirsSet = new HashSet<string>(entries.Where(e => e.IsDirectory && e.IsCurrentOrParentDirectory == false).Select(e => e.Name), destFileSystem.PathParser.PathStringComparer);

                        var notExistDirs = filesAndDirsInDestDir.Where(x => x.IsDirectory && x.IsCurrentOrParentDirectory == false && srcDirsSet.Contains(x.Name) == false);

                        foreach (var d in notExistDirs)
                        {
                            if (param.DetermineToDeleteCallbackProc == null || param.DetermineToDeleteCallbackProc(d))
                            {
                                try
                                {
                                    //$"Debug: Deleting non-exist dir {d.FullPath}"._Error();
                                    await destFileSystem.DeleteDirectoryAsync(d.FullPath, true, cancel, true);
                                }
                                catch (Exception ex)
                                {
                                    c.ThrowIfCancellationRequested();
                                    if (await param.ExceptionCallbackProc(status, d, ex) == false)
                                    {
                                        throw;
                                    }
                                }
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
                cancel,
                param.DetermineToCopyCallbackProc != null ? (d, f) => param.DetermineToCopyCallbackProc(d, f) : null
                );
        }

        status.EndTick = Tick64.Now;

        return status;
    }

    // Verify エラー時に再試行する機能を有するファイルコピー
    public static async Task CopyFileAsync(FileSystem srcFileSystem, string srcPath, FileSystem destFileSystem, string destPath,
        CopyFileParams? param = null, object? state = null, CancellationToken cancel = default, RefBool? readErrorIgnored = null, FileMetadata? newFileMeatadata = null, RefBool? errorOccuredButRecovered = null, Ref<string>? digest = null)
    {
        if (param == null)
            param = new CopyFileParams();

        bool errorOccured = false;

        await TaskUtil.RetryAsync(async c =>
        {
            try
            {
                await CopyFileCoreAsync(srcFileSystem, srcPath, destFileSystem, destPath, param, state, cancel, readErrorIgnored, newFileMeatadata, null, digest);
            }
            catch
            {
                errorOccured = true;
                throw;
            }

            return 0;
        },
        1000,
        param.RetryCount + 1,
        cancel,
        true);

        errorOccuredButRecovered?.Set(errorOccured);
    }

    // ファイルコピーの実体 (Verify エラー時に再試行しない)
    static async Task CopyFileCoreAsync(FileSystem srcFileSystem, string srcPath, FileSystem destFileSystem, string destPath,
        CopyFileParams? param = null, object? state = null, CancellationToken cancel = default, RefBool? readErrorIgnored = null, FileMetadata? newFileMeatadata = null, RefBool? verifyErrorOccured = null, Ref<string>? digest = null)
    {
        if (readErrorIgnored == null)
            readErrorIgnored = new RefBool(false);

        readErrorIgnored.Set(false);

        if (verifyErrorOccured == null)
            verifyErrorOccured = new RefBool(false);

        if (digest == null)
            digest = new Ref<string>();

        digest.Set("");

        verifyErrorOccured.Set(false);

        if (param == null)
            param = new CopyFileParams();

        HashAlgorithm? hash = null;

        if (param.CalcDigest)
        {
            hash = MD5.Create();
        }

        srcPath = await srcFileSystem.NormalizePathAsync(srcPath, cancel: cancel);
        destPath = await destFileSystem.NormalizePathAsync(destPath, cancel: cancel);

        if (srcFileSystem == destFileSystem)
            if (srcFileSystem.PathParser.PathStringComparer.Equals(srcPath, destPath))
                throw new FileException(destPath, "Both source and destination is the same file.");

        using (ProgressReporterBase reporter = param.ProgressReporterFactory.CreateNewReporter($"CopyFile '{srcFileSystem.PathParser.GetFileName(srcPath)}'", state))
        {
            if (param.Flags.Bit(FileFlags.WriteOnlyIfChanged))
            {
                // 新旧両方のファイルが存在する場合、ファイル内容が同一かチェックし、同一の場合はコピーをスキップする
                RefLong skippedFileSize = new RefLong();

                ResultOrError<int> sameRet;

                if (param.EncryptOption.Bit(EncryptOption.Encrypt))
                {
                    sameRet = await CompareEncryptedFileHashAsync(param.EncryptPassword, param.EncryptOption.Bit(EncryptOption.Compress), new FilePath(srcPath, srcFileSystem, flags: param.Flags), new FilePath(destPath, destFileSystem, flags: param.Flags), fileSize: skippedFileSize, cancel: cancel);
                }
                else if (param.EncryptOption.Bit(EncryptOption.Decrypt))
                {
                    sameRet = await CompareEncryptedFileHashAsync(param.EncryptPassword, param.EncryptOption.Bit(EncryptOption.Compress), new FilePath(destPath, destFileSystem, flags: param.Flags), new FilePath(srcPath, srcFileSystem, flags: param.Flags), fileSize: skippedFileSize, cancel: cancel);
                }
                else
                {
                    sameRet = await CompareFileHashAsync(new FilePath(srcPath, srcFileSystem, flags: param.Flags), new FilePath(destPath, destFileSystem, flags: param.Flags), fileSize: skippedFileSize, cancel: cancel);
                }

                if (sameRet.IsOk && sameRet.Value == 0)
                {
                    // 同じファイルなのでスキップする
                    FileMetadata srcFileMetadata = await srcFileSystem.GetFileMetadataAsync(srcPath, param.MetadataCopier.OptimizedMetadataGetFlags, cancel);
                    try
                    {
                        await destFileSystem.SetFileMetadataAsync(destPath, param.MetadataCopier.Copy(srcFileMetadata), cancel);
                    }
                    catch (Exception ex)
                    {
                        Con.WriteDebug($"CopyFileAsync: '{destPath}': SetFileMetadataAsync failed. Error: {ex.Message}");
                    }

                    reporter.ReportProgress(new ProgressData(skippedFileSize, skippedFileSize, true));

                    return;
                }
            }

            await using (var srcFile = await srcFileSystem.OpenAsync(srcPath, flags: param.Flags, cancel: cancel))
            {
                try
                {
                    FileMetadata srcFileMetadata = await srcFileSystem.GetFileMetadataAsync(srcPath, param.MetadataCopier.OptimizedMetadataGetFlags, cancel);

                    bool destFileExists = await destFileSystem.IsFileExistsAsync(destPath, cancel);

                    await using (var destFile = await destFileSystem.CreateAsync(destPath, flags: param.Flags, doNotOverwrite: !param.Overwrite, cancel: cancel))
                    {
                        try
                        {
                            reporter.ReportProgress(new ProgressData(0, srcFileMetadata.Size));

                            Ref<uint> srcZipCrc = new Ref<uint>();

                            if (param.EncryptOption.Bit(EncryptOption.Encrypt) || param.EncryptOption.Bit(EncryptOption.Decrypt))
                            {
                                long copiedSize2 = 0;

                                // Encryption or Decryption
                                Stream? srcStream = null;
                                Stream? destStream = null;
                                XtsAesRandomAccess? xts = null;

                                try
                                {
                                    if (param.EncryptOption.Bit(EncryptOption.Encrypt))
                                    {
                                        // Encryption
                                        srcStream = srcFile.GetStream(disposeTarget: true);

                                        xts = new XtsAesRandomAccess(destFile, param.EncryptPassword, disposeObject: true);

                                        if (param.EncryptOption.Bit(EncryptOption.Compress) == false)
                                        {
                                            destStream = xts.GetStream(disposeTarget: true);
                                        }
                                        else
                                        {
                                            destStream = await xts.GetCompressStreamAsync();
                                        }
                                    }
                                    else
                                    {
                                        // Decryption
                                        destStream = destFile.GetStream(disposeTarget: true);

                                        xts = new XtsAesRandomAccess(srcFile, param.EncryptPassword, disposeObject: true);

                                        if (param.EncryptOption.Bit(EncryptOption.Compress) == false)
                                        {
                                            srcStream = xts.GetStream(disposeTarget: true);
                                        }
                                        else
                                        {
                                            srcStream = await xts.GetDecompressStreamAsync();
                                        }
                                    }

                                    copiedSize2 = await CopyBetweenStreamAsync(srcStream, destStream, param, reporter, srcFileMetadata.Size, cancel, readErrorIgnored, srcZipCrc, hash: hash);
                                }
                                finally
                                {
                                    await destStream._DisposeSafeAsync();
                                    await xts._DisposeSafeAsync();
                                    await srcStream._DisposeSafeAsync();
                                }

                                srcStream = null;
                                destStream = null;
                                xts = null;

                                if (param.Flags.Bit(FileFlags.CopyFile_Verify) && param.IgnoreReadError == false)
                                {
                                    await using var srcFile2 = await srcFileSystem.OpenAsync(srcPath, flags: param.Flags, cancel: cancel);

                                    verifyErrorOccured.Set(false);

                                    try
                                    {
                                        // Verify を実施する
                                        // キャッシュを無効にするため、一度ファイルを閉じて再度開く
                                        // NoCheckFileSize を付けないと、一部の Windows クライアントと一部の Samba サーバーとの間でヘンなエラーが発生する。

                                        await using (var destFile2 = await destFileSystem.OpenAsync(destPath, flags: param.Flags | FileFlags.NoCheckFileSize, cancel: cancel))
                                        {

                                            try
                                            {
                                                if (param.EncryptOption.Bit(EncryptOption.Encrypt))
                                                {
                                                    // Encryption
                                                    xts = new XtsAesRandomAccess(destFile2, param.EncryptPassword, disposeObject: true);

                                                    if (param.EncryptOption.Bit(EncryptOption.Compress) == false)
                                                    {
                                                        destStream = xts.GetStream(disposeTarget: true);
                                                    }
                                                    else
                                                    {
                                                        destStream = await xts.GetDecompressStreamAsync();
                                                    }
                                                }
                                                else
                                                {
                                                    // Decryption
                                                    destStream = destFile2.GetStream(disposeTarget: true);
                                                }

                                                uint destZipCrc = await CalcZipCrc32HandleAsync(destStream, param, cancel);

                                                if (srcZipCrc.Value != destZipCrc)
                                                {
                                                    // なんと Verify に失敗したぞ
                                                    verifyErrorOccured.Set(true);
                                                    throw new CoresException($"CopyFile_Verify error. Src file: '{srcPath}', Dest file: '{destPath}', srcCrc: {srcZipCrc.Value}, destCrc = {destZipCrc}");
                                                }
                                            }
                                            finally
                                            {
                                                await xts._DisposeSafeAsync();
                                                await srcStream._DisposeSafeAsync();
                                                await destStream._DisposeSafeAsync();
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        if (verifyErrorOccured)
                                        {
                                            if (param.DeleteFileIfVerifyFailed)
                                            {
                                                try
                                                {
                                                    await destFileSystem.DeleteFileAsync(destPath, flags: param.Flags, cancel: cancel);
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }

                                reporter.ReportProgress(new ProgressData(copiedSize2, copiedSize2, true));
                            }
                            else
                            {
                                long copiedSize = await CopyBetweenFileBaseAsync(srcFile, destFile, param, reporter, srcFileMetadata.Size, cancel, readErrorIgnored, srcZipCrc, hash: hash);

                                if (param.Flags.Bit(FileFlags.CopyFile_Verify) && param.IgnoreReadError == false)
                                {
                                    // Verify を実施する
                                    // キャッシュを無効にするため、一度ファイルを閉じて再度開く

                                    await destFile.CloseAsync();

                                    verifyErrorOccured.Set(false);

                                    try
                                    {
                                        // NoCheckFileSize を付けないと、一部の Windows クライアントと一部の Samba サーバーとの間でヘンなエラーが発生する。
                                        await using (var destFile2 = await destFileSystem.OpenAsync(destPath, flags: param.Flags | FileFlags.NoCheckFileSize, cancel: cancel))
                                        {
                                            uint destZipCrc = await CalcZipCrc32HandleAsync(destFile2, param, cancel);

                                            if (srcZipCrc.Value != destZipCrc)
                                            {
                                                // なんと Verify に失敗したぞ
                                                verifyErrorOccured.Set(true);
                                                throw new CoresException($"CopyFile_Verify error. Src file: '{srcPath}', Dest file: '{destPath}', srcCrc: {srcZipCrc.Value}, destCrc = {destZipCrc}");
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        if (verifyErrorOccured)
                                        {
                                            if (param.DeleteFileIfVerifyFailed)
                                            {
                                                try
                                                {
                                                    await destFileSystem.DeleteFileAsync(destPath, flags: param.Flags, cancel: cancel);
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }

                                reporter.ReportProgress(new ProgressData(copiedSize, copiedSize, true));
                            }

                            await destFile.CloseAsync();

                            try
                            {
                                await destFileSystem.SetFileMetadataAsync(destPath, newFileMeatadata ?? param.MetadataCopier.Copy(srcFileMetadata), cancel);
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
                                    await destFile.CloseAsync();
                                }
                                catch { }

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

        if (hash != null)
        {
            hash.TransformFinalBlock(new byte[0], 0, 0);

            digest.Set(hash.Hash!._GetHexString());
        }
    }
    public static void CopyFile(FileSystem srcFileSystem, string srcPath, FileSystem destFileSystem, string destPath,
        CopyFileParams? param = null, object? state = null, CancellationToken cancel = default, RefBool? readErrorIgnored = null, FileMetadata? newFileMeatadata = null, RefBool? errorOccuredButRecovered = null, Ref<string>? digest = null)
        => CopyFileAsync(srcFileSystem, srcPath, destFileSystem, destPath, param, state, cancel, readErrorIgnored, newFileMeatadata, errorOccuredButRecovered, digest)._GetResult();


    public static Task CopyFileAsync(FilePath src, FilePath dest, CopyFileParams? param = null, object? state = null, CancellationToken cancel = default, RefBool? readErrorIgnored = null, FileMetadata? newFileMeatadata = null, RefBool? errorOccuredButRecovered = null)
        => CopyFileAsync(src.FileSystem, src.PathString, dest.FileSystem, dest.PathString, param, state, cancel, readErrorIgnored, newFileMeatadata, errorOccuredButRecovered);

    public static void CopyFile(FilePath src, FilePath dest, CopyFileParams? param = null, object? state = null, CancellationToken cancel = default, RefBool? readErrorIgnored = null, FileMetadata? newFileMeatadata = null, RefBool? errorOccuredButRecovered = null)
        => CopyFileAsync(src, dest, param, state, cancel, readErrorIgnored, newFileMeatadata, errorOccuredButRecovered)._GetResult();

    static async Task<uint> CalcZipCrc32HandleAsync(FileBase src, CopyFileParams param, CancellationToken cancel)
    {
        ZipCrc32 srcCrc = new ZipCrc32();

        using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer))
        {
            while (true)
            {
                int readSize = await src.ReadAsync(buffer, cancel);

                Debug.Assert(readSize <= buffer.Length);

                if (readSize <= 0) break;

                ReadOnlyMemory<byte> sliced = buffer.Slice(0, readSize);

                srcCrc.Append(sliced.Span);
            }
        }

        return srcCrc.Value;
    }

    static async Task<uint> CalcZipCrc32HandleAsync(Stream src, CopyFileParams param, CancellationToken cancel)
    {
        ZipCrc32 srcCrc = new ZipCrc32();

        using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer))
        {
            while (true)
            {
                int readSize = await src.ReadAsync(buffer, cancel);

                Debug.Assert(readSize <= buffer.Length);

                if (readSize <= 0) break;

                ReadOnlyMemory<byte> sliced = buffer.Slice(0, readSize);

                srcCrc.Append(sliced.Span);
            }
        }

        return srcCrc.Value;
    }

    public static async Task<long> EraseFileBaseAsync(FileBase dest, CopyFileParams? param = null, ProgressReporterBase? reporter = null,
        long totalSize = -1, CancellationToken cancel = default, RefBool? readErrorIgnored = null, Ref<uint>? srcZipCrc = null)
    {
        if (param == null) param = new CopyFileParams();
        if (reporter == null) reporter = new NullProgressReporter(null);
        if (readErrorIgnored == null) readErrorIgnored = new RefBool();
        if (srcZipCrc == null) srcZipCrc = new Ref<uint>();

        long destSize = dest.Size;

        if (totalSize < 0) totalSize = destSize;

        totalSize = Math.Min(totalSize, destSize);

        ZipCrc32 srcCrc = new ZipCrc32();

        readErrorIgnored.Set(false);

        checked
        {
            long currentPosition = 0;

            using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer))
            {
                while (true)
                {
                    Memory<byte> thisTimeBuffer = buffer;

                    // Truncate
                    long remainSize = Math.Max(totalSize - currentPosition, 0);

                    if (thisTimeBuffer.Length > remainSize)
                    {
                        thisTimeBuffer = thisTimeBuffer.Slice(0, (int)remainSize);
                    }

                    if (remainSize == 0) break;

                    await dest.WriteAsync(thisTimeBuffer, cancel);

                    currentPosition += thisTimeBuffer.Length;
                    reporter.ReportProgress(new ProgressData(currentPosition, totalSize));
                }
            }

            srcZipCrc.Set(srcCrc.Value);

            return currentPosition;
        }
    }

    public static async Task<long> CopyBetweenFileBaseAsync(FileBase src, FileBase dest, CopyFileParams? param = null, ProgressReporterBase? reporter = null,
        long estimatedSize = -1, CancellationToken cancel = default, RefBool? readErrorIgnored = null, Ref<uint>? srcZipCrc = null, long truncateSize = -1, HashAlgorithm? hash = null)
    {
        if (param == null) param = new CopyFileParams();
        if (reporter == null) reporter = new NullProgressReporter(null);
        if (readErrorIgnored == null) readErrorIgnored = new RefBool();
        if (srcZipCrc == null) srcZipCrc = new Ref<uint>();
        if (estimatedSize < 0) estimatedSize = src.Size;

        if (truncateSize >= 0)
        {
            estimatedSize = Math.Min(estimatedSize, truncateSize);
        }

        ZipCrc32 srcCrc = new ZipCrc32();

        readErrorIgnored.Set(false);

        checked
        {
            long currentPosition = 0;

            if (param.IgnoreReadError)
            {
                long fileSize = src.Size;
                int errorCounter = 0;

                if (truncateSize >= 0)
                    fileSize = truncateSize; // Truncate

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
                        Memory<byte> thisTimeBuffer = buffer;

                        if (truncateSize >= 0)
                        {
                            // Truncate
                            long remainSize = Math.Max(truncateSize - currentPosition, 0);

                            if (thisTimeBuffer.Length > remainSize)
                            {
                                thisTimeBuffer = thisTimeBuffer.Slice(0, (int)remainSize);
                            }

                            if (remainSize == 0) break;
                        }

                        int readSize = await src.ReadAsync(thisTimeBuffer, cancel);

                        Debug.Assert(readSize <= thisTimeBuffer.Length);

                        if (readSize <= 0) break;

                        ReadOnlyMemory<byte> sliced = thisTimeBuffer.Slice(0, readSize);

                        if (param.Flags.Bit(FileFlags.CopyFile_Verify))
                        {
                            srcCrc.Append(sliced.Span);
                        }

                        if (hash != null)
                        {
                            var seg = sliced._AsSegment();

                            hash.TransformBlock(seg.Array!, seg.Offset, seg.Count, null, 0);
                        }

                        await dest.WriteAsync(sliced, cancel);

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
                        Task? lastWriteTask = null;
                        int number = 0;
                        int writeSize = 0;

                        long currentReadPosition = 0;

                        Memory<byte>[] buffers = new Memory<byte>[2] { buffer1, buffer2 };

                        while (true)
                        {
                            Memory<byte> buffer = buffers[(number++) % 2];

                            Memory<byte> thisTimeBuffer = buffer;

                            if (truncateSize >= 0)
                            {
                                // Truncate
                                long remainSize = Math.Max(truncateSize - currentReadPosition, 0);

                                if (thisTimeBuffer.Length > remainSize)
                                {
                                    thisTimeBuffer = thisTimeBuffer.Slice(0, (int)remainSize);
                                }
                            }

                            int readSize = await src.ReadAsync(thisTimeBuffer, cancel);

                            Debug.Assert(readSize <= buffer.Length);

                            if (lastWriteTask != null)
                            {
                                await lastWriteTask;
                                currentPosition += writeSize;
                                reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                            }

                            if (readSize <= 0) break;

                            currentReadPosition += readSize;

                            writeSize = readSize;

                            ReadOnlyMemory<byte> sliced = buffer.Slice(0, writeSize);

                            if (param.Flags.Bit(FileFlags.CopyFile_Verify))
                            {
                                srcCrc.Append(sliced.Span);
                            }

                            if (hash != null)
                            {
                                var seg = sliced._AsSegment();

                                hash.TransformBlock(seg.Array!, seg.Offset, seg.Count, null, 0);
                            }

                            lastWriteTask = dest.WriteAsync(sliced, cancel);
                        }

                        reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                    }
                }
            }

            srcZipCrc.Set(srcCrc.Value);

            return currentPosition;
        }
    }

    public static async Task<long> CopyBetweenStreamAsync(Stream src, Stream dest, CopyFileParams? param = null, ProgressReporterBase? reporter = null,
        long estimatedSize = -1, CancellationToken cancel = default, RefBool? readErrorIgnored = null, Ref<uint>? srcZipCrc = null, long truncateSize = -1, HashAlgorithm? hash = null)
    {
        if (param == null) param = new CopyFileParams();
        if (reporter == null) reporter = new NullProgressReporter(null);
        if (readErrorIgnored == null) readErrorIgnored = new RefBool();
        if (srcZipCrc == null) srcZipCrc = new Ref<uint>();

        if (estimatedSize < 0)
        {
            try
            {
                estimatedSize = src.Length;
            }
            catch
            {
                // Gzip 等の圧縮ストリーム等でサイズ取得に失敗するものも存在する
                estimatedSize = -1;
            }
        }

        if (truncateSize >= 0)
        {
            estimatedSize = Math.Min(estimatedSize, truncateSize);
        }

        ZipCrc32 srcCrc = new ZipCrc32();

        readErrorIgnored.Set(false);

        checked
        {
            long currentPosition = 0;

            if (param.AsyncCopy == false)
            {
                // Normal copy
                using (MemoryHelper.FastAllocMemoryWithUsing(param.BufferSize, out Memory<byte> buffer))
                {
                    while (true)
                    {
                        Memory<byte> thisTimeBuffer = buffer;

                        if (truncateSize >= 0)
                        {
                            // Truncate
                            long remainSize = Math.Max(truncateSize - currentPosition, 0);

                            if (thisTimeBuffer.Length > remainSize)
                            {
                                thisTimeBuffer = thisTimeBuffer.Slice(0, (int)remainSize);
                            }

                            if (remainSize == 0) break;
                        }

                        int readSize;
                        if (param.EnsureBufferSize == false)
                        {
                            readSize = await src.ReadAsync(thisTimeBuffer, cancel);
                        }
                        else
                        {
                            readSize = await src._ReadAllAsync(thisTimeBuffer, cancel, true);
                        }

                        Debug.Assert(readSize <= thisTimeBuffer.Length);

                        if (readSize <= 0) break;

                        ReadOnlyMemory<byte> sliced = thisTimeBuffer.Slice(0, readSize);

                        if (param.Flags.Bit(FileFlags.CopyFile_Verify))
                        {
                            srcCrc.Append(sliced.Span);
                        }

                        if (hash != null)
                        {
                            var seg = sliced._AsSegment();

                            hash.TransformBlock(seg.Array!, seg.Offset, seg.Count, null, 0);
                        }

                        await dest.WriteAsync(sliced, cancel);

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
                        ValueTask? lastWriteTask = null;
                        int number = 0;
                        int writeSize = 0;

                        long currentReadPosition = 0;

                        Memory<byte>[] buffers = new Memory<byte>[2] { buffer1, buffer2 };

                        while (true)
                        {
                            Memory<byte> buffer = buffers[(number++) % 2];

                            Memory<byte> thisTimeBuffer = buffer;

                            if (truncateSize >= 0)
                            {
                                // Truncate
                                long remainSize = Math.Max(truncateSize - currentReadPosition, 0);

                                if (thisTimeBuffer.Length > remainSize)
                                {
                                    thisTimeBuffer = thisTimeBuffer.Slice(0, (int)remainSize);
                                }
                            }

                            int readSize;
                            if (param.EnsureBufferSize == false)
                            {
                                readSize = await src.ReadAsync(thisTimeBuffer, cancel);
                            }
                            else
                            {
                                readSize = await src._ReadAllAsync(thisTimeBuffer, cancel, true);
                            }

                            Debug.Assert(readSize <= buffer.Length);

                            if (lastWriteTask != null)
                            {
                                await lastWriteTask.Value;
                                currentPosition += writeSize;
                                reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                            }

                            if (readSize <= 0) break;

                            currentReadPosition += readSize;

                            writeSize = readSize;

                            ReadOnlyMemory<byte> sliced = buffer.Slice(0, writeSize);

                            if (param.Flags.Bit(FileFlags.CopyFile_Verify))
                            {
                                srcCrc.Append(sliced.Span);
                            }

                            if (hash != null)
                            {
                                var seg = sliced._AsSegment();

                                hash.TransformBlock(seg.Array!, seg.Offset, seg.Count, null, 0);
                            }

                            lastWriteTask = dest.WriteAsync(sliced, cancel);
                        }

                        reporter.ReportProgress(new ProgressData(currentPosition, estimatedSize));
                    }
                }
            }

            srcZipCrc.Set(srcCrc.Value);

            return currentPosition;
        }
    }

    // 指定されたディレクトリ内の最新のいくつかのサブディレクトリのみコピー (同期) し、他は削除する
    public static async Task SyncLatestFewDirsAsync(DirectoryPath srcDir, DirectoryPath dstDir, int num = 1, CancellationToken cancel = default)
    {
        num = Math.Max(num, 1);

        if (srcDir.PathString._IsSamei(dstDir.PathString))
        {
            throw new CoresException("srcDir == dstDir");
        }

        // コピー元ディレクトリを列挙し、日付の新しい順に num 個のディレクトリを列挙する (ディレクトリ内に何も入っていないディレクトリは除外する)
        KeyValueList<FileSystemEntity, DateTime> srcSubDirTargetList = new KeyValueList<FileSystemEntity, DateTime>();

        var srcSubDirs = (await srcDir.EnumDirectoryAsync(cancel: cancel)).Where(x => x.IsDirectory);
        foreach (var subDir in srcSubDirs)
        {
            cancel.ThrowIfCancellationRequested();

            var subFiles = await srcDir.FileSystem.EnumDirectoryAsync(subDir.FullPath, cancel: cancel);

            if (subFiles.Any())
            {
                if (Str.TryParseYYMMDDDirName(subDir.Name, out DateTime date))
                {
                    srcSubDirTargetList.Add(subDir, date);
                }
            }
        }

        var copySrcTargets = srcSubDirTargetList.OrderByDescending(x => x.Value).Take(num);

        // コピー先ディレクトリで YYMMDD 形式のディレクトリを列挙し、削除すべきディレクトリ一覧を生成する
        List<FileSystemEntity> dirListToDelete = new List<FileSystemEntity>();

        var dstSubDirs = (await dstDir.EnumDirectoryAsync(cancel: cancel)).Where(x => x.IsDirectory);
        foreach (var subDir in dstSubDirs)
        {
            cancel.ThrowIfCancellationRequested();

            if (Str.TryParseYYMMDDDirName(subDir.Name, out _))
            {
                if (copySrcTargets.Select(x => x.Key.Name).Where(x => x._IsSamei(subDir.Name)).Any() == false)
                {
                    dirListToDelete.Add(subDir);
                }
            }
        }

        // コピー対象ディレクトリをコピーする
        foreach (var copySrcDir in copySrcTargets.Select(x => x.Key))
        {
            await srcDir.FileSystem.CopyDirAsync(copySrcDir.FullPath, dstDir.Combine(copySrcDir.Name), dstDir.FileSystem,
                new CopyDirectoryParams(CopyDirectoryFlags.Default, FileFlags.WriteOnlyIfChanged),
                cancel: cancel);
        }

        // 削除すべき古いディレクトリを削除する
        foreach (var deleteDir in dirListToDelete.OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer))
        {
            $"Deleting old dir '{deleteDir.FullPath}' ..."._Print();
            try
            {
                await dstDir.FileSystem.DeleteDirectoryAsync(deleteDir.FullPath, true, cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
    }
}
