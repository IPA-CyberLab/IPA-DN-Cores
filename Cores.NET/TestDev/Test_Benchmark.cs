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
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable CS0162
#pragma warning disable CS0219
#pragma warning disable CS1998

namespace IPA.TestDev
{
    static class BenchmarkTestTarget1
    {
        public static readonly CriticalSection LockObj_ReadOnly = new CriticalSection();
        public static CriticalSection LockObj_Property { get; } = new CriticalSection();

        public static readonly int Int_ReadOnly = 123;
        public static int Int_GetOnly { get; private set; } = 123;
        public static readonly Copenhagen<int> Int_Copenhagen_ReadOnly = 123;
        public static Copenhagen<int> Int_Copenhagen_GetOnly { get; private set; } = 123;

        public static readonly string String_ReadOnly = "Hello";
        public static string String_GetOnly { get; private set; } = "Hello";
        public static readonly Copenhagen<string> String_Copenhagen_ReadOnly = "Hello";
        public static Copenhagen<string> String_Copenhagen_GetOnly { get; private set; } = "Hello";

        public static async Task<int> SampleAsyncMethod()
        {
            Limbo.SInt32Volatile++;

            return Limbo.SInt32Volatile;
        }

        public static async Task EmptyAsyncMethod() { }

        public static async Task AsyncCallMethodFromAsyncMethod(int count)
        {
            for (int i = 0; i < count; i++)
                await SampleAsyncMethod();
        }

        public static void AsyncCallMethodFromSyncMethod(int count)
        {
            for (int i = 0; i < count; i++)
                SampleAsyncMethod()._GetResult();
        }

        public static async Task<int> SimpleAsyncMethod()
        {
            return Limbo.SInt32Volatile;
        }

        public static async Task<int> CallAsyncWithAwait()
        {
            return await SimpleAsyncMethod();
        }

        public static Task<int> CallAsyncWithNonAwait()
        {
            return SimpleAsyncMethod();
        }

        public static async Task CallAsyncWithAwaitLoop(int count)
        {
            for (int i = 0; i < count; i++)
                await CallAsyncWithAwait();
        }

        public static async Task CallAsyncWithNonAwaitLoop(int count)
        {
            for (int i = 0; i < count; i++)
                await SimpleAsyncMethod();
        }
    }

    class TestClass1
    {
        public int A, B, C;
    }

    class TestClass2<TKey> where TKey : Enum
    {
        public long GetValue(TKey src)
        {
            return src.GetHashCode();
        }
    }


    partial class TestDevCommands
    {
        const int Benchmark_CountForVeryFast = 200000000;
        const int Benchmark_CountForFast = 10000000;
        const int Benchmark_CountForNormal = 10000;
        const int Benchmark_CountForSlow = 100;
        const int Benchmark_CountForVerySlow = 10;

        static void BenchMask_BoostUp_PacketParser(string name)
        {
            var packetMem = Res.AppRoot[name].HexParsedBinary;

            Packet packet = new Packet(packetMem._CloneSpan());

            for (int c = 0; c < 100; c++)
            {
                new PacketParsed(ref packet);
            }
        }

