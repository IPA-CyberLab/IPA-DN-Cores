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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Net.NetworkInformation;

using IPA.Cores.Helper.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class PalX509Certificate
    {
        public X509Certificate NativeCertificate;

        public PalX509Certificate(X509Certificate nativeCertificate)
        {
            NativeCertificate = nativeCertificate;
        }
    }

    struct PalSocketReceiveFromResult
    {
        public int ReceivedBytes;
        public EndPoint RemoteEndPoint;
    }

    class PalSocket : IDisposable
    {
        public static bool OSSupportsIPv4 { get => Socket.OSSupportsIPv4; }
        public static bool OSSupportsIPv6 { get => Socket.OSSupportsIPv6; }

        Socket _Socket;

        public AddressFamily AddressFamily { get; }
        public SocketType SocketType { get; }
        public ProtocolType ProtocolType { get; }

        CriticalSection LockObj = new CriticalSection();

        public CachedProperty<bool> NoDelay { get; }
        public CachedProperty<int> LingerTime { get; }
        public CachedProperty<int> SendBufferSize { get; }
        public CachedProperty<int> ReceiveBufferSize { get; }

        public CachedProperty<EndPoint> LocalEndPoint { get; }
        public CachedProperty<EndPoint> RemoteEndPoint { get; }

        LeakCheckerHolder Leak;

        public PalSocket(Socket s)
        {
            _Socket = s;

            AddressFamily = _Socket.AddressFamily;
            SocketType = _Socket.SocketType;
            ProtocolType = _Socket.ProtocolType;

            NoDelay = new CachedProperty<bool>(value => _Socket.NoDelay = value, () => _Socket.NoDelay);
            LingerTime = new CachedProperty<int>(value =>
            {
                if (value <= 0) value = 0;
                if (value == 0)
                    _Socket.LingerState = new LingerOption(false, 0);
                else
                    _Socket.LingerState = new LingerOption(true, value);

                try
                {
                    if (value == 0)
                        _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
                    else
                        _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, false);
                }
                catch { }

                return value;
            }, () =>
            {
                var lingerOption = _Socket.LingerState;
                if (lingerOption == null || lingerOption.Enabled == false)
                    return 0;
                else
                    return lingerOption.LingerTime;
            });
            SendBufferSize = new CachedProperty<int>(value => _Socket.SendBufferSize = value, () => _Socket.SendBufferSize);
            ReceiveBufferSize = new CachedProperty<int>(value => _Socket.ReceiveBufferSize = value, () => _Socket.ReceiveBufferSize);
            LocalEndPoint = new CachedProperty<EndPoint>(null, () => _Socket.LocalEndPoint);
            RemoteEndPoint = new CachedProperty<EndPoint>(null, () => _Socket.RemoteEndPoint);

            Leak = LeakChecker.Enter();
        }

        public PalSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType)
            : this(new Socket(addressFamily, socketType, protocolType)) { }

        public Task ConnectAsync(IPAddress address, int port) => ConnectAsync(new IPEndPoint(address, port));

        public async Task ConnectAsync(EndPoint remoteEP)
        {
            await _Socket.ConnectAsync(remoteEP);

            this.LocalEndPoint.Flush();
            this.RemoteEndPoint.Flush();
        }

        public void Connect(EndPoint remoteEP) => _Socket.Connect(remoteEP);

        public void Connect(IPAddress address, int port) => _Socket.Connect(address, port);

        public void Bind(EndPoint localEP)
        {
            _Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            _Socket.Bind(localEP);
            this.LocalEndPoint.Flush();
            this.RemoteEndPoint.Flush();
        }

        public void Listen(int backlog = int.MaxValue)
        {
            _Socket.Listen(backlog);
            this.LocalEndPoint.Flush();
            this.RemoteEndPoint.Flush();
        }

        public async Task<PalSocket> AcceptAsync()
        {
            Socket newSocket = await _Socket.AcceptAsync();
            return new PalSocket(newSocket);
        }

        public Task<int> SendAsync(IEnumerable<Memory<byte>> buffers)
        {
            List<ArraySegment<byte>> sendArraySegmentsList = new List<ArraySegment<byte>>();
            foreach (Memory<byte> mem in buffers)
                sendArraySegmentsList.Add(mem.AsSegment());

            return _Socket.SendAsync(sendArraySegmentsList, SocketFlags.None);
        }

        public async Task<int> SendAsync(ReadOnlyMemory<byte> buffer)
        {
            return await _Socket.SendAsync(buffer, SocketFlags.None);
        }

        public async Task<int> ReceiveAsync(Memory<byte> buffer)
        {
            return await _Socket.ReceiveAsync(buffer, SocketFlags.None);
        }

        public async Task<int> SendToAsync(Memory<byte> buffer, EndPoint remoteEP)
        {
            try
            {
                Task<int> t = _Socket.SendToAsync(buffer.AsSegment(), SocketFlags.None, remoteEP);
                if (t.IsCompleted == false)
                    await t;
                int ret = t.Result;
                if (ret <= 0) throw new SocketDisconnectedException();
                return ret;
            }
            catch (SocketException e) when (CanUdpSocketErrorBeIgnored(e))
            {
                return buffer.Length;
            }
        }

        static readonly IPEndPoint StaticUdpEndPointIPv4 = new IPEndPoint(IPAddress.Any, 0);
        static readonly IPEndPoint StaticUdpEndPointIPv6 = new IPEndPoint(IPAddress.IPv6Any, 0);
        const int UdpMaxRetryOnIgnoreError = 1000;

        public async Task<PalSocketReceiveFromResult> ReceiveFromAsync(Memory<byte> buffer)
        {
            int numRetry = 0;

            var bufferSegment = buffer.AsSegment();

            LABEL_RETRY:

            try
            {
                Task<SocketReceiveFromResult> t = _Socket.ReceiveFromAsync(bufferSegment, SocketFlags.None,
                    this.AddressFamily == AddressFamily.InterNetworkV6 ? StaticUdpEndPointIPv6 : StaticUdpEndPointIPv4);
                if (t.IsCompleted == false)
                {
                    numRetry = 0;
                    await t;
                }
                SocketReceiveFromResult ret = t.Result;
                if (ret.ReceivedBytes <= 0) throw new SocketDisconnectedException();
                return new PalSocketReceiveFromResult()
                {
                    ReceivedBytes = ret.ReceivedBytes,
                    RemoteEndPoint = ret.RemoteEndPoint,
                };
            }
            catch (SocketException e) when (CanUdpSocketErrorBeIgnored(e) || _Socket.Available >= 1)
            {
                numRetry++;
                if (numRetry >= UdpMaxRetryOnIgnoreError)
                {
                    throw;
                }
                await Task.Yield();
                goto LABEL_RETRY;
            }
        }

        Once DisposeFlag;
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (DisposeFlag.IsFirstCall() && disposing)
            {
                _Socket.DisposeSafe();

                Leak.DisposeSafe();
            }
        }

        public static bool CanUdpSocketErrorBeIgnored(SocketException e)
        {
            switch (e.SocketErrorCode)
            {
                case SocketError.ConnectionReset:
                case SocketError.NetworkReset:
                case SocketError.MessageSize:
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                case SocketError.NoBufferSpaceAvailable:
                case SocketError.AddressNotAvailable:
                case SocketError.ConnectionRefused:
                case SocketError.Interrupted:
                case SocketError.WouldBlock:
                case SocketError.TryAgain:
                case SocketError.InProgress:
                case SocketError.InvalidArgument:
                case (SocketError)12: // ENOMEM
                case (SocketError)10068: // WSAEUSERS
                    return true;
            }
            return false;
        }
    }

    class FastStreamToPalNetworkStream : NetworkStream, IFastStream
    {
        private FastStreamToPalNetworkStream() : base(null) { }
        FastStream FastStream;

        bool DisposeObject = false;

        private void _InternalInit(FastStream fastStream, bool disposeObject)
        {
            FastStream = fastStream;
            DisposeObject = disposeObject;

            ReadTimeout = Timeout.Infinite;
            WriteTimeout = Timeout.Infinite;
        }

        public static FastStreamToPalNetworkStream CreateFromFastStream(FastStream fastStream, bool disposeObject = false)
        {
            FastStreamToPalNetworkStream ret = Util.NewWithoutConstructor<FastStreamToPalNetworkStream>();

            ret._InternalInit(fastStream, disposeObject);

            return ret;
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            if (DisposeFlag.IsFirstCall() && disposing)
            {
                if (this.DisposeObject)
                {
                    FastStream.DisposeSafe();
                }
            }
            base.Dispose(disposing);
        }


        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotImplementedException();

        public override bool CanTimeout => true;
        public override int ReadTimeout { get => FastStream.ReadTimeout; set => FastStream.ReadTimeout = value; }
        public override int WriteTimeout { get => FastStream.WriteTimeout; set => FastStream.WriteTimeout = value; }

        public override bool DataAvailable => FastStream.DataAvailable;

        public override void Flush() => FastStream.FlushAsync().Wait();

        public override Task FlushAsync(CancellationToken cancellationToken = default) => FastStream.FlushAsync(cancellationToken);

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            => await FastStream.WriteAsync(buffer.AsReadOnlyMemory(offset, count), cancellationToken);

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
            => await FastStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, default).Wait();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, default).Result;

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            => ReadAsync(buffer, offset, count, default).AsApm(callback, state);
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            => WriteAsync(buffer, offset, count, default).AsApm(callback, state);
        public override int EndRead(IAsyncResult asyncResult) => ((Task<int>)asyncResult).Result;
        public override void EndWrite(IAsyncResult asyncResult) => ((Task)asyncResult).Wait();

        public override bool Equals(object obj) => object.Equals(this, obj);
        public override int GetHashCode() => 0;
        public override string ToString() => "FastStreamToPalNetworkStream";
        public override object InitializeLifetimeService() => base.InitializeLifetimeService();
        public override void Close() => Dispose(true);

        public override void CopyTo(Stream destination, int bufferSize)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int count;
                while ((count = this.Read(array, 0, array.Length)) != 0)
                {
                    destination.Write(array, 0, count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array, false);
            }
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                for (; ; )
                {
                    int num = await this.ReadAsync(new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    int num2 = num;
                    if (num2 == 0)
                    {
                        break;
                    }
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, num2), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, false);
            }
        }

        [Obsolete]
        protected override WaitHandle CreateWaitHandle() => new ManualResetEvent(false);

        [Obsolete]
        protected override void ObjectInvariant() { }

        public override int Read(Span<byte> buffer)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
            int result;
            try
            {
                int num = this.Read(array, 0, buffer.Length);
                if ((ulong)num > (ulong)((long)buffer.Length))
                {
                    throw new IOException("StreamTooLong");
                }
                new Span<byte>(array, 0, num).CopyTo(buffer);
                result = num;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array, false);
            }
            return result;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => await FastStream.ReadAsync(buffer, cancellationToken);

        public override int ReadByte()
        {
            byte[] array = new byte[1];
            if (this.Read(array, 0, 1) == 0)
            {
                return -1;
            }
            return (int)array[0];
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(array);
                this.Write(array, 0, buffer.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array, false);
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => await FastStream.WriteAsync(buffer, cancellationToken);

        public override void WriteByte(byte value)
            => this.Write(new byte[] { value }, 0, 1);
    }

    class PalStream : FastStream
    {
        protected Stream NativeStream;
        protected NetworkStream NativeNetworkStream;

        public bool IsNetworkStream => (NativeNetworkStream != null);

        public PalStream(Stream nativeStream)
        {
            NativeStream = nativeStream;

            NativeNetworkStream = NativeStream as NetworkStream;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
            => NativeStream.ReadAsync(buffer, cancel);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
            => NativeStream.WriteAsync(buffer, cancel);

        Once DisposeFlag;

        public override int ReadTimeout { get => NativeStream.ReadTimeout; set => NativeStream.ReadTimeout = value; }
        public override int WriteTimeout { get => NativeStream.WriteTimeout; set => NativeStream.WriteTimeout = value; }

        public override bool DataAvailable => NativeNetworkStream?.DataAvailable ?? true;

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                NativeStream.DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }

        public override Task FlushAsync(CancellationToken cancel = default) => NativeStream.FlushAsync(cancel);
    }

    class PalSslClientAuthenticationOptions
    {
        public delegate bool ValidateRemoteCertificateCallback(PalX509Certificate cert);

        public PalSslClientAuthenticationOptions() { }

        public string TargetHost { get; set; }
        public ValidateRemoteCertificateCallback ValidateRemoteCertificateProc { get; set; }
        public PalX509Certificate ClientCertificate { get; set; }

        public SslClientAuthenticationOptions GetNativeOptions()
        {
            SslClientAuthenticationOptions ret = new SslClientAuthenticationOptions()
            {
                TargetHost = TargetHost,
                AllowRenegotiation = true,
                RemoteCertificateValidationCallback = new RemoteCertificateValidationCallback((sender, cert, chain, err) => ValidateRemoteCertificateProc(new PalX509Certificate(cert))),
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            };

            if (this.ClientCertificate != null)
                ret.ClientCertificates.Add(this.ClientCertificate.NativeCertificate);

            return ret;
        }
    }

    class PalSslStream : PalStream
    {
        SslStream ssl;
        public PalSslStream(FastStream innerStream) : base(new SslStream(innerStream.GetPalNetworkStream(), true))
        {
            ssl = (SslStream)NativeStream;
        }

        public Task AuthenticateAsClientAsync(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken)
            => ssl.AuthenticateAsClientAsync(sslClientAuthenticationOptions.GetNativeOptions(), cancellationToken);

        public string SslProtocol => ssl.SslProtocol.ToString();
        public string CipherAlgorithm => ssl.CipherAlgorithm.ToString();
        public int CipherStrength => ssl.CipherStrength;
        public string HashAlgorithm => ssl.HashAlgorithm.ToString();
        public int HashStrength => ssl.HashStrength;
        public string KeyExchangeAlgorithm => ssl.KeyExchangeAlgorithm.ToString();
        public int KeyExchangeStrength => ssl.KeyExchangeStrength;
        public PalX509Certificate LocalCertificate => new PalX509Certificate(ssl.LocalCertificate);
        public PalX509Certificate RemoteCertificate => new PalX509Certificate(ssl.RemoteCertificate);
    }

    static class PalDns
    {
        public static Task<IPAddress[]> GetHostAddressesAsync(string hostNameOrAddress, int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => TaskUtil.DoAsyncWithTimeout(c => Dns.GetHostAddressesAsync(hostNameOrAddress),
                timeout: timeout, cancel: cancel);

        public static Task<IPHostEntry> GetHostEntryAsync(string hostNameOrAddress, int timeout = Timeout.Infinite, CancellationToken cancel = default)
            => TaskUtil.DoAsyncWithTimeout(c => Dns.GetHostEntryAsync(hostNameOrAddress),
                timeout: timeout, cancel: cancel);
    }

    class PalHostNetInfo : BackgroundStateDataBase
    {
        public override BackgroundStateDataUpdatePolicy DataUpdatePolicy =>
            new BackgroundStateDataUpdatePolicy(300, 6000, 2000);

        public string HostName;
        public string DomainName;
        public string FqdnHostName => HostName + (string.IsNullOrEmpty(DomainName) ? "" : "." + DomainName);
        public bool IsIPv4Supported;
        public bool IsIPv6Supported;
        public List<IPAddress> IPAddressList = new List<IPAddress>();

        public static bool IsUnix { get; } = (Environment.OSVersion.Platform != PlatformID.Win32NT);

        static IPAddress[] GetLocalIPAddressBySocketApi() => PalDns.GetHostAddressesAsync(Dns.GetHostName()).Result;

        class ByteComparer : IComparer<byte[]>
        {
            public int Compare(byte[] x, byte[] y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
        }

        public PalHostNetInfo()
        {
            IPGlobalProperties prop = IPGlobalProperties.GetIPGlobalProperties();
            this.HostName = prop.HostName;
            this.DomainName = prop.DomainName;
            HashSet<IPAddress> hash = new HashSet<IPAddress>();

            if (IsUnix)
            {
                UnicastIPAddressInformationCollection info = prop.GetUnicastAddresses();
                foreach (UnicastIPAddressInformation ip in info)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork || ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        hash.Add(ip.Address);
                }
            }
            else
            {
                try
                {
                    IPAddress[] info = GetLocalIPAddressBySocketApi();
                    if (info.Length >= 1)
                    {
                        foreach (IPAddress ip in info)
                        {
                            if (ip.AddressFamily == AddressFamily.InterNetwork || ip.AddressFamily == AddressFamily.InterNetworkV6)
                                hash.Add(ip);
                        }
                    }
                }
                catch { }
            }

            if (PalSocket.OSSupportsIPv4)
            {
                this.IsIPv4Supported = true;
                hash.Add(IPAddress.Any);
                hash.Add(IPAddress.Loopback);
            }
            if (PalSocket.OSSupportsIPv6)
            {
                this.IsIPv6Supported = true;
                hash.Add(IPAddress.IPv6Any);
                hash.Add(IPAddress.IPv6Loopback);
            }

            try
            {
                var cmp = new ByteComparer();
                this.IPAddressList = hash.OrderBy(x => x.AddressFamily)
                    .ThenBy(x => x.GetAddressBytes(), cmp)
                    .ThenBy(x => (x.AddressFamily == AddressFamily.InterNetworkV6 ? x.ScopeId : 0))
                    .ToList();
            }
            catch { }
        }

        public Memory<byte> IPAddressListBinary
        {
            get
            {
                FastMemoryBuffer<byte> ret = new FastMemoryBuffer<byte>();
                foreach (IPAddress addr in IPAddressList)
                {
                    ret.WriteSInt32((int)addr.AddressFamily);
                    ret.Write(addr.GetAddressBytes());
                    if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                        ret.WriteSInt64(addr.ScopeId);
                }
                return ret;
            }
        }

        public override bool Equals(BackgroundStateDataBase otherArg)
        {
            PalHostNetInfo other = otherArg as PalHostNetInfo;
            if (string.Equals(this.HostName, other.HostName) == false) return false;
            if (string.Equals(this.DomainName, other.DomainName) == false) return false;
            if (this.IsIPv4Supported != other.IsIPv4Supported) return false;
            if (this.IsIPv6Supported != other.IsIPv6Supported) return false;
            if (this.IPAddressListBinary.Span.SequenceEqual(other.IPAddressListBinary.Span) == false) return false;
            return true;
        }

        Action callMeCache = null;

        public override void RegisterSystemStateChangeNotificationCallbackOnlyOnce(Action callMe)
        {
            callMeCache = callMe;

            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
        }

        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
        {
            callMeCache();

            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
        }

        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            callMeCache();

            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
        }

        public static IPAddress GetLocalIPForDestinationHost(IPAddress dest)
        {
            try
            {
                using (PalSocket sock = new PalSocket(dest.AddressFamily, SocketType.Dgram, ProtocolType.IP))
                {
                    sock.Connect(dest, 65530);
                    IPEndPoint ep = sock.LocalEndPoint.Value as IPEndPoint;
                    return ep.Address;
                }
            }
            catch { }

            using (PalSocket sock = new PalSocket(dest.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                sock.Connect(dest, 65531);
                IPEndPoint ep = sock.LocalEndPoint.Value as IPEndPoint;
                return ep.Address;
            }
        }

        public static async Task<IPAddress> GetLocalIPv4ForInternetAsync()
        {
            try
            {
                return GetLocalIPForDestinationHost(IPAddress.Parse("8.8.8.8"));
            }
            catch { }

            try
            {
                using (PalSocket sock = new PalSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    var hostent = await PalDns.GetHostEntryAsync("www.msftncsi.com");
                    var addr = hostent.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).First();
                    await sock.ConnectAsync(addr, 443);
                    IPEndPoint ep = sock.LocalEndPoint.Value as IPEndPoint;
                    return ep.Address;
                }
            }
            catch { }

            try
            {
                using (PalSocket sock = new PalSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    var hostent = await PalDns.GetHostEntryAsync("www.msftncsi.com");
                    var addr = hostent.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).First();
                    await sock.ConnectAsync(addr, 80);
                    IPEndPoint ep = sock.LocalEndPoint.Value as IPEndPoint;
                    return ep.Address;
                }
            }
            catch { }

            try
            {
                return BackgroundState<PalHostNetInfo>.Current.Data.IPAddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork)
                    .Where(x => IPAddress.IsLoopback(x) == false).Where(x => x != IPAddress.Any).First();
            }
            catch { }

            return IPAddress.Any;
        }

    }
}


