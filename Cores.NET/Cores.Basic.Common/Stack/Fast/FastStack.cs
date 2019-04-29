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

    abstract class FastStackBase : AsyncCleanupableCancellable
    {
        public FastStackOptionsBase StackOptions { get; }

        public FastStackBase(AsyncCleanuperLady lady, FastStackOptionsBase options, CancellationToken cancel = default) :
            base(lady, cancel)
        {
            try
            {
                StackOptions = options;
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }
    }

    abstract class FastAppStubOptionsBase : FastStackOptionsBase { }

    abstract class FastAppStubBase : FastStackBase
    {
        protected FastPipeEnd Lower { get; }
        protected FastAttachHandle LowerAttach { get; private set; }

        public FastAppStubOptionsBase TopOptions { get; }

        public FastAppStubBase(AsyncCleanuperLady lady, FastPipeEnd lower, FastAppStubOptionsBase options, CancellationToken cancel = default)
            : base(lady, options, cancel)
        {
            try
            {
                TopOptions = options;
                Lower = lower;

                LowerAttach = Lower.Attach(this.Lady, FastPipeEndAttachDirection.B_UpperSide);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public virtual void Disconnect(Exception ex)
        {
            Lower.Disconnect(ex);
        }
    }

    class FastAppStubOptions : FastAppStubOptionsBase { }

    class FastAppStub : FastAppStubBase
    {
        public FastAppStub(AsyncCleanuperLady lady, FastPipeEnd lower, CancellationToken cancel = default, FastAppStubOptions options = null)
            : base(lady, lower, options ?? new FastAppStubOptions(), cancel)
        {
        }

        CriticalSection LockObj = new CriticalSection();

        FastPipeEndStream StreamCache = null;

        public FastPipeEndStream GetStream(bool autoFlash = true)
        {
            lock (LockObj)
            {
                if (StreamCache == null)
                    StreamCache = AttachHandle.GetStream(autoFlash);

                return StreamCache;
            }
        }

        public FastPipeEnd GetPipeEnd()
        {
            Lower.CheckDisconnected();

            return Lower;
        }

        public FastAttachHandle AttachHandle
        {
            get
            {
                Lower.CheckDisconnected();
                return this.LowerAttach;
            }
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                StreamCache.DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }
    }

    abstract class FastProtocolOptionsBase : FastStackOptionsBase { }

    abstract class FastProtocolBase : FastStackBase
    {
        protected FastPipeEnd Upper { get; }

        protected internal FastPipeEnd _InternalUpper { get => Upper; }

        protected FastAttachHandle UpperAttach { get; private set; }

        public FastProtocolOptionsBase ProtocolOptions { get; }

        public FastProtocolBase(AsyncCleanuperLady lady, FastPipeEnd upper, FastProtocolOptionsBase options, CancellationToken cancel = default)
            : base(lady, options, cancel)
        {
            try
            {
                if (upper == null)
                {
                    upper = FastPipeEnd.NewFastPipeAndGetOneSide(FastPipeEndSide.A_LowerSide, Lady, cancel);
                    Lady.Add(upper.Pipe);
                }

                ProtocolOptions = options;
                Upper = upper;

                UpperAttach = Upper.Attach(this.Lady, FastPipeEndAttachDirection.A_LowerSide);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public virtual void Disconnect(Exception ex = null)
        {
            Upper.Disconnect(ex);
        }
    }

    abstract class FastBottomProtocolOptionsBase : FastProtocolOptionsBase { }

    abstract class FastBottomProtocolStubBase : FastProtocolBase
    {
        public FastBottomProtocolStubBase(AsyncCleanuperLady lady, FastPipeEnd upper, FastProtocolOptionsBase options, CancellationToken cancel = default) : base(lady, upper, options, cancel) { }
    }

    abstract class FastTcpProtocolOptionsBase : FastBottomProtocolOptionsBase
    {
        public FastDnsClientStub DnsClient { get; set; }
    }

    abstract class FastTcpProtocolStubBase : FastBottomProtocolStubBase
    {
        public const int DefaultTcpConnectTimeout = 15 * 1000;

        FastTcpProtocolOptionsBase Options { get; }

        public FastTcpProtocolStubBase(AsyncCleanuperLady lady, FastPipeEnd upper, FastTcpProtocolOptionsBase options, CancellationToken cancel = default) : base(lady, upper, options, cancel)
        {
            Options = options;
        }

        protected abstract Task ConnectImplAsync(IPEndPoint remoteEndPoint, int connectTimeout = DefaultTcpConnectTimeout, CancellationToken cancel = default);
        protected abstract void ListenImpl(IPEndPoint localEndPoint);
        protected abstract Task<FastTcpProtocolStubBase> AcceptImplAsync(AsyncCleanuperLady lady, CancellationToken cancelForNewSocket = default);

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

        public async Task<ConnectionSock> AcceptAsync(AsyncCleanuperLady lady, CancellationToken cancelForNewSocket = default)
        {
            if (IsListening == false) throw new ApplicationException("Not listening.");

            return new ConnectionSock(lady, await AcceptImplAsync(lady, cancelForNewSocket));
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
            public int LocalPort { get; set; }
            public int RemotePort { get; set; }
            public IPAddress LocalIPAddress { get; set; }
            public IPAddress RemoteIPAddress { get; set; }
        }

        public FastPalTcpProtocolStub(AsyncCleanuperLady lady, FastPipeEnd upper = null, FastPalTcpProtocolOptions options = null, CancellationToken cancel = default)
            : base(lady, upper, options ?? new FastPalTcpProtocolOptions(), cancel)
        {
        }

        public virtual void FromSocket(PalSocket s)
        {
            AsyncCleanuperLady lady = new AsyncCleanuperLady();

            try
            {
                var socketWrapper = new FastPipeEndSocketWrapper(lady, Upper, s, GrandCancel);

                UpperAttach.SetLayerInfo(new LayerInfo()
                {
                    LocalPort = ((IPEndPoint)s.LocalEndPoint).Port,
                    LocalIPAddress = ((IPEndPoint)s.LocalEndPoint).Address,
                    RemotePort = ((IPEndPoint)s.RemoteEndPoint).Port,
                    RemoteIPAddress = ((IPEndPoint)s.RemoteEndPoint).Address,
                }, this);

                this.Lady.MergeFrom(lady);
            }
            catch
            {
                lady.DisposeAllSafe();
                throw;
            }
        }

        protected override async Task ConnectImplAsync(IPEndPoint remoteEndPoint, int connectTimeout = FastTcpProtocolStubBase.DefaultTcpConnectTimeout, CancellationToken cancel = default)
        {
            AsyncCleanuperLady lady = new AsyncCleanuperLady();

            try
            {
                if (!(remoteEndPoint.AddressFamily == AddressFamily.InterNetwork || remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6))
                    throw new ArgumentException("RemoteEndPoint.AddressFamily");

                PalSocket s = new PalSocket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp).AddToLady(lady);

                await TaskUtil.DoAsyncWithTimeout(async localCancel =>
                {
                    await s.ConnectAsync(remoteEndPoint);
                    return 0;
                },
                cancelProc: () => s.DisposeSafe(),
                timeout: connectTimeout,
                cancel: cancel);

                FromSocket(s);

                this.Lady.MergeFrom(lady);
            }
            catch
            {
                await lady;
                throw;
            }
        }

        PalSocket ListeningSocket = null;

        protected override void ListenImpl(IPEndPoint localEndPoint)
        {
            AsyncCleanuperLady lady = new AsyncCleanuperLady();

            try
            {
                if (!(localEndPoint.AddressFamily == AddressFamily.InterNetwork || localEndPoint.AddressFamily == AddressFamily.InterNetworkV6))
                    throw new ArgumentException("RemoteEndPoint.AddressFamily");

                PalSocket s = new PalSocket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp).AddToLady(lady);

                s.Bind(localEndPoint);

                s.Listen(int.MaxValue);

                ListeningSocket = s;

                this.Lady.MergeFrom(lady);
            }
            catch
            {
                lady.DisposeAllSafe();
                throw;
            }
        }

        protected override async Task<FastTcpProtocolStubBase> AcceptImplAsync(AsyncCleanuperLady lady, CancellationToken cancelForNewSocket = default)
        {
            using (CancelWatcher.EventList.RegisterCallbackWithUsing((caller, type, state) => ListeningSocket.DisposeSafe()))
            {
                PalSocket newSocket = await ListeningSocket.AcceptAsync();

                var newStub = new FastPalTcpProtocolStub(lady, null, null, cancelForNewSocket);

                newStub.FromSocket(newSocket);

                return newStub;
            }
        }
    }

    class NetworkSock : AsyncCleanupable
    {
        FastAppStub AppStub = null;

        public FastProtocolBase Stack { get; }
        public FastPipe Pipe { get; }
        public FastPipeEnd LowerEnd { get; }
        public FastPipeEnd UpperEnd { get; }
        public LayerInfo Info { get => this.LowerEnd.LayerInfo; }
        public CancelWatcher CancelWatcher => Stack.CancelWatcher;

        public NetworkSock(AsyncCleanuperLady lady, FastProtocolBase protocolStack)
            : base(lady)
        {
            try
            {
                Stack = protocolStack;
                LowerEnd = Stack._InternalUpper;
                Pipe = LowerEnd.Pipe;
                UpperEnd = LowerEnd.CounterPart;

                Lady.Add(Stack);
                Lady.Add(Pipe);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public FastAppStub GetFastAppProtocolStub()
        {
            FastAppStub ret = UpperEnd.GetFastAppProtocolStub(this.Lady);

            return ret;
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Pipe.Disconnect();
            }
            finally { base.Dispose(disposing); }
        }

        public void Disconnect(Exception ex = null)
            => this.Dispose();

        public FastPipeEndStream GetStream(bool autoFlush = true)
        {
            if (AppStub == null)
                AppStub = this.GetFastAppProtocolStub();

            return AppStub.GetStream(autoFlush);
        }

        public void EnsureAttach(bool autoFlush = true) => GetStream(autoFlush); // Ensure attach

        public FastAttachHandle AttachHandle => this.AppStub?.AttachHandle ?? throw new ApplicationException("You need to call GetStream() first before accessing to AttachHandle.");
    }

    class ConnectionSock : NetworkSock
    {
        public ConnectionSock(AsyncCleanuperLady lady, FastProtocolBase protocolStack) : base(lady, protocolStack) { }
    }

    class FastDnsClientOptions : FastStackOptionsBase { }

    abstract class FastDnsClientStub : FastStackBase
    {
        public const int DefaultDnsResolveTimeout = 5 * 1000;

        public FastDnsClientStub(AsyncCleanuperLady lady, FastDnsClientOptions options, CancellationToken cancel = default) : base(lady, options, cancel)
        {
        }

        public abstract Task<IPAddress> GetIPFromHostName(string host, AddressFamily? addressFamily = null, CancellationToken cancel = default,
            int timeout = DefaultDnsResolveTimeout);
    }

    class FastPalDnsClient : FastDnsClientStub
    {
        public static FastPalDnsClient Shared { get; }

        static FastPalDnsClient()
        {
            Shared = new FastPalDnsClient(LeakChecker.SuperGrandLady, new FastDnsClientOptions());
        }

        public FastPalDnsClient(AsyncCleanuperLady lady, FastDnsClientOptions options, CancellationToken cancel = default) : base(lady, options, cancel)
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

        public FastMiddleProtocolOptionsBase MiddleOptions { get; }

        public FastMiddleProtocolStackBase(AsyncCleanuperLady lady, FastPipeEnd lower, FastPipeEnd upper, FastMiddleProtocolOptionsBase options, CancellationToken cancel = default)
            : base(lady, upper, options, cancel)
        {
            try
            {
                MiddleOptions = options;
                Lower = lower;

                LowerAttach = Lower.Attach(this.Lady, FastPipeEndAttachDirection.B_UpperSide);

                Lower.ExceptionQueue.Encounter(Upper.ExceptionQueue);
                Lower.LayerInfo.Encounter(Upper.LayerInfo);

                Lower.AddOnDisconnected(() => Upper.Disconnect());
                Upper.AddOnDisconnected(() => Lower.Disconnect());
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public override void Disconnect(Exception ex = null)
        {
            try
            {
                Lower.Disconnect(ex);
            }
            finally { base.Disconnect(ex); }
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

        public FastSslProtocolStack(AsyncCleanuperLady lady, FastPipeEnd lower, FastPipeEnd upper, FastSslProtocolOptions options,
            CancellationToken cancel = default) : base(lady, lower, upper, options ?? new FastSslProtocolOptions(), cancel) { }

        public async Task SslStartClientAsync(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            try
            {
                using (this.CreatePerTaskCancellationToken(out CancellationToken opCancel, cancellationToken))
                {
                    var lowerStream = LowerAttach.GetStream(autoFlush: false);

                    var ssl = new PalSslStream(lowerStream).AddToLady(this);

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
                    }, this);

                    FastPipeEndStreamWrapper upperStreamWrapper = new FastPipeEndStreamWrapper(this.Lady, UpperAttach.PipeEnd, ssl, CancelWatcher.CancelToken);
                }
            }
            catch
            {
                await Lady;
                throw;
            }
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

    delegate Task FastTcpListenerAcceptedProcCallback(FastTcpListenerBase.Listener listener, ConnectionSock newSock);

    abstract class FastTcpListenerBase : IAsyncCleanupable
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

                        AsyncCleanuperLady listenLady = new AsyncCleanuperLady();

                        try
                        {
                            var listenTcp = TcpListener.CreateNewTcpStubForListenImpl(listenLady, _InternalSelfCancelToken);

                            listenTcp.Listen(new IPEndPoint(IPAddress, Port));

                            Status = ListenStatus.Listening;

                            while (true)
                            {
                                _InternalSelfCancelToken.ThrowIfCancellationRequested();

                                AsyncCleanuperLady ladyForNewTcpStub = new AsyncCleanuperLady();

                                ConnectionSock sock = await listenTcp.AcceptAsync(ladyForNewTcpStub);

                                TcpListener.InternalSocketAccepted(this, sock, ladyForNewTcpStub);
                            }
                        }
                        catch (Exception ex)
                        {
                            LastError = ex;
                        }
                        finally
                        {
                            await listenLady;
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
                await _InternalSelfCancelSource.TryCancelAsync();
                try
                {
                    await _InternalTask;
                }
                catch { }
            }
        }

        readonly CriticalSection LockObj = new CriticalSection();

        readonly Dictionary<string, Listener> List = new Dictionary<string, Listener>();

        readonly Dictionary<Task, ConnectionSock> RunningAcceptedTasks = new Dictionary<Task, ConnectionSock>();

        readonly CancellationTokenSource CancelSource = new CancellationTokenSource();

        FastTcpListenerAcceptedProcCallback AcceptedProc { get; }

        public int CurrentConnections
        {
            get
            {
                lock (RunningAcceptedTasks)
                    return RunningAcceptedTasks.Count;
            }
        }

        public FastTcpListenerBase(AsyncCleanuperLady lady, FastTcpListenerAcceptedProcCallback acceptedProc)
        {
            AcceptedProc = acceptedProc;

            AsyncCleanuper = new AsyncCleanuper(this);

            lady.Add(this);
        }

        internal protected abstract FastTcpProtocolStubBase CreateNewTcpStubForListenImpl(AsyncCleanuperLady lady, CancellationToken cancel);

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
                if (DisposeFlag.IsSet) throw new ObjectDisposedException("TcpListenManager");

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

        async Task InternalSocketAcceptedAsync(Listener listener, ConnectionSock sock, AsyncCleanuperLady lady)
        {
            try
            {
                await AcceptedProc(listener, sock);
            }
            finally
            {
                await lady;
            }
        }

        void InternalSocketAccepted(Listener listener, ConnectionSock sock, AsyncCleanuperLady lady)
        {
            try
            {
                Task t = InternalSocketAcceptedAsync(listener, sock, lady);

                if (t.IsCompleted)
                {
                    lady.DisposeAllSafe();
                }
                else
                {
                    lock (LockObj)
                        RunningAcceptedTasks.Add(t, sock);
                    t.ContinueWith(x =>
                    {
                        sock.DisposeSafe();
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

        Once DisposeFlag;
        public void Dispose()
        {
            if (DisposeFlag.IsFirstCall())
            {
            }
        }

        public async Task _CleanupAsyncInternal()
        {
            List<Listener> o = new List<Listener>();
            lock (LockObj)
            {
                List.Values.ToList().ForEach(x => o.Add(x));
                List.Clear();
            }

            foreach (Listener s in o)
                await s._InternalStopAsync().TryWaitAsync();

            List<Task> waitTasks = new List<Task>();
            List<ConnectionSock> disconnectStubs = new List<ConnectionSock>();

            lock (LockObj)
            {
                foreach (var v in RunningAcceptedTasks)
                {
                    disconnectStubs.Add(v.Value);
                    waitTasks.Add(v.Key);
                }
                RunningAcceptedTasks.Clear();
            }

            foreach (var sock in disconnectStubs)
            {
                try
                {
                    await sock.AsyncCleanuper;
                }
                catch { }
            }

            foreach (var task in waitTasks)
                await task.TryWaitAsync();

            Debug.Assert(CurrentConnections == 0);
        }

        public AsyncCleanuper AsyncCleanuper { get; }
    }

    class FastPalTcpListener : FastTcpListenerBase
    {
        public FastPalTcpListener(AsyncCleanuperLady lady, FastTcpListenerAcceptedProcCallback acceptedProc) : base(lady, acceptedProc) { }

        protected internal override FastTcpProtocolStubBase CreateNewTcpStubForListenImpl(AsyncCleanuperLady lady, CancellationToken cancel)
            => new FastPalTcpProtocolStub(lady, null, null, cancel);
    }
}
