using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.Console;
using System.Buffers;

#pragma warning disable CS0162

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace IPA.TestDev.Basic.Common
{
    static class PipeTest
    {

        public static void TestMain()
        {
            CancellationTokenSource cancel = new CancellationTokenSource();

            //Console.Write("Mode>");
            //string mode = Console.ReadLine();

            Thread thread = new Thread(() =>
            {
                Console.ReadLine();

                cancel.Cancel();
            });
            thread.IsBackground = true;
            thread.Start();

            try
            {
                //Test_Pipe_TCP_Client(cancel.Token).Wait();

                //Test_Pipe_SslStream_Client(cancel.Token).Wait();

                Test_Pipe_SpeedTest_Client("speed.sec.softether.co.jp", 9821, 32, 3 * 1000, SpeedTest.ModeFlag.Download, cancel.Token).Wait();
                //Test_Pipe_SpeedTest_Server(9821, cancel.Token).Wait();

                //if (mode.StartsWith("s", StringComparison.OrdinalIgnoreCase))
                //{
                //    Test_Pipe_SpeedTest_Server(9821, cancel.Token).Wait();
                //}
                //else
                //{
                //    //Test_Pipe_SpeedTest_Client("speed.sec.softether.co.jp", 9821, 32, 60 * 60 * 1000, SpeedTest.ModeFlag.Download, cancel.Token).Wait();
                //    Test_Pipe_SpeedTest_Client("127.0.0.1", 9821, 32, 5 * 1000, SpeedTest.ModeFlag.Upload, cancel.Token).Wait();
                //}

                //Test_Pipe_SpeedTest_Server(9821, cancel.Token).Wait();


                //WebSocketHelper.WaitObjectsAsync
            }
            catch (Exception ex)
            {
                WriteLine("TestMain: " + ex.GetSingleException());
            }
        }

        class SpeedTest
        {
            [Flags]
            enum Direction
            {
                Send,
                Recv,
            }

            [Flags]
            public enum ModeFlag
            {
                Upload,
                Download,
                Both,
            }

            public class Result
            {
                public long NumBytesUpload;      // Uploaded size
                public long NumBytesDownload;    // Downloaded size
                public long NumBytesTotal;       // Total size
                public long Span;                // Period (in milliseconds)
                public long BpsUpload;           // Upload throughput
                public long BpsDownload;         // Download throughput
                public long BpsTotal;			 // Total throughput
            }

            bool IsServerMode;
            Memory<byte> SendData;

            IPAddress ServerIP;
            int ServerPort;
            int NumConnection;
            ModeFlag Mode;
            int TimeSpan;
            ulong SessionId;
            CancellationToken Cancel;
            ExceptionQueue ExceptionQueue;
            AsyncManualResetEvent ClientStartEvent;

            int ConnectTimeout = 10 * 1000;
            int RecvTimeout = 5000;

            public SpeedTest(IPAddress ip, int port, int numConnection, int timespan, ModeFlag mode, CancellationToken cancel)
            {
                this.IsServerMode = false;
                this.ServerIP = ip;
                this.ServerPort = port;
                this.Cancel = cancel;
                this.NumConnection = Math.Max(numConnection, 1);
                this.TimeSpan = Math.Max(timespan, 1000);
                this.Mode = mode;
                if (Mode == ModeFlag.Both)
                {
                    this.NumConnection = Math.Max(NumConnection, 2);
                }
                this.ClientStartEvent = new AsyncManualResetEvent();
                InitSendData();
            }

            public SpeedTest(int port, CancellationToken cancel)
            {
                IsServerMode = true;
                this.Cancel = cancel;
                this.ServerPort = port;
                InitSendData();
            }

            void InitSendData()
            {
                int size = 65536;
                byte[] data = Util.Rand(size);
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == (byte)'!')
                        data[i] = (byte)'*';
                }
                SendData = data;
            }

            Once Once;

            public class SessionData
            {
                public bool NoMoreData = false;
            }

            public async Task RunServerAsync()
            {
                if (IsServerMode == false)
                    throw new ApplicationException("Client mode");

                if (Once.IsFirstCall() == false)
                    throw new ApplicationException("You cannot reuse the object.");

                using (var sessions = new GroupManager<ulong, SessionData>(
                    onNewGroup: (key, state) =>
                    {
                        Dbg.Where($"New session: {key}");
                        return new SessionData();
                    },
                    onDeleteGroup: (key, ctx, state) =>
                    {
                        Dbg.Where($"Delete session: {key}");
                    }))
                {
                    AsyncCleanuperLady glady = new AsyncCleanuperLady();

                    FastPalTcpListener listener = new FastPalTcpListener(glady, async (lx, sock) =>
                    {
                        AsyncCleanuperLady lady = new AsyncCleanuperLady();

                        try
                        {

                            Console.WriteLine($"Connected {sock.Info.Tcp.RemoteIPAddress}:{sock.Info.Tcp.RemotePort} -> {sock.Info.Tcp.LocalIPAddress}:{sock.Info.Tcp.LocalPort}");

                            var app = sock.GetFastAppProtocolStub();

                            var st = app.GetStream();

                            var attachHandle = app.AttachHandle;

                            attachHandle.SetStreamReceiveTimeout(RecvTimeout);

                            await st.SendAsync(Encoding.ASCII.GetBytes("TrafficServer\r\n\0"));

                            MemoryBuffer<byte> buf = await st.ReceiveAsync(17);

                            Direction dir = buf.ReadBool8() ? Direction.Send : Direction.Recv;
                            ulong sessionId = 0;
                            long timespan = 0;

                            try
                            {
                                sessionId = buf.ReadUInt64();
                                timespan = buf.ReadSInt64();
                            }
                            catch { }

                            long recvEndTick = FastTick64.Now + timespan;
                            if (timespan == 0) recvEndTick = long.MaxValue;

                            using (var session = sessions.Enter(sessionId))
                            {
                                using (var delay = new DelayAction(lady, (int)(Math.Min(timespan * 3 + 180 * 1000, int.MaxValue)), x => app.Disconnect(new TimeoutException())))
                                {
                                    if (dir == Direction.Recv)
                                    {
                                        RefInt refTmp = new RefInt();
                                        long totalSize = 0;

                                        while (true)
                                        {
                                            var ret = await st.FastReceiveAsync(totalRecvSize: refTmp);
                                            if (ret.Count == 0)
                                            {
                                                break;
                                            }
                                            totalSize += refTmp;

                                            if (ret[0].Span[0] == (byte)'!')
                                                break;

                                            if (FastTick64.Now >= recvEndTick)
                                                break;
                                        }

                                        attachHandle.SetStreamReceiveTimeout(Timeout.Infinite);
                                        attachHandle.SetStreamSendTimeout(60 * 5 * 1000);

                                        session.Context.NoMoreData = true;

                                        while (true)
                                        {
                                            MemoryBuffer<byte> sendBuf = new MemoryBuffer<byte>();
                                            sendBuf.WriteSInt64(totalSize);

                                            await st.SendAsync(sendBuf);

                                            await Task.Delay(100);
                                        }
                                    }
                                    else
                                    {
                                        attachHandle.SetStreamReceiveTimeout(Timeout.Infinite);
                                        attachHandle.SetStreamSendTimeout(Timeout.Infinite);

                                        while (true)
                                        {
                                            if (sessionId == 0 || session.Context.NoMoreData == false)
                                            {
                                                await st.SendAsync(SendData);
                                            }
                                            else
                                            {
                                                var recvMemory = await st.ReceiveAsync();

                                                if (recvMemory.Length == 0)
                                                    break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLine(ex.GetSingleException().Message);
                        }
                        finally
                        {
                            Dbg.Where();
                            await lady;
                            Dbg.Where();
                        }
                    });

                    try
                    {
                        listener.Add(this.ServerPort, IPVersion.IPv4);
                        listener.Add(this.ServerPort, IPVersion.IPv6);

                        WriteLine("Listening.");

                        await TaskUtil.WaitObjectsAsync(cancels: this.Cancel.ToSingleArray());
                    }
                    finally
                    {
                        await glady;
                    }
                }
            }

            public async Task<Result> RunClientAsync()
            {
                if (IsServerMode)
                    throw new ApplicationException("Server mode");

                if (Once.IsFirstCall() == false)
                    throw new ApplicationException("You cannot reuse the object.");

                WriteLine("Client mode start");

                ExceptionQueue = new ExceptionQueue();
                SessionId = Util.RandUInt64();

                List<Task<Result>> tasks = new List<Task<Result>>();
                List<AsyncManualResetEvent> readyEvents = new List<AsyncManualResetEvent>();

                AsyncCleanuperLady lady = new AsyncCleanuperLady();
                try
                {

                    CancelWatcher cancelWatcher = new CancelWatcher(lady, this.Cancel);
                    for (int i = 0; i < NumConnection; i++)
                    {
                        Direction dir;
                        if (Mode == ModeFlag.Download)
                            dir = Direction.Recv;
                        else if (Mode == ModeFlag.Upload)
                            dir = Direction.Send;
                        else
                            dir = ((i % 2) == 0) ? Direction.Recv : Direction.Send;

                        AsyncManualResetEvent readyEvent = new AsyncManualResetEvent();
                        var t = ClientSingleConnectionAsync(dir, readyEvent, cancelWatcher.CancelToken);
                        ExceptionQueue.RegisterWatchedTask(t);
                        tasks.Add(t);
                        readyEvents.Add(readyEvent);
                    }

                    try
                    {
                        using (var whenAllReady = new WhenAll(readyEvents.Select(x => x.WaitAsync())))
                        {
                            await TaskUtil.WaitObjectsAsync(
                                tasks: tasks.Append(whenAllReady.WaitMe).ToArray(),
                                cancels: cancelWatcher.CancelToken.ToSingleArray(),
                                manualEvents: ExceptionQueue.WhenExceptionAdded.ToSingleArray());
                        }

                        Cancel.ThrowIfCancellationRequested();
                        ExceptionQueue.ThrowFirstExceptionIfExists();

                        ExceptionQueue.WhenExceptionAdded.CallbackList.AddSoftCallback(x =>
                        {
                            cancelWatcher.Cancel();
                        });

                        using (new DelayAction(lady, TimeSpan * 3 + 180 * 1000, x =>
                        {
                            cancelWatcher.Cancel();
                        }))
                        {
                            ClientStartEvent.Set(true);

                            using (var whenAllCompleted = new WhenAll(tasks))
                            {
                                await TaskUtil.WaitObjectsAsync(
                                    tasks: whenAllCompleted.WaitMe.ToSingleArray(),
                                    cancels: cancelWatcher.CancelToken.ToSingleArray()
                                    );

                                await whenAllCompleted.WaitMe;
                            }
                        }

                        Result ret = new Result();

                        ret.Span = TimeSpan;

                        foreach (var r in tasks.Select(x => x.Result))
                        {
                            ret.NumBytesDownload += r.NumBytesDownload;
                            ret.NumBytesUpload += r.NumBytesUpload;
                        }

                        ret.NumBytesTotal = ret.NumBytesUpload + ret.NumBytesDownload;

                        ret.BpsUpload = (long)((double)ret.NumBytesUpload * 1000.0 * 8.0 / (double)ret.Span * 1514.0 / 1460.0);
                        ret.BpsDownload = (long)((double)ret.NumBytesDownload * 1000.0 * 8.0 / (double)ret.Span * 1514.0 / 1460.0);
                        ret.BpsTotal = ret.BpsUpload + ret.BpsDownload;

                        return ret;
                    }
                    catch (Exception ex)
                    {
                        await Task.Yield();
                        ExceptionQueue.Add(ex);
                    }
                    finally
                    {
                        cancelWatcher.Cancel();
                        try
                        {
                            await Task.WhenAll(tasks);
                        }
                        catch { }
                    }

                    ExceptionQueue.ThrowFirstExceptionIfExists();
                }
                finally
                {
                    await lady;
                }

                Dbg.Where();

                return null;
            }

            async Task<Result> ClientSingleConnectionAsync(Direction dir, AsyncManualResetEvent fireMeWhenReady, CancellationToken cancel)
            {
                Result ret = new Result();
                AsyncCleanuperLady lady = new AsyncCleanuperLady();
                try
                {
                    var tcp = new FastPalTcpProtocolStub(lady, cancel: cancel);

                    var sock = await tcp.ConnectAsync(ServerIP, ServerPort, cancel, ConnectTimeout);

                    var app = sock.GetFastAppProtocolStub();

                    var attachHandle = app.AttachHandle;

                    var st = app.GetStream();

                    if (dir == Direction.Recv)
                        app.AttachHandle.SetStreamReceiveTimeout(RecvTimeout);

                    try
                    {
                        var hello = await st.ReceiveAllAsync(16);

                        Dbg.Where();
                        if (Encoding.ASCII.GetString(hello.Span).StartsWith("TrafficServer\r\n") == false)
                            throw new ApplicationException("Target server is not a Traffic Server.");
                        Dbg.Where();

                        //throw new ApplicationException("aaaa" + dir.ToString());

                        fireMeWhenReady.Set();

                        cancel.ThrowIfCancellationRequested();

                        await TaskUtil.WaitObjectsAsync(
                            manualEvents: ClientStartEvent.ToSingleArray(),
                            cancels: cancel.ToSingleArray()
                            );

                        long tickStart = FastTick64.Now;
                        long tickEnd = tickStart + this.TimeSpan;

                        var sendData = new MemoryBuffer<byte>();
                        sendData.WriteBool8(dir == Direction.Recv);
                        sendData.WriteUInt64(SessionId);
                        sendData.WriteSInt64(TimeSpan);

                        await st.SendAsync(sendData);

                        if (dir == Direction.Recv)
                        {
                            RefInt totalRecvSize = new RefInt();
                            while (true)
                            {
                                long now = FastTick64.Now;

                                if (now >= tickEnd)
                                    break;

                                await TaskUtil.WaitObjectsAsync(
                                    tasks: st.FastReceiveAsync(totalRecvSize: totalRecvSize).ToSingleArray(),
                                    timeout: (int)(tickEnd - now),
                                    exceptions: ExceptionWhen.TaskException | ExceptionWhen.CancelException);

                                ret.NumBytesDownload += totalRecvSize;
                            }
                        }
                        else
                        {
                            attachHandle.SetStreamReceiveTimeout(Timeout.Infinite);

                            while (true)
                            {
                                long now = FastTick64.Now;

                                if (now >= tickEnd)
                                    break;

                                /*await WebSocketHelper.WaitObjectsAsync(
                                    tasks: st.FastSendAsync(SendData, flush: true).ToSingleArray(),
                                    timeout: (int)(tick_end - now),
                                    exceptions: ExceptionWhen.TaskException | ExceptionWhen.CancelException);*/

                                await st.FastSendAsync(SendData, flush: true);
                            }

                            Task recvResult = Task.Run(async () =>
                            {
                                var recvMemory = await st.ReceiveAllAsync(8);

                                MemoryBuffer<byte> recvMemoryBuf = recvMemory;
                                ret.NumBytesUpload = recvMemoryBuf.ReadSInt64();

                                st.Disconnect();
                            });

                            Task sendSurprise = Task.Run(async () =>
                            {
                                byte[] surprise = new byte[260];
                                surprise.AsSpan().Fill((byte)'!');
                                while (true)
                                {
                                    await st.SendAsync(surprise);

                                    await TaskUtil.WaitObjectsAsync(
                                        manualEvents: sock.Pipe.OnDisconnectedEvent.ToSingleArray(),
                                        timeout: 200);
                                }
                            });

                            await WhenAll.Await(false, recvResult, sendSurprise);

                            await recvResult;
                        }

                        st.Disconnect();

                        Dbg.Where();
                        return ret;
                    }
                    catch (Exception ex)
                    {
                        Dbg.Where(ex.Message);
                        ExceptionQueue.Add(ex);
                        throw;
                    }
                }
                finally
                {
                    await lady;
                }
            }
        }

        static async Task Test_Pipe_SpeedTest_Server(int port, CancellationToken cancel)
        {
            SpeedTest test = new SpeedTest(port, cancel);

            await test.RunServerAsync();
        }

        static async Task Test_Pipe_SpeedTest_Client(string serverHost, int serverPort, int numConnection, int timespan, SpeedTest.ModeFlag mode, CancellationToken cancel, AddressFamily? af = null)
        {
            IPAddress targetIP = await FastPalDnsClient.Shared.GetIPFromHostName(serverHost, af, cancel);

            SpeedTest test = new SpeedTest(targetIP, serverPort, numConnection, timespan, mode, cancel);

            var result = await test.RunClientAsync();

            Console.WriteLine("--- Result ---");
            result.InnerPrint();
        }

        static async Task Test_Pipe_SslStream_Client(CancellationToken cancel)
        {
            string hostname = "news.goo.ne.jp";
            int port = 443;

            AsyncCleanuperLady lady = new AsyncCleanuperLady();
            try
            {
                var tcp = new FastPalTcpProtocolStub(lady, cancel: cancel);

                var sock = await tcp.ConnectAsync(hostname, port);

                FastSslProtocolStack ssl = new FastSslProtocolStack(lady, sock.UpperEnd, null, null, cancel);

                var sslClientOptions = new PalSslClientAuthenticationOptions()
                {
                    TargetHost = hostname,
                    ValidateRemoteCertificateProc = (cert) => { return true; },
                };

                var ssl_sock = await ssl.SslStartClient(sslClientOptions, cancel);

                var app = ssl_sock.GetFastAppProtocolStub(cancel);

                app.AttachHandle.SetStreamReceiveTimeout(1000);

                var st = app.GetStream().GetPalNetworkStream();

                var tcp_info = ssl_sock.Info.GetValue<ILayerInfoTcpEndPoint>();
                var ssl_info = ssl_sock.Info.GetValue<ILayerInfoSsl>();

                WriteLine($"Connected. {tcp_info.LocalIPAddress}:{tcp_info.LocalPort} -> {tcp_info.RemoteIPAddress}:{tcp_info.RemotePort}");
                StreamWriter w = new StreamWriter(st);
                w.AutoFlush = true;

                await w.WriteAsync(
                    "GET / HTTP/1.0\r\n" +
                    $"HOST: {hostname}\r\n\r\n"
                    );

                StreamReader r = new StreamReader(st);
                while (true)
                {
                    string s = await r.ReadLineAsync();
                    if (s == null) break;
                    WriteLine(s);
                }
            }
            finally
            {
                await lady;
            }
        }

        static async Task Test_Pipe_TCP_Client(CancellationToken cancel)
        {
            AsyncCleanuperLady lady = new AsyncCleanuperLady();
            try
            {
                var tcp = new FastPalTcpProtocolStub(lady, cancel: cancel);

                var sock = await tcp.ConnectAsync("www.google.com", 80, null);

                var app = sock.GetFastAppProtocolStub(cancel);

                var st = app.GetStream().GetPalNetworkStream();

                app.AttachHandle.SetStreamTimeout(2000, -1);
                WriteLine("Connected.");
                StreamWriter w = new StreamWriter(st);
                w.AutoFlush = true;

                await w.WriteAsync(
                    "GET / HTTP/1.0\r\n" +
                    "HOST: www.google.com\r\n\r\n"
                    );

                StreamReader r = new StreamReader(st);
                while (true)
                {
                    string s = await r.ReadLineAsync();
                    if (s == null) break;
                    WriteLine(s);
                }
            }
            finally
            {
                await lady;
            }
        }
    }
}