        static void BenchMark_Test1()
        {
            MemoryBuffer<byte> sparseTest1 = new MemoryBuffer<byte>();
            for (int i = 0; i < 10; i++)
            {
                sparseTest1.Write("Hello World"._GetBytes_Ascii());
                sparseTest1.WriteZero(1_000_000);
                sparseTest1.Write("Hello World2"._GetBytes_Ascii());
            }

            int memcopyLength = 10_000_000;
            MemoryBuffer<byte> memcopySrc = new MemoryBuffer<byte>(memcopyLength);
            MemoryBuffer<byte> memcopyDst = new MemoryBuffer<byte>(memcopyLength);
            Span<byte> memcopySrcSpan = memcopySrc.Span;
            for (int i = 0; i < memcopyLength; i++)
            {
                memcopySrcSpan[i] = (byte)i;
            }
            byte[] byteArrayCopy = memcopySrcSpan.ToArray();
            byte[] byteArrayCopy2 = memcopySrcSpan.ToArray();

            int sparseTestSize = 1024 * 1024;

            MemoryBuffer<byte> nonSparse = new byte[sparseTestSize];
            Util.Rand(nonSparse.Span);

            MemoryBuffer<byte> sparse = new byte[sparseTestSize];
            Util.Rand(sparse.Span);
            for (int i = 0; i < 100; i++)
            {
                int pos = Util.RandSInt31() % (sparse.Length - 6000);
                sparse.Span.Slice(pos, 6000).Fill(0);
            }

            MemoryBuffer<byte> allZeroSparse = new byte[sparseTestSize];

            int isZeroTestSize = 4;

            FileStream nativeFile1 = new FileStream(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, false);
            FileStream nativeFile2 = new FileStream(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, false);

            FileObject coresFile1 = Lfs.Create(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), flags: FileOperationFlags.None);
            FileObject coresFile2 = Lfs.Create(Lfs.PathParser.Combine(Env.MyLocalTempDir, $"native_{Str.NewGuid()}.dat"), flags: FileOperationFlags.None);

            Memory<byte> testFileWriteData = new byte[4096];
            Util.Rand(testFileWriteData.Span);


            BenchMask_BoostUp_PacketParser("190527_novlan_simple_udp");
            BenchMask_BoostUp_PacketParser("190527_novlan_simple_tcp");
            BenchMask_BoostUp_PacketParser("190527_vlan_simple_udp");
            BenchMask_BoostUp_PacketParser("190527_vlan_simple_tcp");
            BenchMask_BoostUp_PacketParser("190531_vlan_pppoe_l2tp_tcp");
            BenchMask_BoostUp_PacketParser("190531_vlan_pppoe_l2tp_udp");

            var queue = new MicroBenchmarkQueue()


            .Add(new MicroBenchmark($"Span memory copy 1600 bytes", Benchmark_CountForFast, count =>
            {
                int size = 1600;
                Span<byte> src = new byte[size * 4];
                src.Fill(3);
                Span<byte> dst = new byte[size * 4];
                for (int c = 0; c < count; c++)
                {
                    src.Slice(size / 2, size).CopyTo(dst.Slice(size / 2));
                }

            }), enabled: true, priority: 190528)

            .Add(new MicroBenchmark($"Span memory copy 400 bytes", Benchmark_CountForFast, count =>
            {
                int size = 400;
                Span<byte> src = new byte[size * 4];
                src.Fill(3);
                Span<byte> dst = new byte[size * 4];
                for (int c = 0; c < count; c++)
                {
                    src.Slice(size / 2, size).CopyTo(dst.Slice(size / 2));
                }

            }), enabled: true, priority: 190528)

            .Add(new MicroBenchmark($"Generic Test", Benchmark_CountForNormal, count =>
            {
                for (int c = 0; c < count; c++)
                {
                }

            }), enabled: true, priority: 190531)


            .Add(new MicroBenchmark($"IpChecksum", Benchmark_CountForNormal, count =>
            {
                unsafe
                {
                    Span<byte> src = Secure.Rand(48);
                    int len = src.Length;
                    fixed (byte* ptr = &src[0])
                    {
                        for (int c = 0; c < count; c++)
                        {
                            Limbo.SInt32Volatile += IPUtil.IpChecksum(ptr, len);
                        }
                    }
                }

            }), enabled: true, priority: 190531)

            .Add(new MicroBenchmark($"CalcIPv4Checksum", Benchmark_CountForNormal, count =>
            {
                unsafe
                {
                    IPv4Header h = new IPv4Header();
                    h.HeaderLen = 5;
                    for (int c = 0; c < count; c++)
                    {
                        Limbo.SInt32Volatile += h.CalcIPv4Checksum();
                    }
                }

            }), enabled: true, priority: 190531)

