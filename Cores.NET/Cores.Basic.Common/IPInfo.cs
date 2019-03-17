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
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Xml.Serialization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Net.Mail;

namespace IPA.Cores.Basic
{
    class FullRouteIPInfoEntry : IComparable<FullRouteIPInfoEntry>
    {
        public uint From;
        public uint To;
        public string Registry = "";
        public uint Assigned = 0;
        public string Country2, Country3 = "", CountryFull;

        public int CompareTo(FullRouteIPInfoEntry other)
        {
            if (this.From > other.From)
            {
                return 1;
            }
            else if (this.From < other.From)
            {
                return -1;
            }
            return 0;
        }
    }

    class FullRouteIPInfoCache
    {
        public List<FullRouteIPInfoEntry> EntryList = new List<FullRouteIPInfoEntry>();
        public DateTime TimeStamp;
        public Dictionary<string, string> CountryCodeToName = new Dictionary<string, string>();

        void build_country_code_to_name_db()
        {
            CountryCodeToName.Clear();

            foreach (FullRouteIPInfoEntry e in this.EntryList)
            {
                if (CountryCodeToName.ContainsKey(e.Country2) == false)
                {
                    CountryCodeToName.Add(e.Country2, e.CountryFull);
                }
            }
        }

        public static FullRouteIPInfoCache CreateFromDownload(string url)
        {
            FullRouteIPInfoCache ret = new FullRouteIPInfoCache();
            ret.TimeStamp = DateTime.Now;

            // Download CSV
            WebRequest req = HttpWebRequest.Create(url);
            WebResponse res = req.GetResponse();
            try
            {
                Stream stream = res.GetResponseStream();
                try
                {
                    byte[] rawData = Util.ReadAllFromStream(stream);
                    byte[] data = GZipUtil.Decompress(rawData);

                    Csv csv = new Csv(new Buf(data));
                    foreach (CsvEntry ce in csv.Items)
                    {
                        if (ce.Count >= 7)
                        {
                            FullRouteIPInfoEntry e = new FullRouteIPInfoEntry();

                            e.From = Str.StrToUInt(ce[2]);
                            e.To = Str.StrToUInt(ce[3]);
                            //e.Registry = ce[2];
                            //e.Assigned = Str.StrToUInt(ce[3]);
                            e.Country2 = ce[5];
                            //e.Country3 = ce[5];
                            e.CountryFull = DeleteSemi(ce[6]);

                            if (e.From != 0 && e.To != 0)
                            {
                                ret.EntryList.Add(e);
                            }
                        }
                    }

                    ret.EntryList.Sort();

                    if (ret.EntryList.Count <= 70000)
                    {
                        throw new ApplicationException("ret.EntryList.Count <= 70000");
                    }
                }
                finally
                {
                    stream.Close();
                }
            }
            finally
            {
                res.Close();
            }

            ret.build_country_code_to_name_db();

            return ret;
        }

        public void SaveToFile(string filename)
        {
            Buf b = SaveToBuf();

            b.WriteToFile(filename);
        }

        public Buf SaveToBuf()
        {
            Buf b = new Buf();

            b.WriteInt64((ulong)this.TimeStamp.Ticks);
            b.WriteInt((uint)this.EntryList.Count);

            foreach (FullRouteIPInfoEntry e in this.EntryList)
            {
                b.WriteInt(e.From);
                b.WriteInt(e.To);
                b.WriteStr(e.Registry);
                b.WriteInt(e.Assigned);
                b.WriteStr(e.Country2);
                b.WriteStr(e.Country3);
                b.WriteStr(e.CountryFull);
            }

            b.Write(Secure.HashSHA1(b.ByteData));

            b.SeekToBegin();

            return b;
        }

        public static FullRouteIPInfoCache LoadFromFile(string filename)
        {
            Buf b = Buf.ReadFromFile(filename);
            b.SeekToBegin();

            return LoadFromBuf(b);
        }

        public static FullRouteIPInfoCache LoadFromBuf(Buf b)
        {
            b.Seek(b.Size - 20, SeekOrigin.Begin);
            byte[] hash = b.Read(20);
            b.SeekToBegin();
            byte[] hash2 = Secure.HashSHA1(Util.CopyByte(b.ByteData, 0, (int)b.Size - 20));

            if (Util.CompareByte(hash, hash2) == false)
            {
                throw new ApplicationException("Invalid Hash");
            }

            FullRouteIPInfoCache ret = new FullRouteIPInfoCache();

            ret.TimeStamp = new DateTime((long)b.ReadInt64());
            int num = (int)b.ReadInt();

            int i;
            for (i = 0; i < num; i++)
            {
                FullRouteIPInfoEntry e = new FullRouteIPInfoEntry();
                e.From = b.ReadInt();
                e.To = b.ReadInt();
                e.Registry = b.ReadStr();
                e.Assigned = b.ReadInt();
                e.Country2 = b.ReadStr();
                e.Country3 = b.ReadStr();
                e.CountryFull = DeleteSemi(b.ReadStr());
                ret.EntryList.Add(e);
            }

            ret.EntryList.Sort();

            ret.build_country_code_to_name_db();

            return ret;
        }

        public static string DeleteSemi(string str)
        {
            int i = str.IndexOf(";");
            if (i == -1)
            {
                return str;
            }

            return str.Substring(0, i);
        }
    }

