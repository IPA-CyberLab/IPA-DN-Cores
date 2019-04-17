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
using static IPA.Cores.Globals.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    static partial class AppConfig
    {
        public static partial class LargeFileSystemSettings
        {
            public static readonly Copenhagen<LargeFileSystemParams> LocalLargeFileSystemParams
                = new LargeFileSystemParams(
                    maxSingleFileSize: 1_000_000_000,     // 1 TB
                    logicalMaxSize: 1_000_000_000_000_000_000, // 1 EB
                    splitStr: "~~~"
                    );
        }
    }

    class LargeFileObject : FileObject
    {
        public class Cursor
        {
            public long LogicalPosition { get; }
            public long PhysicalFileNumber { get; }
            public long PhysicalPosition { get; }
            public long PhysicalRemainingLength { get; }
            public long PhysicalDataLength { get; }

            readonly LargeFileObject Lfo;

            public Cursor(LargeFileObject o, long logicalPosision, long dataLength = 0)
            {
                checked
                {
                    Lfo = o;

                    if (logicalPosision < 0)
                        throw new ArgumentOutOfRangeException("logicalPosision < 0");

                    if (dataLength < 0)
                        throw new ArgumentOutOfRangeException("dataLength < 0");

                    var p = Lfo.LargeFileSystem.Params;
                    if (logicalPosision > p.MaxLogicalFileSize)
                        throw new ArgumentOutOfRangeException("logicalPosision > MaxLogicalFileSize");

                    this.LogicalPosition = logicalPosision;
                    this.PhysicalFileNumber = this.LogicalPosition / p.MaxSinglePhysicalFileSize;

                    if (this.PhysicalFileNumber > p.MaxFileNumber)
                        throw new ArgumentOutOfRangeException($"this.PhysicalFileNumber ({this.PhysicalFileNumber}) > p.MaxFileNumber ({p.MaxFileNumber})");

                    this.PhysicalPosition = this.LogicalPosition % p.MaxSinglePhysicalFileSize;
                    this.PhysicalRemainingLength = p.MaxSinglePhysicalFileSize - this.PhysicalPosition;
                    this.PhysicalDataLength = Math.Min(this.PhysicalRemainingLength, dataLength);
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

        protected LargeFileObject(FileSystemBase fileSystem, FileParameters fileParams, LargeFileSystem.ParsedPath[] relatedFiles) : base(fileSystem, fileParams)
        {
            this.LargeFileSystem = (LargeFileSystem)fileSystem;
            this.UnderlayFileSystem = this.LargeFileSystem.UnderlayFileSystem;
            this.InitialRelatedFiles = relatedFiles;
        }

        public static async Task<FileObject> CreateFileAsync(LargeFileSystem fileSystem, FileParameters fileParams, LargeFileSystem.ParsedPath[] relatedFiles, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            LargeFileObject f = new LargeFileObject(fileSystem, fileParams, relatedFiles);

            await f.InternalInitAsync(cancel);

            return f;
        }

        protected async Task InternalInitAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                bool newFille = false;

                var lastFileParsed = InitialRelatedFiles.OrderBy(x => x.FileNumber).LastOrDefault();

                if (lastFileParsed == null)
                {
                    // New file
                    CurrentFileSize = 0;
                    newFille = true;

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
                        long sizeOfLastFile = (await UnderlayFileSystem.GetFileMetadataAsync(lastFileParsed.PhysicalFilePath, FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoSecurity, cancel)).Size;
                        sizeOfLastFile = Math.Min(sizeOfLastFile, LargeFileSystem.Params.MaxSinglePhysicalFileSize);
                        CurrentFileSize = lastFileParsed.FileNumber * LargeFileSystem.Params.MaxSinglePhysicalFileSize + sizeOfLastFile;
                    }
                }

                if (FileParams.Mode == FileMode.Create || FileParams.Mode == FileMode.CreateNew || FileParams.Mode == FileMode.Truncate)
                {
                    if (lastFileParsed != null)
                    {
                        // Delete the files first
                        await LargeFileSystem.DeleteFileAsync(FileParams.Path, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);

                        lastFileParsed = null;
                        CurrentFileSize = 0;

                        newFille = true;
                    }
                }

                long currentPosition = 0;
                if (FileParams.Mode == FileMode.Append)
                    currentPosition = this.CurrentFileSize;

                InitAndCheckFileSizeAndPosition(currentPosition, await GetFileSizeImplAsync(cancel), cancel);

                if (newFille)
                {
                    using (var handle = await GetUnderleyRandomAccessHandle(0, cancel))
                    {
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        readonly Cache<string, LargeFileSystem.ParsedPath> ParsedPathCache = new Cache<string, LargeFileSystem.ParsedPath>(TimeSpan.FromMilliseconds(AppConfig.FileSystemSettings.PooledHandleLifetime.Value), CacheType.UpdateExpiresWhenAccess);

        async Task<RandomAccessHandle> GetUnderleyRandomAccessHandle(long logicalPosition, CancellationToken cancel)
        {
            var cursor = new Cursor(this, logicalPosition);

            string cacheKey = $"{this.FileParams.Path}:{cursor.PhysicalFileNumber}";

            var parsed = ParsedPathCache.GetOrCreate(cacheKey, x => new LargeFileSystem.ParsedPath(LargeFileSystem, this.FileParams.Path, cursor.PhysicalFileNumber));

            return await LargeFileSystem.UnderlayFileSystem.GetRandomAccessHandleAsync(parsed.PhysicalFilePath, FileParams.Access.Bit(FileAccess.Write), this.FileParams.Flags | FileOperationFlags.SparseFile, cancel);
        }

        protected override async Task CloseImplAsync()
        {
            if (FileParams.Access.Bit(FileAccess.Write))
            {
                await LargeFileSystem.UnderlayFileSystemPoolForWrite.EnumAndCloseHandlesAsync((key, file) =>
                {
                    var parsed = new LargeFileSystem.ParsedPath(LargeFileSystem, key);
                    if (parsed.LogicalFilePath.IsSame(this.FileParams.Path, LargeFileSystem.PathParser.PathStringComparison))
                    {
                        return true;
                    }
                    return false;
                });
            }
            else
            {
                await LargeFileSystem.UnderlayFileSystemPoolForRead.EnumAndCloseHandlesAsync((key, file) =>
                {
                    var parsed = new LargeFileSystem.ParsedPath(LargeFileSystem, key);
                    if (parsed.LogicalFilePath.IsSame(this.FileParams.Path, LargeFileSystem.PathParser.PathStringComparison))
                    {
                        return true;
                    }
                    return false;
                });
            }
        }

        protected override async Task FlushImplAsync(CancellationToken cancel = default)
        {
            using (var handle = await GetUnderleyRandomAccessHandle(this.CurrentFileSize, cancel))
            {
                await handle.FlushAsync(cancel);
            }
        }

        protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;

            return this.CurrentFileSize;
        }

        List<Cursor> GenerateCursorList(long position, long length, bool writeMode)
        {
            checked
            {
                if (position < 0) throw new ArgumentOutOfRangeException("position");
                if (length < 0) throw new ArgumentOutOfRangeException("length");

                if (writeMode == false)
                {
                    if (position > this.CurrentFileSize)
                        throw new ApplicationException("position > this.CurrentFileSize");

                    if (position + length > this.CurrentFileSize)
                    {
                        length = this.CurrentFileSize - position;
                    }
                }

                if (length == 0)
                {
                    return new List<Cursor>();
                }

                List<Cursor> ret = new List<Cursor>();

                long eof = position + length;

                while (position < eof)
                {
                    Cursor cursor = new Cursor(this, position, eof - position);
                    ret.Add(cursor);

                    position += cursor.PhysicalDataLength;
                }

                return ret;
            }
        }

        protected override async Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                if (data.Length == 0) return 0;

                int totalLength = 0;

                List<Cursor> cursorList = GenerateCursorList(position, data.Length, false);

                foreach (Cursor cursor in cursorList)
                {
                    bool isLast = (cursor == cursorList.Last());

                    RandomAccessHandle handle = null;

                    try
                    {
                        handle = await GetUnderleyRandomAccessHandle(cursor.LogicalPosition, cancel);
                    }
                    catch (FileNotFoundException) { }

                    var subMemory = data.Slice((int)(cursor.LogicalPosition - position), (int)cursor.PhysicalDataLength);

                    if (handle != null)
                    {
                        using (handle)
                        {
                            int r = await handle.ReadRandomAsync(cursor.PhysicalPosition, subMemory, cancel);

                            Debug.Assert(r <= (int)cursor.PhysicalDataLength);

                            if (r < (int)cursor.PhysicalDataLength)
                            {
                                var zeroClearMemory = subMemory.Slice(r);
                                zeroClearMemory.Span.Fill(0);
                            }

                            totalLength += (int)cursor.PhysicalDataLength;
                        }
                    }
                    else
                    {
                        subMemory.Span.Fill(0);

                        totalLength += (int)cursor.PhysicalDataLength;
                    }
                }

                return totalLength;
            }
        }

        protected override async Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                if (data.Length == 0) return;

                List<Cursor> cursorList = GenerateCursorList(position, data.Length, true);

                if (this.FileParams.Flags.BitAny(FileOperationFlags.LargeFs_AppendWithoutCrossBorder | FileOperationFlags.LargeFs_AppendNewLineForCrossBorder)
                    && position == this.CurrentFileSize && cursorList.Count >= 2)
                {
                    // Crossing the border when the LargeFileSystemAppendWithCrossBorder flag is set
                    if (cursorList.Count >= 3)
                    {
                        // Write fails because it beyonds two borders
                        throw new FileException(this.FileParams.Path, $"LargeFileSystemDoNotAppendBeyondBorder error: pos = {position}, data = {data.Length}");
                    }
                    else
                    {
                        Debug.Assert(data.Length <= this.LargeFileSystem.Params.MaxSinglePhysicalFileSize);

                        var firstCursor = cursorList[0];
                        var secondCursor = cursorList[1];

                        Debug.Assert(firstCursor.LogicalPosition == position);
                        Debug.Assert(secondCursor.LogicalPosition == (firstCursor.LogicalPosition + firstCursor.PhysicalRemainingLength));

                        using (var handle = await GetUnderleyRandomAccessHandle(position, cancel))
                        {
                            // Write the zero-cleared block toward the end of the first physical file
                            byte[] appendBytes = new byte[firstCursor.PhysicalRemainingLength];
                            if (this.FileParams.Flags.Bit(FileOperationFlags.LargeFs_AppendNewLineForCrossBorder))
                            {
                                if (appendBytes.Length >= 2)
                                {
                                    appendBytes[0] = 13;
                                    appendBytes[1] = 10;
                                }
                                else if (appendBytes.Length >= 1)
                                {
                                    appendBytes[0] = 10;
                                }
                            }

                            await handle.WriteRandomAsync(firstCursor.PhysicalPosition, appendBytes, cancel);
                            this.CurrentFileSize += firstCursor.PhysicalRemainingLength;
                        }

                        using (var handle = await GetUnderleyRandomAccessHandle(secondCursor.LogicalPosition, cancel))
                        {
                            // Write the data from the beginning of the second physical file
                            await handle.WriteRandomAsync(0, data, cancel);
                            this.CurrentFileSize += data.Length;
                        }
                    }
                }
                else
                {
                    // Normal write
                    foreach (Cursor cursor in cursorList)
                    {
                        using (var handle = await GetUnderleyRandomAccessHandle(cursor.LogicalPosition, cancel))
                        {
                            await handle.WriteRandomAsync(cursor.PhysicalPosition, data.Slice((int)(cursor.LogicalPosition - position), (int)cursor.PhysicalDataLength), cancel);
                        }
                    }

                    this.CurrentFileSize = Math.Max(this.CurrentFileSize, position + data.Length);
                }
            }
        }

        protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            List<Cursor> cursorList = GenerateCursorList(size, 1, true);

            Cursor cursor = cursorList.Single();

            bool shrink = (this.CurrentFileSize > size);

            if (shrink)
            {
                // Delete oversized files
                LargeFileSystem.ParsedPath[] physicalFiles = await LargeFileSystem.GetPhysicalFileStateInternal(this.FileParams.Path, cancel);
                List<LargeFileSystem.ParsedPath> filesToDelete = physicalFiles.Where(x => x.FileNumber > cursor.PhysicalFileNumber).ToList();

                await LargeFileSystem.UnderlayFileSystemPoolForWrite.EnumAndCloseHandlesAsync((key, file) =>
                {
                    if (filesToDelete.Where(x => x.PhysicalFilePath.IsSame(file.FileParams.Path, LargeFileSystem.PathParser.PathStringComparison)).Any())
                    {
                        return true;
                    }
                    return false;
                },
                () =>
                {
                    foreach (LargeFileSystem.ParsedPath deleteFile in filesToDelete.OrderByDescending(x => x.PhysicalFilePath))
                    {
                        UnderlayFileSystem.DeleteFile(deleteFile.PhysicalFilePath, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                    }
                },
                (x, y) =>
                {
                    return -(x.FileParams.Path.CompareTo(y.FileParams.Path));
                },
                cancel);

                await LargeFileSystem.UnderlayFileSystemPoolForWrite.EnumAndCloseHandlesAsync((key, file) =>
                {
                    if (filesToDelete.Where(x => x.PhysicalFilePath.IsSame(file.FileParams.Path, LargeFileSystem.PathParser.PathStringComparison)).Any())
                    {
                        return true;
                    }
                    return false;
                },
                () =>
                {
                    foreach (LargeFileSystem.ParsedPath deleteFile in filesToDelete.OrderByDescending(x => x.PhysicalFilePath))
                    {
                        UnderlayFileSystem.DeleteFile(deleteFile.PhysicalFilePath, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                    }
                },
                (x, y) =>
                {
                    return -(x.FileParams.Path.CompareTo(y.FileParams.Path));
                },
                cancel);
            }

            using (var handle = await GetUnderleyRandomAccessHandle(cursor.LogicalPosition, cancel))
            {
                await handle.SetFileSizeAsync(cursor.PhysicalPosition, cancel);

                this.CurrentFileSize = size;
            }

        }
    }

    class LargeFileSystemParams
    {
        public const long DefaultMaxSinglePhysicalFileSize = 1_000_000_000; // 1GB
        public const long DefaultMaxLogicalFileSize = 1_000_000_000_000_000_000; // 1EB

        public long MaxSinglePhysicalFileSize { get; }
        public long MaxLogicalFileSize { get; }
        public int NumDigits { get; }
        public string SplitStr { get; }
        public long MaxFileNumber { get; }

        public LargeFileSystemParams(long maxSingleFileSize = DefaultMaxSinglePhysicalFileSize, long logicalMaxSize = DefaultMaxLogicalFileSize, string splitStr = "~~~")
        {
            checked
            {
                this.SplitStr = splitStr.NonNullTrim().FilledOrDefault("~~~");
                this.MaxSinglePhysicalFileSize = Math.Min(Math.Max(maxSingleFileSize, 1), int.MaxValue);
                this.MaxLogicalFileSize = logicalMaxSize;

                long i = (this.MaxLogicalFileSize / this.MaxSinglePhysicalFileSize);

                i = Math.Max(i, 1);
                i = (int)Math.Log10(i);
                NumDigits = (int)i;

                this.MaxFileNumber = Str.MakeCharArray('9', NumDigits).ToLong();
                this.MaxLogicalFileSize = (MaxSinglePhysicalFileSize * (this.MaxFileNumber + 1)) - 1;
            }
        }
    }

    class LargeFileSystem : FileSystemBase
    {
        public class ParsedPath
        {
            public string DirectoryPath { get; }
            public string OriginalFileNameWithoutExtension { get; }
            public long FileNumber { get; }
            public string Extension { get; }
            public string PhysicalFilePath { get; }
            public string LogicalFilePath { get; }

            string _PhysicalFileNameCache = null;
            string _LogicalFileName = null;
            public string PhysicalFileName => _PhysicalFileNameCache ?? (_PhysicalFileNameCache = LargeFileSystem.PathParser.GetFileName(this.PhysicalFilePath));
            public string LogicalFileName => _LogicalFileName ?? (_LogicalFileName = LargeFileSystem.PathParser.GetFileName(this.LogicalFilePath));

            public FileSystemEntity PhysicalEntity { get; }

            readonly LargeFileSystem LargeFileSystem;

            public ParsedPath(LargeFileSystem fs, string logicalFilePath, long fileNumber)
            {
                this.LargeFileSystem = fs;

                this.LogicalFilePath = logicalFilePath;

                string fileName = fs.PathParser.GetFileName(logicalFilePath);
                if (fileName.IndexOf(fs.Params.SplitStr) != -1)
                    throw new ApplicationException($"The original filename '{fileName}' contains '{fs.Params.SplitStr}'.");

                string dir = fs.PathParser.GetDirectoryName(logicalFilePath);
                string filename = fs.PathParser.GetFileName(logicalFilePath);
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

            public ParsedPath(LargeFileSystem fs, string physicalFilePath, FileSystemEntity physicalEntity = null)
            {
                this.LargeFileSystem = fs;

                this.PhysicalEntity = physicalEntity;
                this.PhysicalFilePath = physicalFilePath;

                string dir = fs.PathParser.GetDirectoryName(physicalFilePath);
                string fn = fs.PathParser.GetFileName(physicalFilePath);

                int[] indexes = fn.FindStringIndexes(fs.Params.SplitStr);
                if (indexes.Length != 1)
                    throw new ArgumentException($"Filename '{fn}' is not a large file. indexes.Length != 1.");

                string originalFileName = fn.Substring(0, indexes[0]);
                string afterOriginalFileName = fn.Substring(indexes[0] + fs.Params.SplitStr.Length);
                if (afterOriginalFileName.IsEmpty())
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
                this.LogicalFilePath = fs.PathParser.Combine(this.DirectoryPath, filename);
            }

            public string GeneratePhysicalPath(long? fileNumberOverwrite = null)
            {
                long fileNumber = fileNumberOverwrite ?? FileNumber;
                string fileNumberStr = fileNumber.ToString($"D{LargeFileSystem.Params.NumDigits}");
                Debug.Assert(fileNumberStr.Length == LargeFileSystem.Params.NumDigits);

                string filename = $"{OriginalFileNameWithoutExtension}{LargeFileSystem.Params.SplitStr}{fileNumberStr}{Extension}";

                return LargeFileSystem.PathParser.Combine(DirectoryPath, filename);
            }
        }

        public static LargeFileSystem Local { get; } = LargeFileSystem.CreateFirstLocalInstance();
        public static LargeFileSystem LocalAutoUtf8 { get; } = LargeFileSystem.CreateFirstAutoUtf8LocalInstance();

        static LargeFileSystem _LocalSingletonInstance = null;
        static LargeFileSystem _AutoUtf8SingletonInstance = null;

        static LargeFileSystem CreateFirstLocalInstance()
        {
            if (_LocalSingletonInstance == null)
            {
                _LocalSingletonInstance = new LargeFileSystem(LeakChecker.SuperGrandLady, LocalFileSystem.Local, AppConfig.LargeFileSystemSettings.LocalLargeFileSystemParams.Value);
            }

            return _LocalSingletonInstance;
        }

        static LargeFileSystem CreateFirstAutoUtf8LocalInstance()
        {
            if (_AutoUtf8SingletonInstance == null)
            {
                _AutoUtf8SingletonInstance = new LargeFileSystem(LeakChecker.SuperGrandLady, LocalFileSystem.LocalAutoUtf8, AppConfig.LargeFileSystemSettings.LocalLargeFileSystemParams.Value);
            }

            return _AutoUtf8SingletonInstance;
        }

        CancellationTokenSource CancelSource = new CancellationTokenSource();
        CancellationToken CancelToken => CancelSource.Token;

        public FileSystemBase UnderlayFileSystem { get; }
        public LargeFileSystemParams Params { get; }

        AsyncLock AsyncLockObj = new AsyncLock();

        public FileSystemObjectPool UnderlayFileSystemPoolForRead { get; }
        public FileSystemObjectPool UnderlayFileSystemPoolForWrite { get; }

        public LargeFileSystem(AsyncCleanuperLady lady, FileSystemBase underlayFileSystem, LargeFileSystemParams param) : base(lady, underlayFileSystem.PathParser)
        {
            this.UnderlayFileSystem = underlayFileSystem;
            this.Params = param;

            this.UnderlayFileSystemPoolForRead = this.UnderlayFileSystem.ObjectPoolForRead;
            this.UnderlayFileSystemPoolForWrite = this.UnderlayFileSystem.ObjectPoolForWrite;
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
            => this.UnderlayFileSystem.NormalizePathAsync(path, cancel);

        protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            string fileName = this.PathParser.GetFileName(option.Path);
            if (fileName.IndexOf(Params.SplitStr) != -1)
                throw new ApplicationException($"The original filename '{fileName}' contains '{Params.SplitStr}'.");

            using (TaskUtil.CreateCombinedCancellationToken(out CancellationToken operationCancel, this.CancelToken, cancel))
            {
                using (await AsyncLockObj.LockWithAwait(operationCancel))
                {
                    cancel.ThrowIfCancellationRequested();

                    bool isSimplePhysicalFileExists = await UnderlayFileSystem.IsFileExistsAsync(option.Path);

                    if (isSimplePhysicalFileExists)
                    {
                        // If there is a simple physical file, open it.
                        return await UnderlayFileSystem.CreateFileAsync(option, cancel);
                    }

                    ParsedPath[] relatedFiles = await GetPhysicalFileStateInternal(option.Path, operationCancel);

                    return await LargeFileObject.CreateFileAsync(this, option, relatedFiles, operationCancel);
                }
            }
        }

        public async Task<ParsedPath[]> GetPhysicalFileStateInternal(string logicalFilePath, CancellationToken cancel)
        {
            List<ParsedPath> ret = new List<ParsedPath>();

            ParsedPath parsed = new ParsedPath(this, logicalFilePath, 0);

            FileSystemEntity[] dirEntities = await UnderlayFileSystem.EnumDirectoryAsync(parsed.DirectoryPath, false, EnumDirectoryFlags.NoGetPhysicalSize, cancel);

            var relatedFiles = dirEntities.Where(x => x.IsDirectory == false);
            foreach (var f in relatedFiles)
            {
                if (f.Name.StartsWith(parsed.OriginalFileNameWithoutExtension, PathParser.PathStringComparison))
                {
                    try
                    {
                        ParsedPath parsedForFile = new ParsedPath(this, f.FullPath, f);
                        if (parsed.LogicalFilePath.IsSame(parsedForFile.LogicalFilePath, PathParser.PathStringComparison))
                        {
                            ret.Add(parsedForFile);
                        }
                    }
                    catch { }
                }
            }

            ret.Sort((x, y) => x.FileNumber.CompareTo(y.FileNumber));

            return ret.ToArray();
        }

        protected override async Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, EnumDirectoryFlags flags, CancellationToken cancel = default)
        {
            checked
            {
                FileSystemEntity[] dirEntities = await UnderlayFileSystem.EnumDirectoryAsync(directoryPath, false, flags, cancel);

                var relatedFiles = dirEntities.Where(x => x.IsDirectory == false).Where(x => x.Name.IndexOf(Params.SplitStr) != -1);

                var sortedRelatedFiles = relatedFiles.ToList();
                sortedRelatedFiles.Sort((x, y) => x.Name.Cmp(y.Name, PathParser.PathStringComparison));
                sortedRelatedFiles.Reverse();

                Dictionary<string, FileSystemEntity> parsedFileDictionaly = new Dictionary<string, FileSystemEntity>(PathParser.PathStringComparer);
                
                var normalFiles = dirEntities.Where(x => x.IsDirectory == false).Where(x => x.Name.IndexOf(Params.SplitStr) == -1);
                var normalFileHashSet = new HashSet<string>(normalFiles.Select(x => x.Name), PathParser.PathStringComparer);

                foreach (FileSystemEntity f in sortedRelatedFiles)
                {
                    try
                    {
                        // Split files
                        ParsedPath parsed = new ParsedPath(this, f.FullPath, f);

                        if (parsedFileDictionaly.ContainsKey(parsed.LogicalFileName) == false)
                        {
                            var newFileEntity = new FileSystemEntity()
                            {
                                FullPath = parsed.LogicalFilePath,
                                Name = PathParser.GetFileName(parsed.LogicalFileName),
                                Size = f.Size + parsed.FileNumber * Params.MaxSinglePhysicalFileSize,
                                PhysicalSize = f.PhysicalSize,
                                Attributes = f.Attributes,
                                CreationTime = f.CreationTime,
                                LastWriteTime = f.LastWriteTime,
                                LastAccessTime = f.LastAccessTime,
                            };
                            parsedFileDictionaly.Add(parsed.LogicalFileName, newFileEntity);
                        }
                        else
                        {
                            var fileEntity = parsedFileDictionaly[parsed.LogicalFileName];

                            fileEntity.PhysicalSize += f.PhysicalSize;

                            if (fileEntity.CreationTime > f.CreationTime) fileEntity.CreationTime = f.CreationTime;
                            if (fileEntity.LastWriteTime < f.LastWriteTime) fileEntity.LastWriteTime = f.LastWriteTime;
                            if (fileEntity.LastAccessTime < f.LastAccessTime) fileEntity.LastAccessTime = f.LastAccessTime;
                        }
                    }
                    catch { }
                }

                var logicalFiles = parsedFileDictionaly.Values.Where(x => normalFileHashSet.Contains(x.Name) == false);

                var retList = dirEntities.Where(x => x.IsDirectory)
                    .Concat(logicalFiles)
                    .Concat(normalFiles)
                    .OrderByDescending(x => x.IsDirectory)
                    .ThenBy(x => x.Name);

                return retList.ToArrayList();
            }
        }

        protected override Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => UnderlayFileSystem.CreateDirectoryAsync(directoryPath, flags, cancel);

        protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
            => UnderlayFileSystem.DeleteDirectoryAsync(directoryPath, recursive);


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

        protected override async Task<bool> IsFileExistsImplAsync(string path, CancellationToken cancel = default)
        {
            // Try physical file first
            try
            {
                if (await UnderlayFileSystem.IsFileExistsAsync(path, cancel))
                    return true;
            }
            catch { }

            LargeFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternal(path, cancel);
            var lastFileParsed = physicalFiles.OrderBy(x => x.FileNumber).LastOrDefault();

            return lastFileParsed != null;
        }

        protected override Task<bool> IsDirectoryExistsImplAsync(string path, CancellationToken cancel = default)
            => UnderlayFileSystem.IsDirectoryExistsAsync(path, cancel);

        protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
        {
            // Try physical file first
            try
            {
                return await UnderlayFileSystem.GetFileMetadataAsync(path, flags, cancel);
            }
            catch { }

            LargeFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternal(path, cancel);
            var lastFileParsed = physicalFiles.OrderBy(x => x.FileNumber).LastOrDefault();

            if (lastFileParsed == null)
            {
                // File not found
                throw new IOException($"The file '{path}' not found.");
            }
            else
            {
                // Large file exists
                checked
                {
                    FileMetadata ret = await UnderlayFileSystem.GetFileMetadataAsync(lastFileParsed.PhysicalFilePath, flags | FileMetadataGetFlags.NoSecurity | FileMetadataGetFlags.NoAlternateStream, cancel);
                    long sizeOfLastFile = ret.Size;
                    sizeOfLastFile = Math.Min(sizeOfLastFile, Params.MaxSinglePhysicalFileSize);

                    long currentFileSize = lastFileParsed.FileNumber * Params.MaxSinglePhysicalFileSize + sizeOfLastFile;

                    ret.Size = currentFileSize;
                    ret.PhysicalSize = physicalFiles.Sum(x => x.PhysicalEntity.PhysicalSize);
                    ret.CreationTime = physicalFiles.Min(x => x.PhysicalEntity.CreationTime);
                    ret.LastWriteTime = physicalFiles.Max(x => x.PhysicalEntity.LastWriteTime);
                    ret.LastAccessTime = physicalFiles.Max(x => x.PhysicalEntity.LastAccessTime);

                    return ret;
                }
            }
        }

        protected override async Task DeleteFileImplAsync(string path, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
        {
            // Try physical file first
            try
            {
                if (await UnderlayFileSystem.IsFileExistsAsync(path, cancel))
                {
                    await UnderlayFileSystem.DeleteFileAsync(path, flags, cancel);
                    return;
                }
            }
            catch { }

            LargeFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternal(path, cancel);

            if (physicalFiles.IsEmpty())
            {
                // File not found
                return;
            }

            await UnderlayFileSystemPoolForWrite.EnumAndCloseHandlesAsync((key, file) =>
            {
                if (physicalFiles.Where(x => x.PhysicalFilePath.IsSame(file.FileParams.Path, PathParser.PathStringComparison)).Any())
                {
                    return true;
                }
                return false;
            },
            () =>
            {
                foreach (LargeFileSystem.ParsedPath deleteFile in physicalFiles.OrderByDescending(x => x.PhysicalFilePath))
                {
                    UnderlayFileSystem.DeleteFile(deleteFile.PhysicalFilePath, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
            },
            (x, y) =>
            {
                return -(x.FileParams.Path.CompareTo(y.FileParams.Path));
            },
            cancel);

            await UnderlayFileSystemPoolForRead.EnumAndCloseHandlesAsync((key, file) =>
            {
                if (physicalFiles.Where(x => x.PhysicalFilePath.IsSame(file.FileParams.Path, PathParser.PathStringComparison)).Any())
                {
                    return true;
                }
                return false;
            },
            () =>
            {
                foreach (LargeFileSystem.ParsedPath deleteFile in physicalFiles.OrderByDescending(x => x.PhysicalFilePath))
                {
                    UnderlayFileSystem.DeleteFile(deleteFile.PhysicalFilePath, FileOperationFlags.ForceClearReadOnlyOrHiddenBitsOnNeed, cancel);
                }
            },
            (x, y) =>
            {
                return -(x.FileParams.Path.CompareTo(y.FileParams.Path));
            },
            cancel);
        }

        protected override async Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        {
            // Try physical file first
            try
            {
                if (await UnderlayFileSystem.IsFileExistsAsync(path, cancel))
                {
                    await UnderlayFileSystem.SetFileMetadataAsync(path, metadata, cancel);
                    return;
                }
            }
            catch { }
            LargeFileSystem.ParsedPath[] physicalFiles = await GetPhysicalFileStateInternal(path, cancel);

            if (physicalFiles.IsEmpty())
            {
                // File not found
                throw new IOException($"The file '{path}' not found.");
            }

            Exception exception = null;

            foreach (var file in physicalFiles.OrderBy(x => x.PhysicalFilePath, PathParser.PathStringComparer))
            {
                cancel.ThrowIfCancellationRequested();

                try
                {
                    await UnderlayFileSystem.SetFileMetadataAsync(file.PhysicalFilePath, metadata, cancel);
                }
                catch (Exception ex)
                {
                    if (exception == null)
                        exception = ex;
                }
            }

            if (exception != null)
                throw exception;
        }

        protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
            => UnderlayFileSystem.SetDirectoryMetadataAsync(path, metadata, cancel);

        protected override Task<FileMetadata> GetDirectoryMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.DefaultAll, CancellationToken cancel = default)
            => UnderlayFileSystem.GetDirectoryMetadataAsync(path, flags | FileMetadataGetFlags.NoAlternateStream | FileMetadataGetFlags.NoSecurity, cancel);

        protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
            => throw new NotImplementedException();

        protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
            => throw new NotImplementedException();
    }
}
