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
        readonly LargeFileSystem LargeFileSystem;

        protected LargeFileObject(FileSystemBase fileSystem, FileParameters fileParams) : base(fileSystem, fileParams)
        {
            this.LargeFileSystem = (LargeFileSystem)fileSystem;
        }

        public static async Task<FileObjectBase> CreateFileAsync(LargeFileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            LargeFileObject f = new LargeFileObject(fileSystem, fileParams);

            await f.CreateAsync(cancel);

            return f;
        }

        protected override async Task CreateAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                await base.CreateAsync(cancel);
            }
            catch
            {
                throw;
            }
        }

        protected override Task CloseImplAsync()
        {
            throw new NotImplementedException();
        }

        protected override Task FlushImplAsync(CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<long> GetCurrentPositionImplAsync(CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task<int> ReadImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task WriteImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }
    }

    class LargeFileSystemParams
    {
        public const long DefaultMaxSingleFileSize = 1000000;
        public const long DefaultLogicalMaxSize = 100000000000000; // 100TB
        public const int DefaultPooledFileCloseDelay = 1000;

        public long MaxSingleFileSize { get; }
        public long LogicalMaxSize { get; }
        public int NumDigits { get; }
        public string SplitStr { get; }
        public int PooledFileCloseDelay { get; }
        public int MaxFileNumber { get; }

        public LargeFileSystemParams(long maxSingleFileSize = DefaultMaxSingleFileSize, long logicalMaxSize = DefaultLogicalMaxSize, string splitStr = "~",
            int pooledFileCloseDelay = DefaultPooledFileCloseDelay)
        {
            checked
            {
                this.SplitStr = splitStr.NonNullTrim().Default("~");
                this.MaxSingleFileSize = Math.Max(maxSingleFileSize, 1);
                this.PooledFileCloseDelay = Math.Max(pooledFileCloseDelay, 1000);

                long i = (int)(LogicalMaxSize / this.MaxSingleFileSize);
                i = Math.Max(i, 1);
                i = (int)Math.Log10(i);
                i++;
                i = Math.Max(Math.Min(i, 9), 1);
                NumDigits = (int)i;

                this.MaxFileNumber = Str.MakeCharArray('9', NumDigits).ToInt();
                this.LogicalMaxSize = MaxSingleFileSize * this.MaxFileNumber;
            }
        }
    }

    class LargeFileSystem : FileSystemBase
    {
        public class ParsedPath
        {
            public string DirectoryPath { get; private set; }
            public string OriginalFileNameWithoutExtension { get; private set; }
            public int FileNumber { get; private set; }
            public string Extension { get; private set; }
            readonly LargeFileSystem fs;

            public ParsedPath(LargeFileSystem fs)
            {
                this.fs = fs;
            }

            public ParsedPath(LargeFileSystem fs, string physicalPath)
            {
                this.fs = fs;
                physicalPath = fs.NormalizePath(physicalPath);

                string dir = fs.Metrics.GetDirectoryName(physicalPath);
                string fn = fs.Metrics.GetFileName(physicalPath);

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
            }

            public string GenerateFullPath(int? fileNumberOverwrite = null)
            {
                int fileNumber = fileNumberOverwrite ?? FileNumber;
                string fileNumberStr = fileNumber.ToString($"D{fs.Params.NumDigits}");
                Debug.Assert(fileNumberStr.Length == fs.Params.NumDigits);

                string filename = $"{OriginalFileNameWithoutExtension}{fs.Params.SplitStr}{fileNumberStr}{Extension}";

                return fs.Metrics.CombinePath(DirectoryPath, filename);
            }

            public static ParsedPath MakeFromOriginalPath(LargeFileSystem fs, string originalPath, int fileNumber)
            {
                originalPath = fs.BaseFileSystem.NormalizePath(originalPath);

                string fileName = fs.Metrics.GetFileName(originalPath);
                if (fileName.IndexOf(fs.Params.SplitStr) != -1)
                    throw new ApplicationException($"The original filename '{fileName}' contains '{fs.Params.SplitStr}'.");

                string dir = fs.Metrics.GetDirectoryName(originalPath);
                string filename = fs.Metrics.GetFileName(originalPath);
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

                ParsedPath ret = new ParsedPath(fs);

                ret.DirectoryPath = dir;
                ret.OriginalFileNameWithoutExtension = filenameWithoutExtension;
                ret.FileNumber = fileNumber;
                ret.Extension = extension;

                return ret;
            }
        }

        public FileSystemBase BaseFileSystem { get; }
        public LargeFileSystemParams Params { get; }

        FileSystemObjectPool FsPool { get; }

        public LargeFileSystem(AsyncCleanuperLady lady, FileSystemBase baseFileSystem, LargeFileSystemParams param) : base(lady, baseFileSystem.Metrics)
        {
            this.BaseFileSystem = baseFileSystem;
            this.Params = param;

            this.FsPool = new FileSystemObjectPool(this.BaseFileSystem, this.Params.PooledFileCloseDelay);
        }

        protected override Task<string> NormalizePathImplAsync(string path, CancellationToken cancel = default)
            => this.BaseFileSystem.NormalizePathAsync(path, cancel);

        protected override async Task<FileObjectBase> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            string fileName = this.Metrics.GetFileName(option.Path);
            if (fileName.IndexOf(Params.SplitStr) != -1)
                throw new ApplicationException($"The original filename '{fileName}' contains '{Params.SplitStr}'.");

            // Get state of the base filesystem
            //BaseFileSystem.EnumDirectoryAsync(

            return null;
        }

        protected override Task<FileSystemEntity[]> EnumDirectoryImplAsync(string directoryPath, CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        protected override Task CreateDirectoryImplAsync(string directoryPath, FileOperationFlags flags = FileOperationFlags.None, CancellationToken cancel = default)
            => BaseFileSystem.CreateDirectoryAsync(directoryPath, flags, cancel);


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
                this.FsPool.DisposeSafe();
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
    }
}
