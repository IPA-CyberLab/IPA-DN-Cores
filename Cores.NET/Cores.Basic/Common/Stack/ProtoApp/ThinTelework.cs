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
            public const int ConnectionQueueLength = 2;
            public const int ReconnectRetrySpanMsecs = 2 * 1000;
            public const int ReconnectRetrySpanMaxMsecs = 3 * 60 * 1000;
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
        None = 0,
        Urdp2 = 2,
        UrdpVeryLimited = 4,
        WinRdpEnabled = 8,
    }

    [Flags]
    public enum ThinServerMask64 : long
    {
        None = 0,
        UrdpClient = 1,
        WinRdpNormal = 2,
        WinRdpTs = 4,
        UserMode = 8,
        ServiceMode = 16,
        PolicyEnforced = 32,
        OtpEnabled = 64,
        SupportWolTrigger = 128,
        IsLimitedMode = 256,
    }

    public class ThinClientAcceptReadyNotification : IDialogRequestData
    {
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

    public class ThinClientConnection : AsyncService
    {
        public WtcSocket Socket { get; }
        public PipeStream Stream { get; }
        public ThinSvcType SvcType { get; }
        public bool IsShareDisabled { get; }
        public ThinServerCaps Caps { get; }
        public ThinServerMask64 ServerMask64 => (ThinServerMask64)Socket.Options.ConnectParam.ServerMask64;
        public bool RunInspect { get; }

        public ThinClientConnection(WtcSocket socket, PipeStream stream, ThinSvcType svcType, bool isShareDisabled, ThinServerCaps caps, bool runInspect)
        {
            Socket = socket;
            Stream = stream;
            SvcType = svcType;
            IsShareDisabled = isShareDisabled;
            Caps = caps;
            RunInspect = runInspect;
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.Stream._DisposeSafeAsync();
                await this.Socket._DisposeSafeAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
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
            return this.SessionManager.StartNewSession(new DialogSessionOptions(DialogSessionMainProcAsync, connectOptions));
        }

        async Task DialogSessionMainProcAsync(DialogSession session, CancellationToken cancel)
        {
            ThinClientConnectOptions connectOptions = (ThinClientConnectOptions)session.Options.Param!;

            await using WideTunnel wt = new WideTunnel(this.Options.WideTunnelOptions);

            ThinClientAuthResponse authResponseCache = null!;

            await using ThinClientConnection connection = await DcConnectEx(wt, connectOptions, cancel, true, true, async (req, c) =>
            {
                // 認証コールバック
                await Task.CompletedTask;

                var response = new ThinClientAuthResponse
                {
                    Password = Lfs.ReadStringFromFile(@"C:\tmp\yagi\TestPass.txt", oneLine: true),
                };

                authResponseCache = response;

                return response;
            });

            using var abort = new CancelWatcher(cancel);

            // このコネクションをキューに追加する
            ConcurrentQueue<ThinClientConnection> connectionQueue = new ConcurrentQueue<ThinClientConnection>();
            connectionQueue.Enqueue(connection);

            AsyncAutoResetEvent connectionAddedEvent = new AsyncAutoResetEvent();
            AsyncAutoResetEvent connectedEvent = new AsyncAutoResetEvent();

            // コネクションのキューの長さが ConnectionQueueLength 未満になると自動的にコネクションを張る非同期タスクを開始する
            var connectionKeepTask = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                int numRetry = 0;

                while (abort.IsCancellationRequested == false)
                {
                    // キューの長さが少なくなれば追加コネクションを接続する
                    while (abort.IsCancellationRequested == false)
                    {
                        if (connectionQueue.Count < Consts.ThinClient.ConnectionQueueLength)
                        {
                            try
                            {
                                // 追加接続
                                ThinClientConnection additionalConnection = await DcConnectEx(wt, connectOptions, abort, false, false, async (req, c) =>
                                {
                                    await Task.CompletedTask;
                                    // 認証コールバック
                                    // キャッシュされた認証情報をそのまま応答
                                    return authResponseCache;
                                });

                                // 接続に成功したのでキューに追加
                                connectionQueue.Enqueue(additionalConnection);
                                connectionAddedEvent.Set(true);

                                numRetry = 0;
                            }
                            catch (Exception ex)
                            {
                                // 接続に失敗したので一定時間待ってリトライする
                                ex._Debug();

                                numRetry++;

                                int waitTime = Util.GenRandIntervalWithRetry(Consts.ThinClient.ReconnectRetrySpanMsecs, numRetry, Consts.ThinClient.ReconnectRetrySpanMaxMsecs);
                                if (waitTime == 0) waitTime = 1;

                                $"Additional tunnel establish failed. Wait for {waitTime} msecs..."._Debug();

                                await connectedEvent.WaitOneAsync(waitTime, abort);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (cancel.IsCancellationRequested) break;

                    await connectedEvent.WaitOneAsync(1000, abort);
                }
            });

            // Listen ソケットの開始
            //LocalNet.CreateListener(new TcpListenParam(

            // 接続が完了いたしました
            var ready = new ThinClientAcceptReadyNotification
            {
            };

            Dbg.Where();
            await session.RequestAndWaitResponseAsync(ready, Timeout.Infinite, Timeout.Infinite, cancel);

            abort.Cancel();
            Dbg.Where();
        }

        async Task<ThinClientConnection> DcConnectEx(WideTunnel wt, ThinClientConnectOptions connectOptions, CancellationToken cancel, bool checkPort, bool firstConnection,
            Func<ThinClientAuthRequest, CancellationToken, Task<ThinClientAuthResponse>> authCallback)
        {
            WtcSocket? sock = null;
            PipeStream? st = null;

            try
            {
                sock = await wt.WideClientConnectAsync(connectOptions.Pcid, connectOptions.ClientOptions, cancel);
                st = sock.GetStream(true);

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
                p = new Pack();

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

                        var authRes = await authCallback(authReq, cancel);
                    }
                    else if (authType == ThinAuthType.Password)
                    {
                        // パスワード認証
                        ThinClientAuthRequest authReq = new ThinClientAuthRequest
                        {
                            AuthType = ThinAuthType.Password,
                            UseAdvancedSecurity = false,
                        };

                        var authRes = await authCallback(authReq, cancel);

                        var passwordHash = Secure.HashSHA1(authRes.Password._GetBytes_UTF8());

                        p.AddData("SecurePassword", Secure.SoftEther_SecurePassword(passwordHash, rand));
                    }
                    else
                    {
                        // 不明な認証方法
                        throw new VpnException(VpnError.ERR_DESK_UNKNOWN_AUTH_TYPE);
                    }
                }
                else
                {
                    // 高度なユーザー認証
                    throw new NotImplementedException();
                }

                await st._SendPackAsync(p, cancel);

                // 結果を受信
                p = await st._RecvPackAsync(cancel);
                err = p.GetErrorFromPack();
                err.ThrowIfError(p);

                st.ReadTimeout = st.WriteTimeout = Timeout.Infinite;

                return new ThinClientConnection(sock, st, svcType, !isShareEnabled, caps, runInspect);
            }
            catch
            {
                await st._DisposeSafeAsync();
                await sock._DisposeSafeAsync();
                throw;
            }
        }
    }
}

#endif

