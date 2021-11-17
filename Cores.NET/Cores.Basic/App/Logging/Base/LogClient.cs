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

#if CORES_BASIC_JSON

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class LogProtocolSettings
    {
        public static readonly Copenhagen<int> DefaultDelay = 250;

        public static readonly Copenhagen<int> DefaultRetryIntervalMin = 1 * 1000;
        public static readonly Copenhagen<int> DefaultRetryIntervalMax = 15 * 1000;
    }
}

public class LogClientOptions
{
    public string ServerHostname { get; }
    public int ServerPort { get; }
    public TcpIpSystem TcpIp { get; }
    public PalSslClientAuthenticationOptions SslAuthOptions { get; }

    public readonly Copenhagen<int> SendKeepAliveInterval = CoresConfig.LogProtocolSettings.DefaultSendKeepAliveInterval.Value;
    public readonly Copenhagen<int> ClientMaxBufferSize = CoresConfig.LogProtocolSettings.BufferingSizeThresholdPerServer.Value;
    public readonly Copenhagen<int> Delay = CoresConfig.LogProtocolSettings.DefaultDelay.Value;
    public readonly Copenhagen<int> RecvTimeout = CoresConfig.LogProtocolSettings.DefaultRecvTimeout.Value;
    public readonly Copenhagen<int> RetryIntervalMin = CoresConfig.LogProtocolSettings.DefaultRetryIntervalMin.Value;
    public readonly Copenhagen<int> RetryIntervalMax = CoresConfig.LogProtocolSettings.DefaultRetryIntervalMax.Value;

    public LogClientOptions(TcpIpSystem? tcpIp, PalSslClientAuthenticationOptions sslAuthOptions, string serverHostname, int serverPort = Consts.Ports.LogServerDefaultServicePort)
    {
        this.ServerHostname = serverHostname._NonNullTrim();
        this.ServerPort = serverPort;
        this.TcpIp = tcpIp ?? LocalNet;
        this.SslAuthOptions = sslAuthOptions;

        if (this.SslAuthOptions.TargetHost._IsEmpty())
        {
            this.SslAuthOptions.TargetHost = this.ServerHostname;
        }
    }
}

public class LogClient : AsyncServiceWithMainLoop
{
    public LogClientOptions Options { get; }

    readonly PipePoint Reader;
    readonly PipePoint Writer;

    readonly Task MainProcTask;

    readonly AsyncAutoResetEvent EmptyEvent = new AsyncAutoResetEvent(true); // すべてのデータが送付されて空になるたびに発生する非同期イベント

    readonly AsyncAutoResetEvent FlushNowEvent = new AsyncAutoResetEvent(); // 今すぐ Flush するべき指示のイベント

    public const int ClientVersion = 1;

    public bool UnderConnectionError { get; private set; } = false;

    public LogClient(LogClientOptions options)
    {
        this.Options = options;

        this.Reader = PipePoint.NewDuplexPipeAndGetOneSide(PipePointSide.A_LowerSide, default, this.Options.ClientMaxBufferSize);
        this.Writer = this.Reader.CounterPart!;

        this.MainProcTask = this.StartMainLoop(MainLoopAsync);
    }

    public void WriteLog(LogJsonData data)
    {
        string str = data._ObjectToJson(compact: true);

        byte[] jsonBuf = str._GetBytes_UTF8();
        if (jsonBuf.Length > CoresConfig.LogProtocolSettings.MaxDataSize)
        {
            return;
        }

        MemoryBuffer<byte> buf = new MemoryBuffer<byte>(jsonBuf.Length + sizeof(int) * 2);
        buf.WriteSInt32((int)LogProtocolDataType.StandardLog);
        buf.WriteSInt32(jsonBuf.Length);
        buf.Write(jsonBuf);

        this.Writer.StreamWriter.NonStopWriteWithLock(buf, true, FastStreamNonStopWriteMode.DiscardWritingData, true);
    }

    public async Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        long threshold = 0;

        if (UnderConnectionError) return;

        if (halfFlush)
        {
            threshold = this.Reader.StreamReader.Threshold / 2;
        }

