// CoreUtil
// 
// Copyright (C) 1997-2010 Daiyuu Nobori. All Rights Reserved.
// Copyright (C) 2004-2010 SoftEther Corporation. All Rights Reserved.

using System;
using System.Threading;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace IPA.Cores.Basic
{
    // WPC クライアント情報
    class WpcClient
    {
        string ipAddress;
        public string IpAddress
        {
            get { return ipAddress; }
        }

        string hostName;
        public string HostName
        {
            get { return hostName; }
        }

        string guid;
        public string Guid
        {
            get
            {
                return guid;
            }
        }

        int seLang = 0;
        public int SeLang
        {
            get { return seLang; }
        }

        bool isUser = false;
        string svcName = "";
        string msid = "";
        public bool IsUser
        {
            get
            {
                return isUser;
            }
        }
        public string SvcName
        {
            get
            {
                return svcName;
            }
        }
        public string Msid
        {
            get
            {
                return msid;
            }
        }
        string pcid;
        public string Pcid
        {
            get { return pcid; }
        }
        int machineId;
        public int MachineId
        {
            get { return machineId; }
        }
        DateTime createDate;
        public DateTime CreateDate
        {
            get { return createDate; }
        }
        DateTime updateDate;
        public DateTime UpdateDate
        {
            get { return updateDate; }
        }
        DateTime lastServerDate;
        public DateTime LastServerDate
        {
            get { return lastServerDate; }
        }
        DateTime lastClientDate;
        public DateTime LastClientDate
        {
            get { return lastClientDate; }
        }
        int numServer;
        public int NumServer
        {
            get { return numServer; }
        }
        int numClient;
        public int NumClient
        {
            get { return numClient; }
        }

        public void SetUserInfo(int machineId, string svcName, string msid, string pcid, DateTime createDate, DateTime updateDate, DateTime lastServerDate,
            DateTime lastClientDate, int numServer, int numClient, int seLang)
        {
            this.isUser = true;
            this.machineId = machineId;
            this.svcName = svcName;
            this.msid = msid;
            this.pcid = pcid;
            this.createDate = createDate;
            this.updateDate = updateDate;
            this.lastServerDate = lastServerDate;
            this.lastClientDate = lastClientDate;
            this.numServer = numServer;
            this.numClient = numClient;
            this.seLang = seLang;
        }

        bool isGate = false;
        public bool IsGate
        {
            get
            {
                return isGate;
            }
        }
        public void GateConnection()
        {
            isGate = true;
        }

        Pack requestPack;
        Cert requestCert;

        Pack responsePack;
        Cert responseCert;
        Rsa responseKey;

        public Pack RequestPack
        {
            get
            {
                return requestPack;
            }
        }

        public Cert RequestCert
        {
            get
            {
                return requestCert;
            }
        }

        public WpcClient(string ip)
        {
            init(ip);
        }
        public WpcClient(HttpListenerRequest httpListenerRequest)
        {
            init(httpListenerRequest.RemoteEndPoint.Address.ToString());
        }

        void init(string ip)
        {
            ipAddress = ip;
            hostName = "";
            try
            {
                hostName = Domain.GetHostName(IPAddress.Parse(ip), 250)[0];
            }
            catch
            {
            }
            if (Str.IsEmptyStr(hostName))
            {
                hostName = ip;
            }
            requestPack = null;
            requestCert = null;
            responsePack = null;
            responseCert = null;
            responseKey = null;
            guid = System.Guid.NewGuid().ToString();
        }

        public void ParseRequest(string requestStr)
        {
            WpcPacket packet = WpcPacket.ParsePacket(requestStr);
            if (packet == null)
            {
                // パケットが不正である
                throw new ApplicationException("Protocol Error: Not WPC Client.");
            }
            requestPack = packet.Pack;
            requestCert = packet.Cert;
        }

        public void SetResponse(Pack pack, Cert cert, Rsa key)
        {
            responsePack = pack;
            responseCert = cert;
            responseKey = key;
        }

        public string ResponseStr
        {
            get
            {
                return WpcPacket.GeneratePacket(responsePack, responseCert, responseKey);
            }
        }
    }

    // WpcEntry
    class WpcEntry
    {
        byte[] entryName;
        public byte[] EntryName
        {
            get { return entryName; }
        }

        byte[] data;
        public byte[] Data
        {
            get { return data; }
        }

        public WpcEntry(string entryName, byte[] data)
        {
            this.entryName = GenerateEntryName(entryName);
            this.data = data;
        }

        public WpcEntry(byte[] entryNameByte, byte[] data)
        {
            this.entryName = entryNameByte;
            this.data = data;
        }

        public static List<WpcEntry> ParseDataEntry(string str)
        {
            Buf src = new Buf(Str.AsciiEncoding.GetBytes(str));
            List<WpcEntry> o = new List<WpcEntry>();

            while (true)
            {
                byte[] entryNameByte = src.Read(4);
                if (entryNameByte.Length != 4)
                {
                    break;
                }
                byte[] sizeStrByte = src.Read(10);
                if (sizeStrByte.Length != 10)
                {
                    break;
                }
                string sizeStr = Str.AsciiEncoding.GetString(sizeStrByte);
                uint size = Str.StrToUInt(sizeStr);

                byte[] strData = src.Read(size);
                if ((uint)strData.Length != size)
                {
                    break;
                }

                string payload = Str.AsciiEncoding.GetString(strData);
                byte[] data = Wpc.StrToByte(payload);

                WpcEntry e = new WpcEntry(entryNameByte, data);

                o.Add(e);
            }

            return o;
        }

        public static WpcEntry FindEntry(List<WpcEntry> entryList, string entryName)
        {
            byte[] entryNameByte = GenerateEntryName(entryName);
            foreach (WpcEntry e in entryList)
            {
                if (Util.CompareByte(e.EntryName, entryNameByte))
                {
                    return e;
                }
            }

            return null;
        }

        public static byte[] GenerateEntryName(string entryName)
        {
            byte[] ret = new byte[4];
            uint len;
            entryName = entryName.ToUpper();
            len = (uint)entryName.Length;
            byte[] entryNameByte = Str.AsciiEncoding.GetBytes(entryName);
            uint i;
            for (i = 0; i < (uint)ret.Length; i++)
            {
                ret[i] = (byte)' ';
            }

            len = (uint)entryNameByte.Length;

            if (len < ret.Length)
            {
                Array.Copy(entryNameByte, 0, ret, 0, (int)len);
            }
            else
            {
                Array.Copy(entryNameByte, 0, ret, 0, ret.Length);
            }

            return ret;
        }

        public static void AddDataEntry(Buf buf, string entryName, byte[] data)
        {
            AddDataEntry(buf, entryName, Wpc.ByteToStr(data));
        }
        public static void AddDataEntry(Buf buf, string entryName, string str)
        {
            byte[] entryNameByte = GenerateEntryName(entryName);
            buf.Write(entryNameByte);

            string numStr = str.Length.ToString("0000000000");
            buf.Write(Str.AsciiEncoding.GetBytes(numStr));

            buf.Write(Str.AsciiEncoding.GetBytes(str));
        }
    }

    // Packet
    class WpcPacket
    {
        Pack pack;
        public Pack Pack
        {
            get { return pack; }
        }

        byte[] hash;
        public byte[] Hash
        {
            get { return hash; }
        }

        Cert cert;
        public Cert Cert
        {
            get { return cert; }
        }

        byte[] sign;
        public byte[] Sign
        {
            get { return sign; }
        }

        public bool IsSigned
        {
            get
            {
                return (cert != null);
            }
        }

        private WpcPacket(Pack pack, byte[] hash)
            : this(pack, hash, null, null)
        {
        }
        private WpcPacket(Pack pack, byte[] hash, Cert cert, byte[] sign)
        {
            this.pack = pack;
            this.hash = hash;
            this.cert = cert;
            this.sign = sign;
        }

        public static string GeneratePacket(Pack pack)
        {
            return GeneratePacket(pack, null, null);
        }
        public static string GeneratePacket(Pack pack, Cert cert, Rsa key)
        {
            Buf b = new Buf();

            byte[] pack_data = pack.WriteToBuf().ByteData;
            WpcEntry.AddDataEntry(b, "PACK", pack_data);

            byte[] hash = Secure.HashSHA1(pack_data);
            WpcEntry.AddDataEntry(b, "HASH", hash);

            if (cert != null && key != null)
            {
                WpcEntry.AddDataEntry(b, "CERT", cert.ByteData);
                WpcEntry.AddDataEntry(b, "SIGN", key.SignData(hash));
            }

            return Str.AsciiEncoding.GetString(b.ByteData);
        }

        public static WpcPacket ParsePacket(string recvStr)
        {
            List<WpcEntry> o = WpcEntry.ParseDataEntry(recvStr);

            WpcEntry e;

            try
            {
                e = WpcEntry.FindEntry(o, "PACK");
                if (e != null)
                {
                    byte[] hash = Secure.HashSHA1(e.Data);
                    Pack pack = null;

                    pack = Pack.CreateFromBuf(new Buf(e.Data));

                    e = WpcEntry.FindEntry(o, "HASH");

                    if (e != null)
                    {
                        byte[] hash2 = e.Data;

                        if (Util.CompareByte(hash, hash2))
                        {
                            e = WpcEntry.FindEntry(o, "CERT");
                            if (e != null)
                            {
                                Cert cert;

                                try
                                {
                                    cert = new Cert(e.Data);
                                }
                                catch
                                {
                                    return null;
                                }

                                e = WpcEntry.FindEntry(o, "SIGN");
                                if (e != null)
                                {
                                    byte[] sign = e.Data;

                                    if (cert.RsaPublicKey.VerifyData(hash2, sign))
                                    {
                                        return new WpcPacket(pack, hash2, cert, sign);
                                    }
                                }
                            }
                            else
                            {
                                return new WpcPacket(pack, hash2);
                            }
                        }
                    }
                }
            }
            catch (OverflowException)
            {
                return null;
            }

            return null;
        }
    }

    // Web Procedure Call
    static class Wpc
    {
        // Base 64 エンコード
        public static string Base64Encode(byte[] data)
        {
            try
            {
                return Convert.ToBase64String(data);
            }
            catch
            {
                return "";
            }
        }

        // Base 64 デコード
        public static byte[] Base64Decode(string str)
        {
            try
            {
                return Convert.FromBase64String(str);
            }
            catch
            {
                return new byte[0];
            }
        }

        // 文字列置換
        public static string Base64ToSafe64(string str)
        {
            return str.Replace('=', '(').Replace('+', ')').Replace('/', '_');
        }
        public static string Safe64ToBase64(string str)
        {
            return str.Replace('(', '=').Replace(')', '+').Replace('_', '/');
        }

        // バイトを文字列に変換
        public static string ByteToStr(byte[] data)
        {
            return Base64ToSafe64(Base64Encode(data));
        }

        // 文字列をバイトに変換
        public static byte[] StrToByte(string str)
        {
            return Base64Decode(Safe64ToBase64(str));
        }

        // ulong を DateTime に変換
        public static DateTime ConvertDateTime(ulong time64)
        {
            if (time64 == 0)
            {
                return new DateTime(0);
            }
            return new DateTime(((long)time64 + 62135629200000) * 10000);
        }

        // DateTime を ulong に変換
        public static ulong ConvertDateTime(DateTime dt)
        {
            if (dt.Ticks == 0)
            {
                return 0;
            }
            return (ulong)dt.Ticks / 10000 - 62135629200000;
        }

        // ulong を TimeSpan に変換
        public static TimeSpan ConvertTimeSpan(ulong tick)
        {
            return new TimeSpan((long)tick * 10000);
        }

        // TimeSpan を ulong に変換
        public static ulong ConvertTimeSpan(TimeSpan span)
        {
            return (ulong)span.Ticks / 10000;
        }
    }
}
