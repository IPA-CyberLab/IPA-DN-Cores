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
    abstract class FastStackOptionsBase { }

    abstract class FastStackBase : AsyncService
    {
        public FastStackOptionsBase Options { get; }

        public FastStackBase(FastStackOptionsBase options, CancellationToken cancel = default) : base(cancel)
        {
            Options = options;
        }
    }

    abstract class FastAppStubOptionsBase : FastStackOptionsBase { }

    abstract class FastAppStubBase : FastStackBase
    {
        protected FastPipeEnd Lower { get; }
        protected FastAttachHandle LowerAttach { get; private set; }

        public new FastAppStubOptionsBase Options => (FastAppStubOptionsBase)base.Options;

        public FastAppStubBase(FastPipeEnd lower, FastAppStubOptionsBase options, CancellationToken cancel = default)
            : base(options, cancel)
        {
            try
            {
                LowerAttach = lower.Attach(FastPipeEndAttachDirection.B_UpperSide);
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

    class FastAppStubOptions : FastAppStubOptionsBase { }

    class FastAppStub : FastAppStubBase
    {
        public new FastAppStubOptions Options => (FastAppStubOptions)base.Options;

        public FastAppStub(FastPipeEnd lower, CancellationToken cancel = default, FastAppStubOptions options = null)
            : base(lower, options ?? new FastAppStubOptions(), cancel)
        {
        }

        CriticalSection LockObj = new CriticalSection();

        FastPipeEndStream StreamCache = null;

        public FastPipeEndStream GetStream(bool autoFlash = true)
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

        public FastPipeEnd GetPipeEnd()
        {
            Lower.CheckCanceled();

            return Lower;
        }

        public FastAttachHandle AttachHandle
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

    abstract class FastProtocolOptionsBase : FastStackOptionsBase { }

    abstract class FastProtocolBase : FastStackBase
    {
        protected FastPipeEnd Upper { get; }

        protected internal FastPipeEnd _InternalUpper { get => Upper; }

        protected FastAttachHandle UpperAttach { get; private set; }

        public new FastProtocolOptionsBase Options => (FastProtocolOptionsBase)base.Options;

        public FastProtocolBase(FastPipeEnd upper, FastProtocolOptionsBase options, CancellationToken cancel = default)
            : base(options, cancel)
        {
            try
            {
                if (upper == null)
                {
                    upper = FastPipeEnd.NewFastPipeAndGetOneSide(FastPipeEndSide.A_LowerSide, cancel);
                }

                UpperAttach = upper.Attach(FastPipeEndAttachDirection.A_LowerSide);
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

    abstract class FastBottomProtocolOptionsBase : FastProtocolOptionsBase { }

    abstract class FastBottomProtocolStubBase : FastProtocolBase
    {
        public new FastBottomProtocolOptionsBase Options => (FastBottomProtocolOptionsBase)base.Options;

        public FastBottomProtocolStubBase(FastPipeEnd upper, FastProtocolOptionsBase options, CancellationToken cancel = default) : base(upper, options, cancel)
        {
        }
    }

    abstract class FastTcpProtocolOptionsBase : FastBottomProtocolOptionsBase
    {
        public FastDnsClientStub DnsClient { get; set; }
    }

    abstract class FastTcpProtocolStubBase : FastBottomProtocolStubBase
    {
        public const int DefaultTcpConnectTimeout = 15 * 1000;

        public new FastTcpProtocolOptionsBase Options => (FastTcpProtocolOptionsBase)base.Options;

        public FastTcpProtocolStubBase(FastPipeEnd upper, FastTcpProtocolOptionsBase options, CancellationToken cancel = default) : base(upper, options, cancel)
        {
        }

        protected abstract Task ConnectImplAsync(IPEndPoint remoteEndPoint, int connectTimeout = DefaultTcpConnectTimeout, CancellationToken cancel = default);
        protected abstract void ListenImpl(IPEndPoint localEndPoint);
        protected abstract Task<FastTcpProtocolStubBase> AcceptImplAsync(CancellationToken cancelForNewSocket = default);

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

        public Task ConnectAsync(IPAddress ip, int port, CancellationToken cancel = default, int connectTimeout = FastTcpProtocolStubBase.DefaultTcpConnectTimeout)
            => ConnectAsync(new IPEndPoint(ip, port), connectTimeout, cancel);

        public async Task ConnectAsync(string host, int port, AddressFamily? addressFamily = null, int connectTimeout = FastTcpProtocolStubBase.DefaultTcpConnectTimeout)
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

    class FastPalTcpProtocolOptions : FastTcpProtocolOptionsBase
    {
        public FastPalTcpProtocolOptions()
        {
            this.DnsClient = FastPalDnsClient.Shared;
        }
    }

    class FastPalTcpProtocolStub : FastTcpProtocolStubBase
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

        public new FastPalTcpProtocolOptions Options => (FastPalTcpProtocolOptions)base.Options;

        PalSocket ConnectedSocket = null;
        FastPipeEndSocketWrapper SocketWrapper = null;

        PalSocket ListeningSocket = null;

        public FastPalTcpProtocolStub(FastPipeEnd upper = null, FastPalTcpProtocolOptions options = null, CancellationToken cancel = default)
            : base(upper, options ?? new FastPalTcpProtocolOptions(), cancel)
        {
        }

        void InitSocketWrapperFromSocket(PalSocket s)
        {
            this.ConnectedSocket = s;
            this.SocketWrapper = new FastPipeEndSocketWrapper(Upper, s, this.GrandCancel);
            AddIndirectDisposeLink(this.SocketWrapper); // Do not add SocketWrapper with AddChild(). It causes deadlock by cyclic reference.

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

        protected override async Task ConnectImplAsync(IPEndPoint remoteEndPoint, int connectTimeout = FastTcpProtocolStubBase.DefaultTcpConnectTimeout, CancellationToken cancel = default)
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

        protected override async Task<FastTcpProtocolStubBase> AcceptImplAsync(CancellationToken cancelForNewSocket = default)
        {
            PalSocket newSocket = await ListeningSocket.AcceptAsync();
            try
            {
                var newStub = new FastPalTcpProtocolStub(null, null, cancelForNewSocket);

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

    class NetworkSock : AsyncService
    {
        FastAppStub AppStub = null;

        public FastProtocolBase Stack { get; }
        public FastPipe Pipe { get; }
        public FastPipeEnd UpperEnd { get; }
        public LayerInfo Info { get => this.Pipe.LayerInfo; }
        public string Guid { get; } = Str.NewGuid();
        public DateTimeOffset Connected { get; } = DateTimeOffset.Now;
        public DateTimeOffset? Disconnected { get; private set; }


        public NetworkSock(FastProtocolBase protocolStack, CancellationToken cancel = default) : base(cancel)
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

        public FastAppStub GetFastAppProtocolStub()
        {
            FastAppStub ret = AddDirectDisposeLink(UpperEnd.GetFastAppProtocolStub());

            ret.AddIndirectDisposeLink(this);

            return ret;
        }

        public FastPipeEndStream GetStream(bool autoFlush = true)
        {
            if (AppStub == null)
                AppStub = this.GetFastAppProtocolStub();

            return AppStub.GetStream(autoFlush);
        }

        public void EnsureAttach(bool autoFlush = true) => GetStream(autoFlush); // Ensure attach

        public FastAttachHandle AttachHandle => this.AppStub?.AttachHandle ?? throw new ApplicationException("You need to call GetStream() first before accessing to AttachHandle.");
    }

    class ConnSock : NetworkSock
    {
        public ConnSock(FastProtocolBase protocolStack) : base(protocolStack) { }
    }

    class FastDnsClientOptions : FastStackOptionsBase { }

    abstract class FastDnsClientStub : FastStackBase
    {
        public const int DefaultDnsResolveTimeout = 5 * 1000;

        public FastDnsClientStub(FastDnsClientOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
        }

        public abstract Task<IPAddress> GetIPFromHostName(string host, AddressFamily? addressFamily = null, CancellationToken cancel = default,
            int timeout = DefaultDnsResolveTimeout);
    }

    class FastPalDnsClient : FastDnsClientStub
    {
        public static FastPalDnsClient Shared { get; private set; }

        public static StaticModule Module { get; } = new StaticModule(ModuleInit, ModuleFree);

        static void ModuleInit()
        {
            Shared = new FastPalDnsClient(new FastDnsClientOptions());
        }

        static void ModuleFree()
        {
            Shared._DisposeSafe();
            Shared = null;
        }


        public FastPalDnsClient(FastDnsClientOptions options, CancellationToken cancel = default) : base(options, cancel)
        {
        }

        public override async Task<IPAddress> GetIPFromHostName(string host, AddressFamily? addressFamily = null, CancellationToken cancel = default,
            int timeout = FastDnsClientStub.DefaultDnsResolveTimeout)
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

    abstract class FastMiddleProtocolOptionsBase : FastProtocolOptionsBase
    {
        public int LowerReceiveTimeoutOnInit { get; set; } = 5 * 1000;
        public int LowerSendTimeoutOnInit { get; set; } = 60 * 1000;

        public int LowerReceiveTimeoutAfterInit { get; set; } = Timeout.Infinite;
        public int LowerSendTimeoutAfterInit { get; set; } = Timeout.Infinite;
    }

    abstract class FastMiddleProtocolStackBase : FastProtocolBase
    {
        protected FastPipeEnd Lower { get; }

        CriticalSection LockObj = new CriticalSection();
        protected FastAttachHandle LowerAttach { get; private set; }

        public new FastMiddleProtocolOptionsBase Options => (FastMiddleProtocolOptionsBase)base.Options;

        public FastMiddleProtocolStackBase(FastPipeEnd lower, FastPipeEnd upper, FastMiddleProtocolOptionsBase options, CancellationToken cancel = default)
            : base(upper, options, cancel)
        {
            try
            {
                LowerAttach = AddIndirectDisposeLink(lower.Attach(FastPipeEndAttachDirection.B_UpperSide));
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

    class FastSslProtocolOptions : FastMiddleProtocolOptionsBase { }

    class FastSslProtocolStack : FastMiddleProtocolStackBase
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

        public FastSslProtocolStack(FastPipeEnd lower, FastPipeEnd upper, FastSslProtocolOptions options,
            CancellationToken cancel = default) : base(lower, upper, options ?? new FastSslProtocolOptions(), cancel) { }

        FastPipeEndStream LowerStream = null;
        PalSslStream SslStream = null;
        FastPipeEndStreamWrapper Wrapper = null;

        public async Task SslStartClientAsync(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            if (Wrapper != null)
                throw new ApplicationException("SSL is already established.");

            using (this.CreatePerTaskCancellationToken(out CancellationToken opCancel, cancellationToken))
            {
                FastPipeEndStream lowerStream = LowerAttach.GetStream(autoFlush: false);
                try
                {
                    PalSslStream ssl = new PalSslStream(lowerStream);
                    try
                    {
                        await ssl.AuthenticateAsClientAsync(sslClientAuthenticationOptions, opCancel);

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

                        this.Wrapper = new FastPipeEndStreamWrapper(UpperAttach.PipeEnd, ssl, CancelWatcher.CancelToken);

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

    delegate Task FastTcpListenerAcceptedProcCallback(FastTcpListenerBase.Listener listener, ConnSock newSock);

    abstract class FastTcpListenerBase : AsyncService
    {
        public class Listener
        {
            public IPVersion IPVersion { get; }
            public IPAddress IPAddress { get; }
            public int Port { get; }

            public ListenStatus Status { get; internal set; }
            public Exception LastError { get; internal set; }

            internal Task _InternalTask { get; }

            internal CancellationTokenSource _InternalSelfCancelSource { get; }
            internal CancellationToken _InternalSelfCancelToken { get => _InternalSelfCancelSource.Token; }

            public FastTcpListenerBase TcpListener { get; }

            public const long RetryIntervalStandard = 1 * 512;
            public const long RetryIntervalMax = 60 * 1000;

            internal Listener(FastTcpListenerBase listener, IPVersion ver, IPAddress addr, int port)
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

                        FastTcpProtocolStubBase listenTcp = TcpListener.CreateNewTcpStubForListenImpl(_InternalSelfCancelToken);

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

        readonly CriticalSection LockObj = new CriticalSection();

        readonly Dictionary<string, Listener> List = new Dictionary<string, Listener>();

        readonly Dictionary<Task, ConnSock> RunningAcceptedTasks = new Dictionary<Task, ConnSock>();

        FastTcpListenerAcceptedProcCallback AcceptedProc { get; }

        public int CurrentConnections
        {
            get
            {
                lock (RunningAcceptedTasks)
                    return RunningAcceptedTasks.Count;
            }
        }

        public FastTcpListenerBase(FastTcpListenerAcceptedProcCallback acceptedProc)
        {
            AcceptedProc = acceptedProc;
        }

        internal protected abstract FastTcpProtocolStubBase CreateNewTcpStubForListenImpl(CancellationToken cancel);

        public Listener Add(int port, IPVersion? ipVer = null, IPAddress addr = null)
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

                var s = Search(Listener.MakeHashKey((IPVersion)ipVer, addr, port));
                if (s != null)
                    return s;
                s = new Listener(this, (IPVersion)ipVer, addr, port);
                List.Add(Listener.MakeHashKey((IPVersion)ipVer, addr, port), s);
                return s;
            }
        }

        public async Task<bool> DeleteAsync(Listener listener)
        {
            Listener s;
            lock (LockObj)
            {
                string hashKey = Listener.MakeHashKey(listener.IPVersion, listener.IPAddress, listener.Port);
                s = Search(hashKey);
                if (s == null)
                    return false;
                List.Remove(hashKey);
            }
            await s._InternalStopAsync();
            return true;
        }

        Listener Search(string hashKey)
        {
            if (List.TryGetValue(hashKey, out Listener ret) == false)
                return null;
            return ret;
        }

        async Task InternalSocketAcceptedAsync(Listener listener, ConnSock sock)
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

        void InternalSocketAccepted(Listener listener, ConnSock sock)
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

        public Listener[] Listeners
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
            List<Listener> o = new List<Listener>();
            lock (LockObj)
            {
                List.Values.ToList().ForEach(x => o.Add(x));
                List.Clear();
            }

            foreach (Listener s in o)
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

    class FastPalTcpListener : FastTcpListenerBase
    {
        public FastPalTcpListener(FastTcpListenerAcceptedProcCallback acceptedProc) : base(acceptedProc) { }

        protected internal override FastTcpProtocolStubBase CreateNewTcpStubForListenImpl(CancellationToken cancel)
            => new FastPalTcpProtocolStub(null, null, cancel);
    }
}
