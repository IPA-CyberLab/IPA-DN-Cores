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
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev
{
    partial class TestDevCommands
    {
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
                    copyFileFlags: FileOperationFlags.SparseFile,
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
                        copyFileFlags: FileOperationFlags.SparseFile,                  //progressCallback: (x, y) => { return Task.FromResult(true); },
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
                        copyFileFlags: FileOperationFlags.SparseFile,                                  //progressCallback: (x, y) => { return Task.FromResult(true); },
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

                    LLfs.AppendDataToFile(filePath, hello._GetBytes_Ascii(), FileOperationFlags.AutoCreateDirectory);
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

                    long position = Secure.Rand63i() % (LLfs.Params.MaxLogicalFileSize - 100);
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

                MemoryBuffer<byte> ram = new MemoryBuffer<byte>();

                for (int i = 0; i < 3; i++)
                {
                    FileOperationFlags flags = FileOperationFlags.AutoCreateDirectory;
                    if ((Util.RandSInt31() % 8) == 0) flags |= FileOperationFlags.NoAsync;
                    if (Util.RandBool()) flags |= FileOperationFlags.BackupMode;

                    using (var normal = Lfs.OpenOrCreate(normalFn, flags: flags))
                    {
                        using (var sparse = Lfs.OpenOrCreate(sparseFn, flags: flags | FileOperationFlags.SparseFile))
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
                    Lfs.CopyFile(normalFn, copySparse2Fn, new CopyFileParams(flags: flags | FileOperationFlags.SparseFile, overwrite: true));
                    Lfs.CopyFile(sparseFn, copySparse3Fn, new CopyFileParams(flags: flags | FileOperationFlags.SparseFile, overwrite: true));
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
}

