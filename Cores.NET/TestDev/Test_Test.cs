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

namespace IPA.TestDev
{
    static class TestClass
    {
        public static void Test()
        {
            Con.WriteLine("This is a test.");

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

            AsyncCleanuperLady lady = new AsyncCleanuperLady();
            try
            {
                LargeFileSystemParams p = new LargeFileSystemParams(30, 10000000);
                //LargeFileSystemParams p = new LargeFileSystemParams(1000000000);
                LargeFileSystem lfs = new LargeFileSystem(lady, Lfs, p);

                byte[] data = Str.MakeCharArray('x', 1).GetBytes_Ascii();

                FileStream fs = File.Create(@"c:\tmp\1.dat", 4096, FileOptions.Asynchronous);

                byte[] data1 = lfs.Open(@"C:\tmp\large\1.dat").GetStream().ReadToEnd();
                Con.WriteLine(data1.Length);


                return;

                using (var f = lfs.OpenOrCreate(@"C:\tmp\large\1.dat", operationFlags: FileOperationFlags.AutoCreateDirectoryOnFileCreation | FileOperationFlags.SetCompressionFlagOnDirectory))
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