    static class FullRouteIPInfo
    {
        public static readonly TimeSpan LifeTime = new TimeSpan(15, 0, 0, 0);
        public const long DownloadRetryMSecs = (3600 * 1000);
        static long nextDownloadRetry = 0;
        public const string Url = "http://files.open.ad.jp/ip-database/gzip/mapping_ipv4_to_country.csv.gz";
        public static readonly string CacheFileName;
        static readonly GlobalLock cache_file_global_lock = new GlobalLock("ipinfo_cache_file");
        static FullRouteIPInfoCache cache = null;
        static object lockObj = new object();

        static FullRouteIPInfo()
        {
//             MutexSecurity sec = new MutexSecurity();
//             sec.AddAccessRule(new MutexAccessRule("Everyone", MutexRights.FullControl, AccessControlType.Allow));
            CacheFileName = Path.Combine(Env.TempDir, "ipinfo_cache2.dat");
        }

        public static string[] GetCountryCodes()
        {
            lock (lockObj)
            {
                try
                {
                    checkUpdate();

                    if (cache != null)
                    {
                        List<string> ret = new List<string>();
                        foreach (string cc in cache.CountryCodeToName.Keys)
                        {
                            ret.Add(cc);
                        }
                        return ret.ToArray();
                    }

                    return null;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string SearchCountry(string cc)
        {
            lock (lockObj)
            {
                try
                {
                    checkUpdate();

                    if (cache != null)
                    {
                        if (cache.CountryCodeToName.ContainsKey(cc))
                        {
                            return cache.CountryCodeToName[cc];
                        }
                    }

                    return "";
                }
                catch
                {
                    return "";
                }
            }
        }

        static Cache<uint, FullRouteIPInfoEntry> hit_cache = new Cache<uint, FullRouteIPInfoEntry>(new TimeSpan(24, 0, 0), CacheType.UpdateExpiresWhenAccess);

        public static FullRouteIPInfoEntry Search(string ipStr)
        {
            try
            {
                return Search(IPUtil.StrToIP(ipStr));
            }
            catch
            {
                return null;
            }
        }
        public static FullRouteIPInfoEntry Search(IPAddress ip)
        {
            uint ip32 = Util.Endian(IPUtil.IPToUINT(ip));

            FullRouteIPInfoEntry e;
            //e = hit_cache[ip32];
            //if (e == null)
            {
                e = SearchFast(ip32);
                //e = SearchWithoutHitCache(ip32);
                /*
				if (e != null)
				{
					hit_cache.Add(ip32, e);
				}*/
            }

            return e;
        }

        public static FullRouteIPInfoEntry SearchFast(uint ip32)
        {
            try
            {
                checkUpdate();
            }
            catch
            {
            }
            try
            {
                FullRouteIPInfoCache c = cache;

                if (c != null)
                {
                    int low, high, middle, pos;

                    low = 0;
                    high = c.EntryList.Count - 1;
                    pos = int.MaxValue;

                    while (low <= high)
                    {
                        middle = (low + high) / 2;

                        uint target_from = c.EntryList[middle].From;

                        if (target_from == ip32)
                        {
                            pos = middle;
                            break;
                        }
                        else if (ip32 < target_from)
                        {
                            high = middle - 1;
                        }
                        else
                        {
                            low = middle + 1;
                        }
                    }

                    if (pos == int.MaxValue)
                    {
                        pos = low;
                    }

                    int pos_start = Math.Max(0, pos - 3);
                    int pos_end = Math.Min(pos + 3, c.EntryList.Count);

                    int i;
                    for (i = pos_start; i < pos_end; i++)
                    {
                        FullRouteIPInfoEntry e = c.EntryList[i];
                        if (ip32 >= e.From && ip32 <= e.To)
                        {
                            return e;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static FullRouteIPInfoEntry SearchWithoutHitCache(uint ip32)
        {

            try
            {
                checkUpdate();
            }
            catch
            {
            }


            try
            {
                FullRouteIPInfoCache current_cache = cache;

                if (current_cache != null)
                {
                    foreach (FullRouteIPInfoEntry e in current_cache.EntryList)
                    {
                        if (ip32 >= e.From && ip32 <= e.To)
                        {
                            return e;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        static void checkUpdate()
        {
            lock (lockObj)
            {
                if (cache != null && (cache.TimeStamp + LifeTime) >= DateTime.Now)
                {
                    return;
                }

                using (cache_file_global_lock.Lock())
                {
                    if (cache == null)
                    {
                        try
                        {
                            cache = FullRouteIPInfoCache.LoadFromFile(CacheFileName);
                        }
                        catch
                        {
                        }
                    }

                    if (cache != null && (cache.TimeStamp + LifeTime) >= DateTime.Now)
                    {
                        return;
                    }

                    try
                    {
                        if (nextDownloadRetry == 0 || (nextDownloadRetry <= Time.Tick64))
                        {
                            FullRouteIPInfoCache c2 = FullRouteIPInfoCache.CreateFromDownload(Url);
                            c2.SaveToFile(CacheFileName);
                            cache = c2;
                        }
                    }
                    catch
                    {
                        nextDownloadRetry = Time.Tick64 + DownloadRetryMSecs;
                    }
                }
            }
        }
    }
}
