// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    // 指定した数までタスクを並行して実行するクラス。
    // 指定した数以上のタスクがすでに実行されている場合は、実行数が指定数以下になるまで新しいタスクの登録を待機する。
    public class AsyncConcurrentTask
    {
        public int MaxConcurrentTasks { get; }

        readonly SemaphoreSlim Sem;

        public AsyncConcurrentTask(int maxConcurrentTasks)
        {
            this.MaxConcurrentTasks = Math.Max(maxConcurrentTasks, 1);

            this.Sem = new SemaphoreSlim(this.MaxConcurrentTasks, this.MaxConcurrentTasks);
        }

        readonly CriticalSection Lock = new CriticalSection();

        readonly AsyncPulse Pulse = new AsyncPulse();

        public int CurrentConcurrentTasks { get; private set; } = 0;

        // タスクを新たに追加し実行開始する。ただし、すでに実行中のタスク数が上限以上の場合は、上限以下になるまで非同期ブロックする
        public async Task<Task<TResult>> StartTaskAsync<TParam, TResult>(Func<TParam, CancellationToken, Task<TResult>> targetTask, TParam param, CancellationToken cancel = default)
        {
            await this.Sem.WaitAsync(cancel);

            // ターゲットタスクを開始する。
            // ターゲットタスクが完了したら、セマフォを解放するようにする。
            Task<TResult> ret = TaskUtil.StartAsyncTaskAsync<TResult>(async () =>
            {
                try
                {
                    return await targetTask(param, cancel);
                }
                finally
                {
                    this.Sem.Release();
                }
            });

            return ret;
        }
    }
}

#endif

