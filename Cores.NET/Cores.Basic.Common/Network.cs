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
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace IPA.Cores.Basic
{
    // ソケットの種類
    enum SockType
    {
        Unknown = 0,
        Tcp = 1,
        Udp = 2,
    }

    // ソケットイベント
    class SockEvent : IDisposable
    {
        Event win32_event;

        internal List<Sock> unix_socklist;
        IntPtr unix_pipe_read, unix_pipe_write;
        int unix_current_pipe_data;

        bool is_released = false;
        object release_lock = new object();

        public SockEvent()
        {
            if (Env.IsWindows)
            {
                win32_event = new Event();
            }
            else
            {
                unix_socklist = new List<Sock>();
                Unisys.NewPipe(out this.unix_pipe_read, out this.unix_pipe_write);
            }
        }

        ~SockEvent()
        {
            release();
        }

        public void Dispose()
        {
            release();
        }

        void release()
        {
            lock (release_lock)
            {
                if (is_released == false)
                {
                    is_released = true;
                    if (Env.IsUnix)
                    {
                        Unisys.Close(this.unix_pipe_read);
                        Unisys.Close(this.unix_pipe_write);
                    }
                }
            }
        }

        // ソケットをソケットイベントに関連付けして非同期に設定する
        public void JoinSock(Sock sock)
        {
            if (sock.asyncMode)
            {
                return;
            }

            if (Env.IsWindows)
            {
                // Windows
                try
                {
                    if (sock.ListenMode != false || (sock.Type == SockType.Tcp && sock.Connected == false))
                    {
                        return;
                    }

                    Sock.WSAEventSelect(sock.Socket.Handle, win32_event.Handle, 35);
                    sock.Socket.Blocking = false;

                    sock.SockEvent = this;

                    sock.asyncMode = true;
                }
                catch
                {
                }
            }
            else
            {
                // UNIX
                if (sock.ListenMode != false || (sock.Type == SockType.Tcp && sock.Connected == false))
                {
                    return;
                }

                sock.asyncMode = true;

                lock (unix_socklist)
                {
                    unix_socklist.Add(sock);
                }

                sock.Socket.Blocking = false;

                sock.SockEvent = this;

                this.Set();
            }
        }

        // イベントを叩く
        public void Set()
        {
            if (Env.IsWindows)
            {
                this.win32_event.Set();
            }
            else
            {
                if (this.unix_current_pipe_data <= 100)
                {
                    Unisys.Write(this.unix_pipe_write, new byte[] { 0 }, 0, 1);
                    this.unix_current_pipe_data++;
                }
            }
        }

        // イベントを待つ
        public bool Wait(int timeout)
        {
            if (Env.IsWindows)
            {
                if (timeout == 0)
                {
                    return false;
                }

                return this.win32_event.Wait(timeout);
            }
            else
            {
                List<IntPtr> reads = new List<IntPtr>();
                List<IntPtr> writes = new List<IntPtr>();

                lock (this.unix_socklist)
                {
                    foreach (Sock s in this.unix_socklist)
                    {
                        reads.Add(s.Fd);
                        if (s.writeBlocked)
                        {
                            writes.Add(s.Fd);
                        }
                    }
                }

                reads.Add(this.unix_pipe_read);

                if (this.unix_current_pipe_data == 0)
                {
                    Unisys.Poll(reads.ToArray(), writes.ToArray(), timeout);
                }

                int readret;
                byte[] tmp = new byte[1024];
                this.unix_current_pipe_data = 0;
                do
                {
                    readret = Unisys.Read(this.unix_pipe_read, tmp, 0, tmp.Length);
                }
                while (readret >= 1);

                return true;
            }
        }
    }

    // ソケットセット
    class SockSet
    {
        List<Sock> list;

        public const int MaxSocketNum = 60;

        public SockSet()
        {
            Clear();
        }

        public void Add(Sock sock)
        {
            if (sock.Type == SockType.Tcp && sock.Connected == false)
            {
                return;
            }

            if (list.Count >= MaxSocketNum)
            {
                return;
            }

            list.Add(sock);
        }

        public void Clear()
        {
            list = new List<Sock>();
        }

        public void Poll()
        {
            Poll(Timeout.Infinite);
        }
        public void Poll(int timeout)
        {
            Poll(timeout, null);
        }
        public void Poll(Event e1)
        {
            Poll(Timeout.Infinite, e1);
        }
        public void Poll(int timeout, Event e1)
        {
            Poll(timeout, e1, null);
        }
        public void Poll(Event e1, Event e2)
        {
            Poll(Timeout.Infinite, e1, e2);
        }
        public void Poll(int timeout, Event e1, Event e2)
        {
            try
            {
                List<Event> array = new List<Event>();

                // イベント配列の設定
                foreach (Sock s in list)
                {
                    s.initAsyncSocket();
                    if (s.hEvent != null)
                    {
                        array.Add(s.hEvent);
                    }
                }

                if (e1 != null)
                {
                    array.Add(e1);
                }

                if (e2 != null)
                {
                    array.Add(e2);
                }

                if (array.Count == 0)
                {
                    ThreadObj.Sleep(timeout);
                }
                else
                {
                    Event.WaitAny(array.ToArray(), timeout);
                }
            }
            catch
            {
            }
        }
    }

    // IPv6 アドレスの種類
    struct IPv6AddressType
    {
        public bool Unicast;
        public bool LocalUnicast;
        public bool GlobalUnicast;
        public bool Multicast;
        public bool AllNodeMulticast;
        public bool AllRouterMulticast;
        public bool SoliciationMulticast;
        public bool Zero;
        public bool Loopback;
    }

    // IP ユーティリティ
    static class IPUtil
    {
        // ユーザーが利用できるホストアドレスかどうか確認
        public static bool IsIPv4UserHostAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress b = GetBroadcastAddress(ip, subnet);
            IPAddress r = GetRouterAddress(ip, subnet);
            IPAddress n = NormalizeIpNetworkAddress(ip, SubnetMaskToInt4(subnet));

            if (IPUtil.CompareIPAddress(ip, b) || IPUtil.CompareIPAddress(ip, r) || IPUtil.CompareIPAddress(ip, n))
            {
                return false;
            }

            return true;
        }

        // ブロードキャストアドレスの取得
        public static IPAddress GetBroadcastAddress(IPAddress ip, IPAddress subnet)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork || subnet.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("invalid address family.");
            }

            if (IsSubnetMask4(subnet) == false)
            {
                throw new ArgumentException("not a subnet mask.");
            }

            IPAddress network = NormalizeIpNetworkAddress(ip, SubnetMaskToInt4(subnet));

            IPAddress broadcast = IPOr(network, IPNot(subnet));

            return broadcast;
        }

        // ルータアドレスの取得
        public static IPAddress GetRouterAddress(IPAddress ip, IPAddress subnet)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork || subnet.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException("invalid address family.");
            }

            if (IsSubnetMask4(subnet) == false)
            {
                throw new ArgumentException("not a subnet mask.");
            }

            if (SubnetMaskToInt4(subnet) >= 31)
            {
                throw new ArgumentException("subnet mask must be <= 30");
            }

            IPAddress network = NormalizeIpNetworkAddress(ip, SubnetMaskToInt4(subnet));

            IPAddress broadcast = IPOr(network, IPNot(subnet));

            IPAddress router = (new IPv4Addr(broadcast).Add(-1)).GetIPAddress();

            return router;
        }

        // 指定された 2 つの IP ネットワークが重なるかどうか検出
        public static bool IsOverlappedSubnets(IPAddress ip1, IPAddress subnet1, IPAddress ip2, IPAddress subnet2)
        {
            // 正規化
            if (IPUtil.IsSubnetMask(subnet1) == false || IPUtil.IsSubnetMask(subnet2) == false)
            {
                return false;
            }

            ip1 = IPUtil.GetPrefixAddress(ip1, subnet1);
            ip2 = IPUtil.GetPrefixAddress(ip2, subnet2);

            KeyValuePair<IPAddress, IPAddress> minmax1 = GetMinMaxIPFromSubnet(ip1, IPUtil.SubnetMaskToInt(subnet1));
            KeyValuePair<IPAddress, IPAddress> minmax2 = GetMinMaxIPFromSubnet(ip2, IPUtil.SubnetMaskToInt(subnet2));

            BigNumber min1 = IPAddr.FromBytes(minmax1.Key.GetAddressBytes()).GetBigNumber();
            BigNumber max1 = IPAddr.FromBytes(minmax1.Value.GetAddressBytes()).GetBigNumber();

            BigNumber min2 = IPAddr.FromBytes(minmax2.Key.GetAddressBytes()).GetBigNumber();
            BigNumber max2 = IPAddr.FromBytes(minmax2.Value.GetAddressBytes()).GetBigNumber();

            if (min2 >= min1 && min2 <= max1 && max1 >= min2 && max1 <= max2)
            {
                return true;
            }
            if (min1 >= min2 && max1 <= max2)
            {
                return true;
            }
            if (min1 >= min2 && min1 <= max2 && max2 >= min1 && max2 >= max1)
            {
                return true;
            }
            if (min2 >= min1 && max2 <= max1)
            {
                return true;
            }

            return false;
        }

        // IP 個数を計算
        public static long CalcNumIPFromSubnetLen(AddressFamily af, int subnet_len)
        {
            if (af == AddressFamily.InterNetwork)
            {
                return (long)(1UL << (32 - subnet_len));
            }
            else
            {
                int v = 64 - subnet_len;
                if (v < 0)
                {
                    v = 0;
                }
                return (long)(1UL << v);
            }
        }

        // 文字列を IP アドレスに変換
        public static IPAddress StrToIP(string str)
        {
            if (Str.InStr(str, ":") == false && Str.InStr(str, ".") == false)
            {
                throw new ArgumentException("str is not IPv4 nor IPv6 address.");
            }
            IPAddress ret = IPAddress.Parse(str);

            if (ret.AddressFamily != AddressFamily.InterNetwork && ret.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ArgumentException("str is not IPv4 nor IPv6 address.");
            }

            return ret;
        }

        // 指定された IP ネットワークとサブネットマスクから、最初と最後の IP を取得
        public static KeyValuePair<IPAddress, IPAddress> GetMinMaxIPFromSubnet(IPAddress network_address, int subnet_len)
        {
            network_address = NormalizeIpNetworkAddress(network_address, subnet_len);

            if (network_address.AddressFamily == AddressFamily.InterNetwork)
            {
                IPAddress mask = IPUtil.IntToSubnetMask4(subnet_len);
                mask = IPUtil.IPNot(mask);

                BigNumber bi = new IPv4Addr(network_address).GetBigNumber() + (new IPv4Addr(mask).GetBigNumber());

                IPAddress end = new IPv4Addr(FullRoute.BigNumberToByte(bi, AddressFamily.InterNetwork)).GetIPAddress();

                return new KeyValuePair<IPAddress, IPAddress>(network_address, end);
            }
            else if (network_address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                IPAddress mask = IPUtil.IntToSubnetMask6(subnet_len);
                mask = IPUtil.IPNot(mask);

                BigNumber bi = new IPv6Addr(network_address).GetBigNumber() + (new IPv6Addr(mask).GetBigNumber());

                IPAddress end = new IPv6Addr(FullRoute.BigNumberToByte(bi, AddressFamily.InterNetworkV6)).GetIPAddress();

                return new KeyValuePair<IPAddress, IPAddress>(network_address, end);
            }
            else
            {
                throw new ApplicationException("invalid AddressFamily");
            }
        }

        // 指定した IP アドレスに数値を足す
        public static IPAddress IPAdd(IPAddress a, int i)
        {
            IPAddr aa = IPAddr.FromBytes(a.GetAddressBytes());

            BigNumber bi = aa.GetBigNumber();
            bi += i;

            byte[] data = FullRoute.BigNumberToByte(bi, a.AddressFamily);

            return new IPAddress(data);
        }

        // IP アドレスを文字列に変換
        public static string IPToStr(IPAddress ip)
        {
            return ip.ToString();
        }

        // 指定された IP アドレスが IPv4 かどうか検査
        public static bool IsIPv4(IPAddress ip)
        {
            return (ip.AddressFamily == AddressFamily.InterNetwork);
        }

        // 指定された IP アドレスが IPv6 かどうか検査
        public static bool IsIPv6(IPAddress ip)
        {
            return (ip.AddressFamily == AddressFamily.InterNetworkV6);
        }

        // 指定された文字列が IP アドレスかどうか検査
        public static bool IsStrIP(string str)
        {
            if (Str.InStr(str, ":") == false && Str.InStr(str, ".") == false)
            {
                return false;
            }
            try
            {
                StrToIP(str);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 指定された文字列が IPv4 アドレスかどうか検査
        public static bool IsStrIPv4(string str)
        {
            try
            {
                return IsIPv4(StrToIP(str));
            }
            catch
            {
                return false;
            }
        }

        // 指定された文字列が IPv6 アドレスかどうか検査
        public static bool IsStrIPv6(string str)
        {
            try
            {
                return IsIPv6(StrToIP(str));
            }
            catch
            {
                return false;
            }
        }

        // IPv4 / IPv6 マスクを文字列に変換
        public static string MaskToStr(IPAddress mask, bool alwaysFullAddress)
        {
            if (alwaysFullAddress == false && IsSubnetMask(mask))
            {
                return Str.IntToStr(SubnetMaskToInt(mask));
            }
            else
            {
                return IPToStr(mask);
            }
        }

        // 文字列を IPv4 / IPv6 マスクに変換
        public static IPAddress StrToMask(string str, bool ipv6)
        {
            if (ipv6)
            {
                return StrToMask6(str);
            }
            else
            {
                return StrToMask4(str);
            }
        }

        // 文字列を IPv4 マスクに変換
        public static IPAddress StrToMask4(string str)
        {
            if (str.StartsWith("/"))
            {
                str = str.Substring(1);
            }

            if (Str.IsNumber(str))
            {
                int n = Str.StrToInt(str);
                if (n >= 0 && n <= 32)
                {
                    return IntToSubnetMask4(n);
                }
                else
                {
                    throw new ArgumentException("str is not subnet mask.");
                }
            }
            else
            {
                IPAddress ip = StrToIP(str);
                if (IsIPv4(ip) == false)
                {
                    throw new ArgumentException("str is not IPv4 address.");
                }
                else
                {
                    return ip;
                }
            }
        }

        // 文字列を IPv6 マスクに変換
        public static IPAddress StrToMask6(string str)
        {
            if (str.StartsWith("/"))
            {
                str = str.Substring(1);
            }

            if (Str.IsNumber(str))
            {
                int n = Str.StrToInt(str);

                if (n >= 0 && n <= 128)
                {
                    return IntToSubnetMask6(n);
                }
                else
                {
                    throw new ArgumentException("str is not subnet mask.");
                }
            }
            else
            {
                IPAddress ip = StrToIP(str);
                if (IsIPv6(ip) == false)
                {
                    throw new ArgumentException("str is not IPv6 address.");
                }
                else
                {
                    return ip;
                }
            }
        }

        // IP アドレスとサブネットマスクのパース
        public static void ParseIPAndSubnetMask(string str, out IPAddress ip, out IPAddress mask)
        {
            ParseIPAndMask(str, out ip, out mask);

            if (IsIPv4(ip))
            {
                if (IsSubnetMask4(mask) == false)
                {
                    throw new ArgumentException("mask is not a subnet.");
                }
            }
            else
            {
                if (IsSubnetMask6(mask) == false)
                {
                    throw new ArgumentException("mask is not a subnet.");
                }
            }
        }

        // IP アドレスとマスクのパース
        public static void ParseIPAndMask(string str, out IPAddress ip, out IPAddress mask)
        {
            string ipstr, subnetstr;
            IPAddress ip2, mask2;

            string[] tokens = str.Split('/');
            if (tokens.Length != 2)
            {
                throw new ArgumentException("str is invalid.");
            }

            ipstr = tokens[0].Trim();
            subnetstr = tokens[1].Trim();

            ip2 = StrToIP(ipstr);
            if (IsStrIP(subnetstr))
            {
                mask2 = StrToIP(subnetstr);
                if (IsIPv6(ip2) && IsIPv6(mask2))
                {
                    // 両方とも IPv6
                    ip = ip2;
                    mask = mask2;
                }
                else if (IsIPv4(ip2) && IsIPv4(mask2))
                {
                    // 両方とも IPv4
                    ip = ip2;
                    mask = mask2;
                }
                else
                {
                    throw new ArgumentException("ip or mask is invalid.");
                }
            }
            else
            {
                if (Str.IsNumber(subnetstr) == false)
                {
                    throw new ArgumentException("mask is invalid.");
                }
                else
                {
                    int i = Str.StrToInt(subnetstr);
                    // マスク部が数値
                    if (IsIPv6(ip2) && i >= 0 && i <= 128)
                    {
                        ip = ip2;
                        mask = IntToSubnetMask6(i);
                    }
                    else if (i <= 32)
                    {
                        ip = ip2;
                        mask = IntToSubnetMask4(i);
                    }
                    else
                    {
                        throw new ArgumentException("mask is invalid.");
                    }
                }
            }
        }

        // IPv4 を uint に変換する
        public static uint IPToUINT(IPAddress ip)
        {
            int i;
            Buf b = new Buf();
            if (IsIPv4(ip) == false)
            {
                throw new ArgumentException("ip is not IPv4.");
            }

            byte[] data = ip.GetAddressBytes();

            for (i = 0; i < 4; i++)
            {
                b.WriteByte(data[i]);
            }

            b.SeekToBegin();

            return b.RawReadInt();
        }

        // uint を IPv4 に変換する
        public static IPAddress UINTToIP(uint value)
        {
            Buf b = new Buf();
            b.RawWriteInt(value);

            b.SeekToBegin();

            return new IPAddress(b.ByteData);
        }

        // 指定された IP アドレスがサブネットマスクかどうか調べる
        public static bool IsSubnetMask(IPAddress ip)
        {
            if (IsIPv6(ip))
            {
                return IsSubnetMask6(ip);
            }
            else if (IsIPv4(ip))
            {
                return IsSubnetMask4(ip);
            }
            else
            {
                return false;
            }
        }
        public static bool IsSubnetMask4(IPAddress ip)
        {
            uint i;

            if (IsIPv4(ip) == false)
            {
                throw new ArgumentException("ip is not IPv4.");
            }

            i = IPToUINT(ip);
            i = Util.Endian(i);

            switch (i)
            {
                case 0x00000000:
                case 0x80000000:
                case 0xC0000000:
                case 0xE0000000:
                case 0xF0000000:
                case 0xF8000000:
                case 0xFC000000:
                case 0xFE000000:
                case 0xFF000000:
                case 0xFF800000:
                case 0xFFC00000:
                case 0xFFE00000:
                case 0xFFF00000:
                case 0xFFF80000:
                case 0xFFFC0000:
                case 0xFFFE0000:
                case 0xFFFF0000:
                case 0xFFFF8000:
                case 0xFFFFC000:
                case 0xFFFFE000:
                case 0xFFFFF000:
                case 0xFFFFF800:
                case 0xFFFFFC00:
                case 0xFFFFFE00:
                case 0xFFFFFF00:
                case 0xFFFFFF80:
                case 0xFFFFFFC0:
                case 0xFFFFFFE0:
                case 0xFFFFFFF0:
                case 0xFFFFFFF8:
                case 0xFFFFFFFC:
                case 0xFFFFFFFE:
                case 0xFFFFFFFF:
                    return true;
            }

            return false;
        }
        public static IPAddress IntToSubnetMask4(int i)
        {
            uint ret = 0xffffffff;

            switch (i)
            {
                case 0: ret = 0x00000000; break;
                case 1: ret = 0x80000000; break;
                case 2: ret = 0xC0000000; break;
                case 3: ret = 0xE0000000; break;
                case 4: ret = 0xF0000000; break;
                case 5: ret = 0xF8000000; break;
                case 6: ret = 0xFC000000; break;
                case 7: ret = 0xFE000000; break;
                case 8: ret = 0xFF000000; break;
                case 9: ret = 0xFF800000; break;
                case 10: ret = 0xFFC00000; break;
                case 11: ret = 0xFFE00000; break;
                case 12: ret = 0xFFF00000; break;
                case 13: ret = 0xFFF80000; break;
                case 14: ret = 0xFFFC0000; break;
                case 15: ret = 0xFFFE0000; break;
                case 16: ret = 0xFFFF0000; break;
                case 17: ret = 0xFFFF8000; break;
                case 18: ret = 0xFFFFC000; break;
                case 19: ret = 0xFFFFE000; break;
                case 20: ret = 0xFFFFF000; break;
                case 21: ret = 0xFFFFF800; break;
                case 22: ret = 0xFFFFFC00; break;
                case 23: ret = 0xFFFFFE00; break;
                case 24: ret = 0xFFFFFF00; break;
                case 25: ret = 0xFFFFFF80; break;
                case 26: ret = 0xFFFFFFC0; break;
                case 27: ret = 0xFFFFFFE0; break;
                case 28: ret = 0xFFFFFFF0; break;
                case 29: ret = 0xFFFFFFF8; break;
                case 30: ret = 0xFFFFFFFC; break;
                case 31: ret = 0xFFFFFFFE; break;
                case 32: ret = 0xFFFFFFFF; break;
                default:
                    throw new ArgumentException("i is not IPv4 subnet numeric.");
            }

            ret = Util.Endian(ret);

            return UINTToIP(ret);
        }

        // サブネットマスクを数値に変換する
        public static int SubnetMaskToInt(IPAddress ip)
        {
            if (IsIPv6(ip))
            {
                return SubnetMaskToInt6(ip);
            }
            else if (IsIPv4(ip))
            {
                return SubnetMaskToInt4(ip);
            }
            else
            {
                throw new ArgumentException("ip is not IPv4 nor IPv6.");
            }
        }
        public static int SubnetMaskToInt4(IPAddress ip)
        {
            int i;
            if (IsIPv4(ip) == false)
            {
                throw new ArgumentException("ip is not IPv4.");
            }

            for (i = 0; i <= 32; i++)
            {
                IPAddress tmp = IntToSubnetMask4(i);

                if (Util.CompareByte(tmp.GetAddressBytes(), ip.GetAddressBytes()))
                {
                    return i;
                }
            }

            throw new ArgumentException("ip is not IPv4 Subnet Mask.");
        }
        public static int SubnetMaskToInt6(IPAddress ip)
        {
            int i;
            if (IsIPv6(ip) == false)
            {
                throw new ArgumentException("ip is not IPv6.");
            }

            for (i = 0; i <= 128; i++)
            {
                IPAddress tmp = IntToSubnetMask6(i);

                if (Util.CompareByte(tmp.GetAddressBytes(), ip.GetAddressBytes()))
                {
                    return i;
                }
            }

            throw new ArgumentException("ip is not IPv6 Subnet Mask.");
        }

        // サブネットマスクの作成
        public static IPAddress IntToSubnetMask6(int i)
        {
            if (!(i >= 0 && i <= 128))
            {
                throw new ArgumentException("i is not IPv6 Subnet Mask Numeric.");
            }
            int j = i / 8;
            int k = i % 8;
            int z;

            byte[] tmp = new byte[16];

            for (z = 0; z < 16; z++)
            {
                if (z < j)
                {
                    tmp[z] = 0xff;
                }
                else if (z == j)
                {
                    tmp[z] = (byte)((~(0xff >> k)) & 0xff);
                }
            }

            return new IPAddress(tmp);
        }

        // 指定された文字列が IPv6 サブネットマスクかどうか識別する
        public static bool IsSubnetMask6(IPAddress ip)
        {
            try
            {
                SubnetMaskToInt6(ip);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // IP ネットワークアドレスの正規化
        public static IPAddress NormalizeIpNetworkAddress(IPAddress a, int subnet_length)
        {
            IPAddress mask;

            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                mask = IPUtil.IntToSubnetMask4(subnet_length);
            }
            else if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                mask = IPUtil.IntToSubnetMask6(subnet_length);
            }
            else
            {
                throw new ApplicationException("invalid AddressFamily");
            }

            IPAddress ret = IPUtil.IPAnd(a, mask);

            return ret;
        }

        // すべてのビットが立っているアドレス
        public static IPAddress AllFilledAddress
        {
            get
            {
                byte[] data = new byte[16];
                int i;
                for (i = 0; i < 16; i++)
                {
                    data[i] = 0xff;
                }

                return new IPAddress(data);
            }
        }

        // ループバックアドレス
        public static IPAddress LoopbackAddress
        {
            get
            {
                byte[] data = new byte[16];
                data[15] = 0x01;
                return new IPAddress(data);
            }
        }

        // 全ノードマルチキャストアドレス
        public static IPAddress AllNodeMulticaseAddress
        {
            get
            {
                byte[] data = new byte[16];
                data[0] = 0xff;
                data[1] = 0x02;
                data[15] = 0x01;
                return new IPAddress(data);
            }
        }

        // 全ルータマルチキャストアドレス
        public static IPAddress AllRouterMulticastAddress
        {
            get
            {
                byte[] data = new byte[16];
                data[0] = 0xff;
                data[1] = 0x02;
                data[15] = 0x02;
                return new IPAddress(data);
            }
        }

        // IPv6 アドレスの論理演算
        public static IPAddress IPAnd(IPAddress a, IPAddress b)
        {
            if (a.AddressFamily != b.AddressFamily)
            {
                throw new ArgumentException("a.AddressFamily != b.AddressFamily");
            }
            byte[] a_data = a.GetAddressBytes();
            byte[] b_data = b.GetAddressBytes();
            byte[] data = new byte[a_data.Length];
            int i;
            for (i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(a_data[i] & b_data[i]);
            }
            return new IPAddress(data);
        }
        public static IPAddress IPOr(IPAddress a, IPAddress b)
        {
            if (a.AddressFamily != b.AddressFamily)
            {
                throw new ArgumentException("a.AddressFamily != b.AddressFamily");
            }
            byte[] a_data = a.GetAddressBytes();
            byte[] b_data = b.GetAddressBytes();
            byte[] data = new byte[a_data.Length];
            int i;
            for (i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(a_data[i] | b_data[i]);
            }
            return new IPAddress(data);
        }
        public static IPAddress IPNot(IPAddress a)
        {
            byte[] a_data = a.GetAddressBytes();
            byte[] data = new byte[a_data.Length];
            int i;
            for (i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(~(a_data[i]));
            }
            return new IPAddress(data);
        }

        // IP アドレス同士を比較する
        public static bool CompareIPAddress(string a, string b)
        {
            return CompareIPAddress(StrToIP(a), StrToIP(b));
        }
        public static bool CompareIPAddress(IPAddress a, IPAddress b)
        {
            if (a.AddressFamily != b.AddressFamily)
            {
                return false;
            }

            return Util.CompareByte(a.GetAddressBytes(), b.GetAddressBytes());
        }

        // IP アドレス同士を比較する
        public static int CompareIPAddressRetInt(string a, string b)
        {
            return CompareIPAddressRetInt(StrToIP(a), StrToIP(b));
        }
        public static int CompareIPAddressRetInt(IPAddress a, IPAddress b)
        {
            if (a.AddressFamily != b.AddressFamily)
            {
                return a.AddressFamily.CompareTo(b.AddressFamily);
            }

            return Util.CompareByteRetInt(a.GetAddressBytes(), b.GetAddressBytes());
        }

        // IP アドレスを正規化する
        public static string NormalizeIPAddress(string str)
        {
            Str.NormalizeString(ref str);

            try
            {
                IPAddr a = IPAddr.FromString(str);

                if (a.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return a.ToString().ToLowerInvariant();
                }
                else if (a.AddressFamily == AddressFamily.InterNetwork)
                {
                    return a.ToString().ToLowerInvariant();
                }
            }
            catch
            {
            }

            return str.ToLowerInvariant();
        }

        // MAC アドレスをバイト配列に変換する
        public static byte[] MacToBytes(string mac)
        {
            Str.NormalizeString(ref mac);

            mac = mac.Replace(":", "").Replace("-", "");

            byte[] ret = Str.HexToByte(mac);

            if (ret.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            return ret;
        }

        // バイト配列を MAC アドレスに変換する
        public static string BytesToMac(byte[] b, bool linux_style)
        {
            if (b.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            string ret = Str.ByteToHex(b, linux_style ? ":" : "-");

            if (linux_style == false)
            {
                ret = ret.ToUpperInvariant();
            }
            else
            {
                ret = ret.ToLowerInvariant();
            }

            return ret;
        }

        // MAC アドレスを正規化する
        public static string NormalizeMac(string mac, bool linux_style)
        {
            return BytesToMac(MacToBytes(mac), linux_style);
        }

        // IPv6 アドレスを正規化する
        public static string NormalizeIPv6Address(string str)
        {
            IPAddr a = IPAddr.FromString(str);

            if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return a.ToString().ToLowerInvariant();
            }

            throw new ApplicationException("a.AddressFamily != AddressFamily.InterNetworkV6");
        }

        // IPv4 アドレスを正規化する
        public static string NormalizeIPv4Address(string str)
        {
            IPAddr a = IPAddr.FromString(str);

            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                return a.ToString().ToLowerInvariant();
            }

            throw new ApplicationException("a.AddressFamily != AddressFamily.InterNetwork");
        }

        // IPv6 アドレスの種類を取得する
        public static IPv6AddressType GetIPv6AddressType(IPAddress ip)
        {
            IPv6AddressType ret = new IPv6AddressType();
            byte[] data;
            if (IsIPv6(ip) == false)
            {
                throw new ArgumentException("ip is not IPv6.");
            }

            data = ip.GetAddressBytes();

            if (data[0] == 0xff)
            {
                IPAddress all_node = AllNodeMulticaseAddress;
                IPAddress all_router = AllRouterMulticastAddress;

                ret.Multicast = true;

                if (CompareIPAddress(ip, all_node))
                {
                    ret.AllNodeMulticast = true;
                }
                else if (CompareIPAddress(ip, all_router))
                {
                    ret.AllRouterMulticast = true;
                }
                else
                {
                    byte[] addr = ip.GetAddressBytes();
                    if (addr[1] == 0x02 && addr[2] == 0 && addr[3] == 0 &&
                        addr[4] == 0 && addr[5] == 0 && addr[6] == 0 &&
                        addr[7] == 0 && addr[8] == 0 && addr[9] == 0 &&
                        addr[10] == 0 && addr[11] == 0x01 && addr[12] == 0xff)
                    {
                        ret.SoliciationMulticast = true;
                    }
                }
            }
            else
            {
                ret.Unicast = true;

                byte[] addr = ip.GetAddressBytes();

                if (addr[0] == 0xfe && (addr[1] & 0xc0) == 0x80)
                {
                    ret.LocalUnicast = true;
                }
                else
                {
                    ret.GlobalUnicast = true;

                    if (Util.IsZero(addr))
                    {
                        ret.Zero = true;
                    }
                    else
                    {
                        if (CompareIPAddress(ip, LoopbackAddress))
                        {
                            ret.Loopback = true;
                        }
                    }
                }
            }

            return ret;
        }

        // ローカルホストの IPv4 アドレスリストを取得
        public static IPAddress[] GetLocalIPv4Addresses()
        {
            try
            {
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

                List<IPAddress> ret = new List<IPAddress>();

                foreach (IPAddress a in localIPs)
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (a.GetAddressBytes()[0] != 127)
                        {
                            ret.Add(a);
                        }
                    }
                }

                return ret.ToArray();
            }
            catch
            {
                return new IPAddress[0];
            }
        }

        // マルチキャストアドレスに対応した MAC アドレスの生成
        public static byte[] GenerateMulticastMacAddress(IPAddress ip)
        {
            byte[] mac = new byte[6];
            byte[] addr = ip.GetAddressBytes();

            mac[0] = 0x33;
            mac[1] = 0x33;
            mac[2] = addr[12];
            mac[3] = addr[13];
            mac[4] = addr[14];
            mac[5] = addr[15];

            return mac;
        }

        // 要請ノードマルチキャストアドレスを取得
        public static IPAddress GetSoliciationMulticastAddr(IPAddress src)
        {
            IPAddress prefix, mask104, or1, or2;
            IPAddress dst;

            byte[] addr = new byte[16];
            addr[0] = 0xff;
            addr[1] = 0x02;
            addr[11] = 0x01;
            addr[12] = 0xff;

            prefix = new IPAddress(addr);

            mask104 = IntToSubnetMask6(104);

            or1 = IPAnd(prefix, mask104);
            or2 = IPAnd(src, mask104);
            dst = IPOr(or1, or2);

            dst.ScopeId = src.ScopeId;

            return dst;
        }

        // IP アドレスがオールゼロかどうか検査する
        public static bool IsZeroIP(IPAddress ip)
        {
            return Util.IsZero(ip.GetAddressBytes());
        }

        // プレフィックスアドレスの取得
        public static IPAddress GetPrefixAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress dst = IPAnd(ip, subnet);

            if (IsIPv6(ip))
            {
                dst.ScopeId = ip.ScopeId;
            }

            return dst;
        }

        // ホストアドレスの取得
        public static IPAddress GetHostAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress dst = IPAnd(ip, IPNot(subnet));

            if (IsIPv6(ip))
            {
                dst.ScopeId = ip.ScopeId;
            }

            return dst;
        }

        // ネットワークプレフィックスアドレスかどうかチェックする
        public static bool IsNetworkPrefixAddress(IPAddress ip, IPAddress subnet)
        {
            IPAddress host;

            host = GetHostAddress(ip, subnet);

            return IsZeroIP(host);
        }

        // 同一のネットワークかどうか調べる
        public static bool IsInSameNetwork(IPAddress ip1, IPAddress ip2, IPAddress subnet)
        {
            IPAddress prefix1, prefix2;

            if (IsIPv6(ip1))
            {
                if (ip1.ScopeId != ip2.ScopeId)
                {
                    return false;
                }
            }

            prefix1 = GetPrefixAddress(ip1, subnet);
            prefix2 = GetPrefixAddress(ip2, subnet);

            if (CompareIPAddress(prefix1, prefix2))
            {
                return true;
            }

            return false;
        }

        // MAC アドレスから EUI-64 アドレスを生成する
        public static IPAddress GenerateEui64LocalAddress(byte[] mac)
        {
            byte[] tmp = new byte[8];

            Util.CopyByte(tmp, 0, mac, 0, 3);
            Util.CopyByte(tmp, 5, mac, 3, 3);

            tmp[3] = 0xff;
            tmp[4] = 0xfe;
            tmp[0] = (byte)(((~(tmp[0] & 0x02)) & 0x02) | (tmp[0] & 0xfd));

            byte[] addr = new byte[16];
            addr[0] = 0xfe;
            addr[1] = 0x80;

            Util.CopyByte(addr, 8, tmp, 0, 8);

            return new IPAddress(addr);
        }

        // MAC アドレスからグローバルアドレスを生成する
        public static IPAddress GenerateEui64GlobalAddress(IPAddress prefix, IPAddress subnet, byte[] mac)
        {
            if (prefix.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ApplicationException("Not IPv6 address");
            }

            if (subnet.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ApplicationException("Not IPv6 address");
            }

            byte[] tmp = new byte[8];

            Util.CopyByte(tmp, 0, mac, 0, 3);
            Util.CopyByte(tmp, 5, mac, 3, 3);

            tmp[3] = 0xff;
            tmp[4] = 0xfe;
            tmp[0] = (byte)(((~(tmp[0] & 0x02)) & 0x02) | (tmp[0] & 0xfd));

            byte[] addr = new byte[16];

            Util.CopyByte(addr, 8, tmp, 0, 8);

            IPAddress subnet_not = IPNot(subnet);
            IPAddress or1 = IPAnd(new IPAddress(addr), subnet_not);
            IPAddress or2 = IPAnd(prefix, subnet);

            return IPOr(or1, or2);
        }

        // MAC アドレスを比較する
        public static int CompareMacAddressRetInt(string a, string b)
        {
            return CompareMacAddressRetInt(MacToBytes(a), MacToBytes(b));
        }
        public static int CompareMacAddressRetInt(byte[] a, byte[] b)
        {
            if (a.Length != 6 || b.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            return Util.CompareByteRetInt(a, b);
        }
        public static bool CompareMacAddress(string a, string b)
        {
            return CompareMacAddress(MacToBytes(a), MacToBytes(b));
        }
        public static bool CompareMacAddress(byte[] a, byte[] b)
        {
            if (a.Length != 6 || b.Length != 6)
            {
                throw new ApplicationException("Invalid MAC address");
            }

            return Util.CompareByte(a, b);
        }

        // EUI-64 から MAC アドレスを取得する
        public static byte[] GetMacAddressFromEui64Address(string addr)
        {
            return GetMacAddressFromEui64Address(StrToIP(addr));
        }
        public static byte[] GetMacAddressFromEui64Address(IPAddress addr)
        {
            if (addr.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ApplicationException("Not IPv6 address");
            }

            byte[] tmp = addr.GetAddressBytes();

            tmp = Util.CopyByte(tmp, 8);

            if (tmp[3] != 0xff || tmp[4] != 0xfe)
            {
                throw new ApplicationException("Not an EUI-64 address");
            }

            tmp[0] = (byte)(((~(tmp[0] & 0x02)) & 0x02) | (tmp[0] & 0xfd));

            byte[] mac = new byte[6];
            mac[0] = tmp[0];
            mac[1] = tmp[1];
            mac[2] = tmp[2];
            mac[3] = tmp[5];
            mac[4] = tmp[6];
            mac[5] = tmp[7];

            return mac;
        }

        // 指定された IPv6 アドレスが指定された MAC アドレスから生成されたものかどうか判定する
        public static bool IsIPv6AddressEui64ForMac(string addr, string mac)
        {
            try
            {
                return IsIPv6AddressEui64ForMac(StrToIP(addr), MacToBytes(mac));
            }
            catch
            {
            }

            return false;
        }
        public static bool IsIPv6AddressEui64ForMac(IPAddress addr, byte[] mac)
        {
            try
            {
                return CompareMacAddress(mac, GetMacAddressFromEui64Address(addr));
            }
            catch
            {
            }

            return false;
        }
    }

    // ソケット
    class Sock
    {
        static readonly SocketFlags DefaultSocketFlags;
        static Sock()
        {
            if (Env.IsWindows)
            {
                Sock.DefaultSocketFlags = SocketFlags.Partial;
            }
            else
            {
                Sock.DefaultSocketFlags = SocketFlags.None;
            }
        }

        public const int TimeoutInfinite = Timeout.Infinite;
        public const int TimeoutTcpPortCheck = 10 * 1000;
        public const int TimeoutSslConnect = 15 * 1000;
        public const int TimeoutGetHostname = 1500;
        public const int MaxSendBufMemSize = 10 * 1024 * 1024;
        public const int SockLater = -1;
        public const string SecureProtocolKey = "SecureProtocol";
        public bool IsIPv6 = false;

        internal object lockObj;
        internal object disconnectLockObj;
        internal object sslLockObj;
        internal Socket socket;
        public Socket Socket
        {
            get { return socket; }
        }
        internal SockType type;
        public SockType Type
        {
            get { return type; }
        }
        internal bool connected;
        public bool Connected
        {
            get { return connected; }
        }
        internal bool serverMode;
        public bool ServerMode
        {
            get { return serverMode; }
        }
        internal bool asyncMode;
        public bool AsyncMode
        {
            get { return asyncMode; }
        }
        internal bool secureMode;
        public bool SecureMode
        {
            get { return secureMode; }
        }
        internal bool listenMode;
        public bool ListenMode
        {
            get { return listenMode; }
        }
        internal bool cancelAccept;
        internal IPAddress remoteIP;
        public IPAddress RemoteIP
        {
            get { return remoteIP; }
        }
        internal IPAddress localIP;
        public IPAddress LocalIP
        {
            get { return localIP; }
        }
        internal string remoteHostName;
        public string RemoteHostName
        {
            get { return remoteHostName; }
        }
        internal int remotePort;
        public int RemotePort
        {
            get { return remotePort; }
        }
        internal int localPort;
        public int LocalPort
        {
            get { return localPort; }
        }
        internal long sendSize;
        public long SendSize
        {
            get { return sendSize; }
        }
        internal long recvSize;
        public long RecvSize
        {
            get { return recvSize; }
        }
        internal long sendNum;
        public long SendNum
        {
            get { return sendNum; }
        }
        internal long recvNum;
        public long RecvNum
        {
            get { return recvNum; }
        }
        internal bool ignoreRecvErr;
        public bool IgnoreLastRecvError
        {
            get { return ignoreRecvErr; }
        }
        public ulong LastRecvError = 0;
        internal bool ignoreSendErr;
        public bool IgnoreLastSendError
        {
            get { return IgnoreLastSendError; }
        }
        internal int timeOut;
        internal bool writeBlocked;
        internal bool disconnecting;
        internal bool udpBroadcast;
        public bool UDPBroadcastMode
        {
            get { return udpBroadcast; }
        }
        internal object param;
        public object Param
        {
            get
            {
                return param;
            }
            set
            {
                param = value;
            }
        }
        internal Event hEvent;

        public SockEvent SockEvent = null;

        public IntPtr Fd = new IntPtr(-1);

        [DllImport("ws2_32.dll", SetLastError = true)]
        internal static extern int WSAEventSelect(IntPtr s, IntPtr hEventObject, int lNetworkEvents);

        // 初期化
        private Sock()
        {
            this.lockObj = new object();
            this.disconnectLockObj = new object();
            this.sslLockObj = new object();
            this.socket = null;
            this.type = SockType.Unknown;
            this.ignoreRecvErr = this.ignoreSendErr = false;
        }

        // UDP 受信
        public byte[] RecvFrom(out IPEndPoint src, int size)
        {
            byte[] data = new byte[size];
            int ret;

            ret = RecvFrom(out src, data, 0, data.Length);

            if (ret > 0)
            {
                Array.Resize<byte>(ref data, ret);

                return data;
            }
            else if (ret == SockLater)
            {
                return new byte[0];
            }
            else
            {
                return null;
            }
        }
        public int RecvFrom(out IPEndPoint src, byte[] data)
        {
            return RecvFrom(out src, data, data.Length);
        }
        public int RecvFrom(out IPEndPoint src, byte[] data, int size)
        {
            return RecvFrom(out src, data, 0, size);
        }
        public int RecvFrom(out IPEndPoint src, byte[] data, int offset, int size)
        {
            Socket s;
            src = null;
            if (this.type != SockType.Udp || this.socket == null)
            {
                return 0;
            }
            if (size == 0)
            {
                return 0;
            }

            s = this.socket;

            int ret = -1;
            SocketError err = SocketError.Success;

            try
            {
                EndPoint ep = new IPEndPoint(this.IsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
                ret = s.ReceiveFrom(data, offset, size, 0, ref ep);
                src = (IPEndPoint)ep;
            }
            catch (SocketException se)
            {
                err = se.SocketErrorCode;
            }
            catch
            {
            }

            if (ret > 0)
            {
                lock (this.lockObj)
                {
                    this.recvNum++;
                    this.recvSize += (long)ret;
                }

                return ret;
            }
            else
            {
                this.ignoreRecvErr = false;

                if (err == SocketError.ConnectionReset || err == SocketError.MessageSize || err == SocketError.NetworkUnreachable ||
                    err == SocketError.NoBufferSpaceAvailable || (int)err == 10068 || err == SocketError.NetworkReset)
                {
                    this.ignoreRecvErr = true;
                }
                else if (err == SocketError.WouldBlock)
                {
                    return SockLater;
                }
                else
                {
                    this.LastRecvError = (ulong)err;
                }

                return 0;
            }
        }

        // UDP 送信
        public int SendTo(IPAddress destAddr, int destPort, byte[] data)
        {
            return SendTo(destAddr, destPort, data, data.Length);
        }
        public int SendTo(IPAddress destAddr, int destPort, byte[] data, int size)
        {
            return SendTo(destAddr, destPort, data, 0, size);
        }
        public int SendTo(IPAddress destAddr, int destPort, byte[] data, int offset, int size)
        {
            Socket s;
            bool isBroadcast = false;
            if (this.type != SockType.Udp || this.socket == null)
            {
                return 0;
            }
            if (size == 0)
            {
                return 0;
            }

            s = this.socket;

            byte[] destBytes = destAddr.GetAddressBytes();
            if (destAddr.AddressFamily == AddressFamily.InterNetwork)
            {
                if (destBytes.Length == 4)
                {
                    if (destBytes[0] == 255 &&
                        destBytes[1] == 255 &&
                        destBytes[2] == 255 &&
                        destBytes[3] == 255)
                    {
                        s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);

                        isBroadcast = true;

                        this.udpBroadcast = true;
                    }
                }
            }

            int ret = -1;
            SocketError err = 0;

            try
            {
                ret = s.SendTo(data, offset, size, (isBroadcast ? SocketFlags.Broadcast : 0), new IPEndPoint(destAddr, destPort));
            }
            catch (SocketException se)
            {
                err = se.SocketErrorCode;
            }
            catch
            {
            }

            if (ret != size)
            {
                this.ignoreSendErr = false;

                if (err == SocketError.ConnectionReset || err == SocketError.MessageSize || err == SocketError.NetworkUnreachable ||
                    err == SocketError.NoBufferSpaceAvailable || (int)err == 10068 || err == SocketError.NetworkReset)
                {
                    this.ignoreSendErr = true;
                }
                else if (err == SocketError.WouldBlock)
                {
                    return SockLater;
                }

                return 0;
            }

            lock (this.lockObj)
            {
                this.sendSize += (long)ret;
                this.sendNum++;
            }

            return ret;
        }

        // UDP ソケットの作成と初期化
        public static Sock NewUDP()
        {
            return NewUDP(0);
        }
        public static Sock NewUDP(int port)
        {
            return NewUDP(port, IPAddress.Any);
        }
        public static Sock NewUDP(int port, IPAddress endpoint)
        {
            return NewUDP(port, endpoint, false);
        }
        public static Sock NewUDP(int port, IPAddress endpoint, bool ipv6)
        {
            Sock sock;
            Socket s;

            if (ipv6 == false)
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            }
            else
            {
                s = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            }

            IPEndPoint ep = new IPEndPoint(endpoint, port);
            s.Bind(ep);

            sock = new Sock();
            sock.type = SockType.Udp;
            sock.connected = false;
            sock.asyncMode = false;
            sock.serverMode = false;
            sock.IsIPv6 = ipv6;
            if (port != 0)
            {
                sock.serverMode = true;
            }

            sock.socket = s;
            sock.Fd = s.Handle;

            sock.querySocketInformation();

            return sock;
        }

        // Pack の送信
        public bool SendPack(Pack p)
        {
            Buf b = new Buf();
            byte[] data = p.ByteData;

            b.WriteInt((uint)data.Length);
            b.Write(data);

            return SendAll(b.ByteData);
        }

        // Pack の受信
        public Pack RecvPack()
        {
            return RecvPack(0);
        }
        public Pack RecvPack(int maxSize)
        {
            byte[] sizeData = RecvAll(Util.SizeOfInt32);
            if (sizeData == null)
            {
                return null;
            }

            int size = Util.ByteToInt(sizeData);

            if (maxSize != 0 && size > maxSize)
            {
                return null;
            }

            byte[] data = RecvAll(size);
            if (data == null)
            {
                return null;
            }

            Buf b = new Buf(data);

            try
            {
                Pack p = Pack.CreateFromBuf(b);

                return p;
            }
            catch
            {
                return null;
            }
        }

        // ソケットを非同期に設定する
        internal void initAsyncSocket()
        {
            try
            {
                if (this.asyncMode)
                {
                    return;
                }
                if (this.listenMode != false || (this.type == SockType.Tcp && this.connected == false))
                {
                    return;
                }

                this.hEvent = new Event();

                // 関連付け
                WSAEventSelect((IntPtr)this.socket.Handle, this.hEvent.Handle, 35);
                this.socket.Blocking = false;

                this.asyncMode = true;
            }
            catch
            {
            }
        }

        // TCP すべて受信
        public byte[] RecvAll(int size)
        {
            byte[] data = new byte[size];
            bool ret = RecvAll(data);
            if (ret)
            {
                return data;
            }
            else
            {
                return null;
            }
        }
        public bool RecvAll(byte[] data)
        {
            return RecvAll(data, 0, data.Length);
        }
        public bool RecvAll(byte[] data, int size)
        {
            return RecvAll(data, 0, size);
        }
        public bool RecvAll(byte[] data, int offset, int size)
        {
            int recv_size, sz, ret;
            if (size == 0)
            {
                return true;
            }
            if (this.asyncMode)
            {
                return false;
            }

            recv_size = 0;

            while (true)
            {
                sz = size - recv_size;

                ret = Recv(data, offset + recv_size, sz);
                if (ret <= 0)
                {
                    return false;
                }

                recv_size += ret;
                if (recv_size >= size)
                {
                    return true;
                }
            }
        }

        // TCP 受信
        public byte[] Recv(int size)
        {
            byte[] data = new byte[size];
            int ret = Recv(data);
            if (ret >= 1)
            {
                Array.Resize<byte>(ref data, ret);
                return data;
            }
            else if (ret == SockLater)
            {
                return new byte[0];
            }
            else
            {
                return null;
            }
        }
        public int Recv(byte[] data)
        {
            return Recv(data, 0, data.Length);
        }
        public int Recv(byte[] data, int size)
        {
            return Recv(data, 0, size);
        }
        public int Recv(byte[] data, int offset, int size)
        {
            Socket s;

            if (this.type != SockType.Tcp || this.connected == false || this.listenMode != false ||
                this.socket == null)
            {
                return 0;
            }

            // 受信
            s = this.socket;

            int ret = -1;
            SocketError err = 0;
            try
            {
                ret = s.Receive(data, offset, size, DefaultSocketFlags);
            }
            catch (SocketException se)
            {
                err = se.SocketErrorCode;
            }
            catch
            {
            }

            if (ret > 0)
            {
                // 受信成功
                lock (lockObj)
                {
                    this.recvSize += (long)ret;
                    this.recvNum++;
                }

                return ret;
            }

            if (this.asyncMode)
            {
                if (err == SocketError.WouldBlock)
                {
                    // ブロッキングしている
                    return SockLater;
                }
            }

            Disconnect();

            return 0;
        }

        // TCP 送信
        public int Send(byte[] data)
        {
            return Send(data, 0, data.Length);
        }
        public int Send(byte[] data, int size)
        {
            return Send(data, 0, size);
        }
        public int Send(byte[] data, int offset, int size)
        {
            Socket s;
            size = Math.Min(size, MaxSendBufMemSize);
            if (this.type != SockType.Tcp || this.connected == false || this.listenMode != false ||
                this.socket == null)
            {
                return 0;
            }

            // 送信
            s = this.socket;
            int ret = -1;
            SocketError err = 0;
            try
            {
                ret = s.Send(data, offset, size, DefaultSocketFlags);
            }
            catch (SocketException se)
            {
                err = se.SocketErrorCode;
            }
            catch
            {
            }

            if (ret > 0)
            {
                // 送信成功
                lock (this.lockObj)
                {
                    this.sendSize += (long)ret;
                    this.sendNum++;
                }
                this.writeBlocked = false;

                return ret;
            }

            // 送信失敗
            if (this.asyncMode)
            {
                // 非同期モードの場合、エラーを調べる
                if (err == SocketError.WouldBlock)
                {
                    // ブロッキングしている
                    this.writeBlocked = true;

                    return SockLater;
                }
            }

            // 切断された
            Disconnect();

            return 0;
        }

        // TCP すべて送信
        public bool SendAll(byte[] data)
        {
            return SendAll(data, 0, data.Length);
        }
        public bool SendAll(byte[] data, int size)
        {
            return SendAll(data, 0, size);
        }
        public bool SendAll(byte[] data, int offset, int size)
        {
            if (this.asyncMode)
            {
                return false;
            }
            if (size == 0)
            {
                return true;
            }

            int sent_size = 0;

            while (true)
            {
                int ret = Send(data, offset + sent_size, size - sent_size);
                if (ret <= 0)
                {
                    return false;
                }
                sent_size += ret;
                if (sent_size >= size)
                {
                    return true;
                }
            }
        }

        // TCP 接続受諾
        public Sock Accept(bool getHostName = false)
        {
            if (this.listenMode == false || this.type != SockType.Tcp || this.serverMode == false)
            {
                return null;
            }
            if (this.cancelAccept)
            {
                return null;
            }

            Socket s = this.socket;
            if (s == null)
            {
                return null;
            }

            try
            {
                Socket newSocket = s.Accept();

                if (newSocket == null)
                {
                    return null;
                }

                if (this.cancelAccept)
                {
                    newSocket.Close();
                    return null;
                }

                Sock ret = new Sock();
                ret.socket = newSocket;
                ret.connected = true;
                ret.asyncMode = false;
                ret.type = SockType.Tcp;
                ret.serverMode = true;
                ret.secureMode = false;
                newSocket.NoDelay = true;

                ret.SetTimeout(TimeoutInfinite);

                ret.Fd = (IntPtr)ret.socket.Handle;

                ret.querySocketInformation();

                if (getHostName)
                {
                    try
                    {
                        ret.remoteHostName = Domain.GetHostName(ret.remoteIP, TimeoutGetHostname)[0];
                    }
                    catch
                    {
                        ret.remoteHostName = ret.remoteIP.ToString();
                    }
                }

                return ret;
            }
            catch
            {
                return null;
            }
        }

        // TCP 待ち受け
        public static Sock Listen(int port, bool localOnly = false, bool ipv6 = false)
        {
            Socket s;

            if (ipv6 == false)
            {
                s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            }
            else
            {
                s = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            }

            IPEndPoint ep = new IPEndPoint(ipv6 ? IPAddress.IPv6Any : IPAddress.Any, port);
            if (localOnly)
            {
                ep = new IPEndPoint(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, port);
            }

            s.Bind(ep);

            s.Listen(0x7fffffff);

            Sock sock = new Sock();
            sock.Fd = s.Handle;
            sock.connected = false;
            sock.asyncMode = false;
            sock.serverMode = true;
            sock.type = SockType.Tcp;
            sock.socket = s;
            sock.listenMode = true;
            sock.secureMode = false;
            sock.localPort = port;
            sock.IsIPv6 = ipv6;

            return sock;
        }

        // TCP 接続
        public static Sock Connect(string hostName, int port, int timeout = 0, bool use46 = false, bool no_cache = false)
        {
            if (timeout == 0)
            {
                timeout = TimeoutInfinite;
            }

            // 正引き
            IPAddress ip;

            if (use46 == false)
            {
                ip = Domain.GetIP(hostName, no_cache)[0];
            }
            else
            {
                ip = Domain.GetIP46(hostName, no_cache)[0];
            }

            IPEndPoint endPoint = new IPEndPoint(ip, port);

            // ソケット作成
            Sock sock = new Sock();
            sock.socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            sock.type = SockType.Tcp;
            sock.serverMode = false;

            // 接続の実施
            connectTimeout(sock.socket, endPoint, timeout);

            // ホスト名解決
            try
            {
                string[] hostname = Domain.GetHostName(ip, TimeoutGetHostname);
                sock.remoteHostName = hostname[0];
            }
            catch
            {
                sock.remoteHostName = ip.ToString();
            }

            sock.socket.LingerState = new LingerOption(false, 0);
            try
            {
                sock.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
            }
            catch
            {
            }
            sock.socket.NoDelay = true;

            sock.querySocketInformation();

            sock.connected = true;
            sock.asyncMode = false;
            sock.secureMode = false;
            sock.Fd = sock.socket.Handle;

            sock.IsIPv6 = (ip.AddressFamily == AddressFamily.InterNetworkV6) ? true : false;

            sock.SetTimeout(TimeoutInfinite);

            return sock;
        }
        // 接続の実施
        static void connectTimeoutCallback(IAsyncResult r)
        {
        }
        static void connectTimeout(Socket s, IPEndPoint endPoint, int timeout)
        {
            IAsyncResult r = s.BeginConnect(endPoint, new AsyncCallback(connectTimeoutCallback), null);
            r.AsyncWaitHandle.WaitOne(timeout, false);
            if (r.IsCompleted == false)
            {
                throw new TimeoutException();
            }
            s.EndConnect(r);
        }

        // 切断
        object unix_asyncmode_lock = new object();
        public void Disconnect()
        {
            try
            {
                if (Env.IsUnix)
                {
                    lock (unix_asyncmode_lock)
                    {
                        if (this.asyncMode)
                        {
                            this.asyncMode = false;

                            if (this.SockEvent != null)
                            {
                                lock (this.SockEvent.unix_socklist)
                                {
                                    this.SockEvent.unix_socklist.Remove(this);
                                }

                                SockEvent se = this.SockEvent;
                                this.SockEvent = null;

                                se.Set();
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                this.disconnecting = true;

                if (this.type == SockType.Tcp && this.listenMode)
                {
                    this.cancelAccept = true;

                    try
                    {
                        Sock s = Sock.Connect((this.IsIPv6 ? "::1" : "127.0.0.1"), this.localPort);

                        s.Disconnect();
                    }
                    catch
                    {
                    }
                }

                lock (disconnectLockObj)
                {
                    if (this.type == SockType.Tcp)
                    {
                        if (this.socket != null)
                        {
                            try
                            {
                                this.socket.LingerState = new LingerOption(false, 0);
                            }
                            catch
                            {
                            }

                            try
                            {
                                this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
                            }
                            catch
                            {
                            }
                        }

                        lock (lockObj)
                        {
                            if (this.socket == null)
                            {
                                return;
                            }

                            Socket s = this.socket;

                            if (this.connected)
                            {
                                s.Shutdown(SocketShutdown.Both);
                            }

                            s.Close();
                        }

                        lock (sslLockObj)
                        {
                            if (this.secureMode)
                            {

                            }
                        }

                        this.socket = null;
                        this.type = SockType.Unknown;
                        this.asyncMode = this.connected = this.listenMode = this.secureMode = false;
                    }
                    else if (this.type == SockType.Udp)
                    {
                        lock (lockObj)
                        {
                            if (this.socket == null)
                            {
                                return;
                            }

                            Socket s = this.socket;

                            s.Close();

                            this.type = SockType.Unknown;
                            this.asyncMode = this.connected = this.listenMode = this.secureMode = false;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        // タイムアウト時間の設定
        public void SetTimeout(int timeout)
        {
            try
            {
                if (this.type == SockType.Tcp)
                {
                    if (timeout < 0)
                    {
                        timeout = TimeoutInfinite;
                    }

                    this.socket.SendTimeout = this.socket.ReceiveTimeout = timeout;
                    this.timeOut = timeout;
                }
            }
            catch
            {
            }
        }

        // タイムアウト時間の取得
        public int GetTimeout()
        {
            try
            {
                if (this.type != SockType.Tcp)
                {
                    return TimeoutInfinite;
                }

                return this.timeOut;
            }
            catch
            {
                return Timeout.Infinite;
            }
        }

        // ソケット情報の取得
        void querySocketInformation()
        {
            try
            {
                lock (this.lockObj)
                {
                    if (this.type == SockType.Tcp)
                    {
                        // リモートホストの情報を取得
                        IPEndPoint ep1 = (IPEndPoint)this.socket.RemoteEndPoint;

                        this.remotePort = ep1.Port;
                        this.remoteIP = ep1.Address;
                    }

                    // ローカルホストの情報を取得
                    IPEndPoint ep2 = (IPEndPoint)this.socket.LocalEndPoint;

                    this.localPort = ep2.Port;
                    this.localIP = ep2.Address;
                }
            }
            catch
            {
            }
        }

        public static bool SetKeepAlive(Socket socket, ulong time, ulong interval)
        {
            const int BytesPerLong = 4;
            const int BitsPerByte = 8;

            try
            {
                var input = new[]
                {
                    (time == 0 || interval == 0) ? 0UL : 1UL,
                    time,
                    interval
                };

                byte[] inValue = new byte[3 * BytesPerLong];
                for (int i = 0; i < input.Length; i++)
                {
                    inValue[i * BytesPerLong + 3] = (byte)(input[i] >> ((BytesPerLong - 1) * BitsPerByte) & 0xff);
                    inValue[i * BytesPerLong + 2] = (byte)(input[i] >> ((BytesPerLong - 2) * BitsPerByte) & 0xff);
                    inValue[i * BytesPerLong + 1] = (byte)(input[i] >> ((BytesPerLong - 3) * BitsPerByte) & 0xff);
                    inValue[i * BytesPerLong + 0] = (byte)(input[i] >> ((BytesPerLong - 4) * BitsPerByte) & 0xff);
                }

                byte[] outValue = BitConverter.GetBytes(0);
                //socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.KeepAlive, true);
                socket.IOControl(IOControlCode.KeepAliveValues, inValue, outValue);
            }
            catch// (Exception ex)
            {
                //Con.WriteLine(ex.ToString());
                return false;
            }

            return true;
        }
    }

    // Ping 応答
    class SendPingReply
    {
        private TimeSpan rttTimeSpan;
        public TimeSpan RttTimeSpan
        {
            get { return rttTimeSpan; }
        }

        private double rttDouble;
        public double RttDouble
        {
            get { return rttDouble; }
        }

        private IPStatus status;
        internal IPStatus Status
        {
            get { return status; }
        }

        private bool ok;
        public bool Ok
        {
            get { return ok; }
        }

        object userObject;
        public object UserObject
        {
            get { return userObject; }
        }

        internal SendPingReply(IPStatus status, TimeSpan span, object userObject)
        {
            this.status = status;
            this.userObject = userObject;

            if (this.status == IPStatus.Success)
            {
                this.rttTimeSpan = span;
                this.rttDouble = span.Ticks / 10000000.0;
                ok = true;
            }
            else
            {
                ok = false;
            }
        }
    }

    // Ping 送信
    class SendPing
    {
        public const int DefaultSendSize = 32;
        public const int DefaultTimeout = 1000;

        public static SendPingReply Send(IPAddress target)
        {
            return Send(target, 0);
        }
        public static SendPingReply Send(IPAddress target, int timeout)
        {
            return Send(target, null, timeout);
        }
        public static SendPingReply Send(string target)
        {
            return Send(target, 0);
        }
        public static SendPingReply Send(string target, int timeout)
        {
            return Send(target, null, timeout);
        }
        public static SendPingReply Send(string target, byte[] data)
        {
            return Send(target, data, 0);
        }
        public static SendPingReply Send(string target, byte[] data, int timeout)
        {
            return Send(Domain.GetIP46(target, true)[0], data, timeout);
        }
        public static SendPingReply Send(IPAddress target, byte[] data)
        {
            return Send(target, data, 0);
        }
        public static SendPingReply Send(IPAddress target, byte[] data, int timeout)
        {
            try
            {
                if (data == null)
                {
                    data = Secure.Rand(DefaultSendSize);
                }
                if (timeout == 0)
                {
                    timeout = DefaultTimeout;
                }

                using (Ping p = new Ping())
                {
                    DateTime startDateTime = Time.NowDateTimeUtc;

                    PingReply ret = p.Send(target, timeout, data);

                    DateTime endDateTime = Time.NowDateTimeUtc;

                    TimeSpan span = endDateTime - startDateTime;

                    SendPingReply r = new SendPingReply(ret.Status, span, null);

                    return r;
                }
            }
            catch
            {
                return new SendPingReply(IPStatus.Unknown, new TimeSpan(), null);
            }
        }
    }

    // DNS
    class Domain
    {
        public static readonly TimeSpan DnsCacheLifeTime = new TimeSpan(0, 1, 0, 0);
        static Cache<string, IPAddress[]> dnsACache = new Cache<string, IPAddress[]>(DnsCacheLifeTime, CacheType.UpdateExpiresWhenAccess);
        static Cache<IPAddress, string[]> dnsPTRCache = new Cache<IPAddress, string[]>(DnsCacheLifeTime, CacheType.UpdateExpiresWhenAccess);


        class GetHostNameData
        {
            public string[] HostName;
            public IPAddress IP;
            public bool NoCache;
        }

        // タイムアウト付き逆引き
        public static string[] GetHostName(IPAddress ip, int timeout)
        {
            return GetHostName(ip, timeout, false);
        }
        public static string[] GetHostName(IPAddress ip, int timeout, bool noCache)
        {
            GetHostNameData d = new GetHostNameData();
            d.HostName = null;
            d.IP = ip;
            d.NoCache = noCache;

            ThreadObj t = new ThreadObj(new ThreadProc(getHostNameThreadProc), d);
            t.WaitForEnd(timeout);

            lock (d)
            {
                if (d.HostName != null)
                {
                    return d.HostName;
                }
                else
                {
                    if (noCache == false)
                    {
                        string[] ret = dnsPTRCache[ip];

                        if (ret != null)
                        {
                            return ret;
                        }
                    }
                    throw new TimeoutException();
                }
            }
        }
        static void getHostNameThreadProc(object param)
        {
            GetHostNameData d = (GetHostNameData)param;

            string[] hostname = Domain.GetHostName(d.IP, d.NoCache);

            lock (d)
            {
                d.HostName = hostname;
            }
        }

        // 逆引き
        public static string[] GetHostName(IPAddress ip)
        {
            return GetHostName(ip, false);
        }
        public static string[] GetHostName(IPAddress ip, bool noCache)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && ip.GetAddressBytes()[0] == 127)
            {
                return new string[] { "localhost" };
            }

            try
            {
                IPHostEntry e = Dns.GetHostEntry(ip);

                string[] ret = IPHostEntryToStringArray(e);

                if (ret.Length == 0)
                {
                    return null;
                }

                if (noCache == false)
                {
                    dnsPTRCache.Add(ip, ret);
                }

                return ret;
            }
            catch
            {
                if (noCache)
                {
                    return null;
                }

                string[] ret = dnsPTRCache[ip];

                if (ret == null)
                {
                    return null;
                }

                return ret;
            }
        }

        // IPHostEntry から文字列リストを取得する
        public static string[] IPHostEntryToStringArray(IPHostEntry e)
        {
            List<string> o = new List<string>();

            o.Add(e.HostName);

            foreach (string s in e.Aliases)
            {
                o.Add(s);
            }

            return Str.UniqueToken(o.ToArray());
        }

        // 正引き (IPv6 も)
        public static IPAddress[] GetIP46(string hostName)
        {
            return GetIP46(hostName, false);
        }
        public static IPAddress[] GetIP46(string hostName, bool noCache)
        {
            hostName = NormalizeHostName(hostName);

            if (IsIPAddress(hostName))
            {
                return new IPAddress[1] { StrToIP(hostName) };
            }

            if (Str.StrCmpi(hostName, "localhost"))
            {
                return new IPAddress[] { new IPAddress(new byte[] { 127, 0, 0, 1 }) };
            }

            try
            {
                IPAddress[] ret = Dns.GetHostAddresses(hostName);

                if (ret.Length == 0)
                {
                    return null;
                }

                if (noCache == false)
                {
                    dnsACache.Add(hostName, ret);
                }

                return ret;
            }
            catch
            {
                if (noCache)
                {
                    throw;
                }
                IPAddress[] ret = dnsACache[hostName];

                if (ret == null)
                {
                    throw;
                }

                return ret;
            }
        }

        // 正引き
        public static IPAddress[] GetIP(string hostName)
        {
            return GetIP(hostName, false);
        }
        public static IPAddress[] GetIP(string hostName, bool noCache)
        {
            hostName = NormalizeHostName(hostName);

            if (IsIPAddress(hostName))
            {
                return new IPAddress[1] { StrToIP(hostName) };
            }

            if (Str.StrCmpi(hostName, "localhost"))
            {
                return new IPAddress[] { new IPAddress(new byte[] { 127, 0, 0, 1 }) };
            }

            try
            {
                IPAddress[] ret = GetIPv4OnlyFromIPAddressList(Dns.GetHostAddresses(hostName));

                if (ret.Length == 0)
                {
                    return null;
                }

                if (noCache == false)
                {
                    dnsACache.Add(hostName, ret);
                }

                return ret;
            }
            catch
            {
                if (noCache)
                {
                    throw;
                }
                IPAddress[] ret = dnsACache[hostName];

                if (ret == null)
                {
                    throw;
                }

                return ret;
            }
        }

        // IP アドレスリストから IPv4 のもののみを抽出する
        public static IPAddress[] GetIPv4OnlyFromIPAddressList(IPAddress[] list)
        {
            List<IPAddress> o = new List<IPAddress>();

            foreach (IPAddress p in list)
            {
                if (p.AddressFamily == AddressFamily.InterNetwork)
                {
                    o.Add(p);
                }
            }

            return o.ToArray();
        }

        // 文字列が IP アドレスかどうか取得する
        public static bool IsIPAddress(string str)
        {
            str = NormalizeHostName(str);

            try
            {
                IPAddress.Parse(str);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 文字列を IP アドレスに変換する
        public static IPAddress StrToIP(string str)
        {
            str = NormalizeHostName(str);

            if (IsIPAddress(str) == false)
            {
                return null;
            }

            return IPAddress.Parse(str);
        }

        // ホスト名の正規化
        public static string NormalizeHostName(string hostName)
        {
            return hostName.Trim().ToLower();
        }
    }
}
