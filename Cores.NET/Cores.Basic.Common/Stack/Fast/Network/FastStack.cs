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

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class LayerInfo
    {
        SharedHierarchy<LayerInfoBase> Hierarchy = new SharedHierarchy<LayerInfoBase>();

        public void Install(LayerInfoBase info, LayerInfoBase targetLayer, bool joinAsSuperior)
        {
            Debug.Assert(info.IsInstalled == false); Debug.Assert(info._InternalHierarchyBodyItem == null); Debug.Assert(info._InternalLayerStack == null);

            info._InternalHierarchyBodyItem = Hierarchy.Join(targetLayer?.Position, joinAsSuperior, info, info.Position);
            info._InternalLayerStack = this;

            Debug.Assert(info.IsInstalled); Debug.Assert(info._InternalHierarchyBodyItem != null);
        }

        public void Uninstall(LayerInfoBase info)
        {
            Debug.Assert(info.IsInstalled); Debug.Assert(info._InternalHierarchyBodyItem != null); Debug.Assert(info._InternalLayerStack != null);

            Hierarchy.Resign(info._InternalHierarchyBodyItem);

            info._InternalHierarchyBodyItem = null;
            info._InternalLayerStack = null;

            Debug.Assert(info.IsInstalled == false);
        }

        public T[] GetValues<T>() where T : class
        {
            var items = Hierarchy.ItemsWithPositionsReadOnly;

            return items.Select(x => x.Value as T).Where(x => x != null).ToArray();
        }

        public T GetValue<T>(int index = 0) where T : class
        {
            return GetValues<T>()[index];
        }

        public void Encounter(LayerInfo other) => this.Hierarchy.Encounter(other.Hierarchy);

        public ILayerInfoSsl Ssl => GetValue<ILayerInfoSsl>();
        public ILayerInfoIpEndPoint Ip => GetValue<ILayerInfoIpEndPoint>();
        public ILayerInfoTcpEndPoint Tcp => GetValue<ILayerInfoTcpEndPoint>();
    }

    interface ILayerInfoSsl
    {
        bool IsServerMode { get; }
        string SslProtocol { get; }
        string CipherAlgorithm { get; }
        int CipherStrength { get; }
        string HashAlgorithm { get; }
        int HashStrength { get; }
        string KeyExchangeAlgorithm { get; }
        int KeyExchangeStrength { get; }
        PalX509Certificate LocalCertificate { get; }
        PalX509Certificate RemoteCertificate { get; }
    }

    interface ILayerInfoIpEndPoint
    {
        IPAddress LocalIPAddress { get; }
        IPAddress RemoteIPAddress { get; }
    }

    interface ILayerInfoTcpEndPoint : ILayerInfoIpEndPoint
    {
        int LocalPort { get; }
        int RemotePort { get; }
    }

    abstract class LayerInfoBase
    {
        public HierarchyPosition Position { get; } = new HierarchyPosition();
        internal SharedHierarchy<LayerInfoBase>.HierarchyBodyItem _InternalHierarchyBodyItem = null;
        internal LayerInfo _InternalLayerStack = null;

        public FastStackBase ProtocolStack { get; private set; } = null;

        public bool IsInstalled => Position.IsInstalled;

        public void Install(LayerInfo info, LayerInfoBase targetLayer, bool joinAsSuperior)
            => info.Install(this, targetLayer, joinAsSuperior);

        public void Uninstall()
            => _InternalLayerStack.Uninstall(this);

        internal void _InternalSetProtocolStack(FastStackBase protocolStack)
            => ProtocolStack = protocolStack;
    }

    class FastPipe : AsyncCleanupable
    {
        public CancelWatcher CancelWatcher { get; }

        FastStreamFifo StreamAtoB;
        FastStreamFifo StreamBtoA;
        FastDatagramFifo DatagramAtoB;
        FastDatagramFifo DatagramBtoA;

        public ExceptionQueue ExceptionQueue { get; } = new ExceptionQueue();
        public LayerInfo Info { get; } = new LayerInfo();

        public FastPipeEnd A_LowerSide { get; }
        public FastPipeEnd B_UpperSide { get; }

        public FastPipeEnd this[FastPipeEndSide side]
        {
            get
            {
                if (side == FastPipeEndSide.A_LowerSide)
                    return A_LowerSide;
                else if (side == FastPipeEndSide.B_UpperSide)
                    return B_UpperSide;
                else
                    throw new ArgumentOutOfRangeException("side");
            }
        }

        public List<Action> OnDisconnected { get; } = new List<Action>();

        public AsyncManualResetEvent OnDisconnectedEvent { get; } = new AsyncManualResetEvent();

        Once internalDisconnectedFlag;
        public bool IsDisconnected { get => internalDisconnectedFlag.IsSet; }

        public FastPipe(AsyncCleanuperLady lady, CancellationToken cancel = default, long? thresholdLengthStream = null, long? thresholdLengthDatagram = null)
            : base(lady)
        {
            try
            {
                CancelWatcher = new CancelWatcher(lady, cancel);

                if (thresholdLengthStream == null) thresholdLengthStream = FastPipeGlobalConfig.MaxStreamBufferLength;
                if (thresholdLengthDatagram == null) thresholdLengthDatagram = FastPipeGlobalConfig.MaxDatagramQueueLength;

                StreamAtoB = new FastStreamFifo(true, thresholdLengthStream);
                StreamBtoA = new FastStreamFifo(true, thresholdLengthStream);

                DatagramAtoB = new FastDatagramFifo(true, thresholdLengthDatagram);
                DatagramBtoA = new FastDatagramFifo(true, thresholdLengthDatagram);

                StreamAtoB.ExceptionQueue.Encounter(ExceptionQueue);
                StreamBtoA.ExceptionQueue.Encounter(ExceptionQueue);

                DatagramAtoB.ExceptionQueue.Encounter(ExceptionQueue);
                DatagramBtoA.ExceptionQueue.Encounter(ExceptionQueue);

                StreamAtoB.Info.Encounter(Info);
                StreamBtoA.Info.Encounter(Info);

                DatagramAtoB.Info.Encounter(Info);
                DatagramBtoA.Info.Encounter(Info);

                StreamAtoB.OnDisconnected.Add(() => Disconnect());
                StreamBtoA.OnDisconnected.Add(() => Disconnect());

                DatagramAtoB.OnDisconnected.Add(() => Disconnect());
                DatagramBtoA.OnDisconnected.Add(() => Disconnect());

                A_LowerSide = new FastPipeEnd(this, FastPipeEndSide.A_LowerSide, CancelWatcher, StreamAtoB, StreamBtoA, DatagramAtoB, DatagramBtoA);
                B_UpperSide = new FastPipeEnd(this, FastPipeEndSide.B_UpperSide, CancelWatcher, StreamBtoA, StreamAtoB, DatagramBtoA, DatagramAtoB);

                A_LowerSide._InternalSetCounterPart(B_UpperSide);
                B_UpperSide._InternalSetCounterPart(A_LowerSide);

                CancelWatcher.CancelToken.Register(() =>
                {
                    Disconnect(new OperationCanceledException());
                });
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        CriticalSection LayerInfoLock = new CriticalSection();

        public LayerInfoBase LayerInfo_A_LowerSide { get; private set; } = null;
        public LayerInfoBase LayerInfo_B_UpperSide { get; private set; } = null;

        public class InstalledLayerHolder : Holder<LayerInfoBase>
        {
            internal InstalledLayerHolder(Action<LayerInfoBase> disposeProc, LayerInfoBase userData = null) : base(disposeProc, userData) { }
        }

        internal InstalledLayerHolder _InternalInstallLayerInfo(FastPipeEndSide side, LayerInfoBase info)
        {
            if (info == null)
                throw new ArgumentNullException("info");

            lock (LayerInfoLock)
            {
                if (side == FastPipeEndSide.A_LowerSide)
                {
                    if (LayerInfo_A_LowerSide != null) throw new ApplicationException("LayerInfo_A_LowerSide is already installed.");
                    Info.Install(info, LayerInfo_B_UpperSide, false);
                    LayerInfo_A_LowerSide = info;
                }
                else
                {
                    if (LayerInfo_B_UpperSide != null) throw new ApplicationException("LayerInfo_B_UpperSide is already installed.");
                    Info.Install(info, LayerInfo_A_LowerSide, true);
                    LayerInfo_B_UpperSide = info;
                }

                return new InstalledLayerHolder(x =>
                {
                    lock (LayerInfoLock)
                    {
                        if (side == FastPipeEndSide.A_LowerSide)
                        {
                            Debug.Assert(LayerInfo_A_LowerSide != null);
                            Info.Uninstall(LayerInfo_A_LowerSide);
                            LayerInfo_A_LowerSide = null;
                        }
                        else
                        {
                            Debug.Assert(LayerInfo_B_UpperSide != null);
                            Info.Uninstall(LayerInfo_B_UpperSide);
                            LayerInfo_B_UpperSide = null;
                        }
                    }
                },
                info);
            }
        }

        public void Disconnect(Exception ex = null)
        {
            if (internalDisconnectedFlag.IsFirstCall())
            {
                if (ex != null)
                {
                    ExceptionQueue.Add(ex);
                }

                Action[] evList;
                lock (OnDisconnected)
                    evList = OnDisconnected.ToArray();

                foreach (var ev in evList)
                {
                    try
                    {
                        ev();
                    }
                    catch { }
                }

                StreamAtoB.Disconnect();
                StreamBtoA.Disconnect();

                DatagramAtoB.Disconnect();
                DatagramBtoA.Disconnect();

                OnDisconnectedEvent.Set(true);
            }
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Disconnect();
            }
            finally { base.Dispose(disposing); }
        }

        public void CheckDisconnected()
        {
            StreamAtoB.CheckDisconnected();
            StreamBtoA.CheckDisconnected();
            DatagramAtoB.CheckDisconnected();
            DatagramBtoA.CheckDisconnected();
        }
    }

    [Flags]
    enum FastPipeEndSide
    {
        A_LowerSide,
        B_UpperSide,
    }

    [Flags]
    enum FastPipeEndAttachDirection
    {
        NoAttach,
        A_LowerSide,
        B_UpperSide,
    }

    class FastPipeEnd
    {
        public FastPipe Pipe { get; }

        public FastPipeEndSide Side { get; }

        public CancelWatcher CancelWatcher { get; }
        public FastStreamFifo StreamWriter { get; }
        public FastStreamFifo StreamReader { get; }
        public FastDatagramFifo DatagramWriter { get; }
        public FastDatagramFifo DatagramReader { get; }

        public FastPipeEnd CounterPart { get; private set; }

        public AsyncManualResetEvent OnDisconnectedEvent { get => Pipe.OnDisconnectedEvent; }

        public ExceptionQueue ExceptionQueue { get => Pipe.ExceptionQueue; }
        public LayerInfo LayerInfo { get => Pipe.Info; }

        public bool IsDisconnected { get => this.Pipe.IsDisconnected; }
        public void Disconnect(Exception ex = null) { this.Pipe.Disconnect(ex); }
        public void AddOnDisconnected(Action action)
        {
            lock (Pipe.OnDisconnected)
                Pipe.OnDisconnected.Add(action);
        }

        public static FastPipeEnd NewFastPipeAndGetOneSide(FastPipeEndSide createNewPipeAndReturnThisSide, AsyncCleanuperLady lady, CancellationToken cancel = default, long? thresholdLengthStream = null, long? thresholdLengthDatagram = null)
        {
            var pipe = new FastPipe(lady, cancel, thresholdLengthStream, thresholdLengthDatagram);
            return pipe[createNewPipeAndReturnThisSide];
        }

        internal FastPipeEnd(FastPipe pipe, FastPipeEndSide side,
            CancelWatcher cancelWatcher,
            FastStreamFifo streamToWrite, FastStreamFifo streamToRead,
            FastDatagramFifo datagramToWrite, FastDatagramFifo datagramToRead)
        {
            this.Side = side;
            this.Pipe = pipe;
            this.CancelWatcher = cancelWatcher;
            this.StreamWriter = streamToWrite;
            this.StreamReader = streamToRead;
            this.DatagramWriter = datagramToWrite;
            this.DatagramReader = datagramToRead;
        }

        internal void _InternalSetCounterPart(FastPipeEnd p)
            => this.CounterPart = p;

        internal CriticalSection _InternalAttachHandleLock = new CriticalSection();
        internal FastAttachHandle _InternalCurrentAttachHandle = null;

        public FastAttachHandle Attach(AsyncCleanuperLady lady, FastPipeEndAttachDirection attachDirection, object userState = null) => new FastAttachHandle(lady, this, attachDirection, userState);

        internal FastPipeEndStream _InternalGetStream(AsyncCleanuperLady lady, bool autoFlush = true)
            => new FastPipeEndStream(this, autoFlush);

        public FastAppStub GetFastAppProtocolStub(AsyncCleanuperLady lady, CancellationToken cancel = default)
            => new FastAppStub(lady, this, cancel);

        public void CheckDisconnected() => Pipe.CheckDisconnected();
    }

    class FastAttachHandle : AsyncCleanupable
    {
        public FastPipeEnd PipeEnd { get; }
        public object UserState { get; }
        public FastPipeEndAttachDirection Direction { get; }

        FastPipe.InstalledLayerHolder InstalledLayerHolder = null;

        LeakCheckerHolder Leak;
        CriticalSection LockObj = new CriticalSection();

        public FastAttachHandle(AsyncCleanuperLady lady, FastPipeEnd end, FastPipeEndAttachDirection attachDirection, object userState = null)
            : base(lady)
        {
            try
            {
                if (end.Side == FastPipeEndSide.A_LowerSide)
                    Direction = FastPipeEndAttachDirection.A_LowerSide;
                else
                    Direction = FastPipeEndAttachDirection.B_UpperSide;

                if (attachDirection != Direction)
                    throw new ArgumentException($"attachDirection ({attachDirection}) != {Direction}");

                end.CheckDisconnected();

                lock (end._InternalAttachHandleLock)
                {
                    if (end._InternalCurrentAttachHandle != null)
                        throw new ApplicationException("The FastPipeEnd is already attached.");

                    this.UserState = userState;
                    this.PipeEnd = end;
                    this.PipeEnd._InternalCurrentAttachHandle = this;
                }

                Leak = LeakChecker.Enter().AddToLady(this);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
        }

        public void SetLayerInfo(LayerInfoBase info, FastStackBase protocolStack = null)
        {
            lock (LockObj)
            {
                if (DisposeFlag.IsSet) return;

                if (InstalledLayerHolder != null)
                    throw new ApplicationException("LayerInfo is already set.");

                info._InternalSetProtocolStack(protocolStack);

                InstalledLayerHolder = PipeEnd.Pipe._InternalInstallLayerInfo(PipeEnd.Side, info).AddToLady(this);
            }
        }

        int receiveTimeoutProcId = 0;
        TimeoutDetector receiveTimeoutDetector = null;

        public void SetStreamTimeout(int recvTimeout = Timeout.Infinite, int sendTimeout = Timeout.Infinite)
        {
            SetStreamReceiveTimeout(recvTimeout);
            SetStreamSendTimeout(sendTimeout);
        }

        public void SetStreamReceiveTimeout(int timeout = Timeout.Infinite)
        {
            if (Direction == FastPipeEndAttachDirection.A_LowerSide)
                throw new ApplicationException("The attachment direction is From_Lower_To_A_LowerSide.");

            lock (LockObj)
            {
                if (timeout < 0 || timeout == int.MaxValue)
                {
                    if (receiveTimeoutProcId != 0)
                    {
                        PipeEnd.StreamReader.EventListeners.UnregisterCallback(receiveTimeoutProcId);
                        receiveTimeoutProcId = 0;
                        receiveTimeoutDetector.DisposeSafe();
                    }
                }
                else
                {
                    if (DisposeFlag.IsSet) return;

                    SetStreamReceiveTimeout(Timeout.Infinite);

                    receiveTimeoutDetector = new TimeoutDetector(new AsyncCleanuperLady(), timeout, callback: (x) =>
                    {
                        if (PipeEnd.StreamReader.IsReadyToWrite == false)
                            return true;
                        PipeEnd.Pipe.Disconnect(new TimeoutException("StreamReceiveTimeout"));
                        return false;
                    });

                    receiveTimeoutProcId = PipeEnd.StreamReader.EventListeners.RegisterCallback((buffer, type, state) =>
                    {
                        if (type == FastBufferCallbackEventType.Written || type == FastBufferCallbackEventType.NonEmptyToEmpty)
                            receiveTimeoutDetector.Keep();
                    });
                }
            }
        }

        int sendTimeoutProcId = 0;
        TimeoutDetector sendTimeoutDetector = null;

        public void SetStreamSendTimeout(int timeout = Timeout.Infinite)
        {
            if (Direction == FastPipeEndAttachDirection.A_LowerSide)
                throw new ApplicationException("The attachment direction is From_Lower_To_A_LowerSide.");

            lock (LockObj)
            {
                if (timeout < 0 || timeout == int.MaxValue)
                {
                    if (sendTimeoutProcId != 0)
                    {
                        PipeEnd.StreamWriter.EventListeners.UnregisterCallback(sendTimeoutProcId);
                        sendTimeoutProcId = 0;
                        sendTimeoutDetector.DisposeSafe();
                    }
                }
                else
                {
                    if (DisposeFlag.IsSet) return;

                    SetStreamSendTimeout(Timeout.Infinite);

                    sendTimeoutDetector = new TimeoutDetector(new AsyncCleanuperLady(), timeout, callback: (x) =>
                    {
                        if (PipeEnd.StreamWriter.IsReadyToRead == false)
                            return true;

                        PipeEnd.Pipe.Disconnect(new TimeoutException("StreamSendTimeout"));
                        return false;
                    });

                    sendTimeoutProcId = PipeEnd.StreamWriter.EventListeners.RegisterCallback((buffer, type, state) =>
                    {
                        //                            WriteLine($"{type}  {buffer.Length}  {buffer.IsReadyToWrite}");
                        if (type == FastBufferCallbackEventType.Read || type == FastBufferCallbackEventType.EmptyToNonEmpty || type == FastBufferCallbackEventType.PartialProcessReadData)
                            sendTimeoutDetector.Keep();
                    });
                }
            }
        }

        public FastPipeEndStream GetStream(bool autoFlush = true)
            => PipeEnd._InternalGetStream(this.Lady, autoFlush);

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;

                lock (LockObj)
                {
                    if (Direction == FastPipeEndAttachDirection.B_UpperSide)
                    {
                        SetStreamReceiveTimeout(Timeout.Infinite);
                        SetStreamSendTimeout(Timeout.Infinite);
                    }
                }

                lock (PipeEnd._InternalAttachHandleLock)
                {
                    PipeEnd._InternalCurrentAttachHandle = null;
                }
            }
            finally { base.Dispose(disposing); }
        }
    }

    class FastPipeEndStream : FastStream
    {
        public bool AutoFlush { get; set; }
        public FastPipeEnd End { get; private set; }

        public FastPipeEndStream(FastPipeEnd end, bool autoFlush = true)
        {
            end.CheckDisconnected();

            End = end;
            AutoFlush = autoFlush;

            ReadTimeout = Timeout.Infinite;
            WriteTimeout = Timeout.Infinite;
        }

        #region Stream
        public bool IsReadyToSend => End.StreamReader.IsReadyToWrite;
        public bool IsReadyToReceive => End.StreamReader.IsReadyToRead;
        public bool IsReadyToSendTo => End.DatagramReader.IsReadyToWrite;
        public bool IsReadyToReceiveFrom => End.DatagramReader.IsReadyToRead;
        public bool IsDisconnected => End.StreamReader.IsDisconnected || End.DatagramReader.IsDisconnected;

        public void CheckDisconnect()
        {
            End.StreamReader.CheckDisconnected();
            End.DatagramReader.CheckDisconnected();
        }

        public Task WaitReadyToSendAsync(CancellationToken cancel, int timeout)
        {
            cancel.ThrowIfCancellationRequested();

            if (End.StreamWriter.IsReadyToWrite) return Task.CompletedTask;

            return End.StreamWriter.WaitForReadyToWrite(cancel, timeout);
        }

        public Task WaitReadyToReceiveAsync(CancellationToken cancel, int timeout)
        {
            cancel.ThrowIfCancellationRequested();

            if (End.StreamReader.IsReadyToRead) return Task.CompletedTask;

            return End.StreamReader.WaitForReadyToRead(cancel, timeout);
        }

        public async Task FastSendAsync(Memory<Memory<byte>> items, CancellationToken cancel = default, bool flush = true)
        {
            await WaitReadyToSendAsync(cancel, WriteTimeout);

            End.StreamWriter.EnqueueAllWithLock(items.Span);

            if (flush) FastFlush(true, false);
        }

        public async Task FastSendAsync(Memory<byte> item, CancellationToken cancel = default, bool flush = true)
        {
            await WaitReadyToSendAsync(cancel, WriteTimeout);

            lock (End.StreamWriter.LockObj)
            {
                End.StreamWriter.Enqueue(item);
            }

            if (flush) FastFlush(true, false);
        }

        public async Task SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
        {
            Memory<byte> sendData = buffer.ToArray();

            await FastSendAsync(sendData, cancel);

            if (AutoFlush) FastFlush(true, false);
        }

        public void Send(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
            => SendAsync(buffer, cancel).GetResult();

        Once receiveAllAsyncRaiseExceptionFlag;

        public async Task ReceiveAllAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            while (buffer.Length >= 1)
            {
                int r = await ReceiveAsync(buffer, cancel);
                if (r <= 0)
                {
                    End.StreamReader.CheckDisconnected();

                    if (receiveAllAsyncRaiseExceptionFlag.IsFirstCall())
                    {
                        End.StreamReader.ExceptionQueue.Raise(new FastBufferDisconnectedException());
                    }
                    else
                    {
                        End.StreamReader.ExceptionQueue.ThrowFirstExceptionIfExists();
                        throw new FastBufferDisconnectedException();
                    }
                }
                buffer.Walk(r);
            }
        }

        public async Task<Memory<byte>> ReceiveAllAsync(int size, CancellationToken cancel = default)
        {
            Memory<byte> buffer = new byte[size];
            await ReceiveAllAsync(buffer, cancel);
            return buffer;
        }

        public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            try
            {
                LABEL_RETRY:
                await WaitReadyToReceiveAsync(cancel, ReadTimeout);

                int ret = 0;

                lock (End.StreamReader.LockObj)
                    ret = End.StreamReader.DequeueContiguousSlow(buffer);

                if (ret == 0)
                {
                    await Task.Yield();
                    goto LABEL_RETRY;
                }

                Debug.Assert(ret <= buffer.Length);

                End.StreamReader.CompleteRead();

                return ret;
            }
            catch (DisconnectedException)
            {
                return 0;
            }
        }

        public async Task<Memory<byte>> ReceiveAsync(int maxSize = int.MaxValue, CancellationToken cancel = default)
        {
            try
            {
                LABEL_RETRY:
                await WaitReadyToReceiveAsync(cancel, ReadTimeout);

                Memory<byte> ret;

                lock (End.StreamReader.LockObj)
                    ret = End.StreamReader.DequeueContiguousSlow(maxSize);

                if (ret.Length == 0)
                {
                    await Task.Yield();
                    goto LABEL_RETRY;
                }

                End.StreamReader.CompleteRead();

                return ret;
            }
            catch (DisconnectedException)
            {
                return Memory<byte>.Empty;
            }
        }

        public void ReceiveAll(Memory<byte> buffer, CancellationToken cancel = default)
            => ReceiveAllAsync(buffer, cancel).GetResult();

        public Memory<byte> ReceiveAll(int size, CancellationToken cancel = default)
            => ReceiveAllAsync(size, cancel).GetResult();

        public int Receive(Memory<byte> buffer, CancellationToken cancel = default)
            => ReceiveAsync(buffer, cancel).GetResult();

        public Memory<byte> Receive(int maxSize = int.MaxValue, CancellationToken cancel = default)
            => ReceiveAsync(maxSize, cancel).GetResult();

        public async Task<List<Memory<byte>>> FastReceiveAsync(CancellationToken cancel = default, RefInt totalRecvSize = null)
        {
            try
            {
                LABEL_RETRY:
                await WaitReadyToReceiveAsync(cancel, ReadTimeout);

                var ret = End.StreamReader.DequeueAllWithLock(out long totalReadSize);

                if (totalRecvSize != null)
                    totalRecvSize.Set((int)totalReadSize);

                if (totalReadSize == 0)
                {
                    await Task.Yield();
                    goto LABEL_RETRY;
                }

                End.StreamReader.CompleteRead();

                return ret;
            }
            catch (DisconnectedException)
            {
                return new List<Memory<byte>>();
            }
        }

        public async Task<List<Memory<byte>>> FastPeekAsync(int maxSize = int.MaxValue, CancellationToken cancel = default, RefInt totalRecvSize = null)
        {
            LABEL_RETRY:
            CheckDisconnect();
            await WaitReadyToReceiveAsync(cancel, ReadTimeout);
            CheckDisconnect();

            long totalReadSize;
            FastBufferSegment<Memory<byte>>[] tmp;
            lock (End.StreamReader.LockObj)
            {
                tmp = End.StreamReader.GetSegmentsFast(End.StreamReader.PinHead, maxSize, out totalReadSize, true);
            }

            if (totalRecvSize != null)
                totalRecvSize.Set((int)totalReadSize);

            if (totalReadSize == 0)
            {
                await Task.Yield();
                goto LABEL_RETRY;
            }

            List<Memory<byte>> ret = new List<Memory<byte>>();
            foreach (FastBufferSegment<Memory<byte>> item in tmp)
                ret.Add(item.Item);

            return ret;
        }

        public async Task<Memory<byte>> FastPeekContiguousAsync(int maxSize = int.MaxValue, CancellationToken cancel = default)
        {
            LABEL_RETRY:
            CheckDisconnect();
            await WaitReadyToReceiveAsync(cancel, ReadTimeout);
            CheckDisconnect();

            Memory<byte> ret;

            lock (End.StreamReader.LockObj)
            {
                ret = End.StreamReader.GetContiguous(End.StreamReader.PinHead, maxSize, true);
            }

            if (ret.Length == 0)
            {
                await Task.Yield();
                goto LABEL_RETRY;
            }

            return ret;
        }

        public async Task<Memory<byte>> PeekAsync(int maxSize = int.MaxValue, CancellationToken cancel = default)
            => (await FastPeekContiguousAsync(maxSize, cancel)).ToArray();

        public async Task<int> PeekAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            var tmp = await PeekAsync(buffer.Length, cancel);
            tmp.CopyTo(buffer);
            return tmp.Length;
        }

        #endregion

        #region Datagram
        public Task WaitReadyToSendToAsync(CancellationToken cancel, int timeout)
        {
            cancel.ThrowIfCancellationRequested();

            if (End.DatagramWriter.IsReadyToWrite) return Task.CompletedTask;

            return End.DatagramWriter.WaitForReadyToWrite(cancel, timeout);
        }

        public Task WaitReadyToReceiveFromAsync(CancellationToken cancel, int timeout)
        {
            cancel.ThrowIfCancellationRequested();

            if (End.DatagramReader.IsReadyToRead) return Task.CompletedTask;

            return End.DatagramReader.WaitForReadyToRead(cancel, timeout);
        }

        public async Task FastSendToAsync(Memory<Datagram> items, CancellationToken cancel = default, bool flush = true)
        {
            await WaitReadyToSendToAsync(cancel, WriteTimeout);

            End.DatagramWriter.EnqueueAllWithLock(items.Span);

            if (flush) FastFlush(false, true);
        }

        public async Task FastSendToAsync(Datagram item, CancellationToken cancel = default, bool flush = true)
        {
            await WaitReadyToSendToAsync(cancel, WriteTimeout);

            lock (End.StreamWriter.LockObj)
            {
                End.DatagramWriter.Enqueue(item);
            }

            if (flush) FastFlush(false, true);
        }

        public async Task SendToAsync(ReadOnlyMemory<byte> buffer, EndPoint remoteEndPoint, CancellationToken cancel = default)
        {
            Datagram sendData = new Datagram(buffer.Span.ToArray(), remoteEndPoint);

            await FastSendToAsync(sendData, cancel);

            if (AutoFlush) FastFlush(false, true);
        }

        public void SendTo(ReadOnlyMemory<byte> buffer, EndPoint remoteEndPoint, CancellationToken cancel = default)
            => SendToAsync(buffer, remoteEndPoint, cancel).GetResult();

        public async Task<List<Datagram>> FastReceiveFromAsync(CancellationToken cancel = default)
        {
            LABEL_RETRY:
            await WaitReadyToReceiveFromAsync(cancel, ReadTimeout);

            var ret = End.DatagramReader.DequeueAllWithLock(out long totalReadSize);
            if (totalReadSize == 0)
            {
                await Task.Yield();
                goto LABEL_RETRY;
            }

            End.DatagramReader.CompleteRead();

            return ret;
        }

        public async Task<Datagram> ReceiveFromAsync(CancellationToken cancel = default)
        {
            LABEL_RETRY:
            await WaitReadyToReceiveFromAsync(cancel, ReadTimeout);

            List<Datagram> dataList;

            long totalReadSize;

            lock (End.DatagramReader.LockObj)
            {
                dataList = End.DatagramReader.Dequeue(1, out totalReadSize);
            }

            if (totalReadSize == 0)
            {
                await Task.Yield();
                goto LABEL_RETRY;
            }

            Debug.Assert(dataList.Count == 1);

            End.DatagramReader.CompleteRead();

            return dataList[0];
        }

        public async Task<PalSocketReceiveFromResult> ReceiveFromAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            var datagram = await ReceiveFromAsync(cancel);
            datagram.Data.CopyTo(buffer);

            PalSocketReceiveFromResult ret = new PalSocketReceiveFromResult();
            ret.ReceivedBytes = datagram.Data.Length;
            ret.RemoteEndPoint = datagram.EndPoint;
            return ret;
        }

        public int ReceiveFrom(Memory<byte> buffer, out EndPoint remoteEndPoint, CancellationToken cancel = default)
        {
            PalSocketReceiveFromResult r = ReceiveFromAsync(buffer, cancel).GetResult();

            remoteEndPoint = r.RemoteEndPoint;

            return r.ReceivedBytes;
        }

        #endregion

        public void FastFlush(bool stream = true, bool datagram = true)
        {
            if (stream)
                End.StreamWriter.CompleteWrite();

            if (datagram)
                End.DatagramWriter.CompleteWrite();
        }

        public void Disconnect() => End.Disconnect();

        public override int ReadTimeout { get; set; }
        public override int WriteTimeout { get; set; }

        public override bool DataAvailable => IsReadyToReceive;

        public virtual void Flush() => FastFlush();

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flush();
            return Task.CompletedTask;
        }

        public virtual Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => SendAsync(buffer.AsReadOnlyMemory(offset, count), cancellationToken);

        public virtual Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReceiveAsync(buffer.AsMemory(offset, count), cancellationToken);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => await ReceiveAsync(buffer, cancellationToken);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => await this.SendAsync(buffer, cancellationToken);

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Disconnect();
            }
            finally { base.Dispose(disposing); }
        }
    }

    class FastPipeNonblockStateHelper
    {
        byte[] LastState = new byte[0];

        public FastPipeNonblockStateHelper() { }
        public FastPipeNonblockStateHelper(IFastBufferState reader, IFastBufferState writer, CancellationToken cancel = default) : this()
        {
            AddWatchReader(reader);
            AddWatchWriter(writer);
            AddCancel(cancel);
        }

        List<IFastBufferState> ReaderList = new List<IFastBufferState>();
        List<IFastBufferState> WriterList = new List<IFastBufferState>();
        List<AsyncAutoResetEvent> WaitEventList = new List<AsyncAutoResetEvent>();
        List<CancellationToken> WaitCancelList = new List<CancellationToken>();

        public void AddWatchReader(IFastBufferState obj)
        {
            if (ReaderList.Contains(obj) == false)
                ReaderList.Add(obj);
            AddEvent(obj.EventReadReady);
        }

        public void AddWatchWriter(IFastBufferState obj)
        {
            if (WriterList.Contains(obj) == false)
                WriterList.Add(obj);
            AddEvent(obj.EventWriteReady);
        }

        public void AddEvent(AsyncAutoResetEvent ev) => AddEvents(new AsyncAutoResetEvent[] { ev });
        public void AddEvents(params AsyncAutoResetEvent[] events)
        {
            foreach (var ev in events)
                if (WaitEventList.Contains(ev) == false)
                    WaitEventList.Add(ev);
        }

        public void AddCancel(CancellationToken c) => AddCancels(new CancellationToken[] { c });
        public void AddCancels(params CancellationToken[] cancels)
        {
            foreach (var cancel in cancels)
                if (cancel.CanBeCanceled)
                    if (WaitCancelList.Contains(cancel) == false)
                        WaitCancelList.Add(cancel);
        }

        public byte[] SnapshotState(long salt = 0)
        {
            SpanBuffer<byte> ret = new SpanBuffer<byte>();
            ret.WriteSInt64(salt);
            foreach (var s in ReaderList)
            {
                lock (s.LockObj)
                {
                    ret.WriteUInt8((byte)(s.IsReadyToRead ? 1 : 0));
                    ret.WriteSInt64(s.PinTail);
                }
            }
            foreach (var s in WriterList)
            {
                lock (s.LockObj)
                {
                    ret.WriteUInt8((byte)(s.IsReadyToWrite ? 1 : 0));
                    ret.WriteSInt64(s.PinHead);
                }
            }
            return ret.Span.ToArray();
        }

        public bool IsStateChanged(int salt = 0)
        {
            byte[] newState = SnapshotState(salt);
            if (LastState.SequenceEqual(newState))
                return false;
            LastState = newState;
            return true;
        }

        public async Task<bool> WaitIfNothingChanged(int timeout = Timeout.Infinite, int salt = 0)
        {
            timeout = TaskUtil.GetMinTimeout(timeout, FastPipeGlobalConfig.PollingTimeout);
            if (timeout == 0) return false;
            if (IsStateChanged(salt)) return false;

            await TaskUtil.WaitObjectsAsync(
                cancels: WaitCancelList.ToArray(),
                events: WaitEventList.ToArray(),
                timeout: timeout);

            return true;
        }
    }


    [Flags]
    enum PipeSupportedDataTypes
    {
        Stream = 1,
        Datagram = 2,
    }

    abstract class FastPipeEndAsyncObjectWrapperBase : AsyncCleanupable
    {
        public CancelWatcher CancelWatcher { get; }
        public FastPipeEnd PipeEnd { get; }
        public abstract PipeSupportedDataTypes SupportedDataTypes { get; }
        Task MainLoopTask = Task.CompletedTask;

        public ExceptionQueue ExceptionQueue { get => PipeEnd.ExceptionQueue; }
        public LayerInfo LayerInfo { get => PipeEnd.LayerInfo; }

        public FastPipeEndAsyncObjectWrapperBase(AsyncCleanuperLady lady, FastPipeEnd pipeEnd, CancellationToken cancel = default)
            : base(lady)
        {
            PipeEnd = pipeEnd;
            CancelWatcher = new CancelWatcher(Lady, cancel);
        }

        public override async Task _CleanupAsyncInternal()
        {
            try
            {
                await MainLoopTask.TryWaitAsync(true);

                await CancelWatcher.AsyncCleanuper;
            }
            finally { await base._CleanupAsyncInternal(); }
        }

        Once ConnectedFlag;
        protected void BaseStart()
        {
            if (ConnectedFlag.IsFirstCall())
                MainLoopTask = MainLoopAsync();
        }

        async Task MainLoopAsync()
        {
            try
            {
                List<Task> tasks = new List<Task>();

                if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream))
                {
                    tasks.Add(StreamReadFromPipeLoopAsync());
                    tasks.Add(StreamWriteToPipeLoopAsync());
                }

                if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Datagram))
                {
                    tasks.Add(DatagramReadFromPipeLoopAsync());
                    tasks.Add(DatagramWriteToPipeLoopAsync());
                }

                await Task.WhenAll(tasks.ToArray());
            }
            catch (Exception ex)
            {
                Disconnect(ex);
            }
            finally
            {
                Disconnect();
            }
        }

        List<Action> OnDisconnectedList = new List<Action>();
        public void AddOnDisconnected(Action proc) => OnDisconnectedList.Add(proc);

        protected abstract Task StreamWriteToObject(FastStreamFifo fifo, CancellationToken cancel);
        protected abstract Task StreamReadFromObject(FastStreamFifo fifo, CancellationToken cancel);

        protected abstract Task DatagramWriteToObject(FastDatagramFifo fifo, CancellationToken cancel);
        protected abstract Task DatagramReadFromObject(FastDatagramFifo fifo, CancellationToken cancel);

        async Task StreamReadFromPipeLoopAsync()
        {
            using (LeakChecker.Enter())
            {
                try
                {
                    var reader = PipeEnd.StreamReader;
                    while (true)
                    {
                        bool stateChanged;
                        do
                        {
                            stateChanged = false;

                            CancelWatcher.CancelToken.ThrowIfCancellationRequested();

                            while (reader.IsReadyToRead)
                            {
                                await StreamWriteToObject(reader, CancelWatcher.CancelToken);
                                stateChanged = true;
                            }
                        }
                        while (stateChanged);

                        await TaskUtil.WaitObjectsAsync(
                            events: new AsyncAutoResetEvent[] { reader.EventReadReady },
                            cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                            timeout: FastPipeGlobalConfig.PollingTimeout
                            );
                    }
                }
                catch (Exception ex)
                {
                    ExceptionQueue.Raise(ex);
                }
                finally
                {
                    PipeEnd.Disconnect();
                    Disconnect();
                }
            }
        }

        async Task StreamWriteToPipeLoopAsync()
        {
            using (LeakChecker.Enter())
            {
                try
                {
                    var writer = PipeEnd.StreamWriter;
                    while (true)
                    {
                        bool stateChanged;
                        do
                        {
                            stateChanged = false;

                            CancelWatcher.CancelToken.ThrowIfCancellationRequested();

                            if (writer.IsReadyToWrite)
                            {
                                long lastTail = writer.PinTail;
                                await StreamReadFromObject(writer, CancelWatcher.CancelToken);
                                if (writer.PinTail != lastTail)
                                {
                                    stateChanged = true;
                                }
                            }

                        }
                        while (stateChanged);

                        await TaskUtil.WaitObjectsAsync(
                            events: new AsyncAutoResetEvent[] { writer.EventWriteReady },
                            cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                            timeout: FastPipeGlobalConfig.PollingTimeout
                            );
                    }
                }
                catch (Exception ex)
                {
                    ExceptionQueue.Raise(ex);
                }
                finally
                {
                    PipeEnd.Disconnect();
                }
            }
        }

        async Task DatagramReadFromPipeLoopAsync()
        {
            using (LeakChecker.Enter())
            {
                try
                {
                    var reader = PipeEnd.DatagramReader;
                    while (true)
                    {
                        bool stateChanged;
                        do
                        {
                            stateChanged = false;

                            CancelWatcher.CancelToken.ThrowIfCancellationRequested();

                            while (reader.IsReadyToRead)
                            {
                                await DatagramWriteToObject(reader, CancelWatcher.CancelToken);
                                stateChanged = true;
                            }
                        }
                        while (stateChanged);

                        await TaskUtil.WaitObjectsAsync(
                            events: new AsyncAutoResetEvent[] { reader.EventReadReady },
                            cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                            timeout: FastPipeGlobalConfig.PollingTimeout
                            );
                    }
                }
                catch (Exception ex)
                {
                    ExceptionQueue.Raise(ex);
                }
                finally
                {
                    PipeEnd.Disconnect();
                    Disconnect();
                }
            }
        }

        async Task DatagramWriteToPipeLoopAsync()
        {
            using (LeakChecker.Enter())
            {
                try
                {
                    var writer = PipeEnd.DatagramWriter;
                    while (true)
                    {
                        bool stateChanged;
                        do
                        {
                            stateChanged = false;

                            CancelWatcher.CancelToken.ThrowIfCancellationRequested();

                            if (writer.IsReadyToWrite)
                            {
                                long lastTail = writer.PinTail;
                                await DatagramReadFromObject(writer, CancelWatcher.CancelToken);
                                if (writer.PinTail != lastTail)
                                {
                                    stateChanged = true;
                                }
                            }

                        }
                        while (stateChanged);

                        await TaskUtil.WaitObjectsAsync(
                            events: new AsyncAutoResetEvent[] { writer.EventWriteReady },
                            cancels: new CancellationToken[] { CancelWatcher.CancelToken },
                            timeout: FastPipeGlobalConfig.PollingTimeout
                            );
                    }
                }
                catch (Exception ex)
                {
                    ExceptionQueue.Raise(ex);
                }
                finally
                {
                    PipeEnd.Disconnect();
                }
            }
        }

        Once DisconnectedFlag;
        public void Disconnect(Exception ex = null)
        {
            if (DisconnectedFlag.IsFirstCall())
            {
                this.PipeEnd.Disconnect(ex);
                CancelWatcher.Cancel();

                foreach (var proc in OnDisconnectedList) try { proc(); } catch { };
            }
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;

                Disconnect();
                CancelWatcher.DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }
    }

    class FastPipeEndSocketWrapper : FastPipeEndAsyncObjectWrapperBase
    {
        public PalSocket Socket { get; }
        public int RecvTmpBufferSize { get; private set; }
        public override PipeSupportedDataTypes SupportedDataTypes { get; }

        public FastPipeEndSocketWrapper(AsyncCleanuperLady lady, FastPipeEnd pipeEnd, PalSocket socket, CancellationToken cancel = default) : base(lady, pipeEnd, cancel)
        {
            this.Socket = socket;
            SupportedDataTypes = (Socket.SocketType == SocketType.Stream) ? PipeSupportedDataTypes.Stream : PipeSupportedDataTypes.Datagram;
            if (Socket.SocketType == SocketType.Stream)
            {
                Socket.LingerTime.Value = 0;
                Socket.NoDelay.Value = false;
            }
            this.AddOnDisconnected(() => Socket.DisposeSafe());

            this.BaseStart();
        }

        protected override async Task StreamWriteToObject(FastStreamFifo fifo, CancellationToken cancel)
        {
            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

            List<Memory<byte>> sendArray;

            sendArray = fifo.DequeueAllWithLock(out long totalReadSize);
            fifo.CompleteRead();

            await TaskUtil.DoAsyncWithTimeout(
                async c =>
                {
                    int ret = await Socket.SendAsync(sendArray);
                    return 0;
                },
                cancel: cancel);
        }

        FastMemoryPool<byte> FastMemoryAllocatorForStream = new FastMemoryPool<byte>();

        AsyncBulkReceiver<Memory<byte>, FastPipeEndSocketWrapper> StreamBulkReceiver = new AsyncBulkReceiver<Memory<byte>, FastPipeEndSocketWrapper>(async me =>
        {
            if (me.RecvTmpBufferSize == 0)
            {
                int i = me.Socket.ReceiveBufferSize;
                if (i <= 0) i = 65536;
                me.RecvTmpBufferSize = Math.Min(i, FastPipeGlobalConfig.MaxStreamBufferLength);
            }

            Memory<byte> tmp = me.FastMemoryAllocatorForStream.Reserve(me.RecvTmpBufferSize);
            int r = await me.Socket.ReceiveAsync(tmp);
            if (r < 0) throw new SocketDisconnectedException();
            me.FastMemoryAllocatorForStream.Commit(ref tmp, r);
            if (r == 0) return new ValueOrClosed<Memory<byte>>();
            return new ValueOrClosed<Memory<byte>>(tmp);
        });

        protected override async Task StreamReadFromObject(FastStreamFifo fifo, CancellationToken cancel)
        {
            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

            Memory<byte>[] recvList = await StreamBulkReceiver.Recv(cancel, this);

            if (recvList == null)
            {
                // disconnected
                fifo.Disconnect();
                return;
            }

            fifo.EnqueueAllWithLock(recvList);

            fifo.CompleteWrite();
        }

        protected override async Task DatagramWriteToObject(FastDatagramFifo fifo, CancellationToken cancel)
        {
            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Datagram) == false) throw new NotSupportedException();

            List<Datagram> sendList;

            sendList = fifo.DequeueAllWithLock(out _);
            fifo.CompleteRead();

            await TaskUtil.DoAsyncWithTimeout(
                async c =>
                {
                    foreach (Datagram data in sendList)
                    {
                        cancel.ThrowIfCancellationRequested();
                        await Socket.SendToAsync(data.Data.AsSegment(), data.EndPoint);
                    }
                    return 0;
                },
                cancel: cancel);
        }

        FastMemoryPool<byte> FastMemoryAllocatorForDatagram = new FastMemoryPool<byte>();

        AsyncBulkReceiver<Datagram, FastPipeEndSocketWrapper> DatagramBulkReceiver = new AsyncBulkReceiver<Datagram, FastPipeEndSocketWrapper>(async me =>
        {
            Memory<byte> tmp = me.FastMemoryAllocatorForDatagram.Reserve(65536);

            var ret = await me.Socket.ReceiveFromAsync(tmp);

            me.FastMemoryAllocatorForDatagram.Commit(ref tmp, ret.ReceivedBytes);

            Datagram pkt = new Datagram(tmp, ret.RemoteEndPoint);
            return new ValueOrClosed<Datagram>(pkt);
        });

        protected override async Task DatagramReadFromObject(FastDatagramFifo fifo, CancellationToken cancel)
        {
            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Datagram) == false) throw new NotSupportedException();

            Datagram[] pkts = await DatagramBulkReceiver.Recv(cancel, this);

            fifo.EnqueueAllWithLock(pkts);

            fifo.CompleteWrite();
        }

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Socket.DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }
    }

    class FastPipeEndStreamWrapper : FastPipeEndAsyncObjectWrapperBase
    {
        public FastStream Stream { get; }
        public int RecvTmpBufferSize { get; private set; }
        public const int SendTmpBufferSize = 65536;
        public override PipeSupportedDataTypes SupportedDataTypes { get; }

        Memory<byte> SendTmpBuffer = new byte[SendTmpBufferSize];

        //static bool UseDontLingerOption = true;

        public FastPipeEndStreamWrapper(AsyncCleanuperLady lady, FastPipeEnd pipeEnd, FastStream stream, CancellationToken cancel = default) : base(lady, pipeEnd, cancel)
        {
            this.Stream = stream;
            SupportedDataTypes = PipeSupportedDataTypes.Stream;

            Stream.ReadTimeout = Stream.WriteTimeout = Timeout.Infinite;
            this.AddOnDisconnected(() => Stream.DisposeSafe());

            this.BaseStart();
        }

        protected override async Task StreamWriteToObject(FastStreamFifo fifo, CancellationToken cancel)
        {
            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

            await TaskUtil.DoAsyncWithTimeout(
                async c =>
                {
                    while (true)
                    {
                        int size = fifo.DequeueContiguousSlow(SendTmpBuffer, SendTmpBuffer.Length);
                        if (size == 0)
                            break;

                        await Stream.WriteAsync(SendTmpBuffer.Slice(0, size));
                    }

                    return 0;
                },
                cancel: cancel);
        }

        FastMemoryPool<byte> FastMemoryAllocatorForStream = new FastMemoryPool<byte>();

        AsyncBulkReceiver<Memory<byte>, FastPipeEndStreamWrapper> StreamBulkReceiver = new AsyncBulkReceiver<Memory<byte>, FastPipeEndStreamWrapper>(async me =>
        {
            if (me.RecvTmpBufferSize == 0)
            {
                int i = 65536;
                me.RecvTmpBufferSize = Math.Min(i, FastPipeGlobalConfig.MaxStreamBufferLength);
            }

            Memory<byte> tmp = me.FastMemoryAllocatorForStream.Reserve(me.RecvTmpBufferSize);
            int r = await me.Stream.ReadAsync(tmp);
            if (r < 0) throw new BaseStreamDisconnectedException();
            me.FastMemoryAllocatorForStream.Commit(ref tmp, r);
            if (r == 0) return new ValueOrClosed<Memory<byte>>();
            return new ValueOrClosed<Memory<byte>>(tmp);
        });

        protected override async Task StreamReadFromObject(FastStreamFifo fifo, CancellationToken cancel)
        {
            if (SupportedDataTypes.Bit(PipeSupportedDataTypes.Stream) == false) throw new NotSupportedException();

            Memory<byte>[] recvList = await StreamBulkReceiver.Recv(cancel, this);

            if (recvList == null)
            {
                // disconnected
                fifo.Disconnect();
                return;
            }

            fifo.EnqueueAllWithLock(recvList);

            fifo.CompleteWrite();
        }

        protected override Task DatagramWriteToObject(FastDatagramFifo fifo, CancellationToken cancel)
            => throw new NotSupportedException();

        protected override Task DatagramReadFromObject(FastDatagramFifo fifo, CancellationToken cancel)
            => throw new NotSupportedException();

        Once DisposeFlag;
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!disposing || DisposeFlag.IsFirstCall() == false) return;
                Stream.DisposeSafe();
            }
            finally { base.Dispose(disposing); }
        }
    }

    interface IFastStream
    {
        ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default);
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default);
        int ReadTimeout { get; set; }
        int WriteTimeout { get; set; }
        bool DataAvailable { get; }
        Task FlushAsync(CancellationToken cancel = default);
    }

    abstract class FastStream : IDisposable, IFastStream
    {
        public abstract int ReadTimeout { get; set; }
        public abstract int WriteTimeout { get; set; }
        public abstract bool DataAvailable { get; }

        public abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default);
        public abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default);
        public abstract Task FlushAsync(CancellationToken cancel = default);

        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing) { }

        public FastStreamToPalNetworkStream GetPalNetworkStream(bool disposeObject = false)
            => FastStreamToPalNetworkStream.CreateFromFastStream(this, disposeObject);
    }

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

        CriticalSection LockObj = new CriticalSection();

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

        protected abstract Task ConnectMainAsync(IPEndPoint remoteEndPoint, int connectTimeout = DefaultTcpConnectTimeout);
        protected abstract void ListenMain(IPEndPoint localEndPoint);
        protected abstract Task<FastTcpProtocolStubBase> AcceptMainAsync(AsyncCleanuperLady lady, CancellationToken cancelForNewSocket = default);

        public bool IsConnected { get; private set; }
        public bool IsListening { get; private set; }
        public bool IsServerMode { get; private set; }

        AsyncLock ConnectLock = new AsyncLock();

        public async Task<FastSock> ConnectAsync(IPEndPoint remoteEndPoint, int connectTimeout = DefaultTcpConnectTimeout)
        {
            using (await ConnectLock.LockWithAwait())
            {
                if (IsConnected) throw new ApplicationException("Already connected.");
                if (IsListening) throw new ApplicationException("Already listening.");

                await ConnectMainAsync(remoteEndPoint, connectTimeout);

                IsConnected = true;
                IsServerMode = false;

                return new FastSock(this.Lady, this);
            }
        }

        public Task<FastSock> ConnectAsync(IPAddress ip, int port, CancellationToken cancel = default, int connectTimeout = FastTcpProtocolStubBase.DefaultTcpConnectTimeout)
            => ConnectAsync(new IPEndPoint(ip, port), connectTimeout);

        public async Task<FastSock> ConnectAsync(string host, int port, AddressFamily? addressFamily = null, int connectTimeout = FastTcpProtocolStubBase.DefaultTcpConnectTimeout)
            => await ConnectAsync(await Options.DnsClient.GetIPFromHostName(host, addressFamily, GrandCancel, connectTimeout), port, default, connectTimeout);

        CriticalSection ListenLock = new CriticalSection();

        public void Listen(IPEndPoint localEndPoint)
        {
            lock (ListenLock)
            {
                if (IsConnected) throw new ApplicationException("Already connected.");
                if (IsListening) throw new ApplicationException("Already listening.");

                ListenMain(localEndPoint);

                IsListening = true;
                IsServerMode = true;
            }
        }

        public async Task<FastSock> AcceptAsync(AsyncCleanuperLady lady, CancellationToken cancelForNewSocket = default)
        {
            if (IsListening == false) throw new ApplicationException("Not listening.");

            return new FastSock(lady, await AcceptMainAsync(lady, cancelForNewSocket));
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

        protected override async Task ConnectMainAsync(IPEndPoint remoteEndPoint, int connectTimeout = FastTcpProtocolStubBase.DefaultTcpConnectTimeout)
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
                cancel: GrandCancel);

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

        protected override void ListenMain(IPEndPoint localEndPoint)
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

        protected override async Task<FastTcpProtocolStubBase> AcceptMainAsync(AsyncCleanuperLady lady, CancellationToken cancelForNewSocket = default)
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

    class FastSock : AsyncCleanupable
    {
        public FastProtocolBase Stack { get; }
        public FastPipe Pipe { get; }
        public FastPipeEnd LowerEnd { get; }
        public FastPipeEnd UpperEnd { get; }
        public LayerInfo Info { get => this.LowerEnd.LayerInfo; }

        internal FastSock(AsyncCleanuperLady lady, FastProtocolBase protocolStack)
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

        public FastAppStub GetFastAppProtocolStub(CancellationToken cancel = default)
        {
            FastAppStub ret = UpperEnd.GetFastAppProtocolStub(this.Lady, cancel);

            return ret;
        }

        public void Disconnect(Exception ex = null)
        {
            Pipe.Disconnect(ex);
        }
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

    abstract class FastSockBase : AsyncCleanupable
    {
        public FastPipe Pipe { get; }
        FastBottomProtocolStubBase Stub;

        public FastPipeEnd UpperSidePipeEnd { get => Pipe.B_UpperSide; }

        public FastAppStub GetFastAppProtocolStub(CancellationToken cancel = default)
            => this.UpperSidePipeEnd.GetFastAppProtocolStub(this.Lady, cancel);

        public FastSockBase(AsyncCleanuperLady lady, FastPipe pipe, FastBottomProtocolStubBase stub)
            : base(lady)
        {
            try
            {
                this.Pipe = pipe.AddToLady(this);
                this.Stub = stub.AddToLady(this);
            }
            catch
            {
                Lady.DisposeAllSafe();
                throw;
            }
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

        public async Task<FastSock> SslStartClient(PalSslClientAuthenticationOptions sslClientAuthenticationOptions, CancellationToken cancellationToken = default)
        {
            try
            {
                var lowerStream = LowerAttach.GetStream(autoFlush: false);

                var ssl = new PalSslStream(lowerStream).AddToLady(this);

                await ssl.AuthenticateAsClientAsync(sslClientAuthenticationOptions, CancelWatcher.CancelToken);

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

                var upperStreamWrapper = new FastPipeEndStreamWrapper(this.Lady, UpperAttach.PipeEnd, ssl, CancelWatcher.CancelToken);

                return new FastSock(this.Lady, this);
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
                            var listenTcp = TcpListener._InternalGetNewTcpStubForListen(listenLady, _InternalSelfCancelToken);

                            listenTcp.Listen(new IPEndPoint(IPAddress, Port));

                            Status = ListenStatus.Listening;

                            while (true)
                            {
                                _InternalSelfCancelToken.ThrowIfCancellationRequested();

                                AsyncCleanuperLady ladyForNewTcpStub = new AsyncCleanuperLady();

                                FastSock sock = await listenTcp.AcceptAsync(ladyForNewTcpStub);

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

        readonly Dictionary<Task, FastSock> RunningAcceptedTasks = new Dictionary<Task, FastSock>();

        readonly CancellationTokenSource CancelSource = new CancellationTokenSource();

        public delegate Task AcceptedProcCallback(Listener listener, FastSock newSock);

        AcceptedProcCallback AcceptedProc { get; }

        public int CurrentConnections
        {
            get
            {
                lock (RunningAcceptedTasks)
                    return RunningAcceptedTasks.Count;
            }
        }

        public FastTcpListenerBase(AsyncCleanuperLady lady, AcceptedProcCallback acceptedProc)
        {
            AcceptedProc = acceptedProc;

            AsyncCleanuper = new AsyncCleanuper(this);

            lady.Add(this);
        }

        internal protected abstract FastTcpProtocolStubBase _InternalGetNewTcpStubForListen(AsyncCleanuperLady lady, CancellationToken cancel);

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

        async Task InternalSocketAcceptedAsync(Listener listener, FastSock sock, AsyncCleanuperLady lady)
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

        void InternalSocketAccepted(Listener listener, FastSock sock, AsyncCleanuperLady lady)
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
            List<FastSock> disconnectStubs = new List<FastSock>();

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
        public FastPalTcpListener(AsyncCleanuperLady lady, AcceptedProcCallback acceptedProc) : base(lady, acceptedProc) { }

        protected internal override FastTcpProtocolStubBase _InternalGetNewTcpStubForListen(AsyncCleanuperLady lady, CancellationToken cancel)
            => new FastPalTcpProtocolStub(lady, null, null, cancel);
    }
}

