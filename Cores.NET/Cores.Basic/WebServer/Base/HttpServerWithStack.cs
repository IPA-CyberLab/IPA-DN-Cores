﻿// IPA Cores.NET
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

#if CORES_BASIC_WEBSERVER

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal;
//using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
//using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Buffers;
using Microsoft.Extensions.Options;
//using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal;
using System.Diagnostics;
using System.IO.Pipelines;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Connections.Features;
using System.Collections;


// Some codes are copied from  https://github.com/aspnet/AspNetCore/tree/7795537181bf5fd4f4855a7846bee29c6a0fcc1c
// 
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0.
// License: https://github.com/aspnet/AspNetCore/blob/7795537181bf5fd4f4855a7846bee29c6a0fcc1c/LICENSE.txt

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class KestrelWithStackSettings
        {
            public static readonly Copenhagen<int> MaxLogStringLen = 1024;
        }
    }

    public sealed class KestrelStackConnectionListener : IConnectionListener
    {
        // From Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketConnectionListener, git\AspNetCore\src\Servers\Kestrel\Transport.Sockets\src\SocketConnectionListener.cs
        public KestrelServerWithStack Server { get; }
        public EndPoint EndPoint { get; private set; }

        private readonly SocketTransportOptions _options;

        public KestrelStackConnectionListener(KestrelServerWithStack server,  EndPoint endpoint, SocketTransportOptions options)
        {
            this.Server = server;
            this.EndPoint = endpoint;
            this._options = options;
        }

        NetTcpListener Listener = null;

        public void Bind()
        {
            if (Listener != null)
                throw new ApplicationException("Listener is already bound.");

            this.Listener = this.Server.Options.TcpIp.CreateListener(new TcpListenParam(compatibleWithKestrel: EnsureSpecial.Yes, null, (IPEndPoint)EndPoint));
        }

        public async ValueTask<ConnectionContext> AcceptAsync(CancellationToken cancellationToken = default)
        {
            ConnSock sock = await this.Listener.AcceptNextSocketFromQueueUtilAsync(cancellationToken);

            var connection = new KestrelStackConnection(sock);

            connection.Start();

            return connection;
        }

        public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
        {
            Listener._DisposeSafe();
            return default;
        }

        public ValueTask DisposeAsync()
        {
            Listener._DisposeSafe();
            return default;
        }
    }

    // Pure copy from: git\AspNetCore\src\Servers\Kestrel\shared\TransportConnection.FeatureCollection.cs
    public partial class TransportConnection : IConnectionIdFeature,
                                                 IConnectionTransportFeature,
                                                 IConnectionItemsFeature,
                                                 IMemoryPoolFeature,
                                                 IConnectionLifetimeFeature
    {
        // NOTE: When feature interfaces are added to or removed from this TransportConnection class implementation,
        // then the list of `features` in the generated code project MUST also be updated.
        // See also: tools/CodeGenerator/TransportConnectionFeatureCollection.cs

        MemoryPool<byte> IMemoryPoolFeature.MemoryPool => MemoryPool;

        IDuplexPipe IConnectionTransportFeature.Transport
        {
            get => Transport;
            set => Transport = value;
        }

        IDictionary<object, object> IConnectionItemsFeature.Items
        {
            get => Items;
            set => Items = value;
        }

        CancellationToken IConnectionLifetimeFeature.ConnectionClosed
        {
            get => ConnectionClosed;
            set => ConnectionClosed = value;
        }

        void IConnectionLifetimeFeature.Abort() => Abort(new ConnectionAbortedException("The connection was aborted by the application via IConnectionLifetimeFeature.Abort()."));
    }

    // Pure copy from: git\AspNetCore\src\Servers\Kestrel\shared\TransportConnection.Generated.cs
    public partial class TransportConnection : IFeatureCollection
    {
        private static readonly Type IConnectionIdFeatureType = typeof(IConnectionIdFeature);
        private static readonly Type IConnectionTransportFeatureType = typeof(IConnectionTransportFeature);
        private static readonly Type IConnectionItemsFeatureType = typeof(IConnectionItemsFeature);
        private static readonly Type IMemoryPoolFeatureType = typeof(IMemoryPoolFeature);
        private static readonly Type IConnectionLifetimeFeatureType = typeof(IConnectionLifetimeFeature);

        private object _currentIConnectionIdFeature;
        private object _currentIConnectionTransportFeature;
        private object _currentIConnectionItemsFeature;
        private object _currentIMemoryPoolFeature;
        private object _currentIConnectionLifetimeFeature;

        private int _featureRevision;

        private List<KeyValuePair<Type, object>> MaybeExtra;

        private void FastReset()
        {
            _currentIConnectionIdFeature = this;
            _currentIConnectionTransportFeature = this;
            _currentIConnectionItemsFeature = this;
            _currentIMemoryPoolFeature = this;
            _currentIConnectionLifetimeFeature = this;

        }

        // Internal for testing
        internal void ResetFeatureCollection()
        {
            FastReset();
            MaybeExtra?.Clear();
            _featureRevision++;
        }

        private object ExtraFeatureGet(Type key)
        {
            if (MaybeExtra == null)
            {
                return null;
            }
            for (var i = 0; i < MaybeExtra.Count; i++)
            {
                var kv = MaybeExtra[i];
                if (kv.Key == key)
                {
                    return kv.Value;
                }
            }
            return null;
        }

        private void ExtraFeatureSet(Type key, object value)
        {
            if (MaybeExtra == null)
            {
                MaybeExtra = new List<KeyValuePair<Type, object>>(2);
            }

            for (var i = 0; i < MaybeExtra.Count; i++)
            {
                if (MaybeExtra[i].Key == key)
                {
                    MaybeExtra[i] = new KeyValuePair<Type, object>(key, value);
                    return;
                }
            }
            MaybeExtra.Add(new KeyValuePair<Type, object>(key, value));
        }

        bool IFeatureCollection.IsReadOnly => false;

        int IFeatureCollection.Revision => _featureRevision;

        object IFeatureCollection.this[Type key]
        {
            get
            {
                object feature = null;
                if (key == IConnectionIdFeatureType)
                {
                    feature = _currentIConnectionIdFeature;
                }
                else if (key == IConnectionTransportFeatureType)
                {
                    feature = _currentIConnectionTransportFeature;
                }
                else if (key == IConnectionItemsFeatureType)
                {
                    feature = _currentIConnectionItemsFeature;
                }
                else if (key == IMemoryPoolFeatureType)
                {
                    feature = _currentIMemoryPoolFeature;
                }
                else if (key == IConnectionLifetimeFeatureType)
                {
                    feature = _currentIConnectionLifetimeFeature;
                }
                else if (MaybeExtra != null)
                {
                    feature = ExtraFeatureGet(key);
                }

                return feature;
            }

            set
            {
                _featureRevision++;

                if (key == IConnectionIdFeatureType)
                {
                    _currentIConnectionIdFeature = value;
                }
                else if (key == IConnectionTransportFeatureType)
                {
                    _currentIConnectionTransportFeature = value;
                }
                else if (key == IConnectionItemsFeatureType)
                {
                    _currentIConnectionItemsFeature = value;
                }
                else if (key == IMemoryPoolFeatureType)
                {
                    _currentIMemoryPoolFeature = value;
                }
                else if (key == IConnectionLifetimeFeatureType)
                {
                    _currentIConnectionLifetimeFeature = value;
                }
                else
                {
                    ExtraFeatureSet(key, value);
                }
            }
        }

        TFeature IFeatureCollection.Get<TFeature>()
        {
            TFeature feature = default;
            if (typeof(TFeature) == typeof(IConnectionIdFeature))
            {
                feature = (TFeature)_currentIConnectionIdFeature;
            }
            else if (typeof(TFeature) == typeof(IConnectionTransportFeature))
            {
                feature = (TFeature)_currentIConnectionTransportFeature;
            }
            else if (typeof(TFeature) == typeof(IConnectionItemsFeature))
            {
                feature = (TFeature)_currentIConnectionItemsFeature;
            }
            else if (typeof(TFeature) == typeof(IMemoryPoolFeature))
            {
                feature = (TFeature)_currentIMemoryPoolFeature;
            }
            else if (typeof(TFeature) == typeof(IConnectionLifetimeFeature))
            {
                feature = (TFeature)_currentIConnectionLifetimeFeature;
            }
            else if (MaybeExtra != null)
            {
                feature = (TFeature)(ExtraFeatureGet(typeof(TFeature)));
            }

            return feature;
        }

        void IFeatureCollection.Set<TFeature>(TFeature feature)
        {
            _featureRevision++;
            if (typeof(TFeature) == typeof(IConnectionIdFeature))
            {
                _currentIConnectionIdFeature = feature;
            }
            else if (typeof(TFeature) == typeof(IConnectionTransportFeature))
            {
                _currentIConnectionTransportFeature = feature;
            }
            else if (typeof(TFeature) == typeof(IConnectionItemsFeature))
            {
                _currentIConnectionItemsFeature = feature;
            }
            else if (typeof(TFeature) == typeof(IMemoryPoolFeature))
            {
                _currentIMemoryPoolFeature = feature;
            }
            else if (typeof(TFeature) == typeof(IConnectionLifetimeFeature))
            {
                _currentIConnectionLifetimeFeature = feature;
            }
            else
            {
                ExtraFeatureSet(typeof(TFeature), feature);
            }
        }

        private IEnumerable<KeyValuePair<Type, object>> FastEnumerable()
        {
            if (_currentIConnectionIdFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IConnectionIdFeatureType, _currentIConnectionIdFeature);
            }
            if (_currentIConnectionTransportFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IConnectionTransportFeatureType, _currentIConnectionTransportFeature);
            }
            if (_currentIConnectionItemsFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IConnectionItemsFeatureType, _currentIConnectionItemsFeature);
            }
            if (_currentIMemoryPoolFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IMemoryPoolFeatureType, _currentIMemoryPoolFeature);
            }
            if (_currentIConnectionLifetimeFeature != null)
            {
                yield return new KeyValuePair<Type, object>(IConnectionLifetimeFeatureType, _currentIConnectionLifetimeFeature);
            }

            if (MaybeExtra != null)
            {
                foreach (var item in MaybeExtra)
                {
                    yield return item;
                }
            }
        }

        IEnumerator<KeyValuePair<Type, object>> IEnumerable<KeyValuePair<Type, object>>.GetEnumerator() => FastEnumerable().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => FastEnumerable().GetEnumerator();
    }

    // Pure copy from: git\AspNetCore\src\Servers\Kestrel\shared\TransportConnection.cs
    public abstract partial class TransportConnection : ConnectionContext
    {
        private IDictionary<object, object> _items;
        private string _connectionId;

        public TransportConnection()
        {
            FastReset();
        }

        public override EndPoint LocalEndPoint { get; set; }
        public override EndPoint RemoteEndPoint { get; set; }

        public override string ConnectionId
        {
            get
            {
                if (_connectionId == null)
                {
                    _connectionId = Str.NewGuid();
                }

                return _connectionId;
            }
            set
            {
                _connectionId = value;
            }
        }

        public override IFeatureCollection Features => this;

        public virtual MemoryPool<byte> MemoryPool { get; }

        public override IDuplexPipe Transport { get; set; }

        public IDuplexPipe Application { get; set; }

        public override IDictionary<object, object> Items
        {
            get
            {
                // Lazily allocate connection metadata
                return _items ?? (_items = new ConnectionItems());
            }
            set
            {
                _items = value;
            }
        }

        public override CancellationToken ConnectionClosed { get; set; }

        // DO NOT remove this override to ConnectionContext.Abort. Doing so would cause
        // any TransportConnection that does not override Abort or calls base.Abort
        // to stack overflow when IConnectionLifetimeFeature.Abort() is called.
        // That said, all derived types should override this method should override
        // this implementation of Abort because canceling pending output reads is not
        // sufficient to abort the connection if there is backpressure.
        public override void Abort(ConnectionAbortedException abortReason)
        {
            Application.Input.CancelPendingRead();
        }
    }

    // From Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal, git\AspNetCore\src\Servers\Kestrel\Transport.Sockets\src\Internal\SocketConnection.cs
    public class KestrelStackConnection : TransportConnection
    {
        readonly ConnSock Sock;
        Task _processingTask;

        public override MemoryPool<byte> MemoryPool { get; }

        public KestrelStackConnection(ConnSock sock)
        {
            this.Sock = sock;

            this.LocalEndPoint = new IPEndPoint(sock.Info.Ip.LocalIPAddress, sock.Info.Tcp.LocalPort);
            this.RemoteEndPoint = new IPEndPoint(sock.Info.Ip.RemoteIPAddress, sock.Info.Tcp.RemotePort);

            this.ConnectionClosed = this.Sock.GrandCancel;

            this.MemoryPool = MemoryPool<byte>.Shared;

            var inputOptions = new PipeOptions();
            var outputOptions = new PipeOptions();

            var pair = PipelineDuplex.CreateConnectionPair(inputOptions, outputOptions);

            // Set the transport and connection id
            Transport = pair.Transport;
            Application = pair.Application;
        }

        public void Start()
        {
            _processingTask = StartAsync();
        }

        async Task StartAsync()
        {
            try
            {
                using (var wrapper = new PipePointDuplexPipeWrapper(this.Sock.UpperPoint, this.Application))
                {
                    // Now wait for complete
                    await wrapper.MainLoopToWaitComplete;
                }
            }
            catch (Exception ex)
            {
                // Stop the socket (for just in case)
                Sock._CancelSafe(new DisconnectedException());

                ex._Debug();
            }
        }

        // Only called after connection middleware is complete which means the ConnectionClosed token has fired.
        public override async ValueTask DisposeAsync()
        {
            Transport.Input.Complete();
            Transport.Output.Complete();

            if (_processingTask != null)
            {
                await _processingTask;
            }
        }

        public override void Abort()
        {
            this.Sock._CancelSafe();
            base.Abort();
        }

        public override void Abort(ConnectionAbortedException abortReason)
        {
            this.Sock._CancelSafe(abortReason);
            base.Abort(abortReason);
        }
    }


    public class KestrelStackTransportFactory : IConnectionListenerFactory
    {
        // From Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketTransportFactory, git\AspNetCore\src\Servers\Kestrel\Transport.Sockets\src\SocketTransportFactory.cs
        readonly SocketTransportOptions Options;
        //readonly IApplicationLifetime AppLifeTime;
        //readonly SocketsTrace Trace;

        public KestrelServerWithStack Server { get; private set; }

        public KestrelStackTransportFactory(
            IOptions<SocketTransportOptions> options,
            //IApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            //if (applicationLifetime == null)
            //    throw new ArgumentNullException(nameof(applicationLifetime));

            if (loggerFactory == null)
                throw new ArgumentNullException(nameof(loggerFactory));

            Options = options.Value;
            //AppLifeTime = applicationLifetime;
            //var logger = loggerFactory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets");
            //Trace = new SocketsTrace(logger);
        }

        public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
        {
            //if (endPointInformation == null)
            //    throw new ArgumentNullException(nameof(endPointInformation));

            //if (endPointInformation.Type != ListenType.IPEndPoint)
            //    throw new ArgumentException("OnlyIPEndPointsSupported");

            //if (dispatcher == null)
            //    throw new ArgumentNullException(nameof(dispatcher));

            //return new KestrelStackTransport(this.Server, endPointInformation, dispatcher, AppLifeTime, Options.IOQueueCount, Trace);

            var transport = new KestrelStackConnectionListener(this.Server, endpoint, Options);
            transport.Bind();
            return new ValueTask<IConnectionListener>(transport);
        }

        public void SetServer(KestrelServerWithStack server)
        {
            this.Server = server;
        }
    }

    // Pure copy from git\AspNetCore\src\Servers\Kestrel\Core\src\Internal\KestrelServerOptionsSetup.cs
    public class KestrelServerOptionsSetup : IConfigureOptions<KestrelServerOptions>
    {
        private IServiceProvider _services;

        public KestrelServerOptionsSetup(IServiceProvider services)
        {
            _services = services;
        }

        public void Configure(KestrelServerOptions options)
        {
            options.ApplicationServices = _services;
        }
    }

    public class KestrelServerWithStackOptions : KestrelServerOptions
    {
        public TcpIpSystem TcpIp = LocalNet;
    }

    public class KestrelServerWithStack : KestrelServer
    {
        public new KestrelServerWithStackOptions Options => (KestrelServerWithStackOptions)base.Options;

        public KestrelServerWithStack(IOptions<KestrelServerWithStackOptions> options, IConnectionListenerFactory transportFactory, ILoggerFactory loggerFactory)
            : base(options, transportFactory, loggerFactory)
        {
            KestrelStackTransportFactory factory = (KestrelStackTransportFactory)transportFactory;

            factory.SetServer(this);
        }
    }

    public static class HttpServerWithStackHelper
    {
        public static IWebHostBuilder UseKestrelWithStack(this IWebHostBuilder hostBuilder, Action<KestrelServerWithStackOptions> options)
        {
            return hostBuilder.UseKestrelWithStack().ConfigureKestrelWithStack(options);
        }

        public static IWebHostBuilder UseKestrelWithStack(this IWebHostBuilder hostBuilder)
        {
            // From Microsoft.AspNetCore.Hosting.WebHostBuilderKestrelExtensions
            return hostBuilder.ConfigureServices(services =>
            {
                // Don't override an already-configured transport
                services.TryAddSingleton<IConnectionListenerFactory, KestrelStackTransportFactory>();

                services.AddTransient<IConfigureOptions<KestrelServerWithStackOptions>, KestrelServerOptionsSetup>();
                services.AddSingleton<IServer, KestrelServerWithStack>();
            });
        }

        public static IWebHostBuilder ConfigureKestrelWithStack(this IWebHostBuilder hostBuilder, Action<KestrelServerWithStackOptions> options)
        {
            return hostBuilder.ConfigureServices(services =>
            {
                services.Configure(options);
            });
        }

        public static IApplicationBuilder UseWebServerLogger(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                int httpResponseCode = 0;

                long tickStart = 0;
                long tickEnd = 0;

                try
                {
                    tickStart = Time.Tick64;
                    await next.Invoke();
                    tickEnd = Time.Tick64;

                    HttpResponse response = context.Response;

                    httpResponseCode = response.StatusCode;
                }
                catch
                {
                    httpResponseCode = -1;
                    tickEnd = Time.Tick64;
                }

                Exception exception = HttpExceptionLoggerMiddleware.GetSavedExceptionFromContext(context);

                int processTime = (int)(tickEnd - tickStart);

                HttpRequest req = context.Request;
                ConnectionInfo conn = context.Connection;
                string username = null;
                string authtype = null;

                if (context.User?.Identity?.IsAuthenticated ?? false)
                {
                    username = context.User?.Identity?.Name;
                    authtype = context.User?.Identity?.AuthenticationType;
                }

                int maxLen = CoresConfig.KestrelWithStackSettings.MaxLogStringLen;

                WebServerLogData log = new WebServerLogData()
                {
                    ConnectionId = conn.Id,
                    LocalIP = conn.LocalIpAddress.ToString(),
                    RemoteIP = conn.RemoteIpAddress.ToString(),
                    LocalPort = conn.LocalPort,
                    RemotePort = conn.RemotePort,

                    Protocol = req.Protocol,
                    Method = req.Method,
                    Host = req.Host.ToString()._TruncStrEx(maxLen),
                    Path = req.Path.ToString()._TruncStrEx(maxLen),
                    QueryString = req.QueryString.ToString()._TruncStrEx(maxLen),
                    Url = req.GetDisplayUrl()._TruncStrEx(maxLen),

                    AuthUserName = username._TruncStrEx(maxLen),
                    AuthType = authtype,

                    ProcessTimeMsecs = processTime,
                    ResponseCode = httpResponseCode,
                    Exception = exception?.ToString(),
                };

                log.AuthUserName = log.AuthUserName._NullIfEmpty();
                log.QueryString = log.QueryString._NullIfEmpty();

                if (context.Items.TryGetValue(BasicAuthMiddleware.BasicAuthResultItemName, out object basicAuthResultObj))
                {
                    if (basicAuthResultObj is ResultAndError<string> basicAuthRet)
                    {
                        log.BasicAuthResult = basicAuthRet.IsOk;
                        log.BasicAuthUserName = basicAuthRet.Value;
                    }
                }

                if (req.Headers.TryGetValue("User-Agent", out StringValues userAgentValue))
                    log.UserAgent = userAgentValue.ToString()._TruncStrEx(maxLen);

                log._PostAccessLog(LogTag.WebServer);

                if (exception != null)
                {
                    try
                    {
                        string msg = $"Web Server Exception on {log.Method} {log.Url}\r\n{exception.ToString()}";

                        msg._Debug();
                    }
                    catch { }
                }
            });
        }
    }
}

#endif // CORES_BASIC_WEBSERVER

