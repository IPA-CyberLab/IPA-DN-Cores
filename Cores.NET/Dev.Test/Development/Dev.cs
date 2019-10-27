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
    // 子プロセスの実行パラメータのフラグ
    [Flags]
    public enum ExecFlags
    {
        None = 0,
        KillProcessGroup = 1,                   // Kill する場合はプロセスグループを Kill する
        EasyInputOutputMode = 2,                // 標準出力およびエラー出力の結果を簡易的に受信し、標準入力の結果を簡易的に送信する (結果を確認しながらの対話はできない)

        Default = KillProcessGroup | ExecFlags.EasyInputOutputMode,
    }

    // 子プロセスの実行パラメータ
    public class ExecOptions
    {
        public string FileName { get; }
        public string Arguments { get; }
        public IEnumerable<string>? ArgumentsList { get; }
        public string CurrentDirectory { get; }
        public ExecFlags Flags { get; }
        public int EasyOutputMaxSize { get; }
        public string? EasyInputStr { get; }

        public ExecOptions(string fileName, string? arguments = null, string? currentDirectory = null, ExecFlags flags = ExecFlags.Default,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null)
        {
            this.FileName = fileName._NullCheck();
            this.Arguments = arguments._NonNull();
            this.ArgumentsList = null;
            this.CurrentDirectory = currentDirectory._NonNull();
            this.Flags = flags;
            this.EasyOutputMaxSize = easyOutputMaxSize._Max(1);
            this.EasyInputStr = easyInputStr._NullIfZeroLen();
        }

        public ExecOptions(string fileName, IEnumerable<string> argumentsList, string? currentDirectory = null, ExecFlags flags = ExecFlags.Default,
            int easyOutputMaxSize = Consts.Numbers.DefaultLargeBufferSize, string? easyInputStr = null)
        {
            this.FileName = fileName._NullCheck();
            this.Arguments = "";
            this.ArgumentsList = argumentsList;
            this.CurrentDirectory = currentDirectory._NonNull();
            this.Flags = flags;
            this.EasyOutputMaxSize = easyOutputMaxSize._Max(1);
            this.EasyInputStr = easyInputStr._NullIfZeroLen();
        }
    }

    // 子プロセス (主に CUI) を実行し結果を取得するユーティリティクラス
    public static class Exec
    {

    }

    // 実行中および実行後の子プロセスのインスタンス
    public class ExecInstance : AsyncServiceWithMainLoop
    {
        public ExecOptions Options { get; }

        public AsyncManualResetEvent ExitEvent { get; } = new AsyncManualResetEvent();

        public Encoding InputEncoding { get; }
        public Encoding OutputEncoding { get; }
        public Encoding ErrorEncoding { get; }

        PipePoint StandardPipePoint_MySide;
        PipePointStreamWrapper StandardPipeWrapper;
        PipePoint _InputOutputPipePoint;

        PipePoint ErrorPipePoint_MySide;
        PipePointStreamWrapper ErrorPipeWrapper;
        PipePoint _ErrorPipePoint;

        volatile bool EasyOutputAndErrorDataCompleted = false;
        Memory<byte> _EasyOutputData;
        Memory<byte> _EasyErrorData;

        public PipePoint InputOutputPipePoint
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode)) throw new NotSupportedException();
                return _InputOutputPipePoint;
            }
        }
        public PipePoint ErrorPipePoint
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode)) throw new NotSupportedException();
                return _ErrorPipePoint;
            }
        }

        public Memory<byte> EasyOutputData
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode) == false) throw new NotSupportedException();
                if (EasyOutputAndErrorDataCompleted == false) throw new CoresException("The result string is not received yet.");
                return _EasyOutputData;
            }
        }

        public Memory<byte> EasyErrorData
        {
            get
            {
                if (this.Options.Flags.Bit(ExecFlags.EasyInputOutputMode) == false) throw new NotSupportedException();
                if (EasyOutputAndErrorDataCompleted == false) throw new CoresException("The result string is not received yet.");
                return _EasyErrorData;
            }
        }

        string? _EasyOutputStr = null;
        public string EasyOutputStr
        {
            get
            {
                if (_EasyOutputStr == null)
                {
                    _EasyOutputStr = EasyOutputData._GetString(this.OutputEncoding);
                }
                return _EasyOutputStr;
            }
        }

        string? _EasyErrorStr = null;
        public string EasyErrorStr
        {
            get
            {
                if (_EasyErrorStr == null)
                {
                    _EasyErrorStr = EasyErrorData._GetString(this.ErrorEncoding);
                }
                return _EasyErrorStr;
            }
        }

        int? _ExitCode = null;

        public int ExitCode
        {
            get
            {
                return _ExitCode ?? throw new CoresException("Process is still running.");
            }
        }

        readonly Process Proc;

        readonly Task? EasyInputTask = null;
        readonly Task<Memory<byte>>? EasyOutputTask = null;
        readonly Task<Memory<byte>>? EasyErrorTask = null;

        readonly NetAppStub? EasyInputOutputStub = null;
        readonly PipeStream? EasyInputOutputStream = null;
        readonly NetAppStub? EasyErrorStub = null;
        readonly PipeStream? EasyErrorStream = null;

        public ExecInstance(ExecOptions options)
        {
            try
            {
                this.InputEncoding = Console.OutputEncoding;
                this.OutputEncoding = this.ErrorEncoding = Console.InputEncoding;

                this.Options = options._NullCheck();

                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = options.FileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = options.CurrentDirectory._NullIfEmpty(),
                };

                if (options.ArgumentsList != null)
                {
                    options.ArgumentsList._DoForEach(a => info.ArgumentList.Add(a._NonNull()));
                }
                else
                {
                    info.Arguments = options.Arguments._NullIfZeroLen();
                }

                Proc = Process.Start(info);

                // 標準入出力を接続する
                this.StandardPipePoint_MySide = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide);
                this._InputOutputPipePoint = this.StandardPipePoint_MySide.CounterPart._NullCheck();
                this.StandardPipeWrapper = new PipePointStreamWrapper(StandardPipePoint_MySide, Proc.StandardOutput.BaseStream, Proc.StandardInput.BaseStream);

                // 標準エラー出力を接続する
                this.ErrorPipePoint_MySide = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide);
                this._ErrorPipePoint = this.ErrorPipePoint_MySide.CounterPart._NullCheck();
                this.ErrorPipeWrapper = new PipePointStreamWrapper(ErrorPipePoint_MySide, Proc.StandardError.BaseStream, Stream.Null);

                if (options.Flags.Bit(ExecFlags.EasyInputOutputMode))
                {
                    this.EasyInputOutputStub = this._InputOutputPipePoint.GetNetAppProtocolStub();
                    this.EasyInputOutputStream = this.EasyInputOutputStub.GetStream();

                    this.EasyErrorStub = this._ErrorPipePoint.GetNetAppProtocolStub();
                    this.EasyErrorStream = this.EasyErrorStub.GetStream();

                    if (options.EasyInputStr != null)
                    {
                        // 標準入力の注入タスク
                        this.EasyInputTask = this.EasyInputOutputStream.SendAsync(this.Options.EasyInputStr!._GetBytes(this.InputEncoding), this.GrandCancel)._LeakCheck();
                    }

                    // 標準出力の読み出しタスク
                    this.EasyOutputTask = this.EasyInputOutputStream._ReadWithMaxBufferSizeAsync(this.Options.EasyOutputMaxSize, this.GrandCancel)._LeakCheck();

                    // 標準エラー出力の読み出しタスク
                    this.EasyErrorTask = this.EasyErrorStream._ReadWithMaxBufferSizeAsync(this.Options.EasyOutputMaxSize, this.GrandCancel)._LeakCheck();
                }

                this.StartMainLoop(MainLoopAsync);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        async Task MainLoopAsync(CancellationToken cancel)
        {
            await Task.Yield();

            try
            {
                // 終了するまで待機する (Cancel された場合は Kill されるので以下の待機は自動的に解除される)
                Proc.WaitForExit();
            }
            catch (Exception ex)
            {
                ex._Debug();
                throw;
            }
            finally
            {
                // 終了した or 待機に失敗した
                // 戻り値をセットする
                try
                {
                    _ExitCode = Proc.ExitCode;
                }
                catch
                {
                    _ExitCode = -1;
                }

                if (this.EasyInputTask != null)
                {
                    await this.EasyInputTask._TryAwait();
                }

                if (this.EasyOutputTask != null)
                {
                    this._EasyOutputData = await this.EasyOutputTask;
                }

                if (this.EasyErrorTask != null)
                {
                    this._EasyErrorData = await this.EasyErrorTask;
                }

                EasyOutputAndErrorDataCompleted = true;

                Interlocked.MemoryBarrier();

                // 完了フラグを立てる
                ExitEvent.Set(true);
            }
        }

        // 終了まで待機する
        public async Task<int> WaitForExitAsync(int timeout = Timeout.Infinite, CancellationToken cancel = default)
        {
            await ExitEvent.WaitAsync(timeout, cancel);

            return this._ExitCode ?? -1;
        }
        public int WaitForExit(int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => WaitForExitAsync(timeout, cancel)._GetResult();

        protected override void CancelImpl(Exception? ex)
        {
            try
            {
                if (Proc != null)
                {
                    if (Proc.HasExited == false)
                    {
                        Proc.Kill(Options.Flags.Bit(ExecFlags.KillProcessGroup));
                    }
                }
            }
            finally
            {
                base.CancelImpl(ex);
            }
        }

        protected override void DisposeImpl(Exception? ex)
        {
            this.EasyInputOutputStub._DisposeSafe();
            this.EasyInputOutputStream._DisposeSafe();

            this.EasyErrorStub._DisposeSafe();
            this.EasyErrorStream._DisposeSafe();

            this.StandardPipeWrapper._DisposeSafe();
            this.ErrorPipeWrapper._DisposeSafe();

            this.StandardPipePoint_MySide._DisposeSafe();
            this.ErrorPipePoint_MySide._DisposeSafe();

            base.DisposeImpl(ex);
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

#endif

