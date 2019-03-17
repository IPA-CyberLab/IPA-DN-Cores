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
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace IPA.Cores.Basic
{
    class DnHttpPostData
    {
        NameValueCollection nv = new NameValueCollection();

        public void Add(string name, string value)
        {
            nv.Add(name, value);
        }

        public string CurrentString
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                bool b = false;

                foreach (string name in nv.Keys)
                {
                    string value = nv[name];

                    string tmp = "";

                    if (b)
                    {
                        tmp += "&";
                    }

                    b = true;

                    tmp += string.Format("{0}={1}", name, value);

                    sb.Append(tmp);
                }

                return sb.ToString();
            }
        }

        public Buf GetData(Encoding enc)
        {
            return new Buf(enc.GetBytes(this.CurrentString));
        }
    }

    class DnHttpClient
    {
        CookieContainer cc = new CookieContainer();
        public CookieContainer CookieContainer
        {
            get { return cc; }
        }

        public bool NoCookie = false;
        public bool Http1_1 = false;
        public string UserAgent = null;

        public string ProxyHostname = null;
        public int ProxyPort = 0;

        TimeSpan timeout = new TimeSpan(0, 0, 15);
        public TimeSpan Timeout
        {
            get { return timeout; }
            set { timeout = value; }
        }

        int timeoutInt
        {
            get
            {
                if (this.timeout.Ticks == 0)
                {
                    return System.Threading.Timeout.Infinite;
                }
                else
                {
                    return (int)this.timeout.TotalMilliseconds;
                }
            }
        }

        static bool OnRemoteCertificateValidationCallback(Object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate,
            System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        static DnHttpClient()
        {
            ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(OnRemoteCertificateValidationCallback);
        }

        public Buf Get(Uri uri)
        {
            HttpWebRequest r = (HttpWebRequest)HttpWebRequest.Create(uri);

            if (this.NoCookie == false)
            {
                r.CookieContainer = this.cc;
            }
            else
            {
                r.CookieContainer = new CookieContainer();
            }

            if (Str.IsEmptyStr(this.ProxyHostname) == false && this.ProxyPort != 0)
            {
                r.Proxy = new WebProxy(this.ProxyHostname, this.ProxyPort);
            }

            r.UnsafeAuthenticatedConnectionSharing = true;
            r.AllowAutoRedirect = true;
            if (Http1_1 == false)
            {
                r.KeepAlive = false;
                r.ProtocolVersion = new Version(1, 0);
            }
            r.Timeout = this.timeoutInt;
            r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            r.Method = "GET";

            if (this.UserAgent != null)
            {
                r.UserAgent = this.UserAgent;
            }

            WebResponse res = r.GetResponse();

            Stream st = res.GetResponseStream();

            return Buf.ReadFromStream(st);
        }

        public Buf Post(Uri uri, DnHttpPostData postData, Encoding postEncoding)
        {
            return Post(uri, postData, postEncoding, null);
        }
        public Buf Post(Uri uri, DnHttpPostData postData, Encoding postEncoding, string referer)
        {
            return Post(uri, postData.GetData(postEncoding).ByteData, referer);
        }
        public Buf Post(Uri uri, byte[] postData, string referer)
        {
            return Post(uri, postData, referer, "application/x-www-form-urlencoded");
        }
        public Buf Post(Uri uri, byte[] postData, string referer, string content_type)
        {
            HttpWebRequest r = (HttpWebRequest)HttpWebRequest.Create(uri);

            if (this.NoCookie == false)
            {
                r.CookieContainer = this.cc;
            }
            else
            {
                r.CookieContainer = new CookieContainer();
            }

            if (Str.IsEmptyStr(this.ProxyHostname) == false && this.ProxyPort != 0)
            {
                r.Proxy = new WebProxy(this.ProxyHostname, this.ProxyPort);
            }

            r.UnsafeAuthenticatedConnectionSharing = true;
            r.AllowAutoRedirect = true;
            if (Http1_1 == false)
            {
                r.KeepAlive = false;
                r.ProtocolVersion = new Version(1, 0);
            }
            r.Timeout = this.timeoutInt;
            r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            r.Method = "POST";

            if (this.UserAgent != null)
            {
                r.UserAgent = this.UserAgent;
            }

            if (Str.IsEmptyStr(referer) == false)
            {
                r.Referer = referer;
            }

            Buf data = new Buf(postData);
            r.ContentType = content_type;
            r.ContentLength = data.Size;

            Stream st2 = r.GetRequestStream();
            st2.Write(data.ByteData, 0, (int)data.Size);
            st2.Flush();
            st2.Close();

            WebResponse res = r.GetResponse();

            Stream st = res.GetResponseStream();

            return Buf.ReadFromStream(st);
        }
    }
}
