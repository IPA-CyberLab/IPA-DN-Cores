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
// Apache Guacamole Protocol Client

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
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
    [Flags]
    public enum GuaProtocol
    {
        Rdp = 0,
        Vnc,
    }

    public class GuaPacket
    {
        public string Opcode { get; set; } = "";
        public List<string> Args { get; set; } = new List<string>();

        public async Task SendPacket(Stream st, CancellationToken cancel = default)
        {
            if (this.Opcode._IsEmpty())
            {
                throw new CoresLibException("Opcode is empty.");
            }

            List<string> tmp = new List<string>();
            tmp.Add(this.Opcode);
            tmp.AddRange(this.Args);

            var data = BuildTokens(tmp);

            await st.WriteAsync(data, cancel);
        }

        public static async Task<GuaPacket> RecvPacket(Stream st, CancellationToken cancel = default)
        {
            List<string> tokens = await RecvAndParseTokensAsync(st, cancel);

            if (tokens.Count == 0)
            {
                throw new CoresLibException("Protocol Error: tokens.Count == 0");
            }

            GuaPacket ret = new GuaPacket
            {
                Opcode = tokens[0],
                Args = tokens.Skip(1).ToList(),
            };

            return ret;
        }

        public static MemoryBuffer<byte> BuildTokens(IEnumerable<string> tokens)
        {
            MemoryBuffer<byte> sb = new MemoryBuffer<byte>();

            int num = 0;

            foreach (string token in tokens)
            {
                if (num >= 1)
                {
                    sb.WriteStr(",");
                }
                num++;

                var data = token._GetBytes_UTF8();

                int len = data.Length;
                sb.WriteStr(len.ToString());
                sb.WriteStr(".");
                sb.Write(data);
            }
            sb.WriteStr(";");

            return sb;
        }

        public static async Task<List<string>> RecvAndParseTokensAsync(Stream st, CancellationToken cancel = default)
        {
            List<string> ret = new List<string>();

            int totalLen = 0;

            while (true)
            {
                int len = await RecvLengthAsync(st, cancel);

                totalLen += len;

                if (totalLen > Consts.Numbers.GenericMaxSize_Middle)
                {
                    throw new CoresLibException($"Protocol Error: totalLen == {totalLen}");
                }

                var data = await st._ReadAllAsync(len, cancel);
                string dataStr = data._GetString_UTF8();

                ret.Add(dataStr);

                if (ret.Count > Consts.Numbers.GenericMaxEntities_Small)
                {
                    throw new CoresLibException($"Protocol Error: ret.Count == {ret.Count}");
                }

                char c = (char)(await st.ReceiveByteAsync(cancel));
                if (c == ',')
                {
                    continue;
                }
                else if (c == ';')
                {
                    break;
                }
                else
                {
                    throw new CoresLibException($"Protocol Error: Unexpected character: '{c}' ({(int)c})");
                }
            }

            return ret;
        }

        // 0 ～ 999999 までの範囲内の数字を受信する
        public static async Task<int> RecvLengthAsync(Stream st, CancellationToken cancel = default)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                char c = (char)(await st.ReceiveByteAsync(cancel));
                if (c == '.')
                {
                    break;
                }
                sb.Append(c);
                if (sb.Length >= 7)
                {
                    throw new CoresLibException("Protocol Error: sb.Length >= 7");
                }
            }
            int ret = sb.ToString()._ToInt();
            if (ret < 0)
            {
                throw new CoresLibException($"Protocol Error: ret == {ret}");
            }

            return ret;
        }
    }

    [Flags]
    public enum GuaResizeMethods
    {
        DisplayUpdate = 0,
        Reconnect,
    }

    public class GuaPreference
    {
        public bool EnableWallPaper { get; set; } = true;
        public bool EnableTheming { get; set; } = true;
        public bool EnableFontSmoothing { get; set; } = true;
        public bool EnableFullWindowDrag { get; set; } = true;
        public bool EnableDesktopComposition { get; set; } = true;
        public bool EnableMenuAnimations { get; set; } = true;
        public GuaResizeMethods ResizeMethod { get; set; } = GuaResizeMethods.DisplayUpdate;
        public int InitialWidth { get; set; } = 1024;
        public int InitialHeight { get; set; } = 768;
    }

    public class GuaClientSettings
    {
        public GuaProtocol Protocol { get; }
        public string GuacdHostname { get; }
        public int GuacdPort { get; }
        public string ServerHostname { get; }
        public int ServerPort { get; }
        public GuaPreference Preference { get; }
        public TcpIpSystem TcpIp { get; }

        public GuaClientSettings(string guacdHostname, int guacdPort, GuaProtocol protocol, string serverHostname, int serverPort, GuaPreference preference, TcpIpSystem? tcpIp = null)
        {
            GuacdHostname = guacdHostname;
            GuacdPort = guacdPort;
            TcpIp = tcpIp ?? LocalNet;
            Protocol = protocol;
            ServerHostname = serverHostname;
            ServerPort = serverPort;
            Preference = preference._CloneWithJson();
        }
    }

    public class GuaClient : AsyncService
    {
        public GuaClientSettings Settings { get; }

        public GuaClient(GuaClientSettings settings)
        {
            this.Settings = settings;
        }

        Once Started;

        ConnSock? Sock = null!;
        PipeStream? Stream = null!;

        public async Task StartAsync(CancellationToken cancel = default)
        {
            if (Started.IsFirstCall() == false) throw new CoresLibException("StartAsync has already been called.");

            try
            {
                Sock = await Settings.TcpIp.ConnectIPv4v6DualAsync(new TcpConnectParam(this.Settings.GuacdHostname, this.Settings.GuacdPort), cancel);
                Stream = Sock.GetStream(true);
            }
            catch
            {
                await Sock._DisposeSafeAsync();
                await Stream._DisposeSafeAsync();

                throw;
            }
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

