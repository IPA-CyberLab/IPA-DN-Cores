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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class RandomPortConfig
        {
            public static readonly Copenhagen<int> RandomPortMin = 4444;
            public static readonly Copenhagen<int> RandomPortMax = 4444;
            public static readonly Copenhagen<int> RandomPortNumTry = 1000;
        }
    }

    public abstract class NetStackOptionsBase { }

    public abstract class NetStackBase : AsyncService
    {
        public NetStackOptionsBase Options { get; }

        public NetStackBase(NetStackOptionsBase options, CancellationToken cancel = default) : base(cancel)
        {
            Options = options;
        }
    }

    public abstract class NetAppStubOptionsBase : NetStackOptionsBase { }

    public abstract class NetAppStubBase : NetStackBase
    {
        protected PipePoint Lower { get; }
        protected AttachHandle LowerAttach { get; private set; }

        public new NetAppStubOptionsBase Options => (NetAppStubOptionsBase)base.Options;

        public NetAppStubBase(PipePoint lower, NetAppStubOptionsBase options, CancellationToken cancel = default, bool noCheckDisconnected = false)
            : base(options, cancel)
        {
            try
            {
                LowerAttach = lower.Attach(AttachDirection.B_UpperSide, noCheckDisconnected: noCheckDisconnected);
                Lower = lower;

                AddIndirectDisposeLink(Lower);
                AddIndirectDisposeLink(LowerAttach);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }
    }

    public class NetAppStubOptions : NetAppStubOptionsBase { }

    public class NetAppStub : NetAppStubBase
    {
        readonly bool NoCheckDisconnected = false;

        public new NetAppStubOptions Options => (NetAppStubOptions)base.Options;

        public NetAppStub(PipePoint lower, CancellationToken cancel = default, NetAppStubOptions? options = null, bool noCheckDisconnected = false)
            : base(lower, options ?? new NetAppStubOptions(), cancel, noCheckDisconnected)
        {
            this.NoCheckDisconnected = noCheckDisconnected;
        }

        readonly CriticalSection LockObj = new CriticalSection<NetAppStub>();

        PipeStream? StreamCache = null;

        public PipeStream GetStream(bool autoFlash = true)
        {
            lock (LockObj)
            {
                if (StreamCache == null)
                {
                    StreamCache = AttachHandle.GetStream(autoFlash, noCheckDisconnected: this.NoCheckDisconnected);
                }

                return StreamCache;
            }
        }

        public PipePoint GetPipePoint()
        {
            Lower.CheckCanceledAndNoMoreData();

            return Lower;
        }

        public AttachHandle AttachHandle
        {
            get
            {
                if (NoCheckDisconnected == false)
                {
                    Lower.CheckCanceledAndNoMoreData();
                }

                return this.LowerAttach;
            }
        }

        protected override void CancelImpl(Exception? ex)
        {
            StreamCache._DisposeSafe();
            base.CancelImpl(ex);
        }

        protected override void DisposeImpl(Exception? ex)
        {
            StreamCache._DisposeSafe();
            base.DisposeImpl(ex);
        }
    }

    public abstract class NetProtocolOptionsBase : NetStackOptionsBase { }

    public abstract class NetProtocolBase : NetStackBase
    {
        protected PipePoint Upper { get; }

        protected internal PipePoint _InternalUpper { get => Upper; }

        protected AttachHandle UpperAttach { get; private set; }

        public new NetProtocolOptionsBase Options => (NetProtocolOptionsBase)base.Options;

        public NetProtocolBase(PipePoint? upper, NetProtocolOptionsBase options, CancellationToken cancel = default, bool noCheckDisconnected = false)
            : base(options, cancel)
        {
            try
            {
                if (upper == null)
                {
                    upper = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide, cancel);
                }

                UpperAttach = upper.Attach(AttachDirection.A_LowerSide, noCheckDisconnected: noCheckDisconnected);
                Upper = upper;

                AddIndirectDisposeLink(Upper);
                AddIndirectDisposeLink(UpperAttach);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }
    }

    public abstract class NetBottomProtocolOptionsBase : NetProtocolOptionsBase { }

    public abstract class NetBottomProtocolStubBase : NetProtocolBase
    {
        public new NetBottomProtocolOptionsBase Options => (NetBottomProtocolOptionsBase)base.Options;

        public NetBottomProtocolStubBase(PipePoint? upper, NetProtocolOptionsBase options, CancellationToken cancel = default, bool noCheckDisconnected = false) : base(upper, options, cancel, noCheckDisconnected)
        {
        }
    }

    public abstract class NetTcpProtocolOptionsBase : NetBottomProtocolOptionsBase
    {
        public NetDnsClientStub? DnsClient { get; set; }

        public string? RateLimiterConfigName { get; set; } = null;
    }

    public abstract class NetTcpProtocolStubBase : NetBottomProtocolStubBase
    {
        public const int DefaultTcpConnectTimeout = 15 * 1000;

        public new NetTcpProtocolOptionsBase Options => (NetTcpProtocolOptionsBase)base.Options;

        public NetTcpProtocolStubBase(PipePoint? upper, NetTcpProtocolOptionsBase options, CancellationToken cancel = default, bool noCheckDisconnected = false) : base(upper, options, cancel, noCheckDisconnected)
        {
        }

        protected abstract Task ConnectImplAsync(IPEndPoint remoteEndPoint, int connectTimeout = DefaultTcpConnectTimeout, CancellationToken cancel = default);
        protected abstract void ListenImpl(IPEndPoint localEndPoint);
        protected abstract Task<NetTcpProtocolStubBase> AcceptImplAsync(CancellationToken cancelForNewSocket = default);

        public bool IsConnected { get; private set; }
        public bool IsListening { get; private set; }
        public bool IsServerMode { get; private set; }

        AsyncLock ConnectLock = new AsyncLock();

        public async Task ConnectAsync(IPEndPoint remoteEndPoint, int connectTimeout = DefaultTcpConnectTimeout, CancellationToken cancel = default)
        {
            using (await ConnectLock.LockWithAwait())
            {
                if (IsConnected) throw new ApplicationException("Already connected.");
                if (IsListening) throw new ApplicationException("Already listening.");

                using (CreatePerTaskCancellationToken(out CancellationToken cancelOp, cancel))
                {
                    await ConnectImplAsync(remoteEndPoint, connectTimeout, cancelOp);
                }

                IsConnected = true;
                IsServerMode = false;
            }
        }

        public Task ConnectAsync(IPAddress ip, int port, CancellationToken cancel = default, int connectTimeout = NetTcpProtocolStubBase.DefaultTcpConnectTimeout)
            => ConnectAsync(new IPEndPoint(ip, port), connectTimeout, cancel);

        public async Task ConnectAsync(string host, int port, AddressFamily? addressFamily = null, int connectTimeout = NetTcpProtocolStubBase.DefaultTcpConnectTimeout)
            => await ConnectAsync(await Options.DnsClient!.GetIPFromHostName(host, addressFamily, GrandCancel, connectTimeout), port, default, connectTimeout);

        readonly CriticalSection ListenLock = new CriticalSection<NetTcpProtocolStubBase>();

        public void Listen(IPEndPoint localEndPoint)
        {
            lock (ListenLock)
            {
                if (IsConnected) throw new ApplicationException("Already connected.");
                if (IsListening) throw new ApplicationException("Already listening.");

                ListenImpl(localEndPoint);

                IsListening = true;
                IsServerMode = true;
            }
        }

        public async Task<ConnSock> AcceptAsync(CancellationToken cancelForNewSocket = default)
        {
            if (IsListening == false) throw new ApplicationException("Not listening.");

            return new ConnSock(await AcceptImplAsync(cancelForNewSocket));
        }
    }

    public class NetPalTcpProtocolOptions : NetTcpProtocolOptionsBase
    {
        public NetPalTcpProtocolOptions()
        {
            this.DnsClient = NetPalDnsClient.Shared;
        }
    }

    public class NetPalTcpProtocolStub : NetTcpProtocolStubBase
    {
        public class LayerInfo : LayerInfoBase, ILayerInfoTcpEndPoint
        {
            public TcpDirectionType Direction { get; set; }
            public int LocalPort { get; set; }
            public int RemotePort { get; set; }
            public IPAddress? LocalIPAddress { get; set; }
            public IPAddress? RemoteIPAddress { get; set; }
            public long NativeHandle { get; set; }
        }

        public new NetPalTcpProtocolOptions Options => (NetPalTcpProtocolOptions)base.Options;

        PalSocket? ConnectedSocket = null;
        PipePointSocketWrapper? SocketWrapper = null;

        PalSocket? ListeningSocket = null;

        IpConnectionRateLimiter? RateLimiter = null;

        public NetPalTcpProtocolStub(PipePoint? upper = null, NetPalTcpProtocolOptions? options = null, CancellationToken cancel = default, bool noCheckDisconnected = false)
            : base(upper, options ?? new NetPalTcpProtocolOptions(), cancel, noCheckDisconnected)
        {
        }

        void InitSocketWrapperFromSocket(PalSocket s)
        {
            this.ConnectedSocket = s;
            this.SocketWrapper = new PipePointSocketWrapper(Upper, s, this.GrandCancel);

            AddIndirectDisposeLink(this.SocketWrapper); // Do not add SocketWrapper with AddChild(). It causes deadlock due to the cyclic reference.

            var info = new LayerInfo();

            info.LocalPort = ((IPEndPoint)s.LocalEndPoint).Port;
            info.LocalIPAddress = ((IPEndPoint)s.LocalEndPoint).Address;
            info.RemotePort = ((IPEndPoint)s.RemoteEndPoint).Port;
            info.RemoteIPAddress = ((IPEndPoint)s.RemoteEndPoint).Address;
            info.Direction = s.Direction;
            info.NativeHandle = s.NativeHandle;

            UpperAttach.SetLayerInfo(info, this, false);
        }

        protected override async Task ConnectImplAsync(IPEndPoint remoteEndPoint, int connectTimeout = NetTcpProtocolStubBase.DefaultTcpConnectTimeout, CancellationToken cancel = default)
        {
            if (!(remoteEndPoint.AddressFamily == AddressFamily.InterNetwork || remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6))
                throw new ArgumentException("RemoteEndPoint.AddressFamily");

            PalSocket s = new PalSocket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp, TcpDirectionType.Client);

            this.CancelWatcher.EventList.RegisterCallback((a, b, c, d) => s._DisposeSafe());

            await TaskUtil.DoAsyncWithTimeout(async localCancel =>
            {
                await s.ConnectAsync(remoteEndPoint);
                return 0;
            },
            cancelProc: () => s._DisposeSafe(),
            timeout: connectTimeout,
            cancel: cancel);

            InitSocketWrapperFromSocket(s);
        }

        protected override void ListenImpl(IPEndPoint localEndPoint)
        {
            if (!(localEndPoint.AddressFamily == AddressFamily.InterNetwork || localEndPoint.AddressFamily == AddressFamily.InterNetworkV6))
                throw new ArgumentException("RemoteEndPoint.AddressFamily");

            PalSocket s = new PalSocket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp, TcpDirectionType.Server);
            try
            {
                s.Bind(localEndPoint);

                s.Listen(int.MaxValue);

                if (this.Options.RateLimiterConfigName._IsFilled())
                {
                    // RateLimiter の作成
                    this.RateLimiter = new IpConnectionRateLimiter(this.Options.RateLimiterConfigName);
                }
            }
            catch
            {
                s._DisposeSafe();
                throw;
            }

            this.ListeningSocket = s;
        }

        protected override async Task<NetTcpProtocolStubBase> AcceptImplAsync(CancellationToken cancelForNewSocket = default)
        {
            int numSocketError = 0;
            if (ListeningSocket == null) throw new CoresException("This protocol stack is not a listening socket.");

            LABEL_RETRY:

            // Accept でエラーが発生した場合は直ちにこの関数を抜ける (try で囲まない)
            PalSocket newSocket = await ListeningSocket.AcceptAsync();

            try
            {
                if (this.RateLimiter != null)
                {
                    // RateLimiter の適用
                    // Socket API の仕様により、一旦 Accept が完了した後でなければ Source Address を取得することができない
                    // ので、このようになっているのである。
                    IPAddress srcIp = ((IPEndPoint)newSocket.RemoteEndPoint).Address;

                    var rateLimiterRet = this.RateLimiter.TryEnter(srcIp);

                    if (rateLimiterRet == false)
                    {
                        // RateLimiter によって制限された
                        newSocket._DisposeSafe();

                        numSocketError = 0;

                        goto LABEL_RETRY;
                    }
                    else
                    {
                        // rateLimiterRet を、newSocket の Dispose() 時に Dispose するよう登録をする
                        newSocket.AddDisposeOnDispose(rateLimiterRet.Value);
                    }
                }

                var newStub = new NetPalTcpProtocolStub(null, null, cancelForNewSocket);

                try
                {
                    // ソケット情報の取得等
                    // 接続後直ちに切断されたクライアントの場合は、ここで例外が発生する場合がある
                    newStub.InitSocketWrapperFromSocket(newSocket);

                    // 成功
                    numSocketError = 0;
                    return newStub;
                }
                catch
                {
                    // エラー発生時は newStub を解放 (解放しないとメモリリークするため)
                    newStub._DisposeSafe();
                    throw;
                }
            }
            catch
            {
                newSocket._DisposeSafe();

                numSocketError++;

                // Accept が完了した後にソケット情報を取得しようとするときにエラーが発生したら
                // これは Accept されたソケットが直ちに切断されたということを意味するので
                // 特に何も考えずにそのまま再試行する
                goto LABEL_RETRY;
            }
        }

        protected override void CancelImpl(Exception? ex)
        {
            this.ConnectedSocket._DisposeSafe();
            this.ListeningSocket._DisposeSafe();

            base.CancelImpl(ex);
        }

        protected override void DisposeImpl(Exception? ex)
        {
            this.ConnectedSocket._DisposeSafe();
            this.ListeningSocket._DisposeSafe();

            base.DisposeImpl(ex);
        }
    }

    public abstract class NetSock : AsyncService
    {
        NetAppStub? AppStub = null;

        public NetProtocolBase Stack { get; }
        public DuplexPipe Pipe { get; }
        public PipePoint UpperPoint { get; }
        public LayerInfo Info { get => this.Pipe.LayerInfo; }
        public string Guid { get; } = Str.NewGuid();
        public DateTimeOffset Connected { get; } = DateTimeOffset.Now;
        public DateTimeOffset? Disconnected { get; private set; }
        public long Id { get; set; } = 0;

        public NetSock(NetProtocolBase protocolStack, CancellationToken cancel = default) : base(cancel)
        {
            try
            {
                if (protocolStack._InternalUpper.CounterPart == null)
                    throw new CoresException("Stack._InternalUpper.CounterPart == null");

                Stack = AddDirectDisposeLink(protocolStack);

                PipePoint counterPart = protocolStack._InternalUpper.CounterPart;
                UpperPoint = AddDirectDisposeLink(counterPart);
                Pipe = AddDirectDisposeLink(UpperPoint.Pipe);

                this.Pipe.OnDisconnected.Add(() =>
                {
                    this.Disconnected = DateTimeOffset.Now;
                    this._DisposeSafe();
                });
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public LogDefSocket GenerateLogDef(LogDefSocketAction action)
        {
            LogDefSocket ret = new LogDefSocket();

            try
            {
                this.Info.FillSocketLogDef(ret);
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            ret.Action = action;

            ret.SockGuid = this.Guid;
            ret.SockType = this.GetType().ToString();
            ret.ConnectedTime = this.Connected;
            ret.DisconnectedTime = this.Disconnected;

            ret.StreamRecv = this.UpperPoint?.StreamReader?.PinTail ?? 0;
            ret.StreamSend = this.UpperPoint?.StreamWriter?.PinTail ?? 0;

            ret.DatagramRecv = this.UpperPoint?.DatagramReader?.PinTail ?? 0;
            ret.DatagramSend = this.UpperPoint?.DatagramWriter?.PinTail ?? 0;

            return ret;
        }

        public NetAppStub GetNetAppProtocolStub()
        {
            NetAppStub ret = AddDirectDisposeLink(UpperPoint.GetNetAppProtocolStub());

            ret.AddIndirectDisposeLink(this);

            return ret;
        }

        public PipeStream GetStream(bool autoFlush = true)
        {
            if (AppStub == null)
                AppStub = this.GetNetAppProtocolStub();

            return AppStub.GetStream(autoFlush);
        }

        public void EnsureAttach(bool autoFlush = true) => GetStream(autoFlush); // Ensure attach

        public AttachHandle AttachHandle => this.AppStub?.AttachHandle ?? throw new ApplicationException("You need to call GetStream() first before accessing to AttachHandle.");
    }

    public class ConnSock : NetSock
    {
        public LogDefIPEndPoints EndPointInfo { get; }

        public ConnSock(NetProtocolBase protocolStack) : base(protocolStack)
        {
            this.EndPointInfo = new LogDefIPEndPoints()
            {
                LocalIP = this.Info?.Ip?.LocalIPAddress?.ToString() ?? "",
                LocalPort = this.Info?.Tcp?.LocalPort ?? 0,

                RemoteIP = this.Info?.Ip?.RemoteIPAddress?.ToString() ?? "",
                RemotePort = this.Info?.Tcp?.RemotePort ?? 0,

                Direction = this.Info?.Tcp?.Direction ?? TcpDirectionType.Client,
            };
        }

        public PCapConnSockRecorder StartPCapRecorder(PCapFileEmitter? initialEmitter = null, int bufferSize = DefaultSize, FastStreamNonStopWriteMode discardMode = FastStreamNonStopWriteMode.DiscardExistingData)
        {
            var ret = new PCapConnSockRecorder(this, initialEmitter, bufferSize, discardMode);

            this.AddOnCancelAction(() =>
            {
                try
                {
                    ret.EmitReset();
                }
                catch { }

                TaskUtil.StartSyncTaskAsync(() => ret._DisposeSafe())._LaissezFaire();
            });

            return ret;
        }
    }

    public class NetDnsClientOptions : NetStackOptionsBase { }

    public abstract class NetDnsClientStub : NetStackBase
    {
        public const int DefaultDnsResolveTimeout = 5 * 1000;

        public NetDnsClientStub(NetDnsClientOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
        }

        public abstract Task<IPAddress> GetIPFromHostName(string host, AddressFamily? addressFamily = null, CancellationToken cancel = default,
            int timeout = DefaultDnsResolveTimeout);
    }

    public class NetPalDnsClient : NetDnsClientStub
    {
        public static NetPalDnsClient Shared { get; private set; } = null!;

        public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

        static void ModuleInit()
        {
            Shared = new NetPalDnsClient(new NetDnsClientOptions());
        }

        static void ModuleFree()
        {
            Shared._DisposeSafe();
            Shared = null!;
        }


        public NetPalDnsClient(NetDnsClientOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
        }

        public override async Task<IPAddress> GetIPFromHostName(string host, AddressFamily? addressFamily = null, CancellationToken cancel = default,
            int timeout = NetDnsClientStub.DefaultDnsResolveTimeout)
        {
            if (IPAddress.TryParse(host, out IPAddress? ip))
            {
                if (addressFamily != null && ip.AddressFamily != addressFamily)
                    throw new ArgumentException("ip.AddressFamily != addressFamily");
            }
            else
            {
                ip = (await PalDns.GetHostAddressesAsync(host, timeout, cancel))
                        .Where(x => x.AddressFamily == AddressFamily.InterNetwork || x.AddressFamily == AddressFamily.InterNetworkV6)
                        .Where(x => addressFamily == null || x.AddressFamily == addressFamily).First();
            }

            return ip;
        }
    }

    public abstract class NetMiddleProtocolOptionsBase : NetProtocolOptionsBase
    {
        public int LowerReceiveTimeoutOnInit { get; set; } = 5 * 1000;
        public int LowerSendTimeoutOnInit { get; set; } = 60 * 1000;

        public int LowerReceiveTimeoutAfterInit { get; set; } = Timeout.Infinite;
        public int LowerSendTimeoutAfterInit { get; set; } = Timeout.Infinite;
    }

    public abstract class NetMiddleProtocolStackBase : NetProtocolBase
    {
        protected PipePoint Lower { get; }

        readonly CriticalSection LockObj = new CriticalSection<NetMiddleProtocolStackBase>();
        protected AttachHandle LowerAttach { get; private set; }

        public new NetMiddleProtocolOptionsBase Options => (NetMiddleProtocolOptionsBase)base.Options;

        public NetMiddleProtocolStackBase(PipePoint lower, PipePoint? upper, NetMiddleProtocolOptionsBase options, CancellationToken cancel = default, bool noCheckDisconnected = false)
            : base(upper, options, cancel, noCheckDisconnected)
        {
            try
            {
                LowerAttach = AddIndirectDisposeLink(lower.Attach(AttachDirection.B_UpperSide, noCheckDisconnected: noCheckDisconnected));
                Lower = AddIndirectDisposeLink(lower);

                Lower.ExceptionQueue.Encounter(Upper.ExceptionQueue);
                Lower.LayerInfo.Encounter(Upper.LayerInfo);

                Lower.AddOnDisconnected(() => Upper.Cancel(new DisconnectedException()));
                Upper.AddOnDisconnected(() => Lower.Cancel(new DisconnectedException()));
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }
    }

    public class NetSslProtocolOptions : NetMiddleProtocolOptionsBase { }

    public class NetSslProtocolStack : NetMiddleProtocolStackBase
    {
        public class LayerInfo : LayerInfoBase, ILayerInfoSsl
        {
            public bool IsServerMode { get; internal set; }
            public string? SslProtocol { get; internal set; }
            public string? CipherAlgorithm { get; internal set; }
            public int CipherStrength { get; internal set; }
            public string? HashAlgorithm { get; internal set; }
            public int HashStrength { get; internal set; }
            public string? KeyExchangeAlgorithm { get; internal set; }
            public int KeyExchangeStrength { get; internal set; }
            public PalX509Certificate? LocalCertificate { get; internal set; }
            public PalX509Certificate? RemoteCertificate { get; internal set; }
        }

        public NetSslProtocolStack(PipePoint lower, PipePoint? upper, NetSslProtocolOptions? options,
            CancellationToken cancel = default) : base(lower, upper, options ?? new NetSslProtocolOptions(), cancel) { }

        PipeStream? LowerStream = null;
        PalSslStream? SslStream = null;
        PipePointStreamWrapper? Wrapper = null;

        public async Task SslStartServerAsync(PalSslServerAuthenticationOptions sslServerAuthOption, CancellationToken cancel = default)
        {
            if (Wrapper != null)
                throw new ApplicationException("SSL is already established.");

            using (this.CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                PipeStream lowerStream = LowerAttach.GetStream(autoFlush: false);
                try
                {
                    lowerStream.ReadTimeout = sslServerAuthOption.NegotiationRecvTimeout;
                    PalSslStream ssl = new PalSslStream(lowerStream);
                    try
                    {
                        await ssl.AuthenticateAsServerAsync(sslServerAuthOption, opCancel);

                        lowerStream.ReadTimeout = Timeout.Infinite;

                        LowerAttach.SetLayerInfo(new LayerInfo()
                        {
                            IsServerMode = true,
                            SslProtocol = ssl.SslProtocol.ToString(),
                            CipherAlgorithm = ssl.CipherAlgorithm.ToString(),
                            CipherStrength = ssl.CipherStrength,
                            HashAlgorithm = ssl.HashAlgorithm.ToString(),
                            HashStrength = ssl.HashStrength,
                            KeyExchangeAlgorithm = ssl.KeyExchangeAlgorithm.ToString(),
                            KeyExchangeStrength = ssl.KeyExchangeStrength,
                            LocalCertificate = ssl.LocalCertificate,
                            RemoteCertificate = ssl.RemoteCertificate,
                        }, this, false);

                        this.SslStream = ssl;

                        lowerStream.ReadTimeout = Timeout.Infinite;
                        this.LowerStream = lowerStream;

                        this.Wrapper = new PipePointStreamWrapper(UpperAttach.PipePoint, ssl, CancelWatcher.CancelToken);

                        AddIndirectDisposeLink(this.Wrapper); // Do not add Wrapper with AddChild(). It makes cyclic reference.
                    }
                    catch
                    {
                        ssl._DisposeSafe();
                        throw;
                    }
                }
                catch
                {
                    lowerStream._DisposeSafe();
                    throw;
                }
            }
        }

        public async Task SslStartClientAsync(PalSslClientAuthenticationOptions sslClientAuthOption, CancellationToken cancel = default)
        {
            if (Wrapper != null)
                throw new ApplicationException("SSL is already established.");

            using (this.CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                PipeStream lowerStream = LowerAttach.GetStream(autoFlush: false);
                try
                {
                    lowerStream.ReadTimeout = sslClientAuthOption.NegotiationRecvTimeout;
                    PalSslStream ssl = new PalSslStream(lowerStream);
                    try
                    {
                        await ssl.AuthenticateAsClientAsync(sslClientAuthOption, opCancel);

                        lowerStream.ReadTimeout = Timeout.Infinite;

                        LowerAttach.SetLayerInfo(new LayerInfo()
                        {
                            IsServerMode = false,
                            SslProtocol = ssl.SslProtocol.ToString(),
                            CipherAlgorithm = ssl.CipherAlgorithm.ToString(),
                            CipherStrength = ssl.CipherStrength,
                            HashAlgorithm = ssl.HashAlgorithm.ToString(),
                            HashStrength = ssl.HashStrength,
                            KeyExchangeAlgorithm = ssl.KeyExchangeAlgorithm.ToString(),
                            KeyExchangeStrength = ssl.KeyExchangeStrength,
                            LocalCertificate = ssl.LocalCertificate,
                            RemoteCertificate = ssl.RemoteCertificate,
                        }, this, false);

                        this.SslStream = ssl;
                        this.LowerStream = lowerStream;

                        this.Wrapper = new PipePointStreamWrapper(UpperAttach.PipePoint, ssl, CancelWatcher.CancelToken);

                        AddIndirectDisposeLink(this.Wrapper); // Do not add Wrapper with AddChild(). It makes cyclic reference.
                    }
                    catch
                    {
                        ssl._DisposeSafe();
                        throw;
                    }
                }
                catch
                {
                    lowerStream._DisposeSafe();
                    throw;
                }
            }
        }

        protected override void CancelImpl(Exception? ex)
        {
            this.SslStream._DisposeSafe();
            this.LowerStream._DisposeSafe();

            base.CancelImpl(ex);
        }

        protected override void DisposeImpl(Exception? ex)
        {
            this.SslStream._DisposeSafe();
            this.LowerStream._DisposeSafe();

            base.DisposeImpl(ex);
        }
    }

    public enum IPVersion
    {
        IPv4 = 0,
        IPv6 = 1,
    }

    public enum ListenStatus
    {
        Trying,
        Listening,
        Stopped,
    }

    public class RandomPortAssigner
    {
        readonly CriticalSection<RandomPortAssigner> LockObj = new CriticalSection<RandomPortAssigner>();

        readonly List<int> Stocks = new List<int>();
        readonly List<int> Uses = new List<int>();

        public RandomPortAssigner(int min, int max)
        {
            min = Math.Max(min, 1);
            max = Math.Max(min, max);
            int count = max - min + 1;
            if (count >= 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(max));
            }

            for (int i = min; i <= max; i++)
            {
                this.Stocks.Add(i);
            }
        }

        public int Assign()
        {
            lock (this.LockObj)
            {
                int count = this.Stocks.Count;

                if (count == 0)
                {
                    // もうない
                    return 0;
                }

                int index = Util.RandSInt31() % count;

                int port = this.Stocks[index];

                this.Stocks.RemoveAt(index);

                this.Uses.Add(port);

                return port;
            }
        }

        public void Release(int port)
        {
            lock (this.LockObj)
            {
                int index = this.Uses.IndexOf(port);
                if (index != -1)
                {
                    this.Uses.RemoveAt(index);
                    this.Stocks.Add(port);
                }
            }
        }

        public static RandomPortAssigner GlobalRandomPortAssigner { get; } = new RandomPortAssigner(CoresConfig.RandomPortConfig.RandomPortMin, CoresConfig.RandomPortConfig.RandomPortMax);
    }

    public delegate Task NetTcpListenerAcceptedProcCallback(NetTcpListenerPort listener, ConnSock newSock);

    public class NetTcpListenerPort
    {
        public IPVersion IPVersion { get; }
        public IPAddress IPAddress { get; }
        public int Port { get; }
        public bool IsRandomPortMode { get; }

        public ListenStatus Status { get; internal set; }
        public Exception? LastError { get; internal set; }

        internal Task _InternalTask { get; }

        internal CancellationTokenSource _InternalSelfCancelSource { get; }
        internal CancellationToken _InternalSelfCancelToken { get => _InternalSelfCancelSource.Token; }

        public NetTcpListener TcpListener { get; }

        public const long RetryIntervalStandard = 1 * 512;
        public const long RetryIntervalMax = 60 * 1000;

        internal NetTcpListenerPort(NetTcpListener listener, IPVersion ver, IPAddress addr, int port, bool isRandomPortMode = false)
        {
            TcpListener = listener;
            IPVersion = ver;
            IPAddress = addr;
            Port = port;
            LastError = null;
            Status = ListenStatus.Trying;
            IsRandomPortMode = isRandomPortMode;
            if (isRandomPortMode)
            {
                Port = 0;
            }

            _InternalSelfCancelSource = new CancellationTokenSource();

            NetTcpProtocolStubBase? initialListenTcp = null;

            if (IsRandomPortMode)
            {
                int portAssignedRandom = 0;

                NetTcpProtocolStubBase listenTcp = TcpListener.CreateNewTcpStubForListenImpl(_InternalSelfCancelToken);

                // ランダムポートモードの場合は 1 つポートを決める
                for (int i = 0; i < CoresConfig.RandomPortConfig.RandomPortNumTry; i++)
                {
                    int portTmp = RandomPortAssigner.GlobalRandomPortAssigner.Assign();

                    try
                    {
                        listenTcp.Listen(new IPEndPoint(IPAddress, portTmp));

                        // OK!
                        portAssignedRandom = portTmp;

                        initialListenTcp = listenTcp;

                        break;
                    }
                    catch
                    {
                        // Listen 失敗
                        RandomPortAssigner.GlobalRandomPortAssigner.Release(portTmp);
                    }
                }

                if (portAssignedRandom == 0)
                {
                    // 十分な回数試行したが、ランダムポートの割当てに失敗した
                    listenTcp._DisposeSafe();
                    throw new CoresLibException("Failed to assign a new random port.");
                }

                // ランダムポートの割り当て成功
                this.Port = portAssignedRandom;
            }

            _InternalTask = ListenLoopAsync(initialListenTcp);
        }

        static internal string MakeHashKey(IPVersion ipVer, IPAddress ipAddress, int port)
        {
            return $"{port} / {ipAddress} / {ipAddress.AddressFamily} / {ipVer}";
        }

        async Task ListenLoopAsync(NetTcpProtocolStubBase? initialListener = null)
        {
            Status = ListenStatus.Trying;

            bool reportError = true;

            int numRetry = 0;

            try
            {
                while (_InternalSelfCancelToken.IsCancellationRequested == false)
                {
                    Status = ListenStatus.Trying;
                    _InternalSelfCancelToken.ThrowIfCancellationRequested();

                    int sleepDelay = (int)Math.Min(RetryIntervalStandard * numRetry, RetryIntervalMax);
                    if (sleepDelay >= 1)
                        sleepDelay = Util.RandSInt31() % sleepDelay;
                    await TaskUtil.WaitObjectsAsync(timeout: sleepDelay,
                        cancels: new CancellationToken[] { _InternalSelfCancelToken });
                    numRetry++;

                    _InternalSelfCancelToken.ThrowIfCancellationRequested();

                    NetTcpProtocolStubBase listenTcp;

                    if (initialListener != null)
                    {
                        // ランダムポート割当てで成功した Listener をそのまま利用開始する
                        listenTcp = initialListener;
                        initialListener = null;
                    }
                    else
                    {
                        listenTcp = TcpListener.CreateNewTcpStubForListenImpl(_InternalSelfCancelToken);
                    }

                    try
                    {
                        if (listenTcp.IsListening == false)
                        {
                            listenTcp.Listen(new IPEndPoint(IPAddress, Port));
                        }

                        numRetry = 0;

                        reportError = true;
                        Status = ListenStatus.Listening;

                        Con.WriteDebug($"Listener starts on [{IPAddress.ToString()}]:{Port}.");

                        while (true)
                        {
                            _InternalSelfCancelToken.ThrowIfCancellationRequested();

                            ConnSock sock = await listenTcp.AcceptAsync();

                            TcpListener.InternalSocketAccepted(this, sock);
                        }
                    }
                    catch (Exception ex)
                    {
                        LastError = ex;

                        if (_InternalSelfCancelToken.IsCancellationRequested == false)
                        {
                            if (reportError)
                            {
                                reportError = false;
                                Con.WriteDebug($"Listener error on [{IPAddress.ToString()}]:{Port}. Error: " + ex.Message);
                            }
                        }
                    }
                    finally
                    {
                        listenTcp._DisposeSafe();
                    }
                }
            }
            finally
            {
                Status = ListenStatus.Stopped;

                if (this.IsRandomPortMode)
                {
                    RandomPortAssigner.GlobalRandomPortAssigner.Release(this.Port);
                }
            }
        }

        internal async Task _InternalStopAsync()
        {
            await _InternalSelfCancelSource._TryCancelAsync();
            try
            {
                await _InternalTask;
            }
            catch { }
        }
    }

    public delegate Task<ConnSock> NetTcpListenerAcceptNextAsync(CancellationToken cancel = default);

    public abstract class NetTcpListener : AsyncService
    {
        readonly CriticalSection LockObj = new CriticalSection<NetTcpListener>();

        readonly Dictionary<string, NetTcpListenerPort> List = new Dictionary<string, NetTcpListenerPort>();

        readonly Dictionary<Task, ConnSock> RunningAcceptedTasks = new Dictionary<Task, ConnSock>();

        NetTcpListenerAcceptedProcCallback AcceptedProc { get; }

        public NetTcpListenerAcceptNextAsync AcceptNextSocketFromQueueUtilAsync { get; set; } = (c) => throw new NotImplementedException();

        public bool HideAcceptProcError { get; set; } = false;

        public int CurrentConnections
        {
            get
            {
                lock (RunningAcceptedTasks)
                    return RunningAcceptedTasks.Count;
            }
        }

        public NetTcpListener(NetTcpListenerAcceptedProcCallback acceptedProc)
        {
            AcceptedProc = acceptedProc;
        }

        internal protected abstract NetTcpProtocolStubBase CreateNewTcpStubForListenImpl(CancellationToken cancel);

        public NetTcpListenerPort? AssignedRandomPort { get; private set; } = null;

        public NetTcpListenerPort AddRandom(IPVersion? ipVer = null, IPAddress? addr = null)
        {
            if (addr == null)
                addr = ((ipVer ?? IPVersion.IPv4) == IPVersion.IPv4) ? IPAddress.Any : IPAddress.IPv6Any;
            if (ipVer == null)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                    ipVer = IPVersion.IPv4;
                else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                    ipVer = IPVersion.IPv6;
                else
                    throw new ArgumentException("Unsupported AddressFamily.");
            }

            lock (LockObj)
            {
                CheckNotCanceled();

                var s = new NetTcpListenerPort(this, (IPVersion)ipVer, addr, 0, isRandomPortMode: true);
                List.Add(NetTcpListenerPort.MakeHashKey((IPVersion)ipVer, addr, s.Port), s);
                if (AssignedRandomPort == null)
                {
                    this.AssignedRandomPort = s;
                }
                return s;
            }
        }

        public NetTcpListenerPort Add(int port, IPVersion? ipVer = null, IPAddress? addr = null)
        {
            if (addr == null)
                addr = ((ipVer ?? IPVersion.IPv4) == IPVersion.IPv4) ? IPAddress.Any : IPAddress.IPv6Any;
            if (ipVer == null)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                    ipVer = IPVersion.IPv4;
                else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                    ipVer = IPVersion.IPv6;
                else
                    throw new ArgumentException("Unsupported AddressFamily.");
            }
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("Port number is out of range.");

            lock (LockObj)
            {
                CheckNotCanceled();

                var s = Search(NetTcpListenerPort.MakeHashKey((IPVersion)ipVer, addr, port));
                if (s != null)
                    return s;
                s = new NetTcpListenerPort(this, (IPVersion)ipVer, addr, port);
                List.Add(NetTcpListenerPort.MakeHashKey((IPVersion)ipVer, addr, port), s);
                return s;
            }
        }

        public async Task<bool> DeleteAsync(NetTcpListenerPort listener)
        {
            NetTcpListenerPort? s;
            lock (LockObj)
            {
                string hashKey = NetTcpListenerPort.MakeHashKey(listener.IPVersion, listener.IPAddress, listener.Port);
                s = Search(hashKey);
                if (s == null)
                    return false;
                List.Remove(hashKey);
            }
            await s._InternalStopAsync();
            return true;
        }

        NetTcpListenerPort? Search(string hashKey)
        {
            if (List.TryGetValue(hashKey, out NetTcpListenerPort? ret) == false)
                return null;
            return ret;
        }

        internal async Task InternalSocketAcceptedAsync(NetTcpListenerPort listener, ConnSock sock)
        {
            try
            {
                await AcceptedProc(listener, sock);
            }
            catch (DisconnectedException) { }
            catch (SocketException ex) when (ex._IsSocketErrorDisconnected()) { }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            catch (Exception ex)
            {
                if (HideAcceptProcError == false)
                {
                    Dbg.WriteLine("AcceptProc error: " + ex.ToString());
                }
            }
            finally
            {
                sock._CancelSafe(new DisconnectedException());
                await sock._CleanupSafeAsync();
                sock._DisposeSafe();
            }
        }

        internal void InternalSocketAccepted(NetTcpListenerPort listener, ConnSock sock)
        {
            try
            {
                Task t = InternalSocketAcceptedAsync(listener, sock);

                if (t.IsCompleted == false)
                {
                    lock (LockObj)
                        RunningAcceptedTasks.Add(t, sock);

                    t.ContinueWith(x =>
                    {
                        lock (LockObj)
                            RunningAcceptedTasks.Remove(t);
                    });
                }
            }
            catch (SocketException ex) when (ex._IsSocketErrorDisconnected()) { }
            catch (Exception ex)
            {
                Dbg.WriteLine("AcceptedProc error: " + ex.ToString());
            }
        }

        public NetTcpListenerPort[] Listeners
        {
            get
            {
                lock (LockObj)
                    return List.Values.ToArray();
            }
        }

        protected override void CancelImpl(Exception? ex)
        {
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            List<NetTcpListenerPort> o = new List<NetTcpListenerPort>();
            lock (LockObj)
            {
                List.Values.ToList().ForEach(x => o.Add(x));
                List.Clear();
            }

            foreach (NetTcpListenerPort s in o)
                await s._InternalStopAsync()._TryWaitAsync();

            List<Task> waitTasks = new List<Task>();
            List<ConnSock> allConnectedSocks = new List<ConnSock>();

            lock (LockObj)
            {
                foreach (var v in RunningAcceptedTasks)
                {
                    allConnectedSocks.Add(v.Value);
                    waitTasks.Add(v.Key);
                }
                RunningAcceptedTasks.Clear();
            }

            foreach (var sock in allConnectedSocks)
            {
                try
                {
                    await sock._CleanupSafeAsync();
                }
                catch { }
            }

            foreach (var task in waitTasks)
                await task._TryWaitAsync();

            Debug.Assert(CurrentConnections == 0);
        }

        protected override void DisposeImpl(Exception? ex) { }
    }

    public class NetPalTcpListener : NetTcpListener
    {
        public string? RateLimiterConfigName { get; }

        public NetPalTcpListener(NetTcpListenerAcceptedProcCallback acceptedProc, string? rateLimiterConfigName = null) : base(acceptedProc)
        {
            this.RateLimiterConfigName = rateLimiterConfigName;
        }

        protected internal override NetTcpProtocolStubBase CreateNewTcpStubForListenImpl(CancellationToken cancel)
        {
            NetPalTcpProtocolOptions options = new NetPalTcpProtocolOptions()
            {
                RateLimiterConfigName = this.RateLimiterConfigName,
            };

            return new NetPalTcpProtocolStub(null, options, cancel);
        }
    }
}
