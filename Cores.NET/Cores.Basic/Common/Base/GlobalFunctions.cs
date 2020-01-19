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
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Globals
{
    public static partial class Basic
    {
        static volatile int VolatileZero = 0;

        public static readonly CriticalSection GlobalSuperLock = new CriticalSection();

        public static int NoOp()
        {
            VolatileZero = 0;
            VolatileZero = VolatileZero & VolatileZero;
            return 0;
        }

        public const MethodImplOptions Inline = MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization;

        public static int DoNothing() => NoOp();

        public static void Sleep(int msecs) => Kernel.SleepThread(msecs);

        public static Task SleepAsync(int msecs) => Task.Delay(msecs);

        public static T UnixOrWindows<T>(T unix, T windows) => Env.IsUnix ? unix : windows;

        [MethodImpl(Inline)]
        public static void Sync(Action action) => TaskUtil.Sync(action);

        [MethodImpl(Inline)]
        public static T Sync<T>(Func<T> func) => TaskUtil.Sync(func);

        [MethodImpl(Inline)]
        public static void Async(Func<Task> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: false)._GetResult();

        [MethodImpl(Inline)]
        public static T Async<T>(Func<Task<T>> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: false)._GetResult();

        [MethodImpl(Inline)]
        public static Task AsyncAwait(Func<Task> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: true);

        [MethodImpl(Inline)]
        public static Task<T> AsyncAwait<T>(Func<Task<T>> asyncFunc) => TaskUtil.StartAsyncTaskAsync(asyncFunc, leakCheck: true);

        [MethodImpl(Inline)]
        public static IAsyncDisposable AsyncAwaitScoped<T>(Func<CancellationToken, Task<T>> asyncFunc, bool noErrorMessage = false)
        {
            CancellationTokenSource cts = new CancellationTokenSource();

            Task<T> task = asyncFunc(cts.Token);

            AsyncHolder h = new AsyncHolder(async () =>
            {
                cts._TryCancelNoBlock();

                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    if (noErrorMessage == false)
                    {
                        ex._Debug();
                    }
                }
            },
            LeakCounterKind.AsyncAwaitScoped);

            return h;
        }

        public static bool TryRetBool(Action action, bool noDebugMessage = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null, bool printThreadId = false)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    DebugWhereContainer c = new DebugWhereContainer(ex, filename, line, printThreadId ? Environment.CurrentManagedThreadId : 0, caller);
                    Dbg.WriteLine(c);
                }
                return false;
            }
        }

        public static T TryIfErrorRetDefault<T>(Func<T> func, T defaultValue = default, bool noDebugMessage = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null, bool printThreadId = false)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    DebugWhereContainer c = new DebugWhereContainer(ex, filename, line, printThreadId ? Environment.CurrentManagedThreadId : 0, caller);
                    Dbg.WriteLine(c);
                }
                return defaultValue;
            }
        }

        public static async Task<T> TryIfErrorRetDefaultAsync<T>(Func<Task<T>> func, T defaultValue = default, bool noDebugMessage = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null, bool printThreadId = false)
        {
            try
            {
                return await func();
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    DebugWhereContainer c = new DebugWhereContainer(ex, filename, line, printThreadId ? Environment.CurrentManagedThreadId : 0, caller);
                    Dbg.WriteLine(c);
                }
                return defaultValue;
            }
        }

        public static LocalFileSystem Lfs => LocalFileSystem.Local;
        public static LargeFileSystem LLfs => LargeFileSystem.Local;
        public static Utf8BomFileSystem LfsUtf8 => LocalFileSystem.LocalUtf8;
        public static LargeFileSystem LLfsUtf8 => LargeFileSystem.LocalUtf8;
        public static ResourceFileSystem CoresRes => Res.Cores;

        public static PathParser PP => PathParser.Local;

#if CORES_BASIC_GIT
        public static GitFileSystem GitFs(string url, string commitIdOrRefName = "") => GitGlobalFs.GetFileSystem(url, commitIdOrRefName);
#endif // CORES_BASIC_GIT

        public static partial class Res
        {
            public static readonly ResourceFileSystem Cores = ResourceFileSystem.CreateOrGet(
                new AssemblyWithSourceInfo(typeof(Res), new SourceCodePathAndMarkerFileName(CoresLib.CoresLibSourceCodeFileName, Consts.FileNames.RootMarker_Library_CoresBasic)));

            public static readonly FileSystem AppRoot = LocalFileSystem.AppRoot;
        }

        public static LocalTcpIpSystem LocalNet => LocalTcpIpSystem.Local;

        public const int DefaultSize = int.MinValue;

        [MethodImpl(Inline)]
        public static Task<TResult> TR<TResult>(TResult result) => Task.FromResult(result);

        public static void For(int count, Action<int> action)
        {
            for (int i = 0; i < count; i++)
                action(i);
        }

        public static void For(int count, Action action)
        {
            for (int i = 0; i < count; i++)
                action();
        }

        public static Memory<byte> Load(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, FileSystem? fs = null, CancellationToken cancel = default) =>
            path._Load(maxSize, flags, fs, cancel);

        public static Memory<byte> Load(string path, FileSystem? fs, FileFlags flags = FileFlags.None, int maxSize = int.MaxValue, CancellationToken cancel = default) =>
            path._Load(maxSize, flags, fs, cancel);

        public static Memory<byte> Load(string path, FileFlags flags, FileSystem? fs = null, int maxSize = int.MaxValue, CancellationToken cancel = default) =>
            path._Load(maxSize, flags, fs, cancel);
    }
}