            .Add(new MicroBenchmark($"BuildPacket #1 - Memory", Benchmark_CountForNormal, count =>
            {
                unsafe
                {
                    Span<byte> initialData = "Hello"._GetBytes_Ascii();
                    for (int c = 0; c < count; c++)
                    {
                        Packet p = new Packet(initialData, true);

                        ref var tcpHeader = ref p.PrependSpan<TCPHeader>(sizeof(TCPHeader) + 4);

                        tcpHeader.AckNumber = 123U._Endian32();
                        tcpHeader.SeqNumber = 456U._Endian32();
                        tcpHeader.Checksum = 0x1234U._Endian16();
                        tcpHeader.SrcPort = 80U._Endian16();
                        tcpHeader.DstPort = 443U._Endian16();
                        tcpHeader.Flag = TCPFlags.Ack | TCPFlags.Fin | TCPFlags.Psh | TCPFlags.Rst;
                        tcpHeader.HeaderLen = (byte)((sizeof(TCPHeader) + 4) / 4);
                        tcpHeader.WindowSize = 1234U._Endian16();

                        ref var v4Hedaer = ref p.PrependSpan<IPv4Header>();

                        v4Hedaer.SrcIP = 0x12345678;
                        v4Hedaer.DstIP = 0xdeadbeef;
                        v4Hedaer.Checksum = 0x1234U._Endian16();
                        v4Hedaer.Flags = IPv4Flags.DontFragment | IPv4Flags.MoreFragments;
                        v4Hedaer.HeaderLen = (byte)(sizeof(IPv4Header) / 4);
                        v4Hedaer.Identification = 0x1234U._Endian16();
                        v4Hedaer.Protocol = IPProtocolNumber.TCP;
                        v4Hedaer.TimeToLive = 12;
                        v4Hedaer.TotalLength = (ushort)(sizeof(IPv4Header) + sizeof(TCPHeader) + 4);
                        v4Hedaer.Version = 4;

                        //ref var vlanHeader = ref p.PrependHeader<VLanHeader>();

                        //vlanHeader.VLanId = 12345U._Endian16();
                        //vlanHeader.Protocol = EthernetProtocolId.IPv4._Endian16();

                        ref var etherHeaderData = ref p.PrependSpan<EthernetHeader>();

                        etherHeaderData.Protocol = EthernetProtocolId.VLan._Endian16();

                        unsafe
                        {
                            etherHeaderData.SrcAddress[0] = 0x00; etherHeaderData.SrcAddress[1] = 0xAC; etherHeaderData.SrcAddress[2] = 0x01;
                            etherHeaderData.SrcAddress[3] = 0x23; etherHeaderData.SrcAddress[4] = 0x45; etherHeaderData.SrcAddress[5] = 0x47;

                            etherHeaderData.DestAddress[0] = 0x00; etherHeaderData.DestAddress[1] = 0x98; etherHeaderData.DestAddress[2] = 0x21;
                            etherHeaderData.DestAddress[3] = 0x33; etherHeaderData.DestAddress[4] = 0x89; etherHeaderData.DestAddress[5] = 0x01;
                        }
                    }
                }

            }), enabled: true, priority: 190531)

            .Add(new MicroBenchmark($"ParsePacket #7 - 190531_vlan_pppoe_l2tp_udp", Benchmark_CountForNormal, count =>
            {
                var packetMem = Res.AppRoot["190531_vlan_pppoe_l2tp_udp.txt"].HexParsedBinary;

                Packet packet = new Packet(packetMem._CloneSpan());

                for (int c = 0; c < count; c++)
                {
                    new PacketParsed(ref packet);
                }

            }), enabled: true, priority: 190531)

            .Add(new MicroBenchmark($"ParsePacket #6 - 190531_vlan_pppoe_l2tp_tcp", Benchmark_CountForNormal, count =>
            {
                var packetMem = Res.AppRoot["190531_vlan_pppoe_l2tp_tcp.txt"].HexParsedBinary;

                Packet packet = new Packet(packetMem._CloneSpan());

                for (int c = 0; c < count; c++)
                {
                    new PacketParsed(ref packet);
                }

            }), enabled: true, priority: 190531)

