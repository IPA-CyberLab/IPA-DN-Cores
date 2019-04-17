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
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;

namespace IPA.TestDev
{
    partial class TestDevCommands
    {
        static byte[] GenerateTestData(int size)
        {
            byte[] ret = new byte[size];
            for (int i = 0; i < size; i++)
                ret[i] = (byte)('A' + (i % 26));
            return ret;
        }

        [ConsoleCommandMethod(
            "SparseFile command",
            "SparseFile [arg]",
            "SparseFile test")]
        static int SparseFile(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string normalFn = @"D:\TMP\sparse_file_test\normal_file.txt";
            string standardApi = @"D:\TMP\sparse_file_test\standard_api.txt";
            string sparseFn = @"D:\TMP\sparse_file_test\sparse_file.txt";
            string copySparse2Fn = @"D:\TMP\sparse_file_test\sparse_file_2.txt";
            string copySparse3Fn = @"D:\TMP\sparse_file_test\sparse_file_3.txt";

            string ramFn = @"D:\TMP\sparse_file_test\ram.txt";

            try
            {
                Lfs.EnableBackupPrivilege();
            }
            catch (Exception ex)
            {
                Con.WriteError(ex);
            }

            Lfs.CreateDirectory(Lfs.PathInterpreter.GetDirectoryName(normalFn));

            while (true)
            {
                Lfs.DeleteFile(normalFn);
                Lfs.DeleteFile(sparseFn);
                Lfs.DeleteFile(standardApi);
                MemoryBuffer<byte> ram = new MemoryBuffer<byte>();

                for (int i = 0; i < 100; i++)
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
                                for (int k = 0; k < 100; k++)
                                {
                                    MemoryBuffer<byte> data = new MemoryBuffer<byte>();

                                    int numBlocks = Util.RandSInt31() % 32;

                                    for (int j = 0; j < numBlocks; j++)
                                    {
                                        if (j >= 1 || Util.RandBool())
                                            data.WriteZero(Util.RandSInt31() % 100_000);
                                        data.Write(GenerateTestData(Util.RandSInt31() % 10000));
                                    }

                                    if (Util.RandBool())
                                        data.WriteZero(Util.RandSInt31() % 10000);

                                    long pos = Util.RandSInt31() % 10_000_000;
                                    normal.WriteRandom(pos, data);
                                    sparse.WriteRandom(pos, data);
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

                    Lfs.CopyFile(normalFn, copySparse2Fn, new CopyFileParams(flags: flags | FileOperationFlags.SparseFile, overwrite: true));
                    Lfs.CopyFile(sparseFn, copySparse3Fn, new CopyFileParams(flags: flags | FileOperationFlags.SparseFile, overwrite: true));
                    //Lfs.WriteToFile(ramFn, ram.Memory);
                }

                string hash0 = Secure.HashSHA1(Lfs.ReadFromFile(standardApi).Span.ToArray()).GetHexString();
                string hash1 = Secure.HashSHA1(Lfs.ReadFromFile(normalFn).Span.ToArray()).GetHexString();
                string hash2 = Secure.HashSHA1(Lfs.ReadFromFile(sparseFn).Span.ToArray()).GetHexString();
                string hash3 = Secure.HashSHA1(Lfs.ReadFromFile(copySparse2Fn).Span.ToArray()).GetHexString();
                string hash4 = Secure.HashSHA1(Lfs.ReadFromFile(copySparse3Fn).Span.ToArray()).GetHexString();
                string hash5 = Secure.HashSHA1(ram.Span.ToArray()).GetHexString();

                if (hash0 != hash1 || hash1 != hash2 || hash1 != hash3 || hash1 != hash4 || hash1 != hash5)
                {
                    Con.WriteLine("Error!!!\n");
                    Con.WriteLine($"hash0 = {hash0}");
                    Con.WriteLine($"hash1 = {hash1}");
                    Con.WriteLine($"hash2 = {hash2}");
                    Con.WriteLine($"hash3 = {hash3}");
                    Con.WriteLine($"hash4 = {hash4}");
                    Con.WriteLine($"hash5 = {hash5}");
                    return 0;
                }
                else
                {
                    Util.DoNothing();
                }

                ram = null;
            }


            return 0;
        }
    }
}

