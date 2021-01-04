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
// Thin Telework System Protocol Stack

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
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static partial class Consts
    {
        public static partial class ThinClient
        {
            public const int ProtocolCommTimeoutMsecs = 3 * 60 * 1000;
            public const int DummyClientVer = 18;
            public const int DummyClientBuild = 9876;
        }
    }

    [Flags]
    public enum ThinAuthType
    {
        None = 0,
        Password = 1,
        UserPassword = 2,
        Cert = 3,
        SmartCard = 4,
    }

    [Flags]
    public enum ThinSvcType
    {
        Rdp = 0,
        Vnc = 1,
    }

    [Flags]
    public enum ThinServerCaps
    {
        Urdp2 = 2,
        UrdpVeryLimited = 4,
        WinRdpEnabled = 8,
    }

    public class ThinClientAuthRequest : IDialogRequestData
    {
        public bool UseAdvancedSecurity;
        public ThinAuthType AuthType;
        public ReadOnlyMemory<byte> Rand;
    }

    public class ThinClientAuthResponse : IDialogRequestData
    {
        public string Password = "";
        public string Username = "";
    }

    public class ThinClientConnectOptions
    {
        public string Pcid { get; }
        public WideTunnelClientOptions ClientOptions { get; }
        public IPAddress ClientIpAddress { get; }
        public string ClientFqdn { get; }

        public ThinClientConnectOptions(string pcid, IPAddress clientIp, string clientFqdn, WideTunnelClientOptions clientOptions = WideTunnelClientOptions.None)
        {
            this.Pcid = pcid;
            this.ClientOptions = clientOptions;
            this.ClientIpAddress = clientIp;
            this.ClientFqdn = clientFqdn;
        }
    }

    public class ThinClientOptions
    {
        public WideTunnelOptions WideTunnelOptions { get; }
        public DialogSessionManager SessionManager { get; }

        public ThinClientOptions(WideTunnelOptions wideTunnelOptions, DialogSessionManager sessionManager)
        {
            this.WideTunnelOptions = wideTunnelOptions;
            this.SessionManager = sessionManager;
        }
    }

    public class ThinClient
    {
        public ThinClientOptions Options { get; }
        public DialogSessionManager SessionManager => Options.SessionManager;

        public ThinClient(ThinClientOptions options)
        {
            this.Options = options;
        }

        public DialogSession StartConnect(ThinClientConnectOptions connectOptions)
        {
            return this.SessionManager.StartNewSession(new DialogSessionOptions(SessionMainProcAsync, connectOptions));
        }

        async Task SessionMainProcAsync(DialogSession session, CancellationToken cancel)
        {
            ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Options.Param!;

            await using WideTunnel wt = new WideTunnel(this.Options.WideTunnelOptions);

            await ConnectMainAsync(wt, connectOptions, cancel, true, true, null!);
        }

        async Task ConnectMainAsync(WideTunnel wt, ThinClientConnectOptions connectOptions, CancellationToken cancel, bool checkPort, bool firstConnection,
            Func<ThinClientAuthRequest, Task<ThinClientAuthResponse>> authCallback)
        {
            await using var sock = await wt.WideClientConnectAsync(connectOptions.Pcid, connectOptions.ClientOptions, cancel);

            await using var st = sock.GetStream(true);

            st.ReadTimeout = st.WriteTimeout = Consts.ThinClient.ProtocolCommTimeoutMsecs;

            // バージョンを送信
            Pack p = new Pack();
            p.AddInt("ClientVer", Consts.ThinClient.DummyClientVer);
            p.AddInt("ClientBuild", Consts.ThinClient.DummyClientBuild);
            p.AddBool("CheckPort", checkPort);
            p.AddBool("FirstConnection", firstConnection);
            p.AddBool("HasURDP2Client", true);
            p.AddBool("SupportOtp", true);
            p.AddBool("SupportOtpEnforcement", true);
            p.AddBool("SupportInspect", true);
            p.AddBool("SupportServerAllowedMacListErr", true);
            p.AddIp("ClientLocalIP", connectOptions.ClientIpAddress);
            p.AddUniStr("UserName", "WebClient");
            p.AddUniStr("ComputerName", "WebClient");
            p.AddBool("SupportWatermark", true);

            await st._SendPackAsync(p, cancel);

            // 認証パラメータを受信
            p = await st._RecvPackAsync(cancel);

            var err = p.GetErrorFromPack();
            err.ThrowIfError(p);

            var authType = (ThinAuthType)p["AuthType"].SIntValueSafeNum;
            var svcType = (ThinSvcType)p["ServiceType"].SIntValueSafeNum;
            var caps = (ThinServerCaps)p["DsCaps"].SIntValueSafeNum;
            var rand = p["Rand"].DataValueNonNull;
            var machineKey = p["MachineKey"].DataValueNonNull;
            var isShareEnabled = p["IsShareDisabled"].BoolValue;
            var useAdvancedSecurity = p["UseAdvancedSecurity"].BoolValue;
            var isOtpEnabled = p["IsOtpEnabled"].BoolValue;
            var runInspect = p["RunInspect"].BoolValue;
            var lifeTime = p["Lifetime"].SIntValue;
            var lifeTimeMsg = p["LifeTimeMsg"].UniStrValueNonNull;
            var waterMarkStr1 = p["WatermarkStr1"].UniStrValueNonNull;
            var waterMarkStr2 = p["WatermarkStr2"].UniStrValueNonNull;

            if (isOtpEnabled)
            {
                throw new NotImplementedException();
            }

            if (runInspect)
            {
                throw new NotImplementedException();
            }

            // ユーザー認証
            if (useAdvancedSecurity == false)
            {
                // 古いユーザー認証
                if (authType == ThinAuthType.None)
                {
                    // 匿名認証
                    ThinClientAuthRequest authReq = new ThinClientAuthRequest
                    {
                        AuthType = ThinAuthType.None,
                        UseAdvancedSecurity = false,
                    };

                    var authRes = await authCallback(authReq);
                }
                else if (authType == ThinAuthType.Password)
                {
                    // パスワード認証
                    ThinClientAuthRequest authReq = new ThinClientAuthRequest
                    {
                        AuthType = ThinAuthType.Password,
                        UseAdvancedSecurity = false,
                    };

                    var authRes = await authCallback(authReq);

                    var passwordHash = Secure.HashSHA1(authRes.Password._GetBytes_UTF8());

                    MemoryBuffer<byte> hashSrc = new MemoryBuffer<byte>();
                    hashSrc.Write(passwordHash);
                    hashSrc.Write(rand);
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}

#endif

