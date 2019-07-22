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
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
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
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal;
using System.Diagnostics;
using System.IO.Pipelines;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class KestrelWithStackSettings
        {
            public static readonly Copenhagen<int> MaxLogStringLen = 1024;
        }
    }

    public class KestrelStackTransport : ITransport
    {
        // From Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketTransport

        readonly IEndPointInformation EndPointInformation;
        readonly IConnectionDispatcher Dispatcher;
        readonly IApplicationLifetime AppLifetime;
        readonly ISocketsTrace Trace;

        readonly PipeScheduler PipeScheduler = PipeScheduler.ThreadPool;

        public KestrelServerWithStack Server { get; }

        public KestrelStackTransport(
            KestrelServerWithStack server,
            IEndPointInformation endPointInformation,
            IConnectionDispatcher dispatcher,
            IApplicationLifetime applicationLifetime,
            int ioQueueCount,
            ISocketsTrace trace)
        {
            Debug.Assert(endPointInformation != null);
            Debug.Assert(endPointInformation.Type == ListenType.IPEndPoint);
            Debug.Assert(dispatcher != null);
            Debug.Assert(applicationLifetime != null);
            Debug.Assert(trace != null);

            EndPointInformation = endPointInformation;
            Dispatcher = dispatcher;
            AppLifetime = applicationLifetime;
            Trace = trace;

            this.Server = server;
        }

        NetTcpListener Listener = null;

        public Task BindAsync()
        {
            if (Listener != null)
                throw new ApplicationException("Listener is already bound.");

            this.Listener = this.Server.Options.TcpIp.CreateListener(new TcpListenParam(ListenerAcceptNewSocketCallback, EndPointInformation.IPEndPoint.Port));

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public async Task UnbindAsync()
        {
            try
            {
                if (Listener != null)
                {
                    await Listener._CleanupSafeAsync();
                    Listener._DisposeSafe();
                    Listener = null;
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
                throw;
            }
        }

        async Task ListenerAcceptNewSocketCallback(NetTcpListenerPort listener, ConnSock newSock)
        {
            using (var connection = new KestrelStackConnection(newSock, this.PipeScheduler))
            {
                // Note:
                // In ASP.NET Core 2.2 or higher, Dispatcher.OnConnection() will return Task.
                // Otherwise, Dispatcher.OnConnection() will return void.
                // Then we need to use the reflection to call the OnConnection() method indirectly.
                Task middlewareTask = Dispatcher._PrivateInvoke("OnConnection", connection) as Task;

                // Wait for transport to end
                await connection.StartAsync();

                // Wait for middleware to end
                if (middlewareTask != null)
                    await middlewareTask;

                connection._DisposeSafe();
            }
        }
    }

    // From Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal.SocketConnection
    public class KestrelStackConnection : TransportConnection, IDisposable
    {
        readonly ConnSock Sock;
        readonly PipeScheduler Scheduler;

        public KestrelStackConnection(ConnSock sock, PipeScheduler scheduler)
        {
            this.Sock = sock;
            this.Scheduler = scheduler;

            LocalAddress = sock.Info.Ip.LocalIPAddress;
            LocalPort = sock.Info.Tcp.LocalPort;

            RemoteAddress = sock.Info.Ip.RemoteIPAddress;
            RemotePort = sock.Info.Tcp.RemotePort;

            this.ConnectionClosed = this.Sock.GrandCancel;
        }

        public async Task StartAsync()
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

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            // Here
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

    public class KestrelStackTransportFactory : ITransportFactory
    {
        // From Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketTransportFactory
        readonly SocketTransportOptions Options;
        readonly IApplicationLifetime AppLifeTime;
        readonly SocketsTrace Trace;

        public KestrelServerWithStack Server { get; private set; }

        public KestrelStackTransportFactory(
            IOptions<SocketTransportOptions> options,
            IApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (applicationLifetime == null)
                throw new ArgumentNullException(nameof(applicationLifetime));

            if (loggerFactory == null)
                throw new ArgumentNullException(nameof(loggerFactory));

            Options = options.Value;
            AppLifeTime = applicationLifetime;
            var logger = loggerFactory.CreateLogger("Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets");
            Trace = new SocketsTrace(logger);
        }

        public ITransport Create(IEndPointInformation endPointInformation, IConnectionDispatcher dispatcher)
        {
            if (endPointInformation == null)
                throw new ArgumentNullException(nameof(endPointInformation));

            if (endPointInformation.Type != ListenType.IPEndPoint)
                throw new ArgumentException("OnlyIPEndPointsSupported");

            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));

            return new KestrelStackTransport(this.Server, endPointInformation, dispatcher, AppLifeTime, Options.IOQueueCount, Trace);
        }

        public void SetServer(KestrelServerWithStack server)
        {
            this.Server = server;
        }
    }

    public class KestrelServerWithStackOptions : KestrelServerOptions
    {
        public TcpIpSystem TcpIp = LocalNet;
    }

    public class KestrelServerWithStack : KestrelServer
    {
        public new KestrelServerWithStackOptions Options => (KestrelServerWithStackOptions)base.Options;

        public KestrelServerWithStack(IOptions<KestrelServerWithStackOptions> options, ITransportFactory transportFactory, ILoggerFactory loggerFactory)
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
                services.TryAddSingleton<ITransportFactory, KestrelStackTransportFactory>();

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