            .Add(new MicroBenchmark($"ParsePacket #5 - 190531_vlan_pppoe_tcp", Benchmark_CountForNormal, count =>
            {
                var packetMem = Res.AppRoot["190531_vlan_pppoe_tcp.txt"].HexParsedBinary;

                Packet packet = new Packet(packetMem._CloneSpan());

                for (int c = 0; c < count; c++)
                {
                    new PacketParsed(ref packet);
                }

            }), enabled: true, priority: 190531)


            .Add(new MicroBenchmark($"ParsePacket #4 - 190527_vlan_simple_udp", Benchmark_CountForNormal, count =>
            {
                var packetMem = Res.AppRoot["190527_vlan_simple_udp.txt"].HexParsedBinary;

                Packet packet = new Packet(packetMem._CloneSpan());

                for (int c = 0; c < count; c++)
                {
                    new PacketParsed(ref packet);
                }

            }), enabled: true, priority: 190531)



            .Add(new MicroBenchmark($"ParsePacket #3 - 190527_vlan_simple_tcp", Benchmark_CountForNormal, count =>
            {
                var packetMem = Res.AppRoot["190527_vlan_simple_tcp.txt"].HexParsedBinary;

                Packet packet = new Packet(packetMem._CloneSpan());

                for (int c = 0; c < count; c++)
                {
                    new PacketParsed(ref packet);
                }

            }), enabled: true, priority: 190531)



            .Add(new MicroBenchmark($"ParsePacket #2 - 190527_novlan_simple_udp", Benchmark_CountForNormal, count =>
            {
                var packetMem = Res.AppRoot["190527_novlan_simple_udp.txt"].HexParsedBinary;

                Packet packet = new Packet(packetMem._CloneSpan());

                for (int c = 0; c < count; c++)
                {
                    new PacketParsed(ref packet);
                }

            }), enabled: true, priority: 190531)


            .Add(new MicroBenchmark($"ParsePacket #1 - 190527_novlan_simple_tcp", Benchmark_CountForNormal, count =>
            {
                var packetMem = Res.AppRoot["190527_novlan_simple_tcp.txt"].HexParsedBinary;

                Packet packet = new Packet(packetMem._CloneSpan());

                for (int c = 0; c < count; c++)
                {
                    new PacketParsed(ref packet);
                }

            }), enabled: true, priority: 190531)

            .Add(new MicroBenchmark($"_IsZeroFastStruct", Benchmark_CountForFast, count =>
            {
                TCPHeader h = new TCPHeader();
                //h.SrcPort = 3;
                for (int c = 0; c < count; c++)
                {
                    h._IsZeroStruct();
                }

            }), enabled: true, priority: 190528)


            .Add(new MicroBenchmark($"sizeof(T)", Benchmark_CountForVeryFast, count =>
            {
                unsafe
                {
                    for (int c = 0; c < count; c++)
                    {
                        Limbo.SInt32 += sizeof(IPv4Header);
                    }
                }

            }), enabled: true, priority: 190528)
            .Add(new MicroBenchmark($"Util.SizeOfStruct(T)", Benchmark_CountForVeryFast, count =>
            {
                unsafe
                {
                    for (int c = 0; c < count; c++)
                    {
                        Limbo.SInt32 += Util.SizeOfStruct<IPv4Header>();
                    }
                }

            }), enabled: true, priority: 190528)

