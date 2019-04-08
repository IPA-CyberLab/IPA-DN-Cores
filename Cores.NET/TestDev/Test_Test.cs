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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;
using System.Security.AccessControl;

namespace IPA.TestDev
{
    static class TestClass
    {
        public static void Test()
        {
            Con.WriteLine("This is a test.");

            //string alt1 = @"D:\Downloads\softether-vpnclient-v4.29-9680-rtm-2019.02.28-windows-x86_x64-intel.exe";
            string alt1 = @"C:\tmp\acl_test2\1.exe";
            string alt2 = @"C:\tmp\acl_test2\2.exe";

            Lfs.EnableBackupPrivilege();

            //var meta1 = Lfs.GetFileMetadata(alt1);
            //Lfs.SetFileMetadata(alt2, meta1.Clone(FileMetadataCopyMode.AlternateStream));
            //Con.WriteJsonLine(meta1);

            //Lfs.CopyFile(@"C:\tmp\acl_test2\1.exe", @"C:\tmp\acl_test2\3.exe", new CopyFileParams(overwrite: true, flags: FileOperationFlags.BackupMode));
            Lfs.CopyFile(@"C:\tmp\acl_test2\2.exe", @"C:\tmp\acl_test2\4.exe", new CopyFileParams(overwrite: true, flags: FileOperationFlags.BackupMode, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.All)));

            return;

            //int n = 0;

            //for (int k = 0; k < 10; k++)
            //{
            //    for (int m = 0; m < 10; m++)
            //    {
            //        for (int p = 0; p < 10; p++)
            //        {
            //            for (int q = 0; q < 10; q++)
            //            {
            //                for (int r = 0; r < 10; r++)
            //                {
            //                    string dir = Path.Combine(@"C:\TMP\acl01", k.ToString(), m.ToString(), p.ToString(), q.ToString(), r.ToString());
            //                    string file = Path.Combine(dir, "test.txt");
            //                    Directory.CreateDirectory(dir);
            //                    File.WriteAllBytes(file, "Hello".GetBytes_Ascii());

            //                    Con.WriteLine(n++);
            //                }
            //            }
            //        }
            //    }
            //}

            //PalFileSystem.SetFileOrDirectorySecurityMetadata(@"C:\TMP\2.c", false, PalFileSystem.GetFileOrDirectorySecurityMetadata(@"C:\TMP\2.c", false));

            //return;

            Lfs.EnableBackupPrivilege();

            //string aclFileName = @"c:\tmp\test.htm";
            //Lfs.AppendToFile(@"C:\tmp\a.dat", "Hello".GetBytes_Ascii());
            //var meta = PalFileSystem.GetFileOrDirectorySecurityMetadata(aclFileName, false);
            //Con.WriteLine(meta.ObjectToJson());

            //PalFileSystem.SetFileOrDirectorySecurityMetadata(@"C:\tmp\a.dat", false, meta);
            //PalFileSystem.SetFileOrDirectorySecurityMetadata(@"C:\TMP\acl_test\test2.txt", false, meta);
            //var aa = PalFileSystem.GetFileOrDirectorySecurityMetadata(@"d:\tmp", false);
            //Con.WriteJsonLine(aa);
            //PalFileSystem.SetFileOrDirectorySecurityMetadata(@"C:\TMP\acl_test\", true, PalFileSystem.GetFileOrDirectorySecurityMetadata(@"d:\tmp", false));
            //Con.WriteJsonLine(PalFileSystem.GetFileOrDirectorySecurityMetadata(@"C:\TMP\acl_test\", false));

