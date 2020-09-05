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
// Description

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public class ShellProcessorSettings
    {
        public Encoding Encoding { get; set; } = Str.Utf8Encoding;
        public int RecvTimeout { get; set; } = Consts.Timeouts.DefaultShellPromptRecvTimeout;
        public int SendTimeout { get; set; } = Consts.Timeouts.DefaultShellPromptSendTimeout;
        public string SendNewLineStr { get; set; } = Str.NewLine_Str_Unix;
    }

    public class ShellProcessor : AsyncService
    {
        public ShellClientSock Sock { get; }
        public ShellProcessorSettings Settings { get; }
        bool DisposeObject { get; }

        BinaryLineReader Reader { get; }

        public ShellProcessor(ShellClientSock sock, bool disposeObject = false, ShellProcessorSettings? settings = null)
        {
            try
            {
                if (settings == null) settings = new ShellProcessorSettings();

                this.Sock = sock;
                this.DisposeObject = disposeObject;
                this.Settings = settings;

                this.Reader = new BinaryLineReader(this.Sock.Stream);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                if (this.DisposeObject)
                {
                    await this.Sock._DisposeSafeAsync();
                }
            }
            catch
            {
                await base.CleanupImplAsync(ex);
            }
        }

        protected string MemoryToString(MemoryStream ms)
        {
            return Settings.Encoding.GetString(ms.ToArray());
        }

        protected async Task<List<string>> ReadLinesInternalAsync(Func<MemoryStream, object?, bool>? finishDeterminer = null, CancellationToken cancel = default)
        {
            this.Sock.Stream.ReadTimeout = Settings.RecvTimeout;

            BinaryLineReaderOption opt = new BinaryLineReaderOption(finishDeterminer, (ms, p, c) =>
            {
                string previewLine = MemoryToString(ms);

                Con.WriteLine($"Recv: {previewLine}");
            });

            RefBool isEof = new RefBool();

            var result = await Reader.ReadLinesUntilEofAsync(isEof, cancel: cancel, option: opt);

            if (result == null || isEof)
            {
                // 期待されたプロンプト等が現われず EOF に達してしまったので、途中のごみは出さずに切断されたことにする
                this.Sock.Disconnect();
                throw new DisconnectedException();
            }

            return result._ToStringList(this.Settings.Encoding);
        }

        protected async Task SendLineInternalAsync(string line, CancellationToken cancel = default)
        {
            this.Sock.Stream.WriteTimeout = Settings.SendTimeout;

            line = line._GetLines(true)._LinesToStr(Settings.SendNewLineStr);

            foreach (var oneLine in line._GetLines())
            {
                Con.WriteLine($"Send: {oneLine}");
            }

            await this.Sock.Stream.SendAsync(this.Settings.Encoding.GetBytes(line), cancel);
            await this.Sock.Stream.FlushAsync(cancel);
        }
    }

    public class UnixShellProcessorSettings : ShellProcessorSettings
    {
    }

    public class UnixShellProcessor : ShellProcessor
    {
        public new ShellProcessorSettings Settings => (ShellProcessorSettings)base.Settings;

        public UnixShellProcessor(ShellClientSock sock, bool disposeObject = false, UnixShellProcessorSettings? settings = null)
            : base(sock, disposeObject, settings ?? new UnixShellProcessorSettings())
        {
            try
            {
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        // bash を初期化する
        public async Task InitBashAsync(CancellationToken cancel = default)
        {
            // 最初のプロンプトを待つ
            await RecvLinesUntilBasicUnixPromptAsync(cancel);

            // "bash" を実行する
            await SendLineInternalAsync("bash", cancel);

            // bash 起動後のプロンプトを待つ
            await RecvLinesUntilBasicUnixPromptAsync(cancel);
        }

        // UNIX 用の標準的なプロンプトが出てくるまで行を読み進める
        public async Task<List<string>> RecvLinesUntilBasicUnixPromptAsync(CancellationToken cancel = default)
        {
            return await this.ReadLinesInternalAsync(BasicUnixPromptDeterminer, cancel);
        }

        // UNIX 用の標準的なシェルの行終了識別関数
        // "#", "$", "# ", "$ " のいずれかで終わるとプロンプトとみなす
        protected virtual bool BasicUnixPromptDeterminer(MemoryStream ms, object? param)
        {
            string str = MemoryToString(ms);

            if (str.EndsWith("#") ||
                str.EndsWith("$") ||
                str.EndsWith("# ") ||
                str.EndsWith("$ "))
            {
                return true;
            }

            return false;
        }
    }
}

#endif

