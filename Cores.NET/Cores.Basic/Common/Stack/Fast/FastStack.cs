﻿// IPA Cores.NET
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
    abstract class NetStackOptionsBase { }

    abstract class NetStackBase : AsyncService
    {
        public NetStackOptionsBase Options { get; }

        public NetStackBase(NetStackOptionsBase options, CancellationToken cancel = default) : base(cancel)
        {
            Options = options;
        }
    }

    abstract class NetAppStubOptionsBase : NetStackOptionsBase { }

    abstract class NetAppStubBase : NetStackBase
    {
        protected PipeEnd Lower { get; }
        protected AttachHandle LowerAttach { get; private set; }

        public new NetAppStubOptionsBase Options => (NetAppStubOptionsBase)base.Options;

        public NetAppStubBase(PipeEnd lower, NetAppStubOptionsBase options, CancellationToken cancel = default)
            : base(options, cancel)
        {
            try
            {
                LowerAttach = lower.Attach(AttachDirection.B_UpperSide);
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

    class NetAppStubOptions : NetAppStubOptionsBase { }

    class NetAppStub : NetAppStubBase
    {
        public new NetAppStubOptions Options => (NetAppStubOptions)base.Options;

        public NetAppStub(PipeEnd lower, CancellationToken cancel = default, NetAppStubOptions options = null)
            : base(lower, options ?? new NetAppStubOptions(), cancel)
        {
        }

        CriticalSection LockObj = new CriticalSection();

        PipeStream StreamCache = null;

        public PipeStream GetStream(bool autoFlash = true)
        {
            lock (LockObj)
            {
                if (StreamCache == null)
                {
                    StreamCache = AttachHandle.GetStream(autoFlash);
                }

                return StreamCache;
            }
        }

        public PipeEnd GetPipeEnd()
        {
            Lower.CheckCanceled();

            return Lower;
        }

        public AttachHandle AttachHandle
        {
            get
            {
                Lower.CheckCanceled();
                return this.LowerAttach;
            }
        }

        protected override void CancelImpl(Exception ex)
        {
            StreamCache._DisposeSafe();
            base.CancelImpl(ex);
        }

        protected override void DisposeImpl(Exception ex)
        {
            StreamCache._DisposeSafe();
            base.DisposeImpl(ex);
        }
    }

    abstract class NetProtocolOptionsBase : NetStackOptionsBase { }

    abstract class NetProtocolBase : NetStackBase
    {
        protected PipeEnd Upper { get; }

        protected internal PipeEnd _InternalUpper { get => Upper; }

        protected AttachHandle UpperAttach { get; private set; }

        public new NetProtocolOptionsBase Options => (NetProtocolOptionsBase)base.Options;

        public NetProtocolBase(PipeEnd upper, NetProtocolOptionsBase options, CancellationToken cancel = default)
            : base(options, cancel)
        {
            try
            {
                if (upper == null)
                {
                    upper = PipeEnd.NewDuplexPipeAndGetOneSide(PipeEndSide.A_LowerSide, cancel);
                }

                UpperAttach = upper.Attach(AttachDirection.A_LowerSide);
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

    abstract class NetBottomProtocolOptionsBase : NetProtocolOptionsBase { }

    abstract class NetBottomProtocolStubBase : NetProtocolBase
    {
        public new NetBottomProtocolOptionsBase Options => (NetBottomProtocolOptionsBase)base.Options;

        public NetBottomProtocolStubBase(PipeEnd upper, NetProtocolOptionsBase options, CancellationToken cancel = default) : base(upper, options, cancel)
        {
        }
    }

    abstract class NetTcpProtocolOptionsBase : NetBottomProtocolOptionsBase
    {
        public NetDnsClientStub DnsClient { get; set; }
    }

    abstract class NetTcpProtocolStubBase : NetBottomProtocolStubBase
    {
        public const int DefaultTcpConnectTimeout = 15 * 1000;

        public new NetTcpProtocolOptionsBase Options => (NetTcpProtocolOptionsBase)base.Options;

        public NetTcpProtocolStubBase(PipeEnd upper, NetTcpProtocolOptionsBase options, CancellationToken cancel = default) : base(upper, options, cancel)
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
            => await ConnectAsync(await Options.DnsClient.GetIPFromHostName(host, addressFamily, GrandCancel, connectTimeout), port, default, connectTimeout);

        CriticalSection ListenLock = new CriticalSection();

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

    class NetPalTcpProtocolOptions : NetTcpProtocolOptionsBase
    {
        public NetPalTcpProtocolOptions()
        {
            this.DnsClient = NetPalDnsClient.Shared;
        }
    }

    class NetPalTcpProtocolStub : NetTcpProtocolStubBase
    {
        public class LayerInfo : LayerInfoBase, ILayerInfoTcpEndPoint
        {
            public TcpDirectionType Direction { get; set; }
            public int LocalPort { get; set; }
            public int RemotePort { get; set; }
            public IPAddress LocalIPAddress { get; set; }
            public IPAddress RemoteIPAddress { get; set; }
            public long NativeHandle { get; set; }
        }

        public new NetPalTcpProtocolOptions Options => (NetPalTcpProtocolOptions)base.Options;

        PalSocket ConnectedSocket = null;
        PipeEndSocketWrapper SocketWrapper = null;

        PalSocket ListeningSocket = null;

        public NetPalTcpProtocolStub(PipeEnd upper = null, NetPalTcpProtocolOptions options = null, CancellationToken cancel = default)
            : base(upper, options ?? new NetPalTcpProtocolOptions(), cancel)
        {
        }

        void InitSocketWrapperFromSocket(PalSocket s)
        {
            this.ConnectedSocket = s;
            this.SocketWrapper = new PipeEndSocketWrapper(Upper, s, this.GrandCancel);
            AddIndirectDisposeLink(this.SocketWrapper); // Do not add SocketWrapper with AddChild(). It causes deadlock due to the cyclic reference.

            UpperAttach.SetLayerInfo(new LayerInfo()
            {
                LocalPort = ((IPEndPoint)s.LocalEndPoint).Port,
                LocalIPAddress = ((IPEndPoint)s.LocalEndPoint).Address,
                RemotePort = ((IPEndPoint)s.RemoteEndPoint).Port,
                RemoteIPAddress = ((IPEndPoint)s.RemoteEndPoint).Address,
                Direction = s.Direction,
                NativeHandle = s.NativeHandle,
            }, this, false);
        }

        protected override async Task ConnectImplAsync(IPEndPoint remoteEndPoint, int connectTimeout = NetTcpProtocolStubBase.DefaultTcpConnectTimeout, CancellationToken cancel = default)
        {
            if (!(remoteEndPoint.AddressFamily == AddressFamily.InterNetwork || remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6))
                throw new ArgumentException("RemoteEndPoint.AddressFamily");

            PalSocket s = new PalSocket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp, TcpDirectionType.Client);

            this.CancelWatcher.EventList.RegisterCallback((a, b, c) => s._DisposeSafe());

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
            PalSocket newSocket = await ListeningSocket.AcceptAsync();
            try
            {
                var newStub = new NetPalTcpProtocolStub(null, null, cancelForNewSocket);

                newStub.InitSocketWrapperFromSocket(newSocket);

                return newStub;
            }
            catch
            {
                newSocket._DisposeSafe();
                throw;
            }
        }

        protected override void CancelImpl(Exception ex)
        {
            this.ConnectedSocket._DisposeSafe();
            this.ListeningSocket._DisposeSafe();

            base.CancelImpl(ex);
        }

        protected override void DisposeImpl(Exception ex)
        {
            this.ConnectedSocket._DisposeSafe();
            this.ListeningSocket._DisposeSafe();

            base.DisposeImpl(ex);
        }
    }

    class NetSock : AsyncService
    {
        NetAppStub AppStub = null;

        public NetProtocolBase Stack { get; }
        public DuplexPipe Pipe { get; }
        public PipeEnd UpperEnd { get; }
        public LayerInfo Info { get => this.Pipe.LayerInfo; }
        public string Guid { get; } = Str.NewGuid();
        public DateTimeOffset Connected { get; } = DateTimeOffset.Now;
        public DateTimeOffset? Disconnected { get; private set; }


        public NetSock(NetProtocolBase protocolStack, CancellationToken cancel = default) : base(cancel)
        {
            try
            {
                Stack = AddDirectDisposeLink(protocolStack);
                UpperEnd = AddDirectDisposeLink(Stack._InternalUpper.CounterPart);
                Pipe = AddDirectDisposeLink(UpperEnd.Pipe);

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

        public LogDefSocket GenerateLogDef()
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

            ret.SockGuid = this.Guid;
            ret.SockType = this.GetType().ToString();
            ret.ConnectedTime = this.Connected;
            ret.DisconnectedTime = this.Disconnected;

            ret.StreamRecv = this.UpperEnd?.StreamReader?.PinTail ?? 0;
            ret.StreamSend = this.UpperEnd?.StreamWriter?.PinTail ?? 0;

            ret.DatagramRecv = this.UpperEnd?.DatagramReader?.PinTail ?? 0;
            ret.DatagramSend = this.UpperEnd?.DatagramWriter?.PinTail ?? 0;

            return ret;
        }

        public NetAppStub GetNetAppProtocolStub()
        {
            NetAppStub ret = AddDirectDisposeLink(UpperEnd.GetNetAppProtocolStub());

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

    class ConnSock : NetSock
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
            };
        }
    }

    class NetDnsClientOptions : NetStackOptionsBase { }

    abstract class NetDnsClientStub : NetStackBase
    {
        public const int DefaultDnsResolveTimeout = 5 * 1000;

        public NetDnsClientStub(NetDnsClientOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
        }

        public abstract Task<IPAddress> GetIPFromHostName(string host, AddressFamily? addressFamily = null, CancellationToken cancel = default,
            int timeout = DefaultDnsResolveTimeout);
    }

    class NetPalDnsClient : NetDnsClientStub
    {
        public static NetPalDnsClient Shared { get; private set; }

        public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

        static void ModuleInit()
        {
            Shared = new NetPalDnsClient(new NetDnsClientOptions());
        }

        static void ModuleFree()
        {
            Shared._DisposeSafe();
            Shared = null;
        }


        public NetPalDnsClient(NetDnsClientOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
        }

        public override async Task<IPAddress> GetIPFromHostName(string host, AddressFamily? addressFamily = null, CancellationToken cancel = default,
            int timeout = NetDnsClientStub.DefaultDnsResolveTimeout)
        {
            if (IPAddress.TryParse(host, out IPAddress ip))
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

    abstract class NetMiddleProtocolOptionsBase : NetProtocolOptionsBase
    {
        public int LowerReceiveTimeoutOnInit { get; set; } = 5 * 1000;
        public int LowerSendTimeoutOnInit { get; set; } = 60 * 1000;

        public int LowerReceiveTimeoutAfterInit { get; set; } = Timeout.Infinite;
        public int LowerSendTimeoutAfterInit { get; set; } = Timeout.Infinite;
    }

    abstract class NetMiddleProtocolStackBase : NetProtocolBase
    {
        protected PipeEnd Lower { get; }

        CriticalSection LockObj = new CriticalSection();
        protected AttachHandle LowerAttach { get; private set; }

        public new NetMiddleProtocolOptionsBase Options => (NetMiddleProtocolOptionsBase)base.Options;

        public NetMiddleProtocolStackBase(PipeEnd lower, PipeEnd upper, NetMiddleProtocolOptionsBase options, CancellationToken cancel = default)
            : base(upper, options, cancel)
        {
            try
            {
                LowerAttach = AddIndirectDisposeLink(lower.Attach(AttachDirection.B_UpperSide));
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

    class NetSslProtocolOptions : NetMiddleProtocolOptionsBase { }

    class NetSslProtocolStack : NetMiddleProtocolStackBase
    {
        public class LayerInfo : LayerInfoBase, ILayerInfoSsl
        {
            public bool IsServerMode { get; internal set; }
            public string SslProtocol { get; internal set; }
            public string CipherAlgorithm { get; internal set; }
            public int CipherStrength { get; internal set; }
            public string HashAlgorithm { get; internal set; }
            public int HashStrength { get; internal set; }
            public string KeyExchangeAlgorithm { get; internal set; }
            public int KeyExchangeStrength { get; internal set; }
            public PalX509Certificate LocalCertificate { get; internal set; }
            public PalX509Certificate RemoteCertificate { get; internal set; }
        }

        public NetSslProtocolStack(PipeEnd lower, PipeEnd upper, NetSslProtocolOptions options,
            CancellationToken cancel = default) : base(lower, upper, options ?? new NetSslProtocolOptions(), cancel) { }

        PipeStream LowerStream = null;
        PalSslStream SslStream = null;
        PipeEndStreamWrapper Wrapper = null;

        public async Task SslStartServerAsync(PalSslServerAuthenticationOptions sslServerAuthOption, CancellationToken cancel = default)
        {
            if (Wrapper != null)
                throw new ApplicationException("SSL is already established.");

            using (this.CreatePerTaskCancellationToken(out CancellationToken opCancel, cancel))
            {
                PipeStream lowerStream = LowerAttach.GetStream(autoFlush: false);
                try
                {
                    PalSslStream ssl = new PalSslStream(lowerStream);
                    try
                    {
                        await ssl.AuthenticateAsServerAsync(sslServerAuthOption, opCancel);

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
                        this.LowerStream = lowerStream;

                        this.Wrapper = new PipeEndStreamWrapper(UpperAttach.PipeEnd, ssl, CancelWatcher.CancelToken);

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
                    PalSslStream ssl = new PalSslStream(lowerStream);
                    try
                    {
                        await ssl.AuthenticateAsClientAsync(sslClientAuthOption, opCancel);

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

                        this.Wrapper = new PipeEndStreamWrapper(UpperAttach.PipeEnd, ssl, CancelWatcher.CancelToken);

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

        protected override void CancelImpl(Exception ex)
        {
            this.SslStream._DisposeSafe();
            this.LowerStream._DisposeSafe();

            base.CancelImpl(ex);
        }

        protected override void DisposeImpl(Exception ex)
        {
            this.SslStream._DisposeSafe();
            this.LowerStream._DisposeSafe();

            base.DisposeImpl(ex);
        }
    }

    enum IPVersion
    {
        IPv4 = 0,
        IPv6 = 1,
    }

    enum ListenStatus
    {
        Trying,
        Listening,
        Stopped,
    }

    delegate Task NetTcpListenerAcceptedProcCallback(NetTcpListenerPort listener, ConnSock newSock);

    class NetTcpListenerPort
    {
        public IPVersion IPVersion { get; }
        public IPAddress IPAddress { get; }
        public int Port { get; }

        public ListenStatus Status { get; internal set; }
        public Exception LastError { get; internal set; }

        internal Task _InternalTask { get; }

        internal CancellationTokenSource _InternalSelfCancelSource { get; }
        internal CancellationToken _InternalSelfCancelToken { get => _InternalSelfCancelSource.Token; }

        public NetTcpListener TcpListener { get; }

        public const long RetryIntervalStandard = 1 * 512;
        public const long RetryIntervalMax = 60 * 1000;

        internal NetTcpListenerPort(NetTcpListener listener, IPVersion ver, IPAddress addr, int port)
        {
            TcpListener = listener;
            IPVersion = ver;
            IPAddress = addr;
            Port = port;
            LastError = null;
            Status = ListenStatus.Trying;
            _InternalSelfCancelSource = new CancellationTokenSource();

            _InternalTask = ListenLoop();
        }

        static internal string MakeHashKey(IPVersion ipVer, IPAddress ipAddress, int port)
        {
            return $"{port} / {ipAddress} / {ipAddress.AddressFamily} / {ipVer}";
        }

        async Task ListenLoop()
        {
            AsyncAutoResetEvent networkChangedEvent = new AsyncAutoResetEvent();
            int eventRegisterId = BackgroundState<PalHostNetInfo>.EventListener.RegisterAsyncEvent(networkChangedEvent);

            Status = ListenStatus.Trying;

            bool reportError = true;

            int numRetry = 0;
            int lastNetworkInfoVer = BackgroundState<PalHostNetInfo>.Current.Version;

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
                        cancels: new CancellationToken[] { _InternalSelfCancelToken },
                        events: new AsyncAutoResetEvent[] { networkChangedEvent });
                    numRetry++;

                    int networkInfoVer = BackgroundState<PalHostNetInfo>.Current.Version;
                    if (lastNetworkInfoVer != networkInfoVer)
                    {
                        lastNetworkInfoVer = networkInfoVer;
                        numRetry = 0;
                    }

                    _InternalSelfCancelToken.ThrowIfCancellationRequested();

                    NetTcpProtocolStubBase listenTcp = TcpListener.CreateNewTcpStubForListenImpl(_InternalSelfCancelToken);

                    try
                    {
                        listenTcp.Listen(new IPEndPoint(IPAddress, Port));

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
                BackgroundState<PalHostNetInfo>.EventListener.UnregisterAsyncEvent(eventRegisterId);
                Status = ListenStatus.Stopped;
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

    abstract class NetTcpListener : AsyncService
    {
        readonly CriticalSection LockObj = new CriticalSection();

        readonly Dictionary<string, NetTcpListenerPort> List = new Dictionary<string, NetTcpListenerPort>();

        readonly Dictionary<Task, ConnSock> RunningAcceptedTasks = new Dictionary<Task, ConnSock>();

        NetTcpListenerAcceptedProcCallback AcceptedProc { get; }

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

        public NetTcpListenerPort Add(int port, IPVersion? ipVer = null, IPAddress addr = null)
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
            NetTcpListenerPort s;
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

        NetTcpListenerPort Search(string hashKey)
        {
            if (List.TryGetValue(hashKey, out NetTcpListenerPort ret) == false)
                return null;
            return ret;
        }

        internal async Task InternalSocketAcceptedAsync(NetTcpListenerPort listener, ConnSock sock)
        {
            try
            {
                await AcceptedProc(listener, sock);
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

        protected override void CancelImpl(Exception ex)
        {
        }

        protected override async Task CleanupImplAsync(Exception ex)
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

        protected override void DisposeImpl(Exception ex) { }
    }

    class NetPalTcpListener : NetTcpListener
    {
        public NetPalTcpListener(NetTcpListenerAcceptedProcCallback acceptedProc) : base(acceptedProc) { }

        protected internal override NetTcpProtocolStubBase CreateNewTcpStubForListenImpl(CancellationToken cancel)
            => new NetPalTcpProtocolStub(null, null, cancel);
    }
}