        while (this.Reader.StreamReader.Length > threshold)
        {
            if (UnderConnectionError) return;

            if (cancel.IsCancellationRequested) return;

            FlushNowEvent.Set(true);

            await EmptyEvent.WaitOneAsync(cancel: cancel);
        }
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        try
        {
            int numRetry = 0;
            await using (PipeStream reader = new PipeStream(this.Reader))
            {
                while (true)
                {
                    if (cancel.IsCancellationRequested) return;

                    try
                    {
                        UnderConnectionError = false;

                        await ConnectAndSendAsync(reader, cancel);
                    }
                    catch (Exception ex)
                    {
                        if (cancel.IsCancellationRequested) return;

                        UnderConnectionError = true;
                        EmptyEvent.Set(true);

                        numRetry++;

                        int nextRetryInterval = Util.GenRandIntervalWithRetry(Options.RetryIntervalMin, numRetry, Options.RetryIntervalMax);

                        Con.WriteError($"LogClient: Error (numRetry = {numRetry}, nextWait = {nextRetryInterval}): {ex.ToString()}");

                        await cancel._WaitUntilCanceledAsync(nextRetryInterval);
                    }
                }
            }
        }
        finally
        {
            EmptyEvent.Set(true);
            UnderConnectionError = true;
        }
    }

    async Task ConnectAndSendAsync(PipeStream reader, CancellationToken cancel)
    {
        await using (ConnSock sock = await Options.TcpIp.ConnectIPv4v6DualAsync(new TcpConnectParam(Options.ServerHostname, Options.ServerPort), cancel))
        {
            await using (SslSock ssl = new SslSock(sock))
            {
                await ssl.StartSslClientAsync(Options.SslAuthOptions);

                await using (PipeStream writer = ssl.GetStream())
                {
                    MemoryBuffer<byte> initSendBuf = new MemoryBuffer<byte>();
                    initSendBuf.WriteSInt32(LogServerBase.MagicNumber);
                    initSendBuf.WriteSInt32(ClientVersion);

                    await writer.SendAsync(initSendBuf, cancel);

                    writer.ReadTimeout = this.Options.RecvTimeout;
                    MemoryBuffer<byte> initRecvBuf = await writer.ReceiveAllAsync(sizeof(int) * 2, cancel);
                    writer.ReadTimeout = Timeout.Infinite;

                    int magicNumber = initRecvBuf.ReadSInt32();
                    int serverVersion = initRecvBuf.ReadSInt32();

                    if (magicNumber != LogServerBase.MagicNumber)
                    {
                        throw new ApplicationException($"Invalid magicNumber = 0x{magicNumber:X}");
                    }

                    LocalTimer keepAliveTimer = new LocalTimer();

                    long nextKeepAlive = keepAliveTimer.AddTimeout(Options.SendKeepAliveInterval);

                    while (true)
                    {
                        if (reader.IsReadyToReceive() == false)
                        {
                            EmptyEvent.Set(true);

                            await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(),
                                events: this.FlushNowEvent._SingleArray(),
                                timeout: this.Options.Delay);

                            cancel.ThrowIfCancellationRequested();

                            if (Time.Tick64 >= nextKeepAlive)
                            {
                                nextKeepAlive = keepAliveTimer.AddTimeout(Options.SendKeepAliveInterval);
                                MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
                                buf.WriteSInt32((int)LogProtocolDataType.KeepAlive);
                                await writer.SendAsync(buf, cancel);
                            }
                        }
                        else
                        {
                            RefInt totalRecvSize = new RefInt();
                            IReadOnlyList<ReadOnlyMemory<byte>> data = await reader.FastPeekAsync(totalRecvSize: totalRecvSize);

                            await writer.FastSendAsync(data.ToArray(), cancel, true);

                            await reader.FastReceiveAsync(cancel, maxSize: totalRecvSize);
                        }
                    }
                }
            }
        }
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.MainProcTask._TryAwait(true);

            await this.Reader._DisposeSafeAsync();
            await this.Writer._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

// LogClient を用いてログを送付するための LogRoute
public class LogClientLogRoute : LogRouteBase
{
    readonly LogClient Client;
    readonly string AppName;

    public LogClientLogRoute(LogClient client, string appName, string kind, LogPriority minimalPriority) : base(kind, minimalPriority)
    {
        this.AppName = appName._NullCheck();
        this.Client = client;
    }

    public override Task FlushAsync(bool halfFlush = false, CancellationToken cancel = default)
    {
        return this.Client.FlushAsync(halfFlush, cancel);
    }

    public override void ReceiveLog(LogRecord record, string kind)
    {
        //Console.WriteLine("Log!");
        //"Log!"._Debug();
        LogJsonData data = new LogJsonData
        {
            TimeStamp = record.TimeStamp,
            Guid = record.Guid._NonNull(),
            MachineName = Env.DnsFqdnHostName,
            AppName = this.AppName,
            Kind = kind._NonNull(),
            Priority = record.Priority.ToString(),
            Tag = record.Tag._NonNull(),
            TypeName = record.Data?.GetType().Name ?? "null",
            Data = record.Data,
        };

        Client.WriteLog(data);
    }
}

// LogClient の稼働を開始させた上で、ローカルのログのルーティングを LogClient に対して適切に設定するためのユーティリティクラス
public class LogClientInstaller : IDisposable
{
    public LogClient Client { get; }
    public LogClientLogRoute LogRoute { get; }

    public LogClientInstaller(LogClientOptions options, string appName, string kind, LogPriority minimalPriority)
    {
        try
        {
            this.Client = new LogClient(options);

            this.LogRoute = new LogClientLogRoute(this.Client, appName, kind, minimalPriority);

            LocalLogRouter.Router?.InstallLogRoute(this.LogRoute);
        }
        catch
        {
            this._DisposeSafe();

            throw;
        }
    }

    public void Dispose() { this.Dispose(true); GC.SuppressFinalize(this); }
    Once DisposeFlag;
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || DisposeFlag.IsFirstCall() == false) return;

        try
        {
            // アンインストールの実施
            LocalLogRouter.Router?.UninstallLogRoute(this.LogRoute);
        }
        catch (Exception ex)
        {
            string str = ex.ToString();
            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine(ex);
            }
        }

        // クライアントの破棄
        this.Client._DisposeSafe();
    }
}

#endif  // CORES_BASIC_JSON

