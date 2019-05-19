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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;

#pragma warning disable CS0162
#pragma warning disable CS0219
#pragma warning disable CS1998

namespace IPA.TestDev
{
    static class BenchmarkTestTarget1
    {
        public static readonly CriticalSection LockObj_ReadOnly = new CriticalSection();
        public static CriticalSection LockObj_Property { get; } = new CriticalSection();

        public static readonly int Int_ReadOnly = 123;
        public static int Int_GetOnly { get; private set; } = 123;
        public static readonly Copenhagen<int> Int_Copenhagen_ReadOnly = 123;
        public static Copenhagen<int> Int_Copenhagen_GetOnly { get; private set; } = 123;

        public static readonly string String_ReadOnly = "Hello";
        public static string String_GetOnly { get; private set; } = "Hello";
        public static readonly Copenhagen<string> String_Copenhagen_ReadOnly = "Hello";
        public static Copenhagen<string> String_Copenhagen_GetOnly { get; private set; } = "Hello";

        public static async Task<int> SampleAsyncMethod()
        {
            Limbo.SInt32Volatile++;

            return Limbo.SInt32Volatile;
        }

        public static async Task EmptyAsyncMethod() { }

        public static async Task AsyncCallMethodFromAsyncMethod(int count)
        {
            for (int i = 0; i < count; i++)
                await SampleAsyncMethod();
        }

        public static void AsyncCallMethodFromSyncMethod(int count)
        {
            for (int i = 0; i < count; i++)
                SampleAsyncMethod()._GetResult();
        }

        public static async Task<int> SimpleAsyncMethod()
        {
            return Limbo.SInt32Volatile;
        }

        public static async Task<int> CallAsyncWithAwait()
        {
            return await SimpleAsyncMethod();
        }

        public static Task<int> CallAsyncWithNonAwait()
        {
            return SimpleAsyncMethod();
        }

        public static async Task CallAsyncWithAwaitLoop(int count)
        {
            for (int i = 0; i < count; i++)
                await CallAsyncWithAwait();
        }

        public static async Task CallAsyncWithNonAwaitLoop(int count)
        {
            for (int i = 0; i < count; i++)
                await SimpleAsyncMethod();
        }
    }

    class TestClass1
    {
        public int A, B, C;
    }

    partial class TestDevCommands
    {
        const int Benchmark_CountForVeryFast = 200000000;
        const int Benchmark_CountForFast = 10000000;
        const int Benchmark_CountForNormal = 10000;
        const int Benchmark_CountForSlow = 100;