            .Add(new MicroBenchmark($"File Write Small - Cores Object - Async", Benchmark_CountForNormal, count =>
            {
                TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    for (int c = 0; c < count; c++)
                    {
                        await coresFile1.WriteAsync(testFileWriteData);
                    }
                }, false, false)._GetResult();

            }), enabled: true, priority: 190521)


            .Add(new MicroBenchmark($"File Write Small - .NET Native - Sync", Benchmark_CountForNormal, count =>
            {
                TaskUtil.StartSyncTaskAsync(() =>
                {
                    for (int c = 0; c < count; c++)
                    {
                        nativeFile1.Write(testFileWriteData.Span);
                    }
                }, false, false)._GetResult();

            }), enabled: true, priority: 190521)

            .Add(new MicroBenchmark($"BitAny", Benchmark_CountForVeryFast, count =>
            {
                var t1 = IPAddressType.GlobalUnicast | IPAddressType.IPv4_APIPA | IPAddressType.IPv6_AllRouterMulticast;
                for (int c = 0; c < count; c++)
                {
                    Limbo.BoolVolatile = t1.BitAny(IPAddressType.GlobalUnicast);
                }
            }), enabled: true, priority: 190520)

            .Add(new MicroBenchmark($"Convert Enum to Long", Benchmark_CountForVeryFast, count =>
            {
                var value1 = IPAddressType.IPv4_APIPA;
                var value2 = LeakCounterKind.CancelWatcher;
                for (int c = 0; c < count; c++)
                {
                    Limbo.SInt32 += (int)value1._RawReadValueSInt64();
                    Limbo.SInt32 += (int)value2._RawReadValueSInt64();
                }
            }), enabled: true, priority: 190520)

            .Add(new MicroBenchmark($"SingletonFastArraySlim Get", Benchmark_CountForFast, count =>
            {
                SingletonFastArraySlim<LeakCounterKind, string> singletonObj = new SingletonFastArraySlim<LeakCounterKind, string>((x) => "Hello");
                for (int c = 0; c < count; c++)
                {
                    string str = singletonObj[LeakCounterKind.StartDaemon];
                }
            }), enabled: true, priority: 190521)

            .Add(new MicroBenchmark($"SingletonFastArray Get", Benchmark_CountForFast, count =>
            {
                SingletonFastArray<LeakCounterKind, string> singletonObj = new SingletonFastArray<LeakCounterKind, string>((x) => "Hello");
                for (int c = 0; c < count; c++)
                {
                    string str = singletonObj[LeakCounterKind.StartDaemon];
                }
            }), enabled: true, priority: 190521)

            .Add(new MicroBenchmark($"SingletonSlim Get", Benchmark_CountForFast, count =>
            {
                SingletonSlim<int, string> singletonObj = new SingletonSlim<int, string>((x) => "Hello");
                for (int c = 0; c < count; c++)
                {
                    string str = singletonObj[0];
                }
            }), enabled: true, priority: 190521)

            .Add(new MicroBenchmark($"Singleton2 Get", Benchmark_CountForFast, count =>
            {
                Singleton<int, string> singletonObj = new Singleton<int, string>((x) => "Hello");
                for (int c = 0; c < count; c++)
                {
                    string str = singletonObj[0];
                }
            }), enabled: true, priority: 190521)

            .Add(new MicroBenchmark($"Singleton1 Get", Benchmark_CountForFast, count =>
            {
                Singleton<string> singletonObj = new Singleton<string>(() => "Hello");
                for (int c = 0; c < count; c++)
                {
                    string str = singletonObj;
                }
            }), enabled: true, priority: 190521)

            .Add(new MicroBenchmark($"New MemoryBuffer", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    new MemoryBuffer<byte>();
                }
            }), enabled: true, priority: 190521)

            .Add(new MicroBenchmark($"MemoryBuffer Pin Lock / Unlock", Benchmark_CountForNormal, count =>
            {
                var buf = new MemoryBuffer<byte>();
                for (int c = 0; c < count; c++)
                {
                    using (buf.PinLock())
                    {
                    }
                }
            }), enabled: true, priority: 190521)

            //.Add(new MicroBenchmark($"New Packet", Benchmark_CountForFast, count =>
            //{
            //    for (int c = 0; c < count; c++)
            //    {
            //        Packet newPacket = new Packet();
            //        Limbo.ObjectVolatileSlow = newPacket;
            //    }
            //}), enabled: true, priority: 190528)

            //.Add(new MicroBenchmark($"New Packet with Data", Benchmark_CountForFast, count =>
            //{
            //    Memory<byte> hello = new byte[64];// "Hello World Hello World Hello World Hello World Hello World Hello World "._GetBytes_Ascii();
            //    for (int c = 0; c < count; c++)
            //    {
            //        Packet newPacket = new Packet(hello);
            //        Limbo.ObjectVolatileSlow = newPacket;
            //    }
            //}), enabled: true, priority: 190519)

            //.Add(new MicroBenchmark($"Packet struct I/O", Benchmark_CountForFast, count =>
            //{
            //    Memory<byte> hello = "Hello World Hello World Hello World Hello World Hello World Hello World "._GetBytes_Ascii();
            //    long v = 8;
            //    for (int c = 0; c < count; c++)
            //    {
            //        Packet newPacket = new Packet(hello);
            //        newPacket.InsertHeaderHead(v);
            //    }
            //}), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"Util.Rand()", Benchmark_CountForSlow, count =>
            {
                Span<byte> dest = new byte[100];

                for (int c = 0; c < count; c++)
                {
                    Util.Rand(dest);
                }
            }), enabled: true, priority: 190518)

            .Add(new MicroBenchmark($"Secure.Rand()", Benchmark_CountForSlow, count =>
            {
                Span<byte> dest = new byte[100];

                for (int c = 0; c < count; c++)
                {
                    Secure.Rand(dest);
                }
            }), enabled: true, priority: 190518)

            .Add(new MicroBenchmark($"IsZero - NonSparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlySpan<byte> data = nonSparse.Slice(0, isZeroTestSize);

                for (int c = 0; c < count; c++)
                {
                    Util.IsZero(data);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"IsZero - All Zero Sparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlySpan<byte> data = allZeroSparse.Slice(0, isZeroTestSize);

                for (int c = 0; c < count; c++)
                {
                    Util.IsZero(data);
                }
            }), enabled: true, priority: 190519)


            .Add(new MicroBenchmark($"GetSparseChunks - NonSparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlyMemory<byte> data = nonSparse;

                for (int c = 0; c < count; c++)
                {
                    Util.GetSparseChunks(data, 4096);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"GetSparseChunks - Sparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlyMemory<byte> data = sparse;

                for (int c = 0; c < count; c++)
                {
                    Util.GetSparseChunks(data, 4096);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"GetSparseChunks - All Zero Sparse", Benchmark_CountForSlow, count =>
            {
                ReadOnlyMemory<byte> data = allZeroSparse;

                for (int c = 0; c < count; c++)
                {
                    Util.GetSparseChunks(data, 4096);
                }
            }), enabled: true, priority: 190519)

            .Add(new MicroBenchmark($"ParseEnum", Benchmark_CountForNormal, count =>
            {
                string str = LogPriority.Info.ToString();

                for (int c = 0; c < count; c++)
                {
                    str._ParseEnum(LogPriority.Trace);
                }
            }), enabled: true, priority: 190518)

            .Add(new MicroBenchmark($"Memory Copy by Array.CopyTo (Util.CopyByte) {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Util.CopyByte(byteArrayCopy2, 0, byteArrayCopy, 0, byteArrayCopy.Length);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Buffer.BlockCopy {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Buffer.BlockCopy(byteArrayCopy2, 0, byteArrayCopy, 0, byteArrayCopy.Length);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Span.CopyTo {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    var span1 = memcopySrc.Span;
                    var span2 = memcopyDst.Span;
                    span1.CopyTo(span2);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Span.ToArray {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    var span1 = memcopySrc.Span;
                    span1.ToArray();
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Copy by Span loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    var span1 = memcopySrc.Span;
                    var span2 = memcopyDst.Span;
                    for (int i = 0; i < memcopyLength; i++)
                    {
                        span1[i] = span2[i];
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Array loop #1 {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    for (int i = 0; i < byteArrayCopy.Length; i++)
                    {
                        byteArrayCopy[i] = 0;
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Array loop #2 {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    int len = byteArrayCopy.Length;
                    for (int i = 0; i < len; i++)
                    {
                        byteArrayCopy[i] = 0;
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Span Loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Span<byte> span = memcopySrc.Span;
                    for (int i = 0; i < memcopyLength; i++)
                    {
                        span[i] = 0;
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by unsafe pointer loop {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Span<byte> span = memcopySrc.Span;
                    unsafe
                    {
                        fixed (byte* ptr = &span[0])
                        {
                            for (int i = 0; i < memcopyLength; i++)
                            {
                                ptr[i] = 0;
                            }
                        }
                    }
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Span.Fill {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Span<byte> span = memcopySrc.Span;
                    span.Fill(0);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark($"Memory Fill by Array.Fill {memcopyLength._ToString3()} bytes", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    Array.Fill<byte>(byteArrayCopy, 0);
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("GetSparseChunks", Benchmark_CountForSlow, count =>
            {
                for (int c = 0; c < count; c++)
                    Util.GetSparseChunks(sparseTest1.Memory._AsReadOnlyMemory(), 10_000);
            }), enabled: true, priority: 190506)

            .Add(new MicroBenchmark("CallAsyncWithAwait", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.CallAsyncWithAwaitLoop(count)._GetResult();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("CallAsyncWithNonAwait", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.CallAsyncWithNonAwaitLoop(count)._GetResult();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Rand[16]", Benchmark_CountForNormal, count =>
            {
                for (int c = 0; c < count; c++)
                    Util.Rand(16);
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Guid.NewGuid().ToString()", Benchmark_CountForNormal, count =>
            {
                for (int c = 0; c < count; c++)
                    Guid.NewGuid().ToString();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_ReadOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_GetOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_Copenhagen_ReadOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_Copenhagen_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the String_Copenhagen_GetOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.ObjectVolatileSlow = BenchmarkTestTarget1.String_Copenhagen_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_ReadOnly value", Benchmark_CountForVeryFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_GetOnly value", Benchmark_CountForVeryFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_Copenhagen_ReadOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_Copenhagen_ReadOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Read the Int_Copenhagen_GetOnly value", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                    Limbo.SInt32Volatile += BenchmarkTestTarget1.Int_Copenhagen_GetOnly;
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("AsyncCallMethodFromAsyncMethod", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.AsyncCallMethodFromAsyncMethod(count)._GetResult();
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("AsyncCallMethodFromSyncMethod", Benchmark_CountForNormal, count =>
            {
                BenchmarkTestTarget1.AsyncCallMethodFromSyncMethod(count);
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Lock for readonly object", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    lock (BenchmarkTestTarget1.LockObj_ReadOnly)
                        Limbo.SInt32Volatile++;
                }
            }), enabled: true, priority: 190505)

            .Add(new MicroBenchmark("Lock for get property object", Benchmark_CountForFast, count =>
            {
                for (int c = 0; c < count; c++)
                {
                    lock (BenchmarkTestTarget1.LockObj_Property)
                        Limbo.SInt32Volatile++;
                }
            }), enabled: true, priority: 190505);

            queue.Run();
        }

        [ConsoleCommand(
            "BenchMark command",
            "BenchMark [arg]",
            "BenchMark test")]
        static int BenchMark(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            bool enableRecords = true;

            if (Env.IsCoresLibraryDebugBuild || IsDebugBuildChecker.IsDebugBuild || Env.IsDebuggerAttached || CoresConfig.DebugSettings.LeakCheckerFullStackLog)
            {
                Con.WriteLine("*** Warning: The benchmark is under the debug mode or full stack trace mode. We do not record to files.");
                Con.WriteLine();

                enableRecords = false;
            }

            Con.WriteLine();

            GlobalMicroBenchmark.SetParameters(enableRecords);

            GlobalMicroBenchmark.RecordStart();

            BenchMark_Test1();

            return 0;
        }
    }
}
