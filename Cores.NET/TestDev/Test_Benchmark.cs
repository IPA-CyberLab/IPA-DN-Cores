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
                SampleAsyncMethod().GetResult();
        }
    }

    partial class TestDevCommands
    {
        const int Benchmark_CountForVeryFast = 200000000;
        const int Benchmark_CountForFast = 10000000;
        const int Benchmark_CountForNormal = 10000;

        static void BenchMark_Test1()
        {
            var queue = new MicroBenchmarkQueue()

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
                BenchmarkTestTarget1.AsyncCallMethodFromAsyncMethod(count).GetResult();
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

        [ConsoleCommandMethod(
            "BenchMark command",
            "BenchMark [arg]",
            "BenchMark test")]
        static int BenchMark(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            bool enableRecords = true;

            if (Env.IsCoresLibraryDebugBuild || IsDebugBuildChecker.IsDebugBuild || Env.IsDebuggerAttached)
            {
                Con.WriteLine("*** Warning: The benchmark is under the debug mode. We do not record to files.");
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