        static void BenchMark_Test1()
        {
            MemoryBuffer<byte> sparseTest1 = new MemoryBuffer<byte>();
            for (int i = 0; i < 10; i++)
            {
                sparseTest1.Write("Hello World"._GetBytes_Ascii());
                sparseTest1.WriteZero(1_000_000);
                sparseTest1.Write("Hello World2"._GetBytes_Ascii());
            }

            int memcopyLength = 10_000_000;
            MemoryBuffer<byte> memcopySrc = new MemoryBuffer<byte>(memcopyLength);
            MemoryBuffer<byte> memcopyDst = new MemoryBuffer<byte>(memcopyLength);
            Span<byte> memcopySrcSpan = memcopySrc.Span;
            for (int i = 0; i < memcopyLength; i++)
            {
                memcopySrcSpan[i] = (byte)i;
            }
            byte[] byteArrayCopy = memcopySrcSpan.ToArray();
            byte[] byteArrayCopy2 = memcopySrcSpan.ToArray();

            int sparseTestSize = 1024 * 1024;

            MemoryBuffer<byte> nonSparse = new byte[sparseTestSize];
            Util.Rand(nonSparse.Span);

            MemoryBuffer<byte> sparse = new byte[sparseTestSize];
            Util.Rand(sparse.Span);
            for (int i = 0; i < 100; i++)
            {
                int pos = Util.RandSInt31() % (sparse.Length - 6000);
                sparse.Span.Slice(pos, 6000).Fill(0);
            }

            MemoryBuffer<byte> allZeroSparse = new byte[sparseTestSize];

            int isZeroTestSize = 4;

            Packet packet1 = new Packet();
            for (int i = 0;i<1500;i++)
            packet1.Enqueue("H"._GetBytes_Ascii());

            var queue = new MicroBenchmarkQueue()

            .Add(new MicroBenchmark($"New Packet", Benchmark_CountForFast, count =>
            {
                Memory<byte> hello = new byte[1500];// "Hello World Hello World Hello World Hello World Hello World Hello World "._GetBytes_Ascii();
                for (int c = 0; c < count; c++)
                {
                    Packet newPacket = new Packet(hello);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"Packet struct I/O", Benchmark_CountForFast, count =>
            {
                Memory<byte> hello = "Hello World Hello World Hello World Hello World Hello World Hello World "._GetBytes_Ascii();
                Packet newPacket = new Packet(hello);
                for (int c = 0; c < count; c++)
                {
                    PacketPin<long> pin = newPacket.GetPin<long>(0);
                    long v = pin.Value;
                    //pin.Value = 1;
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"Packet Test", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    packet1.PutContiguous(0, 1500);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"Util.Rand()", Benchmark_CountForSlow, count =>
            {
                Span<byte> dest = new byte[100];

                for (int c = 0; c < count; c++)
                {
                    Util.Rand(dest);
                }
            }), enabled: true, priority: 190518)

            .Add(new MicroBenchmark($"Secure.Rand()", Benchmark_CountForSlow, count =>
            {
                Span<byte> dest = new byte[100];

                for (int c = 0; c < count; c++)
                {
                    Secure.Rand(dest);
                }
            }), enabled: true, priority: 190518)

            .Add(new MicroBenchmark($"IsZero - NonSparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlySpan<byte> data = nonSparse.Slice(0, isZeroTestSize);

                for (int c = 0; c < count; c++)
                {
                    Util.IsZero(data);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"IsZero - All Zero Sparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlySpan<byte> data = allZeroSparse.Slice(0, isZeroTestSize);

                for (int c = 0; c < count; c++)
                {
                    Util.IsZero(data);
                }
            }), enabled: true, priority: 190519)


            .Add(new MicroBenchmark($"GetSparseChunks - NonSparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlyMemory<byte> data = nonSparse;

                for (int c = 0; c < count; c++)
                {
                    Util.GetSparseChunks(data, 4096);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"GetSparseChunks - Sparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlyMemory<byte> data = sparse;

                for (int c = 0; c < count; c++)
                {
                    Util.GetSparseChunks(data, 4096);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"GetSparseChunks - All Zero Sparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlyMemory<byte> data = allZeroSparse;

                for (int c = 0; c < count; c++)
                {
                    Util.GetSparseChunks(data, 4096);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"ParseEnum", Benchmark_CountForNormal, count =>
            {
                string str = LogPriority.Info.ToString();

                for (int c = 0; c < count; c++)
                {
                    str._ParseEnum(LogPriority.Trace);
                }
            }), enabled: true, priority: 190518)

            .Add(new MicroBenchmark($"Memory Copy by Array.CopyTo (Util.CopyByte) {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Util.CopyByte(byteArrayCopy2, 0, byteArrayCopy, 0, byteArrayCopy.Length);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Buffer.BlockCopy {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Buffer.BlockCopy(byteArrayCopy2, 0, byteArrayCopy, 0, byteArrayCopy.Length);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Span.CopyTo {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    var span1 = memcopySrc.Span;
                    var span2 = memcopyDst.Span;
                    span1.CopyTo(span2);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Span.ToArray {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    var span1 = memcopySrc.Span;
                    span1.ToArray();
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Span loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    var span1 = memcopySrc.Span;
                    var span2 = memcopyDst.Span;
                    for (int i = 0; i < memcopyLength; i++)
                    {
                        span1[i] = span2[i];
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Array loop #1 {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    for (int i = 0; i < byteArrayCopy.Length; i++)
                    {
                        byteArrayCopy[i] = 0;
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Array loop #2 {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    int len = byteArrayCopy.Length;
                    for (int i = 0; i < len; i++)
                    {
                        byteArrayCopy[i] = 0;
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Span Loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Span<byte> span = memcopySrc.Span;
                    for (int i = 0; i < memcopyLength; i++)
                    {
                        span[i] = 0;
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by unsafe pointer loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Span<byte> span = memcopySrc.Span;
                    unsafe
                    {
                        fixed (byte* ptr = &span[0])
                        {
                            for (int i = 0; i < memcopyLength; i++)
                            {
                                ptr[i] = 0;
                            }
                        }
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Span.Fill {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Span<byte> span = memcopySrc.Span;
                    span.Fill(0);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Array.Fill {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Array.Fill<byte>(byteArrayCopy, 0);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("GetSparseChunks", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                    Util.GetSparseChunks(sparseTest1.Memory._AsReadOnlyMemory(), 10_000);
            }), enabled: true, priority: 190506)

            .Add(new MicroBenchmark("CallAsyncWithAwait", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.CallAsyncWithAwaitLoop(count)._GetResult();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("CallAsyncWithNonAwait", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.CallAsyncWithNonAwaitLoop(count)._GetResult();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Rand[16]", Benchmark_CountForNormal, count =>
            {
                for (int c = 0; c < count; c++)
                    Util.Rand(16);
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Guid.NewGuid().ToString()", Benchmark_CountForNormal, count =>
            {
                for (int c = 0; c < count; c++)
                    Guid.NewGuid().ToString();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_ReadOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectSlow = BenchmarkTestTarget1.String_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_GetOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectSlow = BenchmarkTestTarget1.String_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_Copenhagen_ReadOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectSlow = BenchmarkTestTarget1.String_Copenhagen_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_Copenhagen_GetOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectSlow = BenchmarkTestTarget1.String_Copenhagen_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_ReadOnly value", Benchmark_CountForVeryFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_GetOnly value", Benchmark_CountForVeryFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_Copenhagen_ReadOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_Copenhagen_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_Copenhagen_GetOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_Copenhagen_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("AsyncCallMethodFromAsyncMethod", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.AsyncCallMethodFromAsyncMethod(count)._GetResult();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("AsyncCallMethodFromSyncMethod", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.AsyncCallMethodFromSyncMethod(count);
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Lock for readonly object", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    lock (BenchmarkTestTarget1.LockObj_ReadOnly)
                        Limbo.SInt32Volatile++;
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Lock for get property object", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    lock (BenchmarkTestTarget1.LockObj_Property)
                        Limbo.SInt32Volatile++;
                }
            }), enabled: true, priority: 190505);

            queue.Run();
        }

        [ConsoleCommand(
            "BenchMark command",
            "BenchMark [arg]",
            "BenchMark test")]
        static int BenchMark(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            bool enableRecords = true;

            if (Env.IsCoresLibraryDebugBuild || IsDebugBuildChecker.IsDebugBuild || Env.IsDebuggerAttached || CoresConfig.DebugSettings.LeakCheckerFullStackLog)
            {
                Con.WriteLine("*** Warning: The benchmark is under the debug mode or full stack trace mode. We do not record to files.");
                Con.WriteLine();

                enableRecords = false;
            }

            GlobalMicroBenchmark.SetParameters(enableRecords);

            GlobalMicroBenchmark.RecordStart();

            BenchMark_Test1();

            return 0;
        }
    }
}