            Lfs.SetFileOrDirectorySecurityMetadata(@"C:\TMP\acl01\", true, Lfs.GetFileOrDirectorySecurityMetadata(@"C:\TMP\acl02\", true));

            return;

            Lfs.EnableBackupPrivilege();

            //            Lfs.DeleteFile(@"C:\tmp\acl_test\test3.txt");
            //            return;

            string fn = @"c:\tmp\test.htm";

            Lfs.AppendToFile(fn, "xxx".GetBytes_UTF8(), FileOperationFlags.BackupMode);

            var m1 = Lfs.GetFileMetadata(fn);

            Con.WriteLine(m1.ObjectToJson());

            m1.Attributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly;
            m1.CreationTime = DateTimeOffset.Now.AddYears(-1);
            m1.LastWriteTime = DateTimeOffset.Now.AddYears(-2);
            m1.LastAccessTime = DateTimeOffset.Now.AddYears(-3);

            Lfs.SetFileMetadata(fn, m1);

            var m2 = Lfs.GetDirectoryMetadata(@"C:\TMP\acl_test\dir1");
            Con.WriteLine(m2.ObjectToJson());
            m2.Attributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly;
            m2.CreationTime = DateTimeOffset.Now.AddYears(-1);
            m2.LastWriteTime = DateTimeOffset.Now.AddYears(-2);
            m2.LastAccessTime = DateTimeOffset.Now.AddYears(-3);
            Lfs.SetDirectoryMetadata(@"C:\TMP\acl_test\dir1", m2);

            //byte[] xxx = Lfs.ReadFromFile(fn, flags: FileOperationFlags.BackupMode).ToArray();
            //Con.WriteLine(xxx.Length);
            //return;

            //Lfs.WriteToFile(fn, "Hellozz".GetBytes_Ascii(), FileOperationFlags.BackupMode);
            //Lfs.WriteToFile(fn, "Hellozz".GetBytes_Ascii(), flags: FileOperationFlags.IgnoreReadOnlyOrHiddenBits);
            //Lfs.AppendToFile(fn, "ZZZ".GetBytes_Ascii());
            return;

            //var m1 = Lfs.GetFileMetadata(fn);

            //Con.WriteLine(m1.ObjectToJson());

            //m1.Attributes = FileAttributes.Hidden;
            //m1.Created = DateTimeOffset.Now.AddYears(-1);
            //m1.Updated = DateTimeOffset.Now.AddYears(-2);

            //Lfs.SetFileMetadata(fn, m1);


            return;

            //using (var pool = Lfs.GetObjectPool(3000))
            //{
            //    while (true)
            //    {
            //        using (var fo = pool.OpenOrGetWithWriteMode(@"c:\tmp\1.txt"))
            //        {
            //            var f = fo.Object;

            //            f.Append($"Hello {DateTime.Now.ToDtStr()}\r\n".GetBytes_Ascii());
            //            f.Flush();
            //        }

            //        if (Con.ReadLine("?>") == "q")
            //            break;
            //    }
            //}
            //return;

            //while (true)
            //{
            //    string s = Con.ReadLine("Path>");
            //    try
            //    {
            //        Env.LocalFileSystemPathInterpreter.SepareteDirectoryAndFileName(s, out string s1, out string s2);
            //        Con.WriteLine(new { s1 = s1, s2 = s2, combined = Env.LocalFileSystemPathInterpreter.Combine(s1, s2),
            //            fnw = Env.LocalFileSystemPathInterpreter.GetFileNameWithoutExtension(s, true), ext = Env.LocalFileSystemPathInterpreter.GetExtension(s, true) });
            //        Con.WriteLine();
            //    }
            //    catch (Exception ex)
            //    {
            //        Con.WriteError(ex);
            //    }
            //}

            //Lfs.CopyFile(@"C:\tmp\1.c", @"C:\tmp\2.c", new CopyFileParams(overwrite: true, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.All | FileMetadataCopyMode.ReplicateArchiveBit)));
            //Lfs.CopyFile(@"C:\vm\vhd\win2019test.vhdx", @"d:\tmp\190407\test.vhdx",
            //    new CopyFileParams(overwrite: true, flags: FileOperationFlags.AutoCreateDirectoryOnFileCreation, progressReporterFactory: new ProgressFileProcessingReporterFactory(ProgressReporterOutputs.Console) ));
            //return;

            AsyncCleanuperLady lady = new AsyncCleanuperLady();
            try
            {
                //Lfs.AppendToFile(@"c:\tmp\append.txt", $"Hello {DateTimeOffset.Now.ToDtStr()}\r\n".GetBytes_UTF8());
                //return;
                //LargeFileSystemParams p = new LargeFileSystemParams(30, 10000000);
                LargeFileSystemParams p = new LargeFileSystemParams();
                LargeFileSystem largeFs = new LargeFileSystem(lady, Lfs, p);

                //byte[] data = Str.MakeCharArray('x', 1).GetBytes_Ascii();

                //FileStream fs = File.Create(@"c:\tmp\1.dat", 4096, FileOptions.Asynchronous);

                //byte[] data1 = largeFs.Open(@"C:\tmp\large\1.dat").GetStream().ReadToEnd();
                ////Con.WriteLine(data1.Length);
                //Lfs.WriteToFile(@"c:\tmp\2.dat", data1);

                Lfs.CopyFile(@"C:\vm\vhd\xpaoe.vhdx", @"d:\tmp\190407\2/xpaoe.vhdx",
                    new CopyFileParams(overwrite: true, flags: FileOperationFlags.AutoCreateDirectoryOnFileCreation,
                    reporterFactory: CopyFileParams.ConsoleReporterFactory),
                    destFileSystem: largeFs);

                //largeFs.CopyFile(@"d:\tmp\190407\test.vhdx", @"d:\tmp\190407\test2.vhdx",
                //    new CopyFileParams(overwrite: true, flags: FileOperationFlags.AutoCreateDirectoryOnFileCreation,
                //    reporterFactory: CopyFileParams.ConsoleReporterFactory),
                //    destFileSystem: Lfs);

                return;

                using (var f = largeFs.OpenOrCreate(@"C:\tmp\large\1.dat", flags: FileOperationFlags.AutoCreateDirectoryOnFileCreation | FileOperationFlags.SetCompressionFlagOnDirectory))
                {
                    f.Append($"Hello {DateTime.Now.ToDtStr()}\r\n".GetBytes_Ascii());

                    f.WriteRandom(4000, "A".GetBytes_Ascii());
                    f.WriteRandom(4001, "B".GetBytes_Ascii());
                    f.SetFileSize(4002);

                    //f.Append($"Hello {DateTime.Now.ToDtStr()}\r\n".GetBytes_Ascii());
                    //f.Append($"0123456789".GetBytes_Ascii());
                    //f.Seek(1, SeekOrigin.Begin);
                    //f.Write($"_".GetBytes_Ascii());

                    //while (true)
                    //{
                    //    var bench = new MicroBenchmark<object>("Append Test", 1000,
                    //        (obj, iterations) =>
                    //        {
                    //            for (int j = 0; j < iterations; j++)
                    //            {
                    //                f.Append(data);
                    //                //fs.WriteAsync(data).GetResult();
                    //            }
                    //        });

                    //    bench.StartAndPrint();
                    //}

                    f.Append($"Hello {DateTime.Now.ToDtStr()}\r\n".GetBytes_Ascii());
                }
            }
            finally
            {
                lady.CleanupAsync().GetResult();
            }
        }
    }
}

