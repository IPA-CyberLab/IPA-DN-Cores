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
using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class LargeFileObject : FileObjectBase
    {
        public class Cursor
        {
            public long LogicalPosition { get; }
            public int PhysicalFileNumber { get; }
            public long PhysicalPosition { get; }
            public long PhysicalLength { get; }

            readonly LargeFileObject Lfo;

            public Cursor(LargeFileObject o, long logicalPosision)
            {
                checked
                {
                    Lfo = o;

                    if (logicalPosision < 0)
                        throw new ArgumentOutOfRangeException("logicalPosision < 0");

                    var p = Lfo.LargeFileSystem.Params;
                    if (logicalPosision > p.MaxLogicalFileSize)
                        throw new ArgumentOutOfRangeException("logicalPosision > MaxLogicalFileSize");

                    this.LogicalPosition = logicalPosision;
                    this.PhysicalFileNumber = (int)(this.LogicalPosition / p.MaxSinglePhysicalFileSize);

                    if (this.PhysicalFileNumber > p.MaxFileNumber)
                        throw new ArgumentOutOfRangeException($"this.PhysicalFileNumber ({this.PhysicalFileNumber})> p.MaxFileNumber ({p.MaxFileNumber})");

                    this.PhysicalPosition = this.LogicalPosition % p.MaxSinglePhysicalFileSize;
                    this.PhysicalLength = p.MaxSinglePhysicalFileSize - this.PhysicalPosition;
                }
            }

            public LargeFileSystem.ParsedPath GetParsedPath()
            {
                return new LargeFileSystem.ParsedPath(Lfo.LargeFileSystem, Lfo.FileParams.Path, this.PhysicalFileNumber);
            }
        }

        readonly LargeFileSystem LargeFileSystem;
        readonly FileSystemBase UnderlayFileSystem;
        readonly LargeFileSystem.ParsedPath[] InitialRelatedFiles;

        long CurrentFileSize;
        long CurrentPosition;

        protected LargeFileObject(FileSystemBase fileSystem, FileParameters fileParams, LargeFileSystem.ParsedPath[] relatedFiles) : base(fileSystem, fileParams)
        {
            this.LargeFileSystem = (LargeFileSystem)fileSystem;
            this.UnderlayFileSystem = this.LargeFileSystem.UnderlayFileSystem;
            this.InitialRelatedFiles = relatedFiles;
        }

        public static async Task<FileObjectBase> CreateFileAsync(LargeFileSystem fileSystem, FileParameters fileParams, LargeFileSystem.ParsedPath[] relatedFiles, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            LargeFileObject f = new LargeFileObject(fileSystem, fileParams, relatedFiles);

            await f.CreateAsync(cancel);

            return f;
        }

        protected override async Task CreateAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                var lastFileParsed = InitialRelatedFiles.OrderBy(x => x.FileNumber).LastOrDefault();

                if (lastFileParsed == null)
                {
                    // New file
                    CurrentFileSize = 0;

                    if (FileParams.Mode == FileMode.Open || FileParams.Mode == FileMode.Truncate)
                    {
                        throw new IOException($"The file '{FileParams.Path}' not found.");
                    }
                }
                else
                {
                    // File exists
                    if (FileParams.Mode == FileMode.CreateNew)
                    {
                        throw new IOException($"The file '{FileParams.Path}' already exists.");
                    }

                    checked
                    {
                        long sizeOfMaxFile = (await UnderlayFileSystem.GetFileMetadataAsync(lastFileParsed.PhysicalFilePath, cancel)).Size;
                        sizeOfMaxFile = Math.Max(sizeOfMaxFile, LargeFileSystem.Params.MaxSinglePhysicalFileSize);
                        CurrentFileSize = lastFileParsed.FileNumber * LargeFileSystem.Params.MaxSinglePhysicalFileSize + sizeOfMaxFile;
                    }
                }

                if (FileParams.Mode == FileMode.Create || FileParams.Mode == FileMode.CreateNew || FileParams.Mode == FileMode.Truncate)
                {
                    if (lastFileParsed != null)
                    {
                        // Delete the files first
                        await LargeFileSystem.DeleteFileAsync(FileParams.Path, cancel);

                        lastFileParsed = null;
                        CurrentFileSize = 0;
                    }
                }

                // Try to open or create the physical file which contains the tail
                using (await TryOpenPhysicalFile(this.CurrentFileSize, cancel))
                {
                }

                this.CurrentPosition = 0;
                if (FileParams.Mode == FileMode.Append)
                {
                    this.CurrentPosition = this.CurrentFileSize;
                }

                await base.CreateAsync(cancel);
            }
            catch
            {
                throw;
            }
        }

        async Task<RefObjectHandle<FileObjectBase>> TryOpenPhysicalFile(long logicalPosition, CancellationToken cancel)
        {
            var cursor = new Cursor(this, logicalPosition);

            var parsed = new LargeFileSystem.ParsedPath(LargeFileSystem, this.FileParams.Path, cursor.PhysicalFileNumber);

            if (FileParams.Access.Bit(FileAccess.Write))
            {
                return await LargeFileSystem.UnderlayFileSystemPool.OpenOrGetWithWriteModeAsync(parsed.PhysicalFilePath, cancel);
            }
            else
            {
                return await LargeFileSystem.UnderlayFileSystemPool.OpenOrGetWithReadModeAsync(parsed.PhysicalFilePath, cancel);
            }
        }

        protected override async Task CloseImplAsync()
        {
            await FlushImplAsync();
        }

        protected override async Task FlushImplAsync(CancellationToken cancel = default)
        {
            using (var refHandle = await TryOpenPhysicalFile(this.CurrentFileSize, cancel))
            {
                var file = refHandle.Object;

                await file.FlushAsync(cancel);
            }
        }

        protected override async Task<long> GetCurrentPositionImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;

            return this.CurrentPosition;
        }

        protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;

            return this.CurrentFileSize;
        }

        List<Cursor> GenerateCursorList(long position, long length)
        {
            checked
            {
                if (position < 0) throw new ArgumentOutOfRangeException("position");
                if (length < 0) throw new ArgumentOutOfRangeException("length");
                if (length == 0)
                {
                    return new List<Cursor>();
                }

                List<Cursor> ret = new List<Cursor>();

                long eof = position + length;

                while (position < eof)
                {
                    Cursor cursor = new Cursor(this, position);
                    ret.Add(cursor);

                    position += cursor.PhysicalLength;
                }

                return ret;
            }
        }

        protected override async Task<int> ReadImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                if (data.Length == 0) return 0;

                int totalLength = 0;

                List<Cursor> cursorList = GenerateCursorList(position, data.Length);

                foreach (Cursor cursor in cursorList)
                {
                    bool isLast = (cursor == cursorList.Last());

                    using (var refFile = await TryOpenPhysicalFile(cursor.LogicalPosition, cancel))
                    {
                        var file = refFile.Object;
                        int r = await file.ReadRandomAsync(cursor.PhysicalPosition, data.Slice((int)(cursor.LogicalPosition - position), (int)cursor.PhysicalLength), cancel);
                        if (r != (int)cursor.PhysicalLength)
                        {
                            if (isLast == false)
                            {
                                throw new ApplicationException($"Unable to read {cursor.PhysicalLength} bytes from offset {cursor.PhysicalPosition} of the physical file '{cursor.GetParsedPath().PhysicalFilePath}'.");
                            }
                        }

                        totalLength += r;
                    }
                }

                return totalLength;
            }
        }

        protected override async Task WriteImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            if (data.Length == 0) return;

            List<Cursor> cursorList = GenerateCursorList(position, data.Length);

            foreach (Cursor cursor in cursorList)
            {
                bool isLast = (cursor == cursorList.Last());

                using (var refFile = await TryOpenPhysicalFile(cursor.LogicalPosition, cancel))
                {
                    var file = refFile.Object;
                    await file.WriteRandomAsync(cursor.PhysicalPosition, data.Slice((int)(cursor.LogicalPosition - position), (int)cursor.PhysicalLength), cancel);
                }
            }
        }

        protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }
    }

    class LargeFileSystemParams
    {
        public const long DefaultMaxSinglePhysicalFileSize = 1000000;
        public const long DefaultMaxLogicalFileSize = 100000000000000; // 100TB
        public const int DefaultPooledFileCloseDelay = 1000;

        public long MaxSinglePhysicalFileSize { get; }
        public long MaxLogicalFileSize { get; }
        public int NumDigits { get; }
        public string SplitStr { get; }
        public int PooledFileCloseDelay { get; }
        public int MaxFileNumber { get; }

        public LargeFileSystemParams(long maxSingleFileSize = DefaultMaxSinglePhysicalFileSize, long logicalMaxSize = DefaultMaxLogicalFileSize, string splitStr = "~",
            int pooledFileCloseDelay = DefaultPooledFileCloseDelay)
        {
            checked
            {
                this.SplitStr = splitStr.NonNullTrim().Default("~");
                this.MaxSinglePhysicalFileSize = Math.Max(maxSingleFileSize, 1);
                this.PooledFileCloseDelay = Math.Max(pooledFileCloseDelay, 1000);

                long i = (int)(MaxLogicalFileSize / this.MaxSinglePhysicalFileSize);
                i = Math.Max(i, 1);
                i = (int)Math.Log10(i);
                i++;
                i = Math.Max(Math.Min(i, 9), 1);
                NumDigits = (int)i;

                this.MaxFileNumber = Str.MakeCharArray('9', NumDigits).ToInt();
                this.MaxLogicalFileSize = MaxSinglePhysicalFileSize * this.MaxFileNumber;
            }
        }
    }

    class LargeFileSystem : FileSystemBase
    {
        public class ParsedPath
        {
            public string DirectoryPath { get; }
            public string OriginalFileNameWithoutExtension { get; }
            public int FileNumber { get; }
            public string Extension { get; }
            public string PhysicalFilePath { get; }
            public string LogicalFilePath { get; }

            readonly LargeFileSystem fs;

            public ParsedPath(LargeFileSystem fs, string logicalFilePath, int fileNumber)
            {
                logicalFilePath = fs.UnderlayFileSystem.NormalizePath(logicalFilePath);
                this.LogicalFilePath = logicalFilePath;

                string fileName = fs.PathInterpreter.GetFileName(logicalFilePath);
                if (fileName.IndexOf(fs.Params.SplitStr) != -1)
                    throw new ApplicationException($"The original filename '{fileName}' contains '{fs.Params.SplitStr}'.");

                string dir = fs.PathInterpreter.GetDirectoryName(logicalFilePath);
                string filename = fs.PathInterpreter.GetFileName(logicalFilePath);
                string extension;
                int dotIndex = fileName.IndexOf('.');
                string filenameWithoutExtension;
                if (dotIndex != -1)
                {
                    extension = fileName.Substring(dotIndex);
                    filenameWithoutExtension = fileName.Substring(0, dotIndex);
                }
                else
                {
                    extension = "";
                    filenameWithoutExtension = fileName;
                }

                this.DirectoryPath = dir;
                this.OriginalFileNameWithoutExtension = filenameWithoutExtension;
                this.FileNumber = fileNumber;
                this.Extension = extension;
                this.PhysicalFilePath = GeneratePhysicalPath();
            }

            public ParsedPath(LargeFileSystem fs, string physicalFilePath)
            {
                this.fs = fs;
                physicalFilePath = fs.NormalizePath(physicalFilePath);

                this.PhysicalFilePath = physicalFilePath;

                string dir = fs.PathInterpreter.GetDirectoryName(physicalFilePath);
                string fn = fs.PathInterpreter.GetFileName(physicalFilePath);

                int[] indexes = fn.FindStringIndexes(fs.Params.SplitStr);
                if (indexes.Length != 1)
                    throw new ArgumentException($"Filename '{fn}' is not a large file. indexes.Length != 1.");

                string originalFileName = fn.Substring(0, indexes[0]);
                string afterOriginalFileName = fn.Substring(indexes[0] + 1);
                if (originalFileName.IsEmpty() || afterOriginalFileName.IsEmpty())
                    throw new ArgumentException($"Filename '{fn}' is not a large file.");

                string extension;
                int dotIndex = afterOriginalFileName.IndexOf('.');
                string digitsStr;
                if (dotIndex != -1)
                {
                    extension = afterOriginalFileName.Substring(dotIndex);
                    digitsStr = afterOriginalFileName.Substring(0, dotIndex);
                }
                else
                {
                    extension = "";
                    digitsStr = afterOriginalFileName;
                }

                if (digitsStr.IsNumber() == false)
                    throw new ArgumentException($"Filename '{fn}' is not a large file. digitsStr.IsNumber() == false.");

                if (digitsStr.Length != fs.Params.NumDigits)
                    throw new ArgumentException($"Filename '{fn}' is not a large file. digitsStr.Length != fs.Params.NumDigits.");

                this.DirectoryPath = dir;
                this.OriginalFileNameWithoutExtension = originalFileName;
                this.FileNumber = digitsStr.ToInt();
                this.Extension = extension;

                string filename = $"{OriginalFileNameWithoutExtension}{Extension}";
                this.LogicalFilePath = fs.PathInterpreter.Combine(this.DirectoryPath, filename);
            }

            public string GeneratePhysicalPath(int? fileNumberOverwrite = null)
            {
                int fileNumber = fileNumberOverwrite ?? FileNumber;
                string fileNumberStr = fileNumber.ToString($"D{fs.Params.NumDigits}");
                Debug.Assert(fileNumberStr.Length == fs.Params.NumDigits);

                string filename = $"{OriginalFileNameWithoutExtension}{fs.Params.SplitStr}{fileNumberStr}{Extension}";

                return fs.PathInterpreter.Combine(DirectoryPath, filename);
            }
        }

        CancellationTokenSource CancelSource = new CancellationTokenSource();
        CancellationToken CancelToken => CancelSource.Token;

        public FileSystemBase UnderlayFileSystem { get; }
        public LargeFileSystemParams Params { get; }

        AsyncLock AsyncLockObj = new AsyncLock();

        public FileSystemObjectPool UnderlayFileSystemPool { get; }

        public LargeFileSystem(AsyncCleanuperLady lady, FileSystemBase underlayFileSystem, LargeFileSystemParams param) : base(lady, underlayFileSystem.PathInterpreter)
        {
            this.UnderlayFileSystem = underlayFileSystem;
            this.Params = param;

            this.UnderlayFileSystemPool = new FileSystemObjectPool(this.UnderlayFileSystem, this.Params.PooledFileCloseDelay);
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
            => this.UnderlayFileSystem.NormalizePathAsync(path, cancel);

        protected override async Task<FileObjectBase> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            string fileName = this.PathInterpreter.GetFileName(option.Path);
            if (fileName.IndexOf(Params.SplitStr) != -1)
                throw new ApplicationException($"The original filename '{fileName}' contains '{Params.SplitStr}'.");

            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
            {
                using (await AsyncLockObj.LockWithAwait(operationCancel))
                {
                    cancel.ThrowIfCancellationRequested();

                    ParsedPath[] relatedFiles = await GetPhysicalFileStateInternal(option.Path, operationCancel);

                    return await LargeFileObject.CreateFileAsync(this, option, relatedFiles, operationCancel);
                }
            }
        }

        public async Task<ParsedPath[]> GetPhysicalFileStateInternal(string logicalFilePath, CancellationToken cancel)
        {
            List<ParsedPath> ret = new List<ParsedPath>();

            ParsedPath parsed = new ParsedPath(this, logicalFilePath, 0);

            FileSystemEntity[] dirEntities = await UnderlayFileSystem.EnumDirectoryAsync(parsed.DirectoryPath, false, cancel);

            var relatedFiles = dirEntities.Where(x => x.IsDirectory == false);
            foreach (var f in relatedFiles)
            {
                if (f.Name.StartsWith(parsed.OriginalFileNameWithoutExtension, PathInterpreter.PathStringComparison))
                {
                    ParsedPath parsedForFile = new ParsedPath(this, f.FullPath);
                    if (parsed.LogicalFilePath.IsSame(parsedForFile.LogicalFilePath, PathInterpreter.PathStringComparison))
                    {
                        ret.Add(parsedForFile);
                    }
                }
            }

            ret.Sort((x, y) => x.FileNumber.CompareTo(y.FileNumber));

            return ret.ToArray();
        }

        protected override Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => UnderlayFileSystem.CreateDirectoryAsync(directoryPath, flags, cancel);


        public bool TryParseOriginalPath(string physicalPath, out ParsedPath parsed)
        {
            try
            {
                parsed = new ParsedPath(this, physicalPath);
                return true;
            }
            catch
            {
                parsed = null;
                return false;
            }
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                CancelSource.TryCancelNoBlock();
                this.UnderlayFileSystemPool.DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }

        public override async Task _CleanupAsyncInternal()
        {
            try
            {
                // Here
            }
            finally { await base._CleanupAsyncInternal(); }
        }

        protected override Task<FileSystemMetadata> GetFileMetadataImplAsync(string path, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task DeleteFileImplAsync(string path, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }
    }
}
