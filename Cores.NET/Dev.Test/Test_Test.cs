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

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.IO;
using System.IO.Ports;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Data;
using System.Data.Common;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Diagnostics;
using System.Reflection;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;
using IPA.Cores.ClientApi.Acme;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.FileProviders;
using System.Web;
using System.Text;
using IPA.Cores.Basic.App.DaemonCenterLib;
using IPA.Cores.ClientApi.GoogleApi;
using System.Security.Cryptography;
//using IPA.Cores.Basic.Tests;
//using System.Runtime.InteropServices.WindowsRuntime;
using System.IO.Compression;
using IPA.Cores.Basic.HttpClientCore;
using Microsoft.Extensions.Hosting;
using IPA.Cores.ClientApi;
using IPA.Cores.ClientApi.SlackApi;

using IPA.Cores.Codes;


#pragma warning disable CS0219
#pragma warning disable CS0162


namespace IPA.TestDev
{
    [Serializable]
    [DataContract]
    class TestData
    {
        [DataMember]
        public int A;
        [DataMember]
        public string? B;
        [DataMember]
        public int C;
    }

    static class EnumTestClass
    {
        public static int GetValue<TKey>(TKey src) where TKey : unmanaged, Enum
        {
            return src.GetHashCode();
        }
        public static unsafe int GetValue2<TKey>(TKey src) where TKey : unmanaged, Enum
        {
            void* ptr = Unsafe.AsPointer(ref src);
            return *((int*)ptr);
        }
    }

    class TestHiveData1
    {
        public string? Str;
        public string? Date;
        public List<string> StrList = new List<string>();
    }

    class ZZ
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public TcpDirectionType Z;
    }

    class AcmeTestHttpServerBuilder : HttpServerStartupBase
    {
        public static AcmeAccount? AcmeAccount;

        public AcmeTestHttpServerBuilder(IConfiguration configuration) : base(configuration)
        {
        }

        public virtual async Task AcmeGetChallengeFileRequestHandler(HttpRequest request, HttpResponse response, RouteData routeData)
        {
            try
            {
                AcmeAccount? currentAccount = GlobalCertVault.GetAcmeAccountForChallengeResponse();
                string retStr;

                if (currentAccount == null)
                {
                    retStr = "Error: GlobalCertVault.GetAcmeAccountForChallengeResponse() == null";
                }
                else
                {
                    string token = routeData.Values._GetStr("token");

                    retStr = currentAccount.ProcessChallengeRequest(token);
                }

                await response._SendStringContentsAsync(retStr, Consts.MimeTypes.OctetStream);
            }
            catch (Exception ex)
            {
                ex._Debug();
                throw;
            }
        }

        protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            RouteBuilder rb = new RouteBuilder(app);

            rb.MapGet("/.well-known/acme-challenge/{token}", AcmeGetChallengeFileRequestHandler);

            IRouter router = rb.Build();
            app.UseRouter(router);
        }

        protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
        }
    }

    static class TestClass
    {
        public static void Test()
        {
            //Con.WriteLine(Env.AppRealProcessExeFileName);

            //"eyJ0ZXJtc09mU2VydmljZUFncmVlZCI6dHJ1ZSwiY29udGFjdCI6WyJtYWlsdG86ZGEuMTkwNjE1QHNvZnRldGhlci5jby5qcCJdLCJzdGF0dXMiOm51bGwsImlkIjpudWxsLCJjcmVhdGVkQXQiOiIwMDAxLTAxLTAxVDAwOjAwOjAwIiwia2V5IjpudWxsLCJpbml0aWFsSXAiOm51bGwsIm9yZGVycyI6bnVsbCwiTG9jYXRpb24iOm51bGx9"
            //    ._Base64UrlDecode()._GetString_UTF8()._Print();
            //return;

            Test_Generic();

            //Test_DaemonCenterClient();

            //var c = new Certificate(Lfs.ReadDataFromFile(@"S:\CommomDev\DigitalCert\all.open.ad.jp\2018\all.open.ad.jp_chained.crt").Span);

            //Test_RSA_Cert();

            //Test_ECDSA_Cert();

            //Test_Acme();

            //Test_Acme_Junk();

            //Test_HiveLock();

            //Test_PersistentCache();

            //Test_Vault();

            //Test_Vault_With_Kestrel();

            //LocalNet.GetLocalHostPossibleIpAddressListAsync()._GetResult()._DoForEach(x => x._Print());

            //Test_SourceCodeCounter("https://github.com/IPA-CyberLab/IPA-DN-Cores.git");

            //Test_Logger_Server_And_Client();

            //Test_GcDelay();
        }



        static void Test_DaemonCenterClient()
        {
            TcpIpHostDataJsonSafe hostData = new TcpIpHostDataJsonSafe(getThisHostInfo: EnsureSpecial.Yes, true);

            ClientSettings settings = new ClientSettings
            {
                AppId = "ID-0619097921-639-051627953879105-APP-13189-99628",
                HostGuid = "guid1",
                HostName = hostData.FqdnHostName,
                ServerCertSha = "06:52:5B:35:EE:1A:A4:A4:4A:E1:F7:D6:D2:F0:15:9F:E0:B9:22:F7:23:04:20:9A:3D:03:90:E3:80:0A:31:92",
                ServerUrl = "https://pc34.sehosts.com/rpc",
            };

            ClientVariables vars = new ClientVariables
            {
                CurrentCommitId = Dbg.GetCurrentGitCommitId(),
                StatFlag = StatFlag.OnGit,
                CurrentInstanceArguments = "hello",
            };

            using (Client client = new Client(settings, vars, (msg) => Dbg.Where(msg._ObjectToJson())))
            {
                Con.ReadLine();
            }
        }


        volatile static List<object>? __gc_test_list = null;

        public static void Test_GcDelay()
        {
            __gc_test_list = new List<object>();
            int c = 0;
            while (true)
            {
                c++;
                if ((c % 1000) == 0) c._Print();
                double start = Time.NowHighResDouble;
                for (int i = 0; i < 10000; i++)
                {
                    object obj = new object();
                    __gc_test_list.Add(obj);
                }

                __gc_test_list = new List<object>();

                double end = Time.NowHighResDouble;

                double d = end - start;

                if (d >= 0.004)
                {
                    Con.WriteLine(d);
                }
            }
        }

#if false // 2020/11/14 libgit は native ライブラリなので BASIC から外した。後日 Advance にでも入れる予定
        public static void Test_SourceCodeCounter(string url)
        {
            GitGlobalFs.StartRepository(url);
            GitRepository rep = GitGlobalFs.GetRepository(url);
            GitGlobalFs.UpdateRepository(url);
            GitRef master = rep.GetOriginMasterBranch();

            List<GitCommit> commitLogs = master.Commit.GetCommitLogs();

            DateTime start = commitLogs.Select(x => x.TimeStamp).Min().LocalDateTime;
            DateTime end = commitLogs.Select(x => x.TimeStamp).Max().LocalDateTime;

            for (DateTime dt = start; dt <= end; dt = dt.AddDays(7))
            {
                GitCommit? commit = commitLogs.Where(x => x.TimeStamp < dt).OrderByDescending(x => x.TimeStamp).FirstOrDefault();

                if (commit != null)
                {
                    GitFileSystem fs = GitGlobalFs.GetFileSystem(url, commit.CommitId);

                    SourceCodeCounter counter = new SourceCodeCounter(new DirectoryPath("/", fs), "HttpClient.cs");

                    Con.WriteLine(Str.CombineStringArray(",", dt._ToDtStr(option: DtStrOption.DateOnly), counter.NumLines, counter.TotalSize, counter.NumFiles));
                }
            }
        }
#endif

        [Serializable]
        public class STest1
        {
            public string S1 { get; }
            string P1 { get; }

            public string GetP1() => P1;

            public STest1(string arg1)
            {
                this.S1 = arg1;
                this.P1 = arg1 + "_private";
            }
        }

        public class TestClass1
        {
            public string? b { get; set; }
            public string? c { get; set; }
            public string? a { get; set; }
        }

        // ターゲット文字列からホスト名とエイリアスを導出する
        public static void ParseTargetString(string src, out string hostname, out string alias)
        {
            hostname = "";
            alias = "";

            string[] tokens = src._NonNullTrim()._Split(StringSplitOptions.RemoveEmptyEntries, "=");

            string a, b;

            if (tokens.Length == 0) return;

            if (tokens.Length == 1)
            {
                a = tokens[0];
                b = tokens[0];
            }
            else
            {
                a = tokens[0];
                b = tokens[1];
            }

            if (a._IsEmpty()) a = b;

            hostname = a;
            alias = b;

            tokens = alias._Split(StringSplitOptions.RemoveEmptyEntries, "|");
            if (tokens.Length >= 1 && tokens[0]._IsFilled())
            {
                alias = tokens[0];
            }
        }


        class TestClass123
        {
            public static void Test123()
            {
                CoresLibException e = new CoresLibException();

                throw e;
            }
        }


        // SNMP Worker CGI ハンドラ
        public class CgiServerStressTest_TestCgiHandler : CgiHandlerBase
        {
            public CgiServerStressTest_TestCgiHandler()
            {
            }

            protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
            {
                try
                {
                    noAuth.AddAction("/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                    {
                        await Task.CompletedTask;
                        var method = ctx.QueryString._GetStrFirst("method")._ParseEnum(SnmpWorkGetMethod.Get);

                        StringWriter w = new StringWriter();
                        for (int i = 0; i < 100; i++)
                        {
                            w.WriteLine($"Hello World Neko Neko Neko {i}");
                        }

                        return new HttpStringResult(w.ToString()._NormalizeCrlf(CrlfStyle.Lf, true));
                    });
                }
                catch
                {
                    this._DisposeSafe();
                    throw;
                }
            }
        }

        static void CgiServerStressTest_Server()
        {
            // HTTP サーバーを立ち上げる
            var cgi = new CgiHttpServer(new CgiServerStressTest_TestCgiHandler(), new HttpServerOptions()
            {
                AutomaticRedirectToHttpsIfPossible = false,
                DisableHiveBasedSetting = true,
                DenyRobots = true,
                UseGlobalCertVault = false,
                LocalHostOnly = true,
                HttpPortsList = new int[] { 7007 }.ToList(),
                HttpsPortsList = new List<int>(),
                UseKestrelWithIPACoreStack = true,
            },
            true);
        }

        static void CgiServerStressTest_Server2()
        {
            var host = new SnmpWorkHost();

            host.Register("Temperature", 101_00000, new SnmpWorkFetcherTemperature(host));
            host.Register("Ram", 102_00000, new SnmpWorkFetcherMemory(host));
            host.Register("Disk", 103_00000, new SnmpWorkFetcherDisk(host));
            host.Register("Net", 104_00000, new SnmpWorkFetcherNetwork(host));

            host.Register("Ping", 105_00000, new SnmpWorkFetcherPing(host));
            host.Register("Speed", 106_00000, new SnmpWorkFetcherSpeed(host));
            host.Register("Quality", 107_00000, new SnmpWorkFetcherPktQuality(host));
            host.Register("Bird", 108_00000, new SnmpWorkFetcherBird(host));

            Con.WriteLine("SnmpWorkDaemon: Started.");
        }

        static void CgiServerStressTest()
        {
            RefLong count = 0;

            CgiServerStressTest_Server2();

            for (int i = 0; i < 5; i++)
            {
                Task t = AsyncAwait(async () =>
                {
                    while (true)
                    {
                        await Task.Yield();

                        try
                        {
                            using var web = new WebApi(new WebApiOptions());

                            var ret = await web.SimpleQueryAsync(WebMethods.GET, "http://127.0.0.1:7007/?method=GetAll");

                            count++;
                        }
                        catch (Exception ex)
                        {
                            ex.Message._Print();
                        }
                    }
                });
            }

            while (true)
            {
                $"Current: {count}"._Print();

                Sleep(1000);
            }
        }

        static void VaultStressTest()
        {
            int[] ports = { 80, 443, 7009 };
            string dest = "pktlinux.sec.softether.co.jp";
            string url = "https://pktlinux.sec.softether.co.jp/";

            RefInt SslCounter = 0;

            RefInt DisconnectCounter = 0;

            // 接続してすぐ切断する動作
            for (int i = 0; i < 50; i++)
            {
                var task = AsyncAwait(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            int mode = Util.RandSInt31() % 2;
                            int port = ports[Util.RandSInt31() % ports.Length];
                            int waittime = Util.RandSInt31() % 1000;
                            using var sock = await LocalNet.ConnectAsync(new TcpConnectParam(dest, port, connectTimeout: 3000, dnsTimeout: 3000));
                            using var st = sock.GetStream();

                            if (mode == 0)
                            {
                                waittime = 0;
                            }

                            if (waittime >= 1)
                            {
                                await Task.Delay(waittime);
                            }

                            await sock.DisconnectAsync();

                            DisconnectCounter.Increment();
                        }
                        catch (Exception ex)
                        {
                            ex._Error();
                        }
                    }
                });
            }

            // 接続して長時間放っておく動作
            for (int i = 0; i < 50; i++)
            {
                var task = AsyncAwait(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            int port = ports[Util.RandSInt31() % ports.Length];
                            int waittime = 60 * 1000;
                            using var sock = await LocalNet.ConnectAsync(new TcpConnectParam(dest, port, connectTimeout: 3000, dnsTimeout: 3000));
                            using var st = sock.GetStream();

                            if (waittime >= 1)
                            {
                                await Task.Delay(waittime);
                            }

                            await sock.DisconnectAsync();

                            DisconnectCounter.Increment();
                        }
                        catch (Exception ex)
                        {
                            ex._Error();
                        }
                    }
                });
            }

            // SSL
            for (int i = 0; i < 50; i++)
            {
                var task = AsyncAwait(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            using WebApi api = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, DoNotThrowHttpResultError = true }));

                            await api.SimpleQueryAsync(WebMethods.GET, url);

                            SslCounter.Increment();
                        }
                        catch (Exception ex)
                        {
                            ex._Error();
                        }
                    }
                });
            }


            while (true)
            {
                string msg = $"SslCounter = {SslCounter}, DisconnectCounter = {DisconnectCounter}";

                msg._Print();

                Sleep(1000);
            }
        }

        static void Test_MakeDummyCerts_210828()
        {
            string baseDir = @"c:\tmp\210828_dummy_certs\";

            if (false)
            {
                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                DateTime start = new DateTime(2019, 1, 1, 18, 0, 0);
                DateTime end = new DateTime(2020, 1, 1, 19, 59, 59);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "dummycert-expired.example.org", c: "JP", expires: end, shaSize: PkiShaSize.SHA512, issuedAt: start));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(baseDir + @"01_ExpiredDummyCert_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(baseDir + @"01_ExpiredDummyCert.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"01_ExpiredDummyCert.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"01_ExpiredDummyCert.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }

            if (false)
            {
                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                DateTime start = new DateTime(2019, 1, 1, 18, 0, 0);
                DateTime end = new DateTime(2020, 1, 1, 19, 59, 59);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "*.dummycert-expired.example.org", c: "JP", expires: end, shaSize: PkiShaSize.SHA512, issuedAt: start));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(baseDir + @"02_ExpiredWildCardDummyCert_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(baseDir + @"02_ExpiredWildCardDummyCert.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"02_ExpiredWildCardDummyCert.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"02_ExpiredWildCardDummyCert.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }

            if (true)
            {
                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                DateTime start = new DateTime(2019, 1, 1, 18, 0, 0);
                DateTime end = new DateTime(2020, 1, 1, 19, 59, 59);

                List<string> fqdns = new List<string>();
                fqdns.Add("*.multiple-dummycert-2.example.org");
                fqdns.Add("a.multiple-dummycert.example.org");
                fqdns.Add("b.multiple-dummycert.example.org");
                fqdns.Add("c.multiple-dummycert.example.org");
                fqdns.Add("d.multiple-dummycert-2.example.org");
                fqdns.Add("e.multiple-dummycert-2.example.org");
                fqdns.Add("f.multiple-dummycert-2.example.org");

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "*.multiple-dummycert.example.org", subjectAltNames: fqdns.ToArray(), c: "JP", expires: end, shaSize: PkiShaSize.SHA512, issuedAt: start));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(baseDir + @"03_ExpiredMultipleFqdnWildcardDummyCert_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(baseDir + @"03_ExpiredMultipleFqdnWildcardDummyCert.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"03_ExpiredMultipleFqdnWildcardDummyCert.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"03_ExpiredMultipleFqdnWildcardDummyCert.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }
        }

        static void Test_MakeDummyCerts_210604()
        {
            string baseDir = @"c:\tmp\210604_dummy_certs\";

            if (true)
            {
                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "dummycert.example.org", c: "JP", expires: Util.MaxDateTimeOffsetValue, shaSize: PkiShaSize.SHA512));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(baseDir + @"00_DummyCert_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(baseDir + @"00_DummyCert.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_DummyCert.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_DummyCert.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }
        }

        static void Test_MakeDummyCerts_211115()
        {
            string baseDir = @"c:\tmp\211115_dummy_certs2\";

            if (true)
            {
                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "dummycert2.softether.com", c: "JP", expires: Util.MaxDateTimeOffsetValue, shaSize: PkiShaSize.SHA512));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(baseDir + @"00_DummyCert2_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(baseDir + @"00_DummyCert2.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_DummyCert2.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_DummyCert2.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }
        }

        static void Test_MakeThinOssCerts_201120()
        {
            string baseDir = @"M:\\Projects\ThinTelework_OSS\Certs\201120_Certs\";

            if (false)
            {
                string password = "microsoft";

                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "Thin Telework System Open Source Version Sample Gateway Root Cert", c: "JP", expires: Util.MaxDateTimeOffsetValue, shaSize: PkiShaSize.SHA512));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(baseDir + @"00_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(baseDir + @"00_Master.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                Lfs.WriteDataToFile(baseDir + @"00_Master_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_Master.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_Master.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }

            if (false)
            {
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(baseDir + @"00_Master.pfx").Span);

                IssueCert("Thin Telework System Open Source Version Sample Gateway Certificate 01", baseDir + @"01_GatewaySystem");

                void IssueCert(string cn, string fileNameBase)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: Util.MaxDateTimeOffsetValue, shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }
            }

            if (false)
            {
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(baseDir + @"00_Master.pfx").Span);

                IssueCert("Thin Telework System Open Source Version Sample Controller Certificate 02", baseDir + @"02_Controller");

                void IssueCert(string cn, string fileNameBase)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: Util.MaxDateTimeOffsetValue, shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }
            }

            if (false)
            {
                string password = "microsoft";

                PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "*.thinwebclient.example.org", c: "JP", expires: Util.MaxDateTimeOffsetValue, shaSize: PkiShaSize.SHA256));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteDataToFile(baseDir + @"03_WebClient.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                Lfs.WriteDataToFile(baseDir + @"03_WebClient_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"03_WebClient.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"03_WebClient.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }
        }

        static void Test_MakeThinLgwanCerts_201117()
        {
            string baseDir = @"\\labfs\share\Secrets\Projects\ThinTelework_LGWAN\Certs\200930_Certs\";

            if (true)
            {
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(baseDir + @"00_Master.pfx").Span);

                IssueCert("telework.ipa.asp.lgwan.jp", baseDir + @"03_TeleworkWebProxy");

                void IssueCert(string cn, string fileNameBase)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: Util.MaxDateTimeOffsetValue, shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }
            }
        }

        static void Test_MakeThinLgwanCerts_200930()
        {
            string baseDir = @"\\labfs\share\Secrets\Projects\ThinTelework_LGWAN\Certs\200930_Certs\";

            if (true)
            {
                string password = "microsoft";

                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "IPA Telework System for LGWAN Root Certificate", c: "JP", expires: new DateTime(2037, 12, 31), shaSize: PkiShaSize.SHA512));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(baseDir + @"00_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(baseDir + @"00_Master.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                Lfs.WriteDataToFile(baseDir + @"00_Master_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_Master.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(baseDir + @"00_Master.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
            }

            if (true)
            {
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(baseDir + @"00_Master.pfx").Span);

                IssueCert("IPA Telework System for LGWAN Controller Certificate 01", baseDir + @"01_Controller");
                IssueCert("IPA Telework System for LGWAN Gateway Certificate 02", baseDir + @"02_Gates_001");

                void IssueCert(string cn, string fileNameBase)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: new DateTime(2037, 12, 31), shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }
            }
        }

        static void Test_ThinLgWanSshConfigMaker()
        {
            string src = @"c:\ssh\g1.gts";
            for (int i = 2; i <= 32; i++)
            {
                string dst = PP.Combine(PP.GetDirectoryName(src), $"g{i}.gts");
                string body = Lfs.ReadStringFromFile(src);
                body = body._ReplaceStr("g1", $"g{i}");
                Lfs.WriteStringToFile(dst, body);
            }
        }

        static void Test_ThinLgWanConfigMaker()
        {
            if (true)
            {
                string src = @"C:\Dropbox\COENET\メモ資料\200930 自治体テレワーク for LGWAN 開発設計資料\第三世代 本番環境\G. Config および手順書集\G-22. HTTPS 画面通信受付サーバ (LGWAN 公開セグメント側)\_内部用_テンプレート\lgwang0.txt";
                for (int i = 1; i <= 32; i++)
                {
                    string dst = @"C:\Dropbox\COENET\メモ資料\200930 自治体テレワーク for LGWAN 開発設計資料\第三世代 本番環境\G. Config および手順書集\G-22. HTTPS 画面通信受付サーバ (LGWAN 公開セグメント側)\" +
                        $"lgwang{i}.txt";

                    string srcData = Lfs.ReadStringFromFile(src);
                    string dstData = srcData._ReplaceStrWithReplaceClass(
                        new
                        {
                            __INDEX1__ = i,
                            __INDEX2__ = i.ToString("D2"),
                            __VMNUMBER__ = (((i - 1) / 8) + 1),
                        });

                    Lfs.WriteStringToFile(dst, dstData);
                }
            }

            if (true)
            {
                string src = @"C:\Dropbox\COENET\メモ資料\200930 自治体テレワーク for LGWAN 開発設計資料\第三世代 本番環境\G. Config および手順書集\G-26. HTTPS 画面通信受付サーバ (インターネット公開セグメント側)\_内部用_テンプレート\inetg0.txt";
                for (int i = 1; i <= 32; i++)
                {
                    string dst = @"C:\Dropbox\COENET\メモ資料\200930 自治体テレワーク for LGWAN 開発設計資料\第三世代 本番環境\G. Config および手順書集\G-26. HTTPS 画面通信受付サーバ (インターネット公開セグメント側)\" +
                        $"inetg{i}.txt";

                    string srcData = Lfs.ReadStringFromFile(src);
                    string dstData = srcData._ReplaceStrWithReplaceClass(
                        new
                        {
                            __INDEX1__ = i,
                            __INDEX2__ = i.ToString("D2"),
                            __VMNUMBER__ = (((i - 1) / 8) + 1),
                        });

                    Lfs.WriteStringToFile(dst, dstData);
                }
            }
        }

        static void Test_ThinLgWanMapping()
        {
            IPv4Addr privateIp = new IPv4Addr("10.47.3.101");
            IPv4Addr lgwanIp = new IPv4Addr("61.212.19.199");
            IPv4Addr internetIp = new IPv4Addr("163.220.245.201");
            IPv4Addr lgwanPrivateIp = new IPv4Addr("10.47.2.101");
            IPv4Addr internetPrivateIp = new IPv4Addr("10.47.4.101");

            for (int i = 0; i < 32; i++)
            {
                $"GateProxyMappings{i:D3}     {privateIp.Add(i).ToString()._AddSpacePadding(17)} {internetIp.Add(i).ToString()._AddSpacePadding(17)} {lgwanIp.Add(i).ToString()._AddSpacePadding(17)}  inetg{(i + 1)}.gates.lgwan.cyber.ipa.go.jp  lgwang{(i + 1)}.ipa.asp.lgwan.jp   {internetPrivateIp.Add(i).ToString()._AddSpacePadding(17)}  {lgwanPrivateIp.Add(i).ToString()._AddSpacePadding(17)}  ".Trim()._Print();
            }
        }

        static void Test_ThinLgWanConnectivityTest()
        {
            IPv4Addr ipBase = new IPv4Addr("163.220.245.201");

            List<string> hostList = new List<string>();

            for (int i = 0; i < 32; i++)
            {
                var ip = ipBase.Add(i).ToString();

                WebApiSettings setting = new WebApiSettings
                {
                    DoNotThrowHttpResultError = true,
                };

                setting.SslAcceptCertSHAHashList.Add("f2365a80f2f9d50621d91c50b1c7ea56f09fbed6");

                using var web = new WebApi(new WebApiOptions(setting));

                var ret = web.SimpleQueryAsync(WebMethods.GET, $"https://{ip}/")._GetResult();

                $"{ip}: {ret.StatusCodeAndReasonString} {ret.Data._GetString_UTF8()._MakeAsciiOneLinePrintableStr()}"._Print();
            }
        }

        static void ZipTest_201201()
        {
            using var file = Lfs.Create(@"c:\tmp\ziptest\test" + Str.DateTimeToStrShort(DtNow) + ".zip", flags: FileFlags.AutoCreateDirectory);
            using ZipWriter zip = new ZipWriter(new ZipContainerOptions(file));
            Memory<byte> data = ""._GetBytes_Ascii();

            zip.AddFileSimpleData(new FileContainerEntityParam("test.txt", flags: FileContainerEntityFlags.None, encryptPassword: "a"), data);
            zip.Finish();
        }

        [Serializable]
        public class HiveTest_201201_Data
        {
            public int Value;
        }

        static void HiveTest_201201()
        {
            using var hiveData = new HiveData<HiveTest_201201_Data>(Hive.SharedLocalConfigHive, "HiveTest/TestConfigData",
                    getDefaultDataFunc: () => new HiveTest_201201_Data(),
                    policy: HiveSyncPolicy.AutoReadFromFile | HiveSyncPolicy.AutoWriteToFile,
                    serializer: HiveSerializerSelection.RichJson);

            using var hiveData2 = new HiveData<HiveTest_201201_Data>(Hive.SharedLocalConfigHive, $"HiveTest/TestConfigData2", () => new HiveTest_201201_Data(), HiveSyncPolicy.None);

            CancellationTokenSource cancel = new CancellationTokenSource();

            Task t = AsyncAwait(async () =>
            {
                while (true)
                {
                    if (cancel.IsCancellationRequested) break;

                    hiveData.GetManagedDataSnapshot().Value._Print();

                    lock (hiveData.DataLock)
                    {
                        if ((hiveData.ManagedData.Value % 2) == 0)
                        {
                            hiveData.ManagedData.Value++;
                        }
                    }

                    //hiveData2.AccessData(true, (x) =>
                    //{
                    //    x.Value._Print();

                    //    x.Value++;
                    //});


                    await cancel._WaitUntilCanceledAsync(100);
                }
            });

            Con.ReadLine("Enter to quit>");

            cancel.Cancel();

            t._TryGetResult();
        }

        static async Task LambdaTest_201213_Async()
        {
            int a = 0;
            string s = "x";
            var res = await AsyncAwait(async () =>
            {
                a++;
                s = "y";
                await TaskCompleted;
                return 0;
            });
            Con.WriteLine(a);
            Con.WriteLine(s);
        }

        public class Test201213
        {
            public string A = "123";
            public int B = 456;
            [NoDebugDump]
            public List<string> C = new List<string>(new string[] { "a", "b", "c" });
        }

        static async Task Test_DNS_201213()
        {
            await using var dns = new DnsClientLibBasedDnsResolver(new DnsResolverSettings(reverseLookupInternalCacheTimeoutMsecs: 0, flags: DnsResolverFlags.Default,
                dnsServersList: " 64.6.64.6"._ToIPEndPoint(53)!._SingleArray()));

            while (true)
            {
                long stat = TickHighresNow;
                var fqdn = await dns.GetHostNameSingleOrIpAsync("8.8.8.8", noCache: true);
                long end = TickHighresNow;

                $"{fqdn}   {end - stat}"._Print();

                Sleep(10);
            }
        }

        public class TestTable_201213
        {
            [SimpleTableIgnore]
            public string A { get; set; } = "Hello";
            [SimpleTableOrder(8)]
            public string B = "World";
            public int C = 123;
        }

        static void Test_201215()
        {
            EasyJsonStrAttributes a = "{'axx':'b'}";

            a["x"] = "Neko";

            a._PrintAsJson(compact: true);
        }

        static async Task Test_201231Async(CancellationToken cancel)
        {
            var mem = Str.CHexArrayToBinary(Lfs.ReadStringFromFile(@"C:\git\IPA-DNP-ThinApps-Public\src\Vars\VarsActivePatch.h"));

            CoresConfig.WtcConfig.DefaultWaterMark.TrySet(mem);

            using WideTunnel wt = new WideTunnel(new WideTunnelOptions("DESK", "TestSan", new string[] { "https://c__TIME__.controller.dynamic-ip.thin.cyber.ipa.go.jp/widecontrol/" }, new List<Certificate>()));

            await using var c = await wt.WideClientConnectAsync("greenrdp2", new WideTunnelClientOptions(WideTunnelClientFlags.None, "", "", 0), true);

            await cancel._WaitUntilCanceledAsync();
        }

        class TestRequest1 : IDialogRequestData
        {
            public string A = "";
        }

        class TestResponse1 : IDialogResponseData
        {
            public string B = "";
        }

        static void Test_210102()
        {
            using DialogSessionManager sm = new DialogSessionManager();

            var sess = sm.StartNewSession(new DialogSessionOptions(async (sess, cancel) =>
            {
                for (int i = 0; ; i++)
                {
                    Dbg.Where();
                    var response = await sess.RequestAndWaitResponseAsync(new TestRequest1 { A = "abc" }, 10000, 1000);
                    Dbg.Where();
                    TestResponse1 res2 = (TestResponse1)response;
                    res2._PrintAsJson();
                    Dbg.Where();

                    //await Task.Delay(Util.RandSInt31() % 100);
                }

            }, 0, null), "");

            string sessId = sess.SessionId;

            $"Session ID = {sessId}"._Print();

            var t = AsyncAwait(async () =>
            {
                while (true)
                {
                    Dbg.Where();
                    var next = await sm.GetNextRequestAsync(sessId);

                    Dbg.Where();
                    if (next == null)
                    {
                        break;
                    }

                    var request = (TestRequest1)next.RequestData;

                    request._Print();

                    var response = new TestResponse1 { B = request.A + "--OK" };

                    if ((Util.RandSInt31() % 4) == 0)
                    {
                        //sm.SetResponseCancel(sessId, next.RequestId);
                        //sm.SetResponseException(sessId, next.RequestId, new CoresLibException("Neko"));
                    }
                    string s = Con.ReadLine(">")!;
                    if (s._IsSamei("q"))
                    {
                        sm.SetResponseCancel(sessId, next.RequestId);
                    }
                    //sm.SetResponseData(sessId, next.RequestId, response);
                    sm.SendHeartBeat(sessId, next.RequestId);
                }

                "All finished."._Print();
            });

            t._TryGetResult();
        }

        static async Task Test_210104Async(CancellationToken cancel)
        {
            var mem = Str.CHexArrayToBinary(Lfs.ReadStringFromFile(@"C:\git\IPA-DNP-ThinApps-Public\src\Vars\VarsActivePatch.h"));

            CoresConfig.WtcConfig.DefaultWaterMark.TrySet(mem);

            var wideOptions = new WideTunnelOptions("DESK", "TestSan", new string[] { "https://c__TIME__.controller.dynamic-ip.thin.cyber.ipa.go.jp/widecontrol/" }, new List<Certificate>());
            await using var sm = new DialogSessionManager(cancel: cancel);

            ThinClient tc = new ThinClient(new ThinClientOptions(wideOptions, sm));

            var sess = tc.StartConnect(new ThinClientConnectOptions("pc342", IPAddress.Loopback, IPAddress.Loopback.ToString(), false, new WideTunnelClientOptions(WideTunnelClientFlags.None, "", "", 0), false), 0);

            while (cancel.IsCancellationRequested == false)
            {
                var req = await sess.GetNextRequestAsync(cancel: cancel);

                if (req == null)
                {
                    break;
                }

                switch (req.RequestData)
                {
                    case ThinClientAuthRequest authReq:
                        req.SetResponseData(new ThinClientAuthResponse
                        {
                            Password = Lfs.ReadStringFromFile(@"C:\tmp\yagi\TestPass.txt", oneLine: true),
                        });
                        break;

                    case ThinClientAcceptReadyNotification ready:
                        req.SetResponseData(new EmptyDialogResponseData());
                        break;

                    case ThinClientOtpRequest otp:
                        req.SetResponseData(new ThinClientOtpResponse
                        {
                            Otp = "422262100362311531048070455606",
                        });
                        break;

                    case ThinClientInspectRequest insp:
                        req.SetResponseData(new ThinClientInspectResponse
                        {
                            AntiVirusOk = true,
                            WindowsUpdateOk = true,
                            MacAddressList = "11-BB-22-44-CC-DD",
                        });
                        break;
                }
            }

            await sm._DisposeSafeAsync();

            $"Session Error = {sess.Exception?.Message ?? "ok"}"._Print();
        }

        static void Test_210110()
        {
            CertificateStore store = new CertificateStore(Lfs.ReadDataFromFile(@"H:\Crypto\static.lts.dn.ipantt.net\static.lts.dn.ipantt.net.pfx").Span);

            store.PrimaryCertificate.PublicKey.GetPubKeySha256Base64()._Print();
        }

        static void Test_210307_Backup()
        {
            string infolog = @"C:\TMP\210307\log\info.log";
            string errorlog = @"C:\TMP\210307\log\error.log";

            string src = @"C:\tmp\yagi\test2\";
            string dst = $@"\\lts\DataRoot\tmp\test16\{Str.DateTimeToStrShortWithMilliSecs(DateTime.Now)}";

            bool err = false;

            try
            {
                Lfs.EnableBackupPrivilege();
            }
            catch (Exception ex)
            {
                Con.WriteError(ex);
            }

            using (var b = new DirSuperBackup(new DirSuperBackupOptions(Lfs, infolog, errorlog, encryptPassword: "test")))
            {
                Async(async () =>
                {
                    await b.DoSingleDirBackupAsync(src, dst, default);
                });

                if (b.Stat.Error_Dir != 0 || b.Stat.Error_NumFiles != 0)
                {
                    err = true;
                }
            }

            if (err)
            {
                Con.WriteError("Error occured.");
            }
            else
            {
                Con.WriteLine("Ok!!");
            }
        }

        static void Test_210307_Restore()
        {
            string infolog = @"C:\TMP\210307\log\restore_info.log";
            string errorlog = @"C:\TMP\210307\log\restore_error.log";

            string src = @"C:\TMP\210307\X";
            string dst = @"C:\TMP\210307\Y";

            bool err = false;

            try
            {
                Lfs.EnableBackupPrivilege();
            }
            catch (Exception ex)
            {
                Con.WriteError(ex);
            }

            using (var b = new DirSuperBackup(new DirSuperBackupOptions(Lfs, infolog, errorlog, DirSuperBackupFlags.RestoreMakeBackup, encryptPassword: "test")))
            {
                Async(async () =>
                {
                    await b.DoSingleDirRestoreAsync(src, dst, default);
                });

                if (b.Stat.Error_Dir != 0 || b.Stat.Error_NumFiles != 0)
                {
                    err = true;
                }
            }

            if (err)
            {
                Con.WriteError("Error occured.");
            }
            else
            {
                Con.WriteLine("Ok!!");
            }
        }

        static void Test_210307_EncCopy()
        {
            string src = @"C:\tmp\190314_copy_log_ex_1.log";
            string dst = @"C:\tmp\enc2\test.log";
            string dst2 = @"C:\tmp\enc2\test2.log";
            string pass = "pass";

            FileUtil.CopyFileAsync(Lfs, src, Lfs, dst, new CopyFileParams(encryptOption: EncryptOption.Encrypt | EncryptOption.Compress, encryptPassword: "test", flags: FileFlags.AutoCreateDirectory | FileFlags.CopyFile_Verify | FileFlags.WriteOnlyIfChanged))._GetResult();

            FileUtil.CopyFileAsync(Lfs, dst, Lfs, dst2, new CopyFileParams(encryptOption: EncryptOption.Decrypt | EncryptOption.Compress, encryptPassword: "test", flags: FileFlags.AutoCreateDirectory | FileFlags.CopyFile_Verify | FileFlags.WriteOnlyIfChanged))._GetResult();
        }

        static async Task Test_210309_Samba()
        {
            string dirName = @"\\lts\DataRoot\tmp\test11\";

            await Lfs.CreateDirectoryAsync(dirName);

            while (true)
            {
                List<int> o = new List<int>();

                for (int i = 0; i < 1024; i++)
                {
                    o.Add(i);
                }

                await Lfs.EnumDirectoryAsync(dirName, false, EnumDirectoryFlags.NoGetPhysicalSize);

                await TaskUtil.ForEachAsync(256, o, async (number, cancel) =>
                {
                    await Task.Yield();

                    string fileName = $"test.{number:D4}.dat";
                    string filePath = Path.Combine(dirName, fileName);

                    Console.WriteLine(filePath);

                    int size = Secure.RandSInt31() % 100000;

                    byte[] randomData = Util.Rand(size);
                    byte[] readData = new byte[size];

                    await using (var obj = await Lfs.CreateAsync(filePath))
                    {
                        await using var f = obj.GetStream();

                        await f.WriteAsync(randomData, 0, randomData.Length);
                    }

                    await using (var obj = await Lfs.OpenAsync(filePath))
                    {
                        await using var f = obj.GetStream();

                        await f.ReadAsync(readData);
                    }

                    int r = randomData.AsSpan().SequenceCompareTo(readData.AsSpan());
                    if (r != 0)
                    {
                        Console.WriteLine($"*** Different !!!!!!!!!");
                    }
                });
            }
        }

        static async Task Test_210309_Samba2()
        {
            Random rand = new Random((int)DateTime.Now.Ticks);
            string dirName = @"\\lts\DataRoot\tmp\test2\";

            string srcFile = @"C:\dropbox\COECTRL\tmp\Intel IntelDev\x64 - 1 - 253665-sdm-vol-1.pdf";

            await Lfs.CreateDirectoryAsync(dirName);

            while (true)
            {
                List<int> o = new List<int>();

                for (int i = 0; i < 1024; i++)
                {
                    o.Add(i);
                }

                await TaskUtil.ForEachAsync(32, o, async (number, cancel) =>
                {
                    await Task.Yield();

                    string fileName = $"test.{number:D4}.dat";
                    string filePath = Path.Combine(dirName, fileName);

                    Console.WriteLine(filePath);

                    await FileUtil.CopyFileAsync(srcFile, filePath,
                        new CopyFileParams(flags: FileFlags.BackupMode | FileFlags.CopyFile_Verify, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll))
                        );
                });
            }
        }

        static async Task Test_210309_Samba3()
        {
            Random rand = new Random((int)DateTime.Now.Ticks);
            string dirName = @"\\lts\DataRoot\tmp\test2\";

            string srcDir = @"c:\dropbox\.dropbox.cache\2020-01-15\";

            await Lfs.CreateDirectoryAsync(dirName);

            for (int j = 0; ; j++)
            {
                string destDir = $@"\\lts\DataRoot\tmp\test4\{j:D4}";

                CancellationToken cancel = default;

                string? ignoreDirNames = null;

                var srcEnum = await Lfs.EnumDirectoryAsync(srcDir);

                var srcFiles = srcEnum.Where(x => x.IsFile);

                await Lfs.CreateDirectoryAsync(destDir);

                await TaskUtil.ForEachAsync(32, srcFiles, async (srcFile, cancel) =>
                {
                    try
                    {
                        await Task.Yield();

                        string fileName = Lfs.PathParser.Combine(destDir, srcFile.Name);
                        string filePath = Path.Combine(dirName, fileName);

                        Console.WriteLine(filePath);

                        await FileUtil.CopyFileAsync(srcFile.FullPath, filePath,
                            new CopyFileParams(flags: FileFlags.BackupMode | FileFlags.CopyFile_Verify, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll))
                            );
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                        throw;
                    }
                });
            }
        }

        static async Task Test_210309_Samba4()
        {
            try
            {
                Lfs.EnableBackupPrivilege();
            }
            catch (Exception ex)
            {
                Con.WriteError(ex);
            }

            Random rand = new Random((int)DateTime.Now.Ticks);
            string dirName = $@"\\lts\DataRoot\tmp\test16\{Str.DateTimeToStrShortWithMilliSecs(DateTime.Now)}";

            string srcDir = @"C:\tmp\yagi\test2\";

            await Lfs.CreateDirectoryAsync(dirName);

            DirSuperBackupStat Stat = new DirSuperBackupStat();

            var Fs = Lfs;

            for (int j = 0; ; j++)
            {
                string destDir = Lfs.PathParser.Combine(dirName, $"{j:D4}");

                await Lfs.CreateDirectoryAsync(destDir);

                CancellationToken cancel = default;

                string? ignoreDirNames = null;

                {

                    {
                        DateTimeOffset now = DateTimeOffset.Now;

                        FileSystemEntity[]? srcDirEnum = null;

                        try
                        {
                            srcDirEnum = (await Fs.EnumDirectoryAsync(srcDir, false, EnumDirectoryFlags.NoGetPhysicalSize, cancel)).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer).ToArray();

                            // 元ディレクトリに存在するファイルを 1 つずつバックアップする
                            var fileEntries = srcDirEnum.Where(x => x.IsFile);

                            RefInt concurrentNum = new RefInt();

                            AsyncLock SafeLock = new AsyncLock();

                            await TaskUtil.ForEachAsync(48, fileEntries, async (srcFile, cancel) =>
                            {
                                long? encryptedPhysicalSize = null;

                                await Task.Yield();

                                string destFilePath = Fs.PathParser.Combine(destDir, srcFile.Name);

                                //if (Options.EncryptPassword._IsNullOrZeroLen() == false)
                                //{
                                //    destFilePath += Consts.Extensions.CompressedXtsAes256;
                                //}


                                concurrentNum.Increment();

                                try
                                {

                                    // ファイルをコピーする
                                    // 属性は、ファイルの日付情報のみコピーする
                                    //await WriteLogAsync(DirSuperBackupLogType.Info, Str.CombineStringArrayForCsv("FileCopy", srcFile.FullPath, destFilePath));

                                    destFilePath._Debug();

                                    await Fs.CopyFileAsync(srcFile.FullPath, destFilePath,
                                        new CopyFileParams(flags: FileFlags.BackupMode | FileFlags.CopyFile_Verify /*| FileFlags.Async*/, metadataCopier: new FileMetadataCopier(FileMetadataCopyMode.TimeAll),
                                        encryptOption: EncryptOption.None),
                                        cancel: cancel); ;

                                    Stat.Copy_NumFiles++;
                                    Stat.Copy_TotalSize += srcFile.Size;
                                }
                                catch (Exception ex)
                                {
                                    Stat.Error_NumFiles++;
                                    Stat.Error_TotalSize += srcFile.Size;

                                    // ファイル単位のエラー発生
                                    //await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("FileError", srcFile.FullPath, destFilePath, ex.ToString()));

                                    ex._Debug();
                                }
                                finally
                                {
                                    concurrentNum.Decrement();
                                }
                            }, cancel: cancel);

                            // 新しいメタデータをファイル名でソートする

                            // 新しいメタデータを書き込む
                            //string newMetadataFilePath = Fs.PathParser.Combine(destDir, $"{PrefixMetadata}{Str.DateTimeToStrShortWithMilliSecs(now.UtcDateTime)}{SuffixMetadata}");

                            //await Fs.WriteJsonToFileAsync(newMetadataFilePath, destDirNewMetaData, FileFlags.BackupMode | FileFlags.OnCreateSetCompressionFlag, cancel: cancel);
                        }
                        catch (Exception ex)
                        {
                            Stat.Error_Dir++;

                            // ディレクトリ単位のエラー発生
                            //await WriteLogAsync(DirSuperBackupLogType.Error, Str.CombineStringArrayForCsv("DirError", srcDir, destDir, ex.Message));

                            ex._Debug();
                        }
                    }


                }
            }
        }

        public static async Task GuaTest_210320()
        {
            await using GuaClient gc = new GuaClient(new GuaClientSettings("dn-ttguacd1.sec.softether.co.jp", 4822, GuaProtocol.Rdp, "pc37.sec.softether.co.jp", 3333,
                new GuaPreference(), false, false));

            await gc.StartAsync();

            Con.ReadLine("OK>");

            gc.ConnectionId._Print();
        }

        static void Test_210401()
        {
            //QueryStringList q = new QueryStringList();
            //q.Add("aaa", "123=456<789>?abc def");

            //q = new QueryStringList(q.ToString());
            //q.ToString()._Print();
            //return;

            while (true)
            {
                string str = Con.ReadLine("STR>")!;

                $"EncodeUrl          : {str._EncodeUrl()}"._Print();
                $"EncodeUrlPath      : {str._EncodeUrlPath()}"._Print();
                $"DecodeUrlPath      : {str._EncodeUrlPath()._DecodeUrlPath()}"._Print();
                $"DecodeUrlPath2     : {str._DecodeUrlPath()}"._Print();
                $"EscapeDataString   : {Uri.EscapeDataString(str)}"._Print();
                $"UnescapeDataString : {Uri.UnescapeDataString(Uri.EscapeDataString(str))}"._Print();
                $"UnescapeDataString2: {Uri.UnescapeDataString(str)}"._Print();

                ""._Print();
            }
        }

        static async Task Test_210414_Async()
        {
            /*var ret = await TestDevCommands.GmapAccessToPhotoUrlAsync(
                "https://www.google.co.jp/maps/preview/photo?authuser=0&hl=ja&gl=jp&pb=!1e3!5m62!2m2!1i203!2i100!3m3!2i4!3sCAEIBAgFCAYgAQ!5b1!7m50!1m3!1e1!2b0!3e3!1m3!1e2!2b1!3e2!1m3!1e2!2b0!3e3!1m3!1e3!2b0!3e3!1m3!1e8!2b0!3e3!1m3!1e3!2b1!3e2!1m3!1e10!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e9!2b1!3e2!1m3!1e10!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e10!2b0!3e4!2b1!4b1!8m0!9b0!11m1!4b1!6m3!1sFWd2YLrVJsLd-Qaa2JuoDg!7e81!15i11021!9m2!2d139.7667263527284!3d35.67531462990534!10d25"
                );

            ret._PrintAsJson();*/

            // string url =
            //    "https://www.google.co.jp/maps/preview/photo?authuser=0&hl=ja&gl=jp&pb=!1e3!5m62!2m2!1i203!2i100!3m3!2i4!3sCAEIBAgFCAYgAQ!5b1!7m50!1m3!1e1!2b0!3e3!1m3!1e2!2b1!3e2!1m3!1e2!2b0!3e3!1m3!1e3!2b0!3e3!1m3!1e8!2b0!3e3!1m3!1e3!2b1!3e2!1m3!1e10!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e9!2b1!3e2!1m3!1e10!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e10!2b0!3e4!2b1!4b1!8m0!9b0!11m1!4b1!6m3!1sFWd2YLrVJsLd-Qaa2JuoDg!7e81!15i11021!9m2!2d139.7667263527284!3d35.67531462990534!10d25";
            string url =
                "https://www.google.com/maps/photometa/v1?authuser=0&hl=ja&gl=jp&pb=!1m4!1smaps_sv.tactile!11m2!2m1!1b1!2m2!1sja!2sjp!3m3!1m2!1e2!2s4PRev_CWGvIvrwRIXU4Cag!4m57!1e1!1e2!1e3!1e4!1e5!1e6!1e8!1e12!2m1!1e1!4m1!1i48!5m1!1e1!5m1!1e2!6m1!1e1!6m1!1e2!9m36!1m3!1e2!2b1!3e2!1m3!1e2!2b0!3e3!1m3!1e3!2b1!3e2!1m3!1e3!2b0!3e3!1m3!1e8!2b0!3e3!1m3!1e1!2b0!3e3!1m3!1e4!2b0!3e3!1m3!1e10!2b1!3e2!1m3!1e10!2b0!3e3";

            await TestDevCommands.GmapStreetViewPhotoUrlAnalysisAsync(url, @"c:\tmp\sv2\", "abc", 1);
        }

        public class PhotoCsvEntry
        {
            public string No1 = "";
            public string No2 = "";
            public string Url = "";
            public string DateTimeStr = "";
            public string Result = "";
        }

        static async Task Test_210419_02_photometa_csv_analyze_Async()
        {
            string src = @"C:\TMP\210419_csv_analyze\0_before_all_urls.csv";
            string dst = @"C:\TMP\210419_csv_analyze\1_after_photometa.csv";

            string body = await Lfs.ReadStringFromFileAsync(src);
            var lines = body._GetLines();

            List<PhotoCsvEntry> list = new List<PhotoCsvEntry>();

            foreach (var line in lines)
            {
                if (line._InStr("/photometa"))
                {
                    var tokens = line._Split(StringSplitOptions.None, ",");
                    if (tokens.Length >= 4)
                    {
                        PhotoCsvEntry e = new PhotoCsvEntry
                        {
                            No1 = tokens[0].Trim(),
                            No2 = tokens[1].Trim(),
                            Url = tokens[2].Trim(),
                        };

                        e.DateTimeStr = tokens[3];

                        e.Result = "";

                        list.Add(e);
                    }
                }
            }

            int pos = 0;

            foreach (var e in list)
            {
                pos++;

                try
                {
                    string url = $"https://streetviewpixels-pa.googleapis.com/v1/tile?cb_client=maps_sv.tactile&panoid={GmapPoint.GetPhotoKeyFromPhotoMetaUrl(e.Url)}&x=0&y=0&zoom=0";
                    var downloadResult = await SimpleHttpDownloader.DownloadAsync(url, printStatus: false);

                    if (downloadResult.Data.Length >= 2000)
                    {
                        e.Result = "OK";
                    }
                    else
                    {
                        e.Result = "Deleted";
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                    e.Result = "ERROR: " + ex.ToString()._OneLine();
                }

                Con.WriteLine($"Status: total = {list.Count}, pos = {pos}, num_ok = {list.Where(x => x.Result == "OK").Count()}");
            }

            string csvBody = list._ObjectArrayToCsv(true);

            await Lfs.WriteStringToFileAsync(dst, csvBody, FileFlags.AutoCreateDirectory);
        }

        class PhotoMetaTest
        {
            public string Url = "";
            public bool Ok;
        }

        static async Task Test_210419_Async()
        {
            string fn = @"c:\tmp\210419.txt";
            string body = await Lfs.ReadStringFromFileAsync(fn);
            var lines = body._GetLines();

            List<PhotoMetaTest> list = new List<PhotoMetaTest>();

            foreach (var line in lines)
            {
                if (line._InStr("photometa"))
                {
                    PhotoMetaTest t = new PhotoMetaTest { Url = line.Trim(), Ok = false };

                    list.Add(t);
                }
            }

            Con.WriteLine($"件数: {list.Count}");

            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];

                Con.WriteLine($"Current pos: {i} / {list.Count}: {t.Url}");


                try
                {
                    string url = $"https://streetviewpixels-pa.googleapis.com/v1/tile?cb_client=maps_sv.tactile&panoid={GmapPoint.GetPhotoKeyFromPhotoMetaUrl(t.Url)}&x=0&y=0&zoom=0";
                    var downloadResult = await SimpleHttpDownloader.DownloadAsync(url, printStatus: false);

                    if (downloadResult.Data.Length >= 2000)
                    {
                        t.Ok = true;
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                Con.WriteLine($"Current result: OK = {(list.Where(x => x.Ok).Count())} / Total = i");

                Con.WriteLine();
            }

        }

        static void Test_210604()
        {
            Time.Time64ToDateTimeOffsetUtc(1000)._Print();
            Time.DateTimeToTime64(DateTime.UtcNow)._Print();
            Time.DateTimeToTime64(new DateTime(9999, 1, 1))._Print();
        }

        // UDP ソケットクライアント (DNS Client 動作テスト)
        static void Test_210616_Udp_Indirect_Socket_DNS_Client()
        {
            var dnsQueryMessage = "0008010000010000000000000476706e3109736f66746574686572036e65740000010001"._GetHexBytes();

            //while (true)
            {
                Where();
                Async(async () =>
                {
                    var uu = LocalNet.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Client, new IPEndPoint(IPAddress.Any, 0)));
                    {
                        await using var sock = uu.GetSocket(true);

                        await sock.SendDatagramAsync(new Datagram(dnsQueryMessage, IPEndPoint.Parse("8.8.8.8:53"), null));

                        var recv = await sock.ReceiveDatagramAsync(timeout: 100, noTimeoutException: true);


                        //var dns = DnsUtil.ParsePacket(recv.Data.Span);

                        //dns._PrintAsJson();

                        await sock.DisconnectAsync();
                        await sock._DisposeSafeAsync();
                    }
                });
                Where();
            }
        }

#pragma warning disable CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます
        // UDP ソケット DatagramSock 経由間接叩き 送受信ベンチマーク (DNS Server 模擬) - DNS 部をマルチタスク処理
        static void Test_210615_Udp_Indirect_SendRecv_Bench_DNS_Server_MultiTaskProcess()
        {
            // --- 受信 ---
            // pktlinux (Xeon 4C) ===> dn-vpnvault2 (Xeon 4C)
            // 受信とパース: 351 kpps くらい出た
            // 打ち返し: 250 kqps くらい出た --> 100 回パースループを入れると 14 kpps くらい

            bool reply = true;
            using var uu = LocalNet.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Server));
            uu.AddEndPoint(new IPEndPoint(IPAddress.Any, 5454));

            using var sock = uu.GetSocket();
            ThroughputMeasuse recvMeasure = new ThroughputMeasuse(1000, 1000);

            using var recvPrinter = recvMeasure.StartPrinter("UDP Recv: ", toStr3: true);

            using AsyncOneShotTester test = new AsyncOneShotTester(async c =>
            {
                while (true)
                {
                    var allRecvList = await sock.ReceiveDatagramsListAsync();
                    recvMeasure.Add(allRecvList.Count);

                    var allSendList = await allRecvList._ProcessDatagramWithMultiTasksAsync(async (perTaskRecvList) =>
                    {
                        List<Datagram> perTaskSendList = new List<Datagram>(perTaskRecvList.Count);

                        foreach (var item in perTaskRecvList)
                        {
                            try
                            {
                                var msg = DnsUtil.ParsePacket(item.Data.Span);

                                //for (int i = 0;i < 100;i++) DnsUtil.ParsePacket(item.Data.Span);

                                if (reply)
                                {
                                    var newData = msg.BuildPacket().ToArray().AsMemory();
                                    var newDg = new Datagram(newData, item.RemoteIPEndPoint!, item.LocalIPEndPoint);
                                    perTaskSendList.Add(newDg);
                                }
                            }
                            catch (Exception ex)
                            {
                                ex._Debug();
                            }
                        }

                        return perTaskSendList;
                    },
                    operation: MultitaskDivideOperation.RoundRobin,
                    cancel: c);

                    await sock.SendDatagramsListAsync(allSendList.ToArray());
                }
            });

            Con.ReadLine(">");

            sock.DisconnectAsync();
        }
#pragma warning restore CS1998 // 非同期メソッドは、'await' 演算子がないため、同期的に実行されます

        // UDP ソケット DatagramSock 経由間接叩き 送受信ベンチマーク (DNS Server 模擬)
        static void Test_210615_Udp_Indirect_SendRecv_Bench_DNS_Server()
        {
            // --- 受信 ---
            // pktlinux (Xeon 4C) ===> dn-vpnvault2 (Xeon 4C)
            // 受信とパース: 440 kpps くらい出た
            // 打ち返し: 220 kqps くらい出た --> 100 回パースループを入れると 7 kpps くらい
            // RasPi4 で 6 kpps くらい 遅いなあ

            bool reply = true;
            using var uu = LocalNet.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Server));
            uu.AddEndPoint(new IPEndPoint(IPAddress.Any, 5454));

            using var sock = uu.GetSocket();
            ThroughputMeasuse recvMeasure = new ThroughputMeasuse(1000, 1000);

            using var recvPrinter = recvMeasure.StartPrinter("UDP Recv: ", toStr3: true);

            using AsyncOneShotTester test = new AsyncOneShotTester(async c =>
            {
                while (true)
                {
                    var list = await sock.ReceiveDatagramsListAsync();

                    recvMeasure.Add(list.Count);

                    List<Datagram> sendList = new List<Datagram>(list.Count);

                    foreach (var item in list)
                    {
                        try
                        {
                            var msg = DnsUtil.ParsePacket(item.Data.Span);

                            for (int i = 0; i < 100; i++) DnsUtil.ParsePacket(item.Data.Span);

                            if (reply)
                            {
                                var newData = msg.BuildPacket().ToArray().AsMemory();
                                var newDg = new Datagram(newData, item.RemoteIPEndPoint!, item.LocalIPEndPoint);
                                sendList.Add(newDg);
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }

                    if (reply)
                    {
                        await sock.SendDatagramsListAsync(sendList.ToArray());
                    }
                }
            });

            Con.ReadLine(">");

            sock.DisconnectAsync();
        }

        // UDP ソケット DatagramSock 経由間接叩き 送受信ベンチマーク
        static void Test_210615_Udp_Indirect_SendRecv_Bench()
        {
            // --- 受信 ---
            // pktlinux (Xeon 4C) ===> dn-vpnvault2 (Xeon 4C)
            // 受信のみ: 800 kpps くらい出た
            // 打ち返し: 450 ～ 500 kpps くらい出た。コツは、ユーザースレッドからのパケット挿入時に softly: true にすること。複数スレッドの CPU に分散して処理されるので高速。
            //          (Windows でも 250 kpps くらい出た)

            bool reply = true;
            using var uu = LocalNet.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Server));
            uu.AddEndPoint(new IPEndPoint(IPAddress.Any, 5454));

            using var sock = uu.GetSocket();
            ThroughputMeasuse recvMeasure = new ThroughputMeasuse(1000, 1000);

            using var recvPrinter = recvMeasure.StartPrinter("UDP Recv: ", toStr3: true);

            using AsyncOneShotTester test = new AsyncOneShotTester(async c =>
            {
                while (true)
                {
                    var list = await sock.ReceiveDatagramsListAsync();

                    recvMeasure.Add(list.Count);

                    if (reply)
                    {
                        await sock.SendDatagramsListAsync(list.ToArray());
                    }
                }
            });

            Con.ReadLine(">");

            sock.DisconnectAsync();
        }

        // UDP ソケット Pipe 経由間接叩き 送受信ベンチマーク
        static void Test_210613_02_Udp_Indirect_SendRecv_Bench()
        {
            // --- 受信 ---
            // pktlinux (Xeon 4C) ===> dn-vpnvault2 (Xeon 4C)
            // 受信のみ: 800 kpps くらい出た
            // 打ち返し: 450 ～ 500 kpps くらい出た。コツは、ユーザースレッドからのパケット挿入時に softly: true にすること。複数スレッドの CPU に分散して処理されるので高速。
            //          (Windows でも 250 kpps くらい出た)

            bool reply = true;

            using var uu = LocalNet.CreateUdpListener(new NetUdpListenerOptions(TcpDirectionType.Server));
            uu.AddEndPoint(new IPEndPoint(IPAddress.Any, 5454));

            using var sock = uu.GetSocket();
            ThroughputMeasuse recvMeasure = new ThroughputMeasuse(1000, 1000);

            using var recvPrinter = recvMeasure.StartPrinter("UDP Recv: ", toStr3: true);

            using AsyncOneShotTester test = new AsyncOneShotTester(async c =>
            {
                var r = sock.UpperPoint.DatagramReader;
                var w = sock.UpperPoint.DatagramWriter;

                long start = TickNow;

                while (c.IsCancellationRequested == false)
                {
                    while (c.IsCancellationRequested == false)
                    {
                        var list = r.DequeueAllWithLock(out _);
                        if (list == null || list.Count == 0)
                        {
                            w.CompleteWrite(softly: true);
                            break;
                        }
                        //$"User Loop: Dequeue OK: Fetch Length = {list.Count}, Remain Length = {r.Length}"._Debug();
                        recvMeasure.Add(list.Count);

                        if (reply)
                        {
                            if (w.IsReadyToWrite())
                            {
                                w.EnqueueAllWithLock(list.ToArray(), false);
                            }
                        }
                    }

                    await r.WaitForReadyToReadAsync(c, Timeout.Infinite);
                }
            });

            Con.ReadLine(">");
        }


        static void Test_210613()
        {
            var packetMem = Res.AppRoot["210613_novlan_dns_query_simple.txt"].HexParsedBinary;
            Packet packet = new Packet(default, packetMem._CloneSpan());
            var parsed = new PacketParsed(ref packet);
            var dnsPacket = parsed.L7.Generic.GetSpan(ref packet);

            dnsPacket._GetHexString()._Print();

            var msg = DnsUtil.ParsePacket(dnsPacket);

            msg._DebugAsJson();

            var data3 = msg.BuildPacket();
            data3._GetHexString()._Print();

            DnsUtil.ParsePacket(data3)._DebugAsJson();
        }

        // UDP ソケット直接叩き 送受信ベンチマーク
        static void Test_210614_Udp_DirectRecvSendBench()
        {
            // ベンチマークメモ
            // 
            // --- 受信 ---
            // pktlinux (Xeon 4C) ===> dn-vpnvault2 (Xeon 4C)
            // Async (MS SocketTaskExtensions): 1 コア: 360 kpps くらい, 4 コア: 776 kpps くらい
            // Async (UdpSocketExtensions): 1 コア: 450 kpps くらい, 4 コア: 850 ～ 900 kpps くらい
            // Sync:  1 コア: 550 kpps くらい、4 コア: 1000 kpps くらい出るぞ
            // これらの結果から、 UdpSocketExtensions を用いた async が一番良さそうだぞ
            // 
            // Async + BulkRecv + UdpSocketExtensions  4 コア 800 kpps くらい
            // RasPi4 で 30 kpps くらい 遅いなあ
            // 
            // --- pktlinux --> dn-vpnvault2 --> pktlinux 受信したものを別スレッド打ち返して送信 ---
            // 4 コア: 400 ～ 500 kpps くらい
            // 1 コア: 180 kpps くらい
            // RasPi4 で 30 kpps くらい 遅いなあ

            int numCpu = Env.NumCpus;

            //numCpu = 1;

            List<PalSocket> socketList = new List<PalSocket>();

            for (int i = 0; i < numCpu; i++)
            {
                PalSocket? s = new PalSocket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, TcpDirectionType.Server);
                s.Bind(new IPEndPoint(IPAddress.Any, 5454), true);
                socketList.Add(s);

                Con.WriteLine($"Socket #{i} Bind() ok.");
            }

            using CancelWatcher w = new CancelWatcher();

            ThroughputMeasuse recvMeasure = new ThroughputMeasuse(1000, 1000);
            using var recvPrinter = recvMeasure.StartPrinter("UDP Recv: ", toStr3: true);

            using AsyncOneShotTester test = new AsyncOneShotTester(async c =>
            {
                byte[] mem = new byte[65536];
                var array = new ArraySegment<byte>(mem);

                List<Task> taskList = new List<Task>();
                try
                {
                    foreach (var sock in socketList)
                    {
                        taskList.Add(LoopAsync(sock));

                        async Task LoopAsync(PalSocket s)
                        {
                            bool reply = true;

                            ConcurrentQueue<Datagram[]> sendQueue = new ConcurrentQueue<Datagram[]>();

                            AsyncAutoResetEvent sendQueueEvent = new AsyncAutoResetEvent(true);

                            FastMemoryPool<byte> memAlloc = new FastMemoryPool<byte>();

                            var datagramBulkReceiver = new AsyncBulkReceiver<Datagram, PalSocket>(async (s, cancel) =>
                            {
                                Memory<byte> tmp = memAlloc.Reserve(65536);
                                //Memory<byte> tmp = new byte[64];

                                var ret = await s.ReceiveFromAsync(tmp);

                                memAlloc.Commit(ref tmp, ret.ReceivedBytes);

                                Datagram pkt = new Datagram(tmp, ret.RemoteEndPoint, ret.LocalEndPoint);
                                return new ValueOrClosed<Datagram>(pkt);
                            }, 256);

                            var sendTask = TaskUtil.StartSyncTaskAsync(async () =>
                            {
                                if (reply)
                                {
                                    while (c.IsCancellationRequested == false)
                                    {
                                        var ss = s.NativeSocket;

                                        while (sendQueue.TryDequeue(out Datagram[]? sendList))
                                        {
                                            foreach (var dg in sendList)
                                            {
                                                //await ss.SendToAsync(dg.EndPoint!, dg.Data);
                                                await s.SendToAsync(dg.Data, dg.RemoteEndPoint!);
                                            }
                                        }

                                        await sendQueueEvent.WaitOneAsync(cancel: c);
                                    }
                                }
                            });

                            try
                            {
                                await Task.Yield();

                                while (c.IsCancellationRequested == false)
                                {
                                    var ss = s.NativeSocket;
                                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                                    EndPoint ep2 = ep;

                                    for (int i = 0; i < 1000; i++)
                                    {
                                        int count = 1;
                                        if (false)
                                        {
                                            ss.ReceiveFrom(mem, ref ep2);
                                        }
                                        else
                                        {
                                            if (true)
                                            {
                                                var res = await datagramBulkReceiver.RecvAsync(c, s);
                                                recvMeasure.Add(res!.Length);
                                                //$"count = {res!.Length}"._Debug();
                                                count = 0;

                                                if (reply && sendQueue.Count <= 128)
                                                {
                                                    sendQueue.Enqueue(res);

                                                    sendQueueEvent.Set();
                                                }
                                            }
                                            else if (true)
                                            {
                                                var result = await s.ReceiveFromAsync(mem);
                                                //mem.AsMemory().Slice(0, result.ReceivedBytes)._CloneMemory();

                                                //// 打ち返し
                                                //await s.SendToAsync(mem, result.RemoteEndPoint);
                                            }
                                            else if (false)
                                            {
                                                await ss.ReceiveFromAsync(array, SocketFlags.None, ep);
                                            }
                                            else
                                            {
                                                await ss.ReceiveFromAsync(mem, AddressFamily.InterNetwork);
                                            }
                                        }

                                        recvMeasure.AddFast(count);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                ex._Debug();
                            }

                            await sendTask._TryWaitAsync();
                        }
                    }
                }
                finally
                {
                    await taskList._DoForEachAsync(async t => await t._TryAwait());
                }
            });

            Where();
            Con.ReadLine(">");
            socketList.ForEach(x => x._DisposeSafe());
            Where();
        }

        static async Task Test_210627_Async()
        {
            while (true)
            {
                Where();

                string url = "http://ssl-cert-server.wctest.ipantt.net/wildcard_cert_files/wctest.ipantt.net/latest/";
                string username = "user123";
                string password = "pass123";

                await using var http = new WebApi();

                http.SetBasicAuthHeader(username, password);

                var res = await http.SimpleQueryAsync(WebMethods.GET, url + "/cert.cer");

                //res.ToString()._Print();
            }
        }

        static async Task Test_210627_02_Async()
        {
            var r = await LocalNet.QueryDnsAsync(new DnsGetIpQueryParam("vpn1.v4.softether.net"));

            "---"._Print();
            r.IPAddressList._DoForEach(x => x.ToString()._Print());
            "---"._Print();
        }

        static void Test_210706()
        {
            StrTable table = new StrTable();

            table.ImportFile(@"C:\git\IPA-DN-ThinApps-Private\src\bin\hamcore\strtable_ja.patch.stb");
            table.ImportFile(@"C:\git\IPA-DN-ThinApps-Private\submodules\IPA-DN-Ultra\src\bin\hamcore\strtable_ja.stb");

            while (true)
            {
                string key = Con.ReadLine()!;

                table[key]._Print();
            }
        }

        static void Test_210712()
        {
            //while (true)
            {
                var parentCert = new Certificate(Lfs.ReadDataFromFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.cer").Span);
                var parentPalCert = parentCert.X509Certificate;
                //parentPalCert.PkiCertificate.Export()._DataToFile(@"c:\tmp\210712\test.cer", flags: FileFlags.AutoCreateDirectory);

                var childCert = new Certificate(Lfs.ReadDataFromFile(@"S:\NTTVPN\Certs\200418_Certs\01_Controller.cer").Span);
                var childPalCert = childCert.X509Certificate;
                //childCert.VerifySignedByCertificate(parentCert)._Print();

                //childPalCert.PkiCertificate.VerifySignedByCertificate(parentPalCert.PkiCertificate)._Print();

                childCert.CertData.GetEncoded()._DataToFile(@"c:\tmp\210714\test.cer", flags: FileFlags.AutoCreateDirectory);
                childCert.GetSignature()._GetHexString()._Print();
                childCert.DigestSHA1Str._Print();

                childCert.IsExpired()._Print();
            }
        }

        static void Test_210901_EasyDnsServer()
        {
            using EasyDnsServer s = new EasyDnsServer(new EasyDnsServerSetting(
                reqList =>
                {
                    Span<DnsUdpPacket> resList = new DnsUdpPacket[reqList.Length];

                    int index = 0;

                    foreach (var req in reqList)
                    {
                        DnsUdpPacket res = new DnsUdpPacket(req.RemoteEndPoint, req.LocalEndPoint, req.Message);

                        resList[index++] = res;
                    }

                    return resList;
                }, 5353
                ));

            Console.Write("Quit>");
            Console.ReadLine();
        }

        static void Test_211108()
        {
            Async(async () =>
            {
                HadbCodeTest t = new HadbCodeTest();
                await t.Test1Async();
                t.SystemName._Print();
            }
            );
        }

        static void Test_211017()
        {
            Async(async () =>
            {
                var settings = new HadbSqlSettings("TEST",
                    new SqlDatabaseConnectionSetting("10.40.0.103", "TEST_DN_DBSVC1", "sql_test_dn_dbsvc1_reader", "testabc"),
                    new SqlDatabaseConnectionSetting("10.40.0.103", "TEST_DN_DBSVC1", "sql_test_dn_dbsvc1_writer", "testabc"),
                    HadbOptionFlags.NoAutoDbUpdate);

                await using HadbTest db = new HadbTest(settings,
                    new HadbTestDynamicConfig() { /*TestDef = new string[] { "Hello", "World" }*/ });

                db.Start();

                Con.WriteLine("Wait for ready...");
                await db.WaitUntilReadyForAtomicAsync();
                Con.WriteLine("Ready.");

                while (true)
                {
                    string? str = Con.ReadLine("?>");
                    if (str._IsEmpty())
                    {
                        break;
                    }

                    if (str._TryTrimStartWith(out string uid, StringComparison.OrdinalIgnoreCase, "?"))
                    {
                        await db.TranAsync(writeMode: false, async tran =>
                        {
                            var obj = await tran.AtomicGetAsync<HadbTestData>(uid);
                            obj._PrintAsJson();
                            return false;
                        });
                    }
                    else if (str._TryTrimStartWith(out string key, StringComparison.OrdinalIgnoreCase, "!"))
                    {
                        await db.TranAsync(writeMode: false, async tran =>
                        {
                            var obj = await tran.AtomicSearchByKeyAsync<HadbTestData>(new HadbKeys(key));
                            obj._PrintAsJson();
                            return false;
                        });
                    }
                    else if (str._TryTrimStartWith(out string label, StringComparison.OrdinalIgnoreCase, ">"))
                    {
                        await db.TranAsync(writeMode: false, async tran =>
                        {
                            var obj = await tran.AtomicSearchByLabelsAsync<HadbTestData>(new HadbLabels(label));
                            obj._PrintAsJson();
                            return false;
                        });
                    }
                    else if (str._TryTrimStartWith(out string uid2, StringComparison.OrdinalIgnoreCase, "-"))
                    {
                        await db.TranAsync(writeMode: true, async tran =>
                        {
                            var obj = await tran.AtomicDeleteAsync<HadbTestData>(uid2);
                            Con.WriteLine($"Deleted = {obj._ObjectToJson()}");
                            return true;
                        });
                    }
                    else if (str._TryTrimStartWith(out string key3, StringComparison.OrdinalIgnoreCase, "*"))
                    {
                        var obj = db.FastSearchByKey<HadbTestData>(new HadbKeys(key3));
                        if (obj == null)
                        {
                            Con.WriteLine("Not found.");
                        }
                        else
                        {
                            obj.FastUpdate<HadbTestData>(x =>
                            {
                                x.TestInt++;
                                x.IPv4Address += "_" + Secure.RandSInt31() % 10;
                                return true;
                            });
                        }
                    }
                    else if (str._TryTrimStartWith(out string key2, StringComparison.OrdinalIgnoreCase, "+"))
                    {
                        await db.TranAsync(writeMode: true, async tran =>
                        {
                            var obj = await tran.AtomicSearchByKeyAsync<HadbTestData>(new HadbKeys(key2));
                            if (obj == null)
                            {
                                Con.WriteLine("Not found.");
                                return false;
                            }
                            else
                            {
                                var data = obj.GetData<HadbTestData>();
                                data.IPv4Address += "_" + Secure.RandSInt31() % 10;
                                data.IPv6Address += "_" + Secure.RandSInt31() % 10;
                                data.HostName += "_" + Secure.RandSInt31() % 10;
                                obj = await tran.AtomicUpdateAsync(obj);
                                obj._PrintAsJson();
                                return true;
                            }
                        });
                    }
                    else
                    {
                        await db.TranAsync(writeMode: true, async tran =>
                        {
                            HadbTestData host = new HadbTestData
                            {
                                HostName = str,
                                IPv4Address = "apple",
                                IPv6Address = "microsoft",
                                TestInt = 1,
                            };

                            await tran.AtomicAddAsync(host);
                            return true;
                        });
                    }
                }
            });
        }

        static void Test_211031()
        {
            Async(async () =>
            {
                List<string> conns = new List<string>();
                conns.Add(new SqlDatabaseConnectionSetting("10.40.0.103", "TESTDB3", "sql_test_211031", "Micro12az"));
                conns.Add(new SqlDatabaseConnectionSetting("10.40.0.103", "TESTDB4", "sql_test_211031", "Micro12az"));

                foreach (var connstr in conns)
                {
                    await using var db = new Database(connstr);

                    await db.EnsureOpenAsync();

                    for (int i = 0; i < 10000; i++)
                    {
                        await db.EasyExecuteAsync("insert into TEST (TEST_STR) values (@A)", new { A = Util.RandSInt63().ToString() });

                        if ((i % 100) == 0) i._ToString3()._Print();
                    }
                }
            });
        }

        public static void Test_Generic()
        {
            if (true)
            {
                Test_MakeDummyCerts_211115();
                return;
            }

            if (true)
            {
                Test_211108();
                return;
            }

            if (true)
            {
                Test_211017();
                return;
            }

            if (true)
            {
                Test_210901_EasyDnsServer();
                return;
            }

            if (true)
            {
                Test_210615_Udp_Indirect_SendRecv_Bench_DNS_Server_MultiTaskProcess();
                return;
            }

            if (true)
            {
                Test_210615_Udp_Indirect_SendRecv_Bench_DNS_Server();
                return;
            }

            if (true)
            {
                Test_MakeDummyCerts_210828();
                return;
            }

            if (true)
            {
                using IisAdmin a = new IisAdmin();
                a.Test();
                return;
            }

            if (true)
            {
                Con.WriteLine("A");
                Con.WriteLine();
                Con.WriteLine("B");
                Con.WriteLine();
                Con.WriteLine("C");
                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    var a = await GetMyPrivateIpNativeUtil.GetMyPrivateIpAsync(IPVersion.IPv4);
                    a._Print();
                    a = await GetMyPrivateIpNativeUtil.GetMyPrivateIpAsync(IPVersion.IPv6);
                    a._Print();
                });
                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    while (true)
                    {
                        string line = Con.ReadLine()!;

                        if (line._IsEmpty()) break;

                        try
                        {
                            IPAddress ip = await LocalNet.DnsResolver.GetIpAddressSingleAsync(line, DnsResolverQueryType.AAAA);

                            ip.ToString()._Print();
                        }
                        catch (Exception ex)
                        {
                            ex._Error();
                        }
                    }
                });
                return;
            }

            if (true)
            {
                Con.WriteLine("Top level message", flags: LogFlags.Heading);
                return;
            }

            if (true)
            {
                while (true)
                {
                    string line = Con.ReadLine()!;

                    var a = Str.ParseHostnaneAndPort(line, 80);

                    a._PrintAsJson();
                }
                return;
            }

            if (false)
            {
                string b = "http://1.2.3.4/abc/def/";
                var x = b._CombineUrl("x");
                x.ToString()._Print();

                var y = b._ParseUrl()._CombineUrl("y");
                y.ToString()._Print();

                return;
            }

            if (false)
            {
                string b = "http://1.2.3.4/abc/def/";
                var u = b._ParseUrl();

                var x = b._CombineUrl("x/");
                var y = x._CombineUrl("y/");
                var z = y._CombineUrl("z/");
                b.ToString()._Print();
                x.ToString()._Print();
                y.ToString()._Print();
                z.ToString()._Print();
                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    var list = await WildcardCertServerUtil.DownloadAllLatestCertsAsync("https://secertsvr1.sehosts.com/wildcard_cert_files/", "user_cert", Lfs.ReadStringFromFile(@"\\10.21.2.65\home\yagi\TMP\test_pass.txt", oneLine: true));

                    list.Count()._Print();

                    using IisAdmin util = new IisAdmin();

                    //var x = util.GetCurrentMachineCertificateList();
                    //x.Count._Print();
                    //x._DoForEach(x => x.Value.ToString()._Print());

                    util.UpdateCerts(list, false);
                });
                return;
            }

            if (true)
            {
                using IisAdmin util = new IisAdmin();
                util.Test();
                return;
            }

            if (false)
            {
                // 2021/8/5 NTTVPN Sub Certs additional
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.pfx").Span);

                IssueCert("update-check.dynamic-ip.thin.cyber.ipa.go.jp", @"S:\NTTVPN\Certs\200418_Certs\06_UpdateCheck");

                void IssueCert(string cn, string fileNameBase)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: new DateTime(2037, 12, 31), shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }
                return;
            }

            if (true)
            {
                return;
            }

            if (true)
            {
                Test_210712();
                return;
            }

            if (true)
            {
                Test_210706();
                return;
            }

            if (false)
            {
                Test_210627_02_Async()._GetResult();
                return;
            }
            if (true)
            {
                Test_210627_Async()._GetResult();
                return;
            }


            if (false)
            {
                Test_210616_Udp_Indirect_Socket_DNS_Client();
                return;
            }

            if (true)
            {
                Test_210613_02_Udp_Indirect_SendRecv_Bench();
                return;
            }

            if (false)
            {
                Test_210614_Udp_DirectRecvSendBench();
                return;
            }

            if (true)
            {
                Test_210613();
                return;
            }

            if (true)
            {
                CSharpConcatUtil.DoConcat(@"C:\tmp2\210612test\src\", @"C:\tmp2\210612test\dst\");
                return;
            }

            if (true)
            {
                Test_MakeDummyCerts_210604();
                return;
            }

            if (true)
            {
                Test_210419_02_photometa_csv_analyze_Async()._GetResult();
                return;
            }

            if (true)
            {
                //Test_210414_Async()._GetResult();
                return;
            }

            if (true)
            {
                Test_210401();
                return;
            }

            if (true)
            {
                GuaTest_210320()._GetResult();
                return;
            }

            if (false)
            {
                Test_MakeThinOssCerts_201120();
                return;
            }

            if (false)
            {
                Test_210307_Backup();
                return;
            }

            if (true)
            {
                Test_210309_Samba4()._GetResult();
                return;
            }

            if (false)
            {
                Test_210307_EncCopy();
                return;
            }

            if (true)
            {
                Test_210307_Restore();
                return;
            }

            if (false)
            {
                while (true)
                {
                    string line = Con.ReadLine()!;

                    AwsSns.NormalizePhoneNumber(line)._Print();
                }
            }

            if (true)
            {
                // TODO!!!
                Async(async () =>
                {
                    for (int i = 0; i < 15; i++)
                    {
                        AwsSnsSettings settings = new AwsSnsSettings("ap -northeast-1", "aaa", Lfs.ReadStringFromFile(@"h:\tmp\210119sak.txt", oneLine: true));

                        AwsSns sns = new AwsSns(settings);

                        await sns.SendAsync($"こんにちは！{i}", "aaa");
                    }
                });
                return;
            }

            if (true)
            {
                while (true)
                {
                    string line = Con.ReadLine()!;

                    VlanRange r = new VlanRange(line);

                    r.ToString(VlanRangeStyle.Apresia)._Print();
                }
                return;
            }

            if (true)
            {
                Test_210110();
                return;
            }

            if (false)
            {
                using var l = LocalNet.CreateTcpListener(new TcpListenParam(isRandomPortMode: EnsureSpecial.Yes, async (listen, sock) =>
                {
                    using var x = sock.GetStream();
                    using var w = new StreamWriter(x);

                    while (true)
                    {
                        await w.WriteLineAsync(DtNow.ToString());
                        await w.FlushAsync();
                        await Task.Delay(100);
                    }
                }));
                Con.ReadLine();
                return;
            }

            if (true)
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                var task = Test_210104Async(cts.Token);
                Con.ReadLine();
                cts.Cancel();
                task._GetResult();
                return;
            }

            if (true)
            {
                while (true)
                {
                    string line = Con.ReadLine()!;

                    Secure.HashSHA0(line._GetBytes_Ascii())._GetHexString()._Print();
                }
                return;
            }

            if (false)
            {
                Test_210102();
                return;
            }

            if (true)
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                var task = Test_201231Async(cts.Token);
                Con.ReadLine();
                cts.Cancel();
                task._GetResult();
                return;
            }

            if (true)
            {
                ThroughputMeasuse m = new ThroughputMeasuse(5000, 1000);
                AsyncAwait(async () =>
                {
                    while (true)
                    {
                        double t = m.CurrentThroughput;
                        t.ToString("F3")._Print();
                        await Task.Delay(100);
                    }
                });

                while (true)
                {
                    Con.ReadLine();
                    m.Add(1);
                }

                return;
            }

            if (true)
            {
                Test_201215();
                return;
            }

            if (true)
            {
                while (true)
                {
                    string line = Con.ReadLine(">")!;
                    EasyIpAcl.Evaluate("!192.168.3.8,192.168.3.0/24", line!._ToIPAddress()!, enableCache: true)._Print();
                }
                return;
            }


            if (true)
            {
                List<TestTable_201213> a = new List<TestTable_201213>();

                a.Add(new TestTable_201213());
                a.Add(new TestTable_201213() { A = "Neko", B = "Inu", C = 456 });


                var table = new SimpleTableView<TestTable_201213>(a);

                table.GenerateHtml()._Print();

                return;
            }

            if (true)
            {
                Async(async () => await Test_DNS_201213());
                return;
            }

            if (true)
            {
                FastCache<string, string> c = new FastCache<string, string>(expireMsecs: 1000);
                string key = "neko";

                c[key] = "Hello";
                c[key] = "World";

                while (true)
                {
                    c["NEKO"]._Print();
                    Sleep(100);
                }

                return;
            }

            if (true)
            {
                Test201213 x = new Test201213();

                x._GetObjectDumpForJsonFriendly()._Print();

                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    await LambdaTest_201213_Async();
                });
                return;
            }

            if (true)
            {
                TestDevCommands.ConvertCErrorsToCsErrors(@"C:\git\IPA-DNP-ThinApps-Lgwan", @"c:\tmp\201212\test.cs");
                return;
            }

            if (true)
            {
                Pack p = new Pack();
                p.AddStr("1", "Hello");
                p.AddStr("2", "World");
                WpcPack wp = new WpcPack(p, "1122334455667788990011223344556677889900", "0011223344556677889900112233445566778899");
                string str = wp.ToPacketString();
                str._Print();
                var wp2 = WpcPack.Parse(str, true);
                wp2.HostKey._Print();
                wp2.HostSecret2._Print();
                wp2.Pack["1"]._Print();
                wp2.Pack["2"]._Print();
                return;
            }

            if (true)
            {
                WpcItemList l = new WpcItemList();
                l.Add("test", "Hello"._GetBytes_Ascii());
                l.Add("Tes", "Hello2"._GetBytes_Ascii());
                string str = l.ToPacketString();

                str._Print();

                var l2 = WpcItemList.Parse(str);

                l2._PrintAsJson();


                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    KeyValueList<string, string> list = new KeyValueList<string, string>();

                    list.Add("# configuration files on the boot partition.", "0x0A68646D695F736166653D310A230A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A");
                    list.Add("- growpart", "0x0A0A0A0A0A0A0A0A0A0A");
                    list.Add("- resizefs", "0x0A0A0A0A0A0A0A0A0A0A");

                    //list.Add("# configuration files on the boot partition.", "\nhdmi_safe=1\n#");
                    //list.Add("- growpart", "");
                    //list.Add("- resizefs", "");

                    list.Add("APT::Periodic::Update-Package-Lists \"1\";", "APT::Periodic::Update-Package-Lists \"0\";");
                    list.Add("APT::Periodic::Unattended-Upgrade \"1\";", "APT::Periodic::Unattended-Upgrade \"0\";");

                    var ret = await MiscUtil.ReplaceBinaryFileAsync(@"D:\Downloads\ubuntu-19.10.1-preinstalled-server-arm64+raspi3.img", @"C:\tmp2\bintest\ubuntutest.img", list);

                    ret._DebugAsJson();
                });
                return;
            }
            if (true)
            {
                Async(async () =>
                {
                    KeyValueList<string, string> list = new KeyValueList<string, string>();
                    list.Add("5", "G");
                    var ret = await MiscUtil.ReplaceBinaryFileAsync(@"C:\tmp\bintest\aaa.txt", @"C:\tmp\bintest\aaa2.txt", list, bufferSize: 1);

                    ret._DebugAsJson();
                });
                return;
            }


            if (true)
            {
                while (true)
                {
                    using var timeoutCts = new CancellationTokenSource(10000);
                    Util.DoNothing();
                }
            }

            if (true)
            {
                using var statman = new StatMan(new StatManConfig
                {
                    Callback = (p, v1, v2) =>
                    {
                        v1["callback1"] = Time.HighResTick64;
                        v1["traffic_total"] = 10;
                        v2["today"] = DtOffsetNow._ToDtStr(true);

                        return TR();
                    }
                });

                statman.AddReport("int1", 123);
                statman.AddReport("str1", "hello");

                statman.AddReport("int2_total", 1);

                Con.ReadLine("quit>");

                return;
            }

            if (true)
            {
                LocalNet.GetMyPrivateIpAsync()._GetResult()._Print();
                return;
            }

            if (true)
            {
                KeyValueList<string, long> x = new KeyValueList<string, long>();

                KeyValueList<string, string> y = new KeyValueList<string, string>();
                x.Add("ab", 123);
                x.Add("cd", 456);
                y.Add("ef", "123");
                y._DebugAsJson();
                return;
            }

            if (true)
            {
                while (true)
                {
                    string fqdn = Con.ReadLine()!;

                    MasterData.DomainSuffixList.ParseDomainBySuffixList(fqdn, out string suffix, out string plusOne, out string hostname);

                    $"suffix = {suffix}\nplusOne = {plusOne}\nhostname = {hostname}\n\n"._Print();
                }
                return;
            }

            if (true)
            {
                HiveTest_201201();
                return;
            }

            if (true)
            {
                ZipTest_201201();
                return;
            }

            if (true)
            {
                Test_MakeThinLgwanCerts_201117();
                return;
            }

            if (true)
            {
                using var si = SingleInstance.TryGet("abc123");
                Con.WriteLine("Obtained.");
                Con.ReadLine("Enter to release>");
                return;
            }

            if (true)
            {
                while (true)
                {
                    Con.WriteLine();

                    string line = Con.ReadLine("INPUT>")._NonNullTrim();

                    if (line._IsEmpty())
                        return;

                    using DnsHostNameScanner scan = new DnsHostNameScanner(
                        dnsSettings: new DnsResolverSettings(dnsServersList: new IPEndPoint[] { new IPEndPoint("8.8.8.8"._ToIPAddress()!, 53) }));

                    scan.PerformAsync(line)._GetResult();
                }
                return;
            }

            if (true)
            {
                using DnsHostNameScanner scan = new DnsHostNameScanner(
                    dnsSettings: new DnsResolverSettings(dnsServersList: new IPEndPoint[] { new IPEndPoint("8.8.8.8"._ToIPAddress(), 53) }));


                return;
            }

            if (true)
            {
                //DnsTest2();
                LocalNet.DnsResolver.GetHostNameAsync("1.2.3.4")._GetResult();
                return;
            }

            if (true)
            {
                Test_ThinLgWanConnectivityTest();
                return;
            }

            if (true)
            {
                Test_ThinLgWanSshConfigMaker();
                return;
            }

            if (true)
            {
                Test_ThinLgWanConfigMaker();
                return;
            }

            if (true)
            {
                Test_ThinLgWanMapping();
                return;
            }

            if (true)
            {
                Test_MakeThinLgwanCerts_200930();
                return;
            }

            if (true)
            {
                ThreadObj.StartMany(16, (p) =>
                {
                    for (int i = 0; ; i++)
                    {
                        //i._Print();

                        var poderosa = Lfs.ReadPoderosaFile(@"H:\SSH\dnlinux.gts");

                        Async(async () =>
                        {
                            using var ssh = poderosa.CreateSshClient();

                            using var sock = await ssh.ConnectAndGetSockAsync();

                            using var proc = sock.CreateUnixShellProcessor();

                            var res = await proc.ExecBashCommandAsync("cat /proc/cpuinfo");

                            //var res2 = await proc.ExecBashCommandAsync("cat /etc/passwd");

                            //res._Print();
                        });

                        Dbg.GcCollect();
                    }
                });

                SleepInfinite();

                return;
            }

            if (true)
            {
                while (true)
                {
                    string line = Con.ReadLine()!;
                    if (Str.TryParseYYMMDDDirName(line, out DateTime dt))
                    {
                        dt.ToString()._Print();
                    }
                }
                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    AsyncAutoResetEvent ev1 = new AsyncAutoResetEvent();
                    AsyncAutoResetEvent ev2 = new AsyncAutoResetEvent();

                    CancellationTokenSource cts = new CancellationTokenSource();

                    Task t2 = AsyncAwait(async () =>
                    {
                        while (cts.IsCancellationRequested == false)
                        {
                            $"task 1-A: {ThreadObj.CurrentThreadId}"._Print();

                            await ev1.WaitOneAsync(cancel: cts.Token);

                            $"task 1-B: {ThreadObj.CurrentThreadId}"._Print();

                            ev2.Set();
                        }
                    });

                    for (int i = 0; i < 100; i++)
                    {
                        ev1.Set();

                        $"task 0-A: {ThreadObj.CurrentThreadId}"._Print();

                        await ev2.WaitOneAsync();

                        $"task 0-B: {ThreadObj.CurrentThreadId}"._Print();
                    }

                    cts.Cancel();

                    await t2;
                });
                return;
            }

            if (true)
            {
                VaultStressTest();
                return;
            }

            if (true)
            {
                CgiServerStressTest();
                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    while (true)
                    {
                        string cmd = Con.ReadLine("CMD>")!;

                        var result = await EasyExec.ExecAsync(cmd);

                        result.ErrorAndOutputStr._Print();

                        ""._Print();
                    }
                });
                return;
            }

            if (false)
            {
                ComPortClient.GetPortTargetNames()._DebugAsJson();
                return;
            }

            if (true)
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                var cancel = cts.Token;

                var sensor = new ThermometerSensor528018();

                sensor.StartAsync(cancel)._TryGetResult();

                using var poll = AsyncScoped(async c =>
                {
                    while (c.IsCancellationRequested == false)
                    {
                        sensor.CurrentData.PrimaryValue._Debug();

                        await c._WaitUntilCanceledAsync(500);
                    }
                });

                Con.ReadLine();

                cts.Cancel();

                sensor._DisposeSafe();

                return;
            }

            if (true)
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                var cancel = cts.Token;

                var sensor = new VoltageSensor8870(new ComPortBasedSensorSettings(new ComPortSettings("/dev/ttyACM0")));

                sensor.StartAsync(cancel)._TryGetResult();

                using var poll = AsyncScoped(async c =>
                {
                    while (c.IsCancellationRequested == false)
                    {
                        sensor.CurrentData.PrimaryValue._Debug();

                        await c._WaitUntilCanceledAsync(500);
                    }
                });

                Con.ReadLine();

                cts.Cancel();

                sensor._DisposeSafe();

                return;
            }

            if (true)
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                var cancel = cts.Token;
                ComPortClient client = null!;
                Task t = AsyncAwait(async () =>
                {
                    var com = new ComPortClient(new ComPortSettings("COM9"));
                    client = com;

                    using var sock = await com.ConnectAndGetSockAsync();

                    var r = new BinaryLineReader(sock.Stream);

                    while (true)
                    {
                        string? line = await r.ReadSingleLineStringAsync(cancel: cancel);

                        if (line == null)
                        {
                            "Disconectd!"._Print();
                        }

                        line = line._NonNull();

                        $"Recv: {line}"._Print();
                    }
                });

                Con.ReadLine();

                Dbg.Where();

                //client._DisposeSafe();
                cts.Cancel();

                Dbg.Where();


                Dbg.Where();

                t._GetResult();

                return;
            }

            if (true)
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                SerialPort p = new SerialPort("COM9");

                Task t = AsyncAwait(async () =>
                {

                    p.Open();

                    "Open"._Print();

                    var stream = p.BaseStream;

                    while (true)
                    {
                        var memory = await stream._ReadAsync(cancel: cts.Token);

                        memory._GetString_Ascii()._Print();
                    }
                });

                Con.ReadLine();

                Dbg.Where();

                p.Close();

                Dbg.Where();

                cts.Cancel();

                Dbg.Where();

                t._GetResult();

                return;
            }


            if (true)
            {
                Async(async () =>
                {
                    using var file = await Lfs.OpenAsync(@"C:\TMP\200724_raspi4_boot_fail_sdimg\sd_fixtest.img", true);
                    long totalSize = await file.GetFileSizeAsync();
                    int unitSize = 100_000_000;
                    Memory<byte> tmp = new byte[unitSize];
                    Memory<byte> target = "LABEL=writable\t/\t ext4\tdefaults\t0 0"._GetBytes_Ascii();
                    Memory<byte> newdata = "LABEL=writable\t/\t ext4\tdefaults\t0 1"._GetBytes_Ascii();

                    for (long i = 0; i < totalSize; i += unitSize)
                    {
                        await file.ReadRandomAsync(i, tmp);

                        int r = ((ReadOnlySpan<byte>)tmp.Span).IndexOf(target.Span);
                        if (r != -1)
                        {
                            long pos = i + r;

                            $"Found: {pos}"._Debug();

                            await file.WriteRandomAsync(pos, newdata);
                        }

                        i._ToString3()._Debug();
                    }
                });
                return;
            }

            if (true)
            {
                LogBrowserSecureJson j = new LogBrowserSecureJson()
                {
                    AuthSubject = " 独立行政法人 情報処理推進機構 (IPA) 独立行政法人 情報処理推進機構 (IPA) ",
                    UploadIp = "1.2.3.4.5.6.7.8",
                };

                string str = EasyCookieUtil.SerializeObject("abc");

                str._Print();

                string? k = EasyCookieUtil.DeserializeObject<string>(str);

                k._DebugAsJson();

                return;
            }

            if (true)
            {
                PPWin.Combine(@"c:\tmp\", @"./././a/b/c/../../d/../.x/")._Debug();
                return;
            }

            if (true)
            {
                bool b1 = true;
                bool b2 = false;

                byte v1 = b1._RawReadValueUInt8();
                byte v2 = b2._RawReadValueUInt8();

                Con.WriteLine(v1);
                Con.WriteLine(v2);

                Con.WriteLine();

                v1 = 9;

                b1._RawWriteValueUInt8(v1);
                v1 = b1._RawReadValueUInt8();

                Con.WriteLine(v1);

                Con.WriteLine();

                bool b3 = true;
                //b3._RawWriteValueUInt8(12);

                bool r = (b1);
                r._Print();

                return;
            }

            if (true)
            {
                Async(async () =>
                {
                    using var srcFile = await Lfs.OpenAsync(@"C:\git\IPA-DN-FileCenter\IPA-DN-FileCenter\Local\DataRoot\test2\標準ﾏｯﾌﾟ.bmp");
                    using var srcFileStream = srcFile.GetStream();
                    using var destFile = await Lfs.CreateAsync(@"C:\git\IPA-DN-FileCenter\IPA-DN-FileCenter\Local\DataRoot\test2\MapEncrypted.bmp");
                    using var enc = new XtsAesRandomAccess(destFile, "b");
                    using var destFileStream = enc.GetStream();
                    await srcFileStream.CopyBetweenStreamAsync(destFileStream);
                });

                return;
            }

            if (true)
            {
                LogBrowserSecureJson json = new LogBrowserSecureJson
                {
                    AuthRequired = true,
                    AuthDatabase = new KeyValueList<string, string>(),
                    AuthSubject = "IPA の皆様",
                    AllowAccessToAccessLog = true,
                    AuthSubDirName = "neko",
                };

                json.AuthDatabase.Add("a", Secure.SaltPassword("b"));

                json._ObjectToFile(@"C:\git\IPA-DN-FileCenter\IPA-DN-FileCenter\Local\DataRoot\test2\_secure.json");

                return;
            }

            if (false)
            {
                Async(async () =>
                {
                    using var file = await Lfs.CreateAsync(@"c:\tmp\test1.dat");
                    using var es = new XtsAesRandomAccess(file, "test", true);
                    using var st = es.GetStream(true);
                    using var w = new StreamWriter(st);
                    w.WriteLine("a");
                    w.Flush();
                    w.WriteLine("Neko");
                    w.Flush();
                    st.Position = 1000000;
                    w.WriteLine("Cat");
                    w.Flush();
                    st.Position = 500000;
                    w.WriteLine("Dog");
                    w.Flush();
                });

                Async(async () =>
                {
                    using var file = await Lfs.OpenAsync(@"c:\tmp\test1.dat", false);
                    using var es = new XtsAesRandomAccess(file, "test", true);
                    using var st = es.GetStream(true);
                    using var r = new StreamReader(st);
                    r.ReadLine()._Print();
                    r.ReadLine()._Print();
                });

                return;
            }

            if (false)
            {
                Secure.Rand(32)._GetHexString()._Print();

                string src = "Hello World Neko San Neko San 2 Neko San 3";
                var srcData = src._GetBytes_UTF8();

                var encrypted = ChaChaPoly.EasyEncryptWithPassword(srcData, "microsoft");

                var result = ChaChaPoly.EasyDecryptWithPassword(encrypted, "microsoft");
                result.ThrowIfException();

                result.Value._GetString_UTF8()._Print();

                return;
            }

            if (false)
            {
                Async(async () =>
                {
                    using var file = Lfs.Open(@"c:\tmp\test1.dat");

                    using var sector = new SectorBasedRandomAccessSimpleTest(file, 100000);

                    using var stream = sector.GetStream(true);

                    using SHA1 sha1 = SHA1.Create();
                    byte[] hash = await Secure.CalcStreamHashAsync(stream, sha1);
                    if (hash._GetHexString()._CompareHex("FF7040CEC7824248E9DCEB818E111772DD779B97") != 0)
                    {
                        stream._SeekToBegin();

                        using var file2 = await Lfs.CreateAsync(@"D:\Downloads\tmp.iso");
                        using var filest = file2.GetStream();

                        await stream.CopyBetweenStreamAsync(filest);

                        throw new CoresException($"Hash different: {hash._GetHexString()}");
                    }
                    else
                    {
                        "Hash OK!"._Print();
                    }
                });

                return;
            }

            if (false)
            {
                while (true)
                {
                    Memory<byte> x = new byte[100000];
                    x.Span.Fill(1);
                    Limbo.ObjectVolatileSlow = x;
                }
            }

            if (true)
            {
                using CancelWatcher c = new CancelWatcher();

                List<Task> taskList = new List<Task>();

                for (int k = 0; k < 20; k++)
                {
                    var task1 = AsyncAwait(async () =>
                    {
                        int taskId = k;
                        try
                        {
                            for (int i = 0; ; i++)
                            {

                                //await Task.Yield();

                                //await Task.Delay(taskId * 100);

                                c.ThrowIfCancellationRequested();

                                $"----------- {i}"._Debug();

                                using var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Console, title: $"Task {taskId}", unit: "bytes", toStr3: true));

                                HugeMemoryBuffer<byte> mem = new HugeMemoryBuffer<byte>();

                                //using var stream = new BufferBasedStream(mem);

                                using var file = Lfs.Create(@$"f:\tmp\200810\{taskId}.dat", flags: FileFlags.AutoCreateDirectory | FileFlags.SparseFile);

                                using var sector = new XtsAesRandomAccess(file, "neko");
                                using var stream = sector.GetStream(true);

                                //using var stream = file.GetStream();

                                await FileDownloader.DownloadFileParallelAsync(
                            "https://ossvault.sec.softether.co.jp/vault/oss/20072701_ubuntu_cdimage/20.04/release/ubuntu-20.04-live-server-s390x.iso",
                            stream,
                            new FileDownloadOption(20, webApiOptions: new WebApiOptions(new WebApiSettings { Timeout = 1 * 1000, SslAcceptAnyCerts = true })),
                            progressReporter: reporter,
                            cancel: c);
                                //await FileDownloader.DownloadFileParallelAsync("http://speed.sec.softether.co.jp/003.100Mbytes.dat", stream,
                                //    new FileDownloadOption(maxConcurrentThreads: 30, bufferSize: 123457, webApiOptions: new WebApiOptions(new WebApiSettings { Timeout = 1 * 1000 })), cancel: c);

                                //using SHA1Managed sha1 = new SHA1Managed();
                                //stream._SeekToBegin();
                                //byte[] hash = await Secure.CalcStreamHashAsync(stream, sha1);
                                //if (hash._GetHexString()._CompareHex("FF7040CEC7824248E9DCEB818E111772DD779B97") != 0)
                                //{
                                //    stream._SeekToBegin();

                                //    using var file2 = await Lfs.CreateAsync(@"D:\Downloads\tmp.iso");
                                //    using var filest = file2.GetStream();

                                //    await stream.CopyBetweenStreamAsync(filest);

                                //    throw new CoresException($"Hash different: {hash._GetHexString()}");
                                //}

                                await AsyncAwait(async () =>
                        {
                            using var file = Lfs.Open(@$"f:\tmp\200810\{taskId}.dat");

                            using var sector = new XtsAesRandomAccess(file, "neko");

                            using var stream = sector.GetStream(true);

                            using SHA1 sha1 = SHA1.Create();
                            byte[] hash = await Secure.CalcStreamHashAsync(stream, sha1);
                            if (hash._GetHexString()._CompareHex("FF7040CEC7824248E9DCEB818E111772DD779B97") != 0)
                            {
                                stream._SeekToBegin();

                                using var file2 = await Lfs.CreateAsync(@"D:\Downloads\tmp.iso");
                                using var filest = file2.GetStream();

                                await stream.CopyBetweenStreamAsync(filest);

                                throw new CoresException($"Hash different 2: {hash._GetHexString()}");
                            }
                            else
                            {
                                "Hash OK!"._Print();
                            }

                        });
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    });

                    taskList.Add(task1);
                }

                Con.ReadLine();

                c.Cancel();

                Task.WhenAll(taskList.ToArray())._TryGetResult();

                return;
            }

            if (true)
            {
                using CancelWatcher c = new CancelWatcher();

                var task1 = AsyncAwait(async () =>
                {
                    try
                    {
                        for (int i = 0; ; i++)
                        {
                            Dbg.GcCollect();

                            await Task.Yield();

                            c.ThrowIfCancellationRequested();

                            $"----------- {i}"._Debug();

                            using var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Console, title: "Downloading", unit: "bytes", toStr3: true));

                            HugeMemoryBuffer<byte> mem = new HugeMemoryBuffer<byte>();

                            //using var stream = new BufferBasedStream(mem);

                            using var file = Lfs.Create(@"c:\tmp\test1.dat");

                            using var sector = new XtsAesRandomAccess(file, "neko");
                            using var stream = sector.GetStream(true);

                            //using var stream = file.GetStream();

                            await FileDownloader.DownloadFileParallelAsync(
                        "http://ossvault.sec.softether.co.jp/vault/oss/20072701_ubuntu_cdimage/20.04/release/ubuntu-20.04-live-server-s390x.iso",
                        stream,
                        new FileDownloadOption(20, webApiOptions: new WebApiOptions(new WebApiSettings { Timeout = 1 * 1000, SslAcceptAnyCerts = true })),
                        progressReporter: reporter,
                        cancel: c);
                            //await FileDownloader.DownloadFileParallelAsync("http://speed.sec.softether.co.jp/003.100Mbytes.dat", stream,
                            //    new FileDownloadOption(maxConcurrentThreads: 30, bufferSize: 123457, webApiOptions: new WebApiOptions(new WebApiSettings { Timeout = 1 * 1000 })), cancel: c);

                            using SHA1 sha1 = SHA1.Create();
                            stream._SeekToBegin();
                            byte[] hash = await Secure.CalcStreamHashAsync(stream, sha1);
                            if (hash._GetHexString()._CompareHex("FF7040CEC7824248E9DCEB818E111772DD779B97") != 0)
                            {
                                stream._SeekToBegin();

                                using var file2 = await Lfs.CreateAsync(@"D:\Downloads\tmp.iso");
                                using var filest = file2.GetStream();

                                await stream.CopyBetweenStreamAsync(filest);

                                throw new CoresException($"Hash different: {hash._GetHexString()}");
                            }

                            //await AsyncAwait(async () =>
                            //{
                            //    using var file = Lfs.Open(@"c:\tmp\test1.dat");

                            //    using var sector = new XtsAesRandomAccess(file, "neko");

                            //    using var stream = sector.GetStream(true);

                            //    using SHA1Managed sha1 = new SHA1Managed();
                            //    byte[] hash = await Secure.CalcStreamHashAsync(stream, sha1);
                            //    if (hash._GetHexString()._CompareHex("FF7040CEC7824248E9DCEB818E111772DD779B97") != 0)
                            //    {
                            //        stream._SeekToBegin();

                            //        using var file2 = await Lfs.CreateAsync(@"D:\Downloads\tmp.iso");
                            //        using var filest = file2.GetStream();

                            //        await stream.CopyBetweenStreamAsync(filest);

                            //        throw new CoresException($"Hash different 2: {hash._GetHexString()}");
                            //    }
                            //    else
                            //    {
                            //        "Hash OK!"._Print();
                            //    }

                            //});
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                });

                Con.ReadLine();

                c.Cancel();

                task1._TryGetResult();

                return;
            }

            //if (true)
            //{
            //    SectorBasedRandomAccessTest.Test();

            //    return;
            //}

            if (false)
            {
                var pair = new PipeStreamPairWithSubTask(async (st) =>
                {
                    try
                    {
                        var r = new StreamReader(st);
                        while (true)
                        {
                            string? line = await r.ReadLineAsync();
                            if (line == null)
                            {
                                Con.WriteLine("[EOF]");
                                break;
                            }
                            Con.WriteLine(line);
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                });

                while (true)
                {
                    StreamWriter w = new StreamWriter(pair.StreamA);
                    w.AutoFlush = true;
                    string line = Con.ReadLine("IN>")!;
                    if (line._IsEmpty())
                    {
                        pair.Dispose();
                        break;
                    }
                    w.WriteLine(line);
                }
                return;
            }

            if (false)
            {
                //MsReg.IsValue(RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion", "")._Debug();
                //MsReg.GetValueType(RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "AnimationAttributionHashingEnabled")._Debug();
                //var x = MsReg.ReadValue(RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "test1");
                MsReg.WriteStr(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\Test1", "str", "Hello");
                MsReg.WriteInt32(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\Test1", "int32", 32);
                MsReg.WriteInt64(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\Test1", "int64", 64);
                MsReg.WriteBin(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\Test1", "bin", "Hello World"._GetBytes_UTF8());
                MsReg.EnumKey(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\")._DebugAsJson();
                MsReg.EnumValue(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\Test1")._DebugAsJson();
                MsReg.DeleteValue(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\Test1", "int64");
                MsReg.DeleteKey(RegRoot.CurrentUser, @"SOFTWARE\Microsoft\Windows\Test1");
                return;
            }

            if (false)
            {
                Async((Func<Task>)(async () =>
                {
                    try
                    {
                        var result1 = await EasyExec.ExecAsync(Util.GetGitForWindowsExeFileName(), $"pull origin master", @"C:\git\IPA-DNP-DeskVPN",
                            timeout: (int?)CoresConfig.GitParallelUpdater.GitCommandTimeoutMsecs,
                            easyOutputMaxSize: (int)CoresConfig.GitParallelUpdater.GitCommandOutputMaxSize,
                            flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut,
                            cancel: default,
                            debug: false,
                            printTag: "Git");
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }));
                return;
            }

            if (false)
            {
                Async((Func<Task>)(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            var result1 = await EasyExec.ExecAsync("cmd.exe", "/k ipconfig", @"C:\TMP2\gitneko\IPA-DNP-Hotate",
                                timeout: (int?)CoresConfig.GitParallelUpdater.GitCommandTimeoutMsecs,
                                easyOutputMaxSize: (int)CoresConfig.GitParallelUpdater.GitCommandOutputMaxSize,
                                flags: ExecFlags.Default | ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut,
                                cancel: default,
                                debug: false,
                                printTag: "CMD");
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }
                }));
                return;
            }

            if (false)
            {
                Async(async () =>
                {
                    await GitParallelUpdater.ExecGitParallelUpdaterAsync(@"C:\TMP2\gitneko", 1);
                });
                return;
            }

            if (false)
            {
                NamedAsyncLocks named = new NamedAsyncLocks();

                RefInt concurrent = new RefInt();

                for (int j = 0; j < 1; j++)
                {
                    var t = AsyncAwait(async () =>
                     {
                         try
                         {
                             int taskId = j;

                             for (int i = 0; ; i++)
                             {
                                 string name = $"{i}/{taskId}"; i.ToString();
                                 using (await named.LockWithAwait(name))
                                 {
                                     concurrent.Increment();
                                     try
                                     {
                                         //Con.WriteLine(taskId + " : " + i + "   (" + concurrent + ")");
                                         await Task.Yield();
                                     }
                                     finally
                                     {
                                         concurrent.Decrement();
                                     }
                                 }
                                 await Task.Yield();
                             }
                         }
                         catch (Exception ex)
                         {
                             ex._Debug();
                         }
                     });
                }

                ThreadObj.SleepInfinite();

                return;
            }

            if (false)
            {
                AsyncPulse p = new AsyncPulse();
                RefInt c = new RefInt();

                for (int i = 0; i < 20; i++)
                {
                    Task t = TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        var waiter = p.GetPulseWaiter();
                        while (true)
                        {
                            await waiter.WaitAsync();
                            Console.WriteLine(c.Increment());
                        }
                    });
                }

                while (true)
                {
                    Con.ReadLine();
                    p.FirePulse(false);
                }

                return;
            }

            if (false)
            {
                RefInt counter = new RefInt();

                for (int i = 0; i < 10000; i++)
                {
                    AsyncAwait(async () =>
                    {
                        while (true)
                        {
                            AsyncPulse p = new AsyncPulse();
                            AsyncManualResetEvent e1 = new AsyncManualResetEvent();
                            AsyncManualResetEvent e2 = new AsyncManualResetEvent();

                            Task t1 = AsyncAwait(async () =>
                            {
                                await e1.WaitAsync();

                                p.FirePulse();
                            });

                            Task t2 = AsyncAwait(async () =>
                            {
                                var waiter = p.GetPulseWaiter();

                                await e2.WaitAsync();

                                if (await waiter.WaitAsync(1000) == false)
                                {
                                    Dbg.Where();
                                }
                            });


                            if (Util.RandBool())
                            {
                                e2.Set(true);
                                await Task.Delay(10);
                                e1.Set(true);
                            }
                            else
                            {
                                e1.Set(true);
                                await Task.Delay(10);
                                e2.Set(true);
                            }

                            await t1;
                            await t2;

                            //counter.Increment()._Debug();
                        }
                    });
                }

                Thread.Sleep(Timeout.Infinite);

                return;
            }

            if (false)
            {
                Async(async () =>
                {
                    using var http = new WebApi();

                    using var res = await http.HttpSendRecvDataAsync(new WebSendRecvRequest(WebMethods.GET, "https://jp.softether-download.com/files/softether/v4.34-9745-rtm-2020.04.05-tree/Windows/SoftEther_VPN_Client/softether-vpnclient-v4.34-9745-rtm-2020.04.05-windows-x86_x64-intel.exe",
                        rangeStart: 78, rangeLength: null));

                    using var file = await Lfs.CreateAsync(@"c:\tmp\test1.dat", flags: FileFlags.AutoCreateDirectory);

                    using var fileStream = file.GetStream();

                    await res.DownloadStream.CopyBetweenStreamAsync(fileStream);
                });
                return;
            }

            if (true)
            {
                using CancelWatcher c = new CancelWatcher();

                var task1 = AsyncAwait(async () =>
                {
                    try
                    {
                        for (int i = 0; ; i++)
                        {
                            Dbg.GcCollect();

                            await Task.Yield();

                            c.ThrowIfCancellationRequested();

                            $"----------- {i}"._Debug();

                            using var reporter = new ProgressReporter(new ProgressReporterSetting(ProgressReporterOutputs.Console, title: "Downloading", unit: "bytes", toStr3: true));

                            HugeMemoryBuffer<byte> mem = new HugeMemoryBuffer<byte>();

                            using var stream = new BufferBasedStream(mem);

                            //using var file = Lfs.Create(@"c:\tmp\test1.dat", flags: FileFlags.SparseFile);
                            //using var stream = file.GetStream();

                            await FileDownloader.DownloadFileParallelAsync(
                        "https://ossvault.sec.softether.co.jp/vault/oss/20072701_ubuntu_cdimage/20.04/release/ubuntu-20.04-live-server-s390x.iso",
                        stream,
                        new FileDownloadOption(maxConcurrentThreads: Util.GetRandWithPercentageInt(90), bufferSize: Util.GetRandWithPercentageInt(123457), webApiOptions: new WebApiOptions(new WebApiSettings { Timeout = 1 * 1000, SslAcceptAnyCerts = true })),
                        progressReporter: reporter,
                        cancel: c);
                            //await FileDownloader.DownloadFileParallelAsync("http://speed.sec.softether.co.jp/003.100Mbytes.dat", stream,
                            //    new FileDownloadOption(maxConcurrentThreads: 30, bufferSize: 123457, webApiOptions: new WebApiOptions(new WebApiSettings { Timeout = 1 * 1000 })), cancel: c);

                            using SHA1 sha1 = SHA1.Create();
                            stream._SeekToBegin();
                            byte[] hash = await Secure.CalcStreamHashAsync(stream, sha1);
                            if (hash._GetHexString()._CompareHex("FF7040CEC7824248E9DCEB818E111772DD779B97") != 0)
                            {
                                stream._SeekToBegin();

                                using var file2 = await Lfs.CreateAsync(@"D:\Downloads\tmp.iso");
                                using var filest = file2.GetStream();

                                await stream.CopyBetweenStreamAsync(filest);

                                throw new CoresException($"Hash different: {hash._GetHexString()}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                });

                Con.ReadLine();

                c.Cancel();

                task1._TryGetResult();

                return;
            }

            if (false)
            {
                AsyncConcurrentTask t = new AsyncConcurrentTask(5);

                RefInt c = new RefInt();

                func1(0)._GetResult();

                async Task<int> func1(int a)
                {
                    while (true)
                    {
                        await t.StartTaskAsync<int, int>(async (p1, c1) =>
                        {
                            c.Increment()._Print();
                            await c1._WaitUntilCanceledAsync(300);
                            c.Decrement()._Print();
                            return 0;
                        }, 0);
                    }
                }
                return;
            }

            if (true)
            {
                FileDownloader.DownloadUrlListedAsync("https://raw.githubusercontent.com/dotnet/core/master/release-notes/3.1/3.1.6/3.1.6.md", @"c:\tmp\down1", "tar.gz,zip,exe",
                    reporterFactory: new ProgressFileDownloadingReporterFactory(ProgressReporterOutputs.Console)
                    )._GetResult();
                return;
            }

            if (true)
            {
                var data = LogStatMemoryLeakAnalyzer.AnalyzeLogFiles(@"C:\git\IPA-DN-Cores\Cores.NET\Dev.Test\Log\Stat");

                data._ObjectArrayToCsv(true)._WriteTextFile(@"c:\tmp\test.csv");
                return;
            }

            if (true)
            {
                string testStr = "{\"TimeStamp\":\"2020-07-13T13:15:07.0557759+09:00\",\"Data\":{\"Task\":1,\"D\":19,\"Q\":34,\"S\":2,\"Obj\":359,\"IO\":0,\"Cpu\":6,\"Mem\":26022,\"Task2\":32766},\"TypeName\":\"CoresRuntimeStat\",\"Kind\":\"Stat\",\"Priority\":\"Info\",\"Tag\":\"Snapshot\",\"AppName\":\"PPPoEServer\",\"MachineName\":\"mist-pppoe-server1.lab.coe.ad.jp\",\"Guid\":\"9271e0b0186f4bad83920044f950c2ad\"}";

                var x = testStr._JsonToObject<LogJsonParseAsRuntimeStat>();

                return;
            }

            if (true)
            {
                for (int i = 2; i < 4094; i++)
                {
                    if ((i % 2) == 0)
                    {
                        Con.WriteLine($"switchport trunk allowed vlan add {i}");
                    }
                }
                return;
            }

            if (true)
            {
                for (int i = 0; ; i++)
                {
                    //if ((i % 100) == 0)
                    {
                        $"count = {i}"._Print();
                        GC.Collect();
                    }
                    SecureShellClientSettings s = new SecureShellClientSettings("dnlinux.sec.softether.co.jp", 22, "root", "xxxxxxxx");

                    using (var ssh = new SecureShellClient(s))
                    {
                        using var pp = ssh.ConnectAsync()._GetResult();
                        using var stub = pp.GetNetAppProtocolStub();
                        using var st = stub.GetStream();
                        //using var r = new StreamReader(st);
                        Dbg.Where();
                        while (true)
                        {
                            //string? line = r.ReadLine();
                            //line._Print();
                            byte c = (byte)st.ReadByte();
                            char ch = (char)c;
                            ch._Print();
                            //c._Print();
                            if (ch == '#')
                            {
                                break;
                            }
                        }
                    }
                }
                return;
            }

            if (true)
            {
                for (int i = 0; ; i++)
                {
                    if ((i % 100) == 0)
                    {
                        i._Print();
                        GC.Collect();
                    }

                    try
                    {
                        using (var proc = Process.Start(Env.IsWindows ? @"c:\windows\System32\cacls.exe" : "/bin/true"))
                        {
                            proc._FixProcessObjectHandleLeak();
                            proc.WaitForExit();
                            proc.Kill(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }
                return;
            }

            if (true)
            {
                for (int i = 0; i <= 15; i++)
                {
                    Con.WriteLine($"SECONDARY:[https://163-220-245-{i}.thin-secure.v4.cyber.ipa.go.jp/widecontrol/?flag=limited]");
                }

                for (int i = 32; i <= 47; i++)
                {
                    Con.WriteLine($"SECONDARY:[https://219-100-39-{i}.thin-secure.v4.cyber.ipa.go.jp/widecontrol/?flag=limited]");
                }

                return;
            }

            if (true)
            {
                List<int> o = new List<int>();
                for (int i = 32; i <= 82; i++)
                {
                    o.Add(i);
                }
                o.Remove(74);

                foreach (var i in o)
                {
                    Con.WriteLine($"vpn-thing4-{i:D3}        219.100.39.{i}");
                }
                return;
            }

            if (false)
            {
                string password = "microsoft";

                // 2020/4/13 NTTVPN Master Cert
                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "Thin Telework System Root Certificate", c: "JP", expires: new DateTime(2037, 12, 31), shaSize: PkiShaSize.SHA512));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteStringToFile(@"S:\NTTVPN\Certs\200418_Certs\00_Memo.txt", $"Created by {Env.AppRealProcessExeFileName} {DateTime.Now._ToDtStr()}", FileFlags.AutoCreateDirectory, doNotOverwrite: true, writeBom: true);

                Lfs.WriteDataToFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                Lfs.WriteDataToFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                Lfs.WriteDataToFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                //return;
            }

            if (false)
            {
                // 2020/4/13 NTTVPN Sub Certs
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.pfx").Span);

                IssueCert("*.controller.dynamic-ip.thin.cyber.ipa.go.jp", @"S:\NTTVPN\Certs\200418_Certs\01_Controller");
                IssueCert("*.gates.dynamic-ip.thin.v4.cyber.ipa.go.jp", @"S:\NTTVPN\Certs\200418_Certs\02_Gates_001");

                void IssueCert(string cn, string fileNameBase)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: new DateTime(2037, 12, 31), shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }

                return;
            }

            if (false)
            {
                // 2020/4/13 NTTVPN Sub Certs additional
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.pfx").Span);

                IssueCert("vEqFWu6uC2cagK4B", @"S:\NTTVPN\Certs\200418_Certs\03_Controller_002", "6aFqCaF5eXYzwBrC");
                IssueCert("ac2xvGbQ7MuTsjCH", @"S:\NTTVPN\Certs\200418_Certs\04_Gates_003", "egubkPjrCev4NvXM");

                void IssueCert(string cn, string fileNameBase, string issuerName)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: new DateTime(2037, 12, 31), shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }),

                                new CertificateOptions(PkiAlgorithm.RSA, cn: issuerName, c: "JP"));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }

                return;
            }


            if (true)
            {
                // 2020/5/6 NTTVPN Sub Certs for load-balancer.thin-secure.cyber.ipa.go.jp
                string password = "microsoft";

                CertificateStore master = new CertificateStore(Lfs.ReadDataFromFile(@"S:\NTTVPN\Certs\200418_Certs\00_Master.pfx").Span);

                IssueCert("load-balancer.thin-secure.cyber.ipa.go.jp", @"S:\NTTVPN\Certs\200418_Certs\05_Controller_ThinSecure");

                void IssueCert(string cn, string fileNameBase)
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                    var cert = new Certificate(priv, master, new CertificateOptions(PkiAlgorithm.RSA, cn, c: "JP", expires: new DateTime(2037, 12, 31), shaSize: PkiShaSize.SHA256,
                        keyUsages: Org.BouncyCastle.Asn1.X509.KeyUsage.DigitalSignature | Org.BouncyCastle.Asn1.X509.KeyUsage.KeyEncipherment | Org.BouncyCastle.Asn1.X509.KeyUsage.DataEncipherment,
                        extendedKeyUsages:
                            new Org.BouncyCastle.Asn1.X509.KeyPurposeID[] {
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPServerAuth, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPClientAuth,
                                Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecEndSystem, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecTunnel, Org.BouncyCastle.Asn1.X509.KeyPurposeID.IdKPIpsecUser }));

                    var store = new CertificateStore(cert, priv);
                    Lfs.WriteDataToFile(fileNameBase + ".pfx", store.ExportPkcs12(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                    Lfs.WriteDataToFile(fileNameBase + "_Encrypted.pfx", store.ExportPkcs12(password), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".cer", store.PrimaryCertificate.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);

                    Lfs.WriteDataToFile(fileNameBase + ".key", store.PrimaryPrivateKey.Export(), FileFlags.AutoCreateDirectory, doNotOverwrite: true);
                }

                return;
            }

            if (true)
            {
                var result = EasyExec.ExecBashAsync("ps -eo nlwp | tail -n +2 | awk '{ num_threads += $1 } END { print num_threads }'")._GetResult();

                string valueStr = result.OutputStr._GetFirstFilledLineFromLines();

                Con.WriteLine(valueStr);

                return;
            }

            if (true)
            {
                var start = IPAddr.FromString("1.2.0.0");

                var current = start;

                for (int i = 1; i <= 4000; i++)
                {
                    if (i <= 4 || i >= 3998)
                        $"VLAN SW0-{i:D4}: {current}/30"._Print();
                    current = current.Add(4);
                }

                for (int i = 1; i <= 4000; i++)
                {
                    if (i <= 4 || i >= 3998)
                        $"VLAN SW1-{i:D4}: {current}/30"._Print();
                    current = current.Add(4);
                }


                return;
            }

            if (true)
            {
                using (SnmpWorkHost host = new SnmpWorkHost())
                {
                    host.Register("Temperature", 101_00000, new SnmpWorkFetcherTemperature(host));
                    host.Register("Ram", 102_00000, new SnmpWorkFetcherMemory(host));
                    host.Register("Disk", 103_00000, new SnmpWorkFetcherDisk(host));
                    host.Register("Net", 104_00000, new SnmpWorkFetcherNetwork(host));

                    host.Register("Ping", 105_00000, new SnmpWorkFetcherPing(host));
                    host.Register("Speed", 106_00000, new SnmpWorkFetcherSpeed(host));
                    host.Register("Quality", 107_00000, new SnmpWorkFetcherPktQuality(host));
                    host.Register("Bird", 108_00000, new SnmpWorkFetcherBird(host));

                    while (true)
                    {
                        //host.GetValues()._PrintAsJson();

                        Sleep(300);
                    }

                    //Con.ReadLine(">");
                }
                return;
            }

            if (true)
            {
                string src = "SignAuthenticodeInternal a b /comment:\"SE File\" /driver:no /cert:SoftEtherFile";

                string[] dst = Str.ParseCmdLine(src);

                foreach (var tmp1 in dst)
                {
                    tmp1._Print();
                }

                return;
            }

            if (true)
            {
                string fn1 = @"c:\tmp\200125\json.txt";
                string fn2 = @"c:\tmp\200125\json2.txt";

                DirSuperBackupMetadata o = Lfs.ReadJsonFromFile<DirSuperBackupMetadata>(fn1, maxSize: long.MaxValue);

                Lfs.WriteJsonToFile(fn2, o, flags: FileFlags.AutoCreateDirectory);

                return;
            }

            if (true)
            {
                DirSuperBackupMetadata o = new DirSuperBackupMetadata();

                o.DirMetadata = Lfs.GetDirectoryMetadata(@"c:\tmp\");

                o.FileList = new List<DirSuperBackupMetadataFile>();

                o.DirList = new List<string>();

                var fileMeta = Lfs.GetFileMetadata(@"xxxxx");

                Dbg.Where();

                for (int i = 0; i < 1000000; i++)
                {
                    o.FileList.Add(new DirSuperBackupMetadataFile
                    {
                        FileName = "aaaaa12345aaaaa12345aaaaa12345aaaaa12345aaaaa12345aaaaa12345",
                        MetaData = fileMeta,
                    });
                }

                string fn = @"c:\tmp\200125\json.txt";

                Lfs.WriteJsonToFile(fn, o, flags: FileFlags.AutoCreateDirectory);

                return;
            }

            if (true)
            {
                using (AuthenticodeSignClient ac = new AuthenticodeSignClient("https://codesignserver:7006/sign", "7BDBCA40E9C4CE374C7889CD3A26EE8D485B94153C2943C09765EEA309FCA13D"))
                {
                    byte[] ret = ac.SignAsync(Lfs.ReadStringFromFile(@"\\fss\share\tmp\signserver\password.txt", oneLine: true),
                        Load(@"C:\TMP\200101_signtest\src\src.exe"), "SoftEtherEv", "Driver", "Hello")._GetResult();

                    ret._Save(@"c:\tmp\200119\dst.exe", FileFlags.AutoCreateDirectory);
                }
                return;
            }

            if (false)
            {
                // HDD Seek test
                string hddName = "PhysicalDrive2";

                using (var fs = new LocalRawDiskFileSystem())
                {
                    using (var disk = fs.Open("/" + hddName))
                    {
                        int size = 4096;
                        long diskSize = disk.Size;
                        Memory<byte> tmp = new byte[size];

                        int numSeek = 0;

                        long startTick = Time.HighResTick64;

                        long last = 0;

                        while (true)
                        {
                            long pos = (Util.RandSInt63() % (diskSize - (long)size)) / 4096L * 4096L;

                            disk.ReadRandom(pos, tmp);
                            numSeek++;

                            if ((numSeek % 10) == 0)
                            {
                                long now = Time.HighResTick64;

                                if (now > startTick)
                                {
                                    if (last == 0 || (last + 1000) <= now)
                                    {
                                        last = now;

                                        double secs = (double)(now - startTick) / 1000.0;

                                        double averageSeekTime = secs / (double)numSeek;

                                        Con.WriteLine(averageSeekTime);
                                    }
                                }
                            }
                        }
                    }
                }

                return;
            }

            if (true)
            {
                // SQLite 実験 結論: ネットワーク越しの場合、ファイルロックがかけられていないとトランザクションはおかしくなります
                string filename = @"\\dn-smbtest1\share\nfse\tmp\dbtest.sqlite";

                if (Con.ReadLine("2?>")._ToBool())
                {
                    filename = @"\\dn-smbtest2\share\nfse\tmp\dbtest.sqlite";
                }

                Lfs.CreateDirectory(filename._GetDirectoryName()!);

                Con.WriteLine("Opening db...");
                using (var db = new Database($"Data Source='{filename}'", serverType: DatabaseServerType.SQLite))
                {
                    Con.WriteLine("Creating table...");
                    db.EasyExecute("CREATE TABLE if not exists favorite_beers  (name VARCHAR(50))");

                    Con.WriteLine("Starting tran...");

                    using (var tran = db.UsingTran(IsolationLevel.ReadCommitted))
                    {
                        int i = 0;

                        i++;
                        db.EasyExecute($"INSERT INTO favorite_beers VALUES('test {i}')");

                        Con.ReadLine("wait>");

                        i++;
                        db.EasyExecute($"INSERT INTO favorite_beers VALUES('test {i}')");

                        db.Commit();
                    }
                }
                return;
            }

            if (true)
            {
                using (var fs = new LocalRawDiskFileSystem())
                {
                    //fs.EnumDirectory("/").Where(x=>x.Name.EndsWith("11"))._PrintAsJson();

                    //using (var f = fs.Open("/PhysicalDrive11"))
                    //{
                    //    Memory<byte> data = new byte[4096 * 16];
                    //    long totalReadSize = 0;

                    //    // skip
                    //    long skipSize = f.Size - 4096 * 16 * 812;

                    //    f.Seek(skipSize, SeekOrigin.Begin);
                    //    totalReadSize = skipSize;

                    //    while (true)
                    //    {
                    //        totalReadSize._Print();
                    //        int r = f.Read(data);
                    //        if (r == 0) break;
                    //        totalReadSize += r;
                    //    }
                    //}

                    //if (false)
                    //{
                    //    using (var disk = fs.Open("/PhysicalDrive11"))
                    //    {
                    //        using (var file = Lfs.Create(@"c:\tmp\200103\test1.dat", flags: FileFlags.AutoCreateDirectory))
                    //        {
                    //            FileUtil.CopyBetweenFileBaseAsync(disk, file, truncateSize: 140 * 1024 * 1024, param: new CopyFileParams(asyncCopy: true))._GetResult();
                    //        }
                    //    }
                    //}
                    //else
                    //{
                    //    using (var disk = fs.Open("/PhysicalDrive11", writeMode: true))
                    //    {
                    //        using (var file = Lfs.Open(@"C:\Downloads\2019-09-26-raspbian-buster-lite.ddi"))
                    //        {
                    //            FileUtil.CopyBetweenFileBaseAsync(file, disk, param: new CopyFileParams(asyncCopy: true))._GetResult();
                    //        }
                    //    }
                    //}
                }
                return;
            }

            if (true)
            {
                List<TestClass1> o = new List<TestClass1>();
                o.Add(new TestClass1 { a = "hello", b = "World", c = "a,b,c" });
                o.Add(new TestClass1 { a = null, b = "neko", c = "x\"y" });

                Str.ObjectArrayToCsv(o, withHeader: true)._Print();

                return;
            }

            if (true)
            {
                Str.CombineStringArrayForCsv("a", "b", "c")._Print();
                Str.CombineStringArrayForCsv("this,is", "a,test", "string")._Print();
                Str.CombineStringArrayForCsv("printf(\"Hello World\");")._Print();
                Str.CombineStringArrayForCsv("new\r\nline")._Print();

                return;
            }

            if (true)
            {
                var info = LocalNet.GetTcpIpHostDataJsonSafe(true);

                info._PrintAsJson();

                return;
            }

            if (true)
            {
                string cmd = @"c:\windows\system32\cmd.exe";
                string arg = "";

                if (Env.IsUnix)
                {
                    cmd = "/bin/bash";
                    arg = "";
                }

                using (ExecInstance inst = new ExecInstance(new ExecOptions(cmd, arg, flags: ExecFlags.KillProcessGroup)))
                {
                    using var stdStub = inst.InputOutputPipePoint.GetNetAppProtocolStub();
                    using var stdStream = stdStub.GetStream();

                    using var errStub = inst.ErrorPipePoint.GetNetAppProtocolStub();
                    using var errStream = errStub.GetStream();

                    Task printTask = TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        while (true)
                        {
                            Memory<byte> mem = await stdStream._ReadAsync();
                            if (mem.Length == 0)
                            {
                                break;
                            }

                            Console.WriteLine("" + mem._GetString(inst.OutputEncoding) + "");
                        }
                    });

                    Task inputTask = TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        while (true)
                        {
                            string line = Console.ReadLine()!;

                            if (line == "q")
                            {
                                await inst.CancelAsync();
                            }

                            await stdStream.WriteAsync((line + Env.NewLine)._GetBytes(inst.InputEncoding));
                        }
                    });


                    Task errorTask = TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        while (true)
                        {
                            Memory<byte> mem = await errStream._ReadAsync();
                            if (mem.Length == 0)
                            {
                                break;
                            }

                            Console.WriteLine("" + mem._GetString(inst.ErrorEncoding) + "");
                        }
                    });

                    int ret = inst.WaitForExit();

                    printTask._TryWait(true);
                    inputTask._TryWait(true);
                    errorTask._TryWait(true);

                    Con.WriteLine(ret);
                }
                return;
            }

            if (true)
            {
                string cmd = @"c:\windows\system32\ipconfig.exe";
                string arg = "/all";

                if (Env.IsUnix)
                {
                    cmd = "/sbin/ifconfig";
                    arg = "-a";
                    //cmd = "/bin/bash";
                    //arg = "aaa";
                }

                using (ExecInstance inst = new ExecInstance(new ExecOptions(cmd, arg, easyOutputMaxSize: int.MaxValue)))
                {
                    int ret = inst.WaitForExit();

                    inst.EasyOutputStr._Print();
                    inst.EasyErrorStr._Print();

                    Con.WriteLine(ret);
                }
                return;
            }

            if (true)
            {
                for (int i = 0; i < 16; i++)
                {
                    Task t = TaskUtil.StartSyncTaskAsync(() =>
                    {
                        while (true)
                        {
                            Limbo.SInt32++;
                        }
                    });
                }
            }

            if (true)
            {
                Hive.AppSettings["test2"].AccessData(true, kv =>
                {
                    kv.SetStr("Str", "123");
                });
                Hive.LocalAppSettings["test2"].AccessData(true, kv =>
                {
                    kv.SetStr("Str", "123");
                });
                return;
            }

            if (true)
            {
                FileHistoryManager mgr = new FileHistoryManager(new FileHistoryManagerOptions(
                    fn => Str.FileNameStrToDateTimeOffset(PP.GetFileNameWithoutExtension(fn)),
                    FileHistoryManagerPolicy.GetTestPolicy()));

                string dir = @"C:\tmp\190904logtest";
                Lfs.CreateDirectory(dir);

                while (true)
                {
                    var currentFiles = Lfs.EnumDirectory(dir, false).Where(x => x.IsFile).Select(x => x.FullPath);

                    string newFile = PP.Combine(dir, Str.DateTimeOffsetToFileNameStr(DateTimeOffset.Now)) + ".log";

                    var deleteList = mgr.GenerateFileListToDelete(currentFiles);

                    deleteList.ForEach(x => Lfs.DeleteFile(x));

                    if (mgr.DetermineIsNewFileToCreate(currentFiles, newFile))
                    {
                        "ABC"._WriteTextFile(newFile);
                    }

                    Dbg.Where();
                    Sleep(100);
                }

                return;
            }

            if (true)
            {
                Lfs.CreateZipArchive(@"C:\TMP\190904\190904_3.zip", @"C:\Dev\ACCamera");

                return;
            }

            if (true)
            {
                string firstPassword = "ipa";

                DateTimeOffset now = new DateTime(2003, 12, 17, 1, 2, 3);

                string text = "おめでとう ございます！\r\n\r\n未踏ソフトウェア創造事業 万歳！！\r\n";

                byte[] zipInternalContents = text._GetBytes_UTF8(true);
                string zipInternalFilename = "IPA.txt";

                int numZip = 10000;

                List<string> pwList = new List<string>();

                for (int i = 0; i < numZip; i++)
                {
                    string pw;
                    if (i != (numZip - 1))
                    {
                        pw = Str.GenRandPassword(16);
                    }
                    else
                    {
                        pw = firstPassword;
                    }

                    pwList.Add(pw);
                }

                for (int i = 0; i < numZip; i++)
                {
                    $"{i} 回目"._Print();

                    using MemoryStream outputMs = new MemoryStream();
                    using var outputRandomAccess = outputMs._GetWriteOnlyStreamBasedRandomAccess(false);
                    using var zip = new ZipWriter(new ZipContainerOptions(outputRandomAccess));

                    if (i >= 1)
                    {
                        string text2 = @$"<< 暗号化パスワード通知 >>

和式の伝統に則り、セキュリティを確保している安心感を得るためだけに、
ZIP パスワードをかけておるのです。

ZIP ファイルのパスワード:
{pwList[i - 1]._Normalize(false, false, true, false)}

(ただし、実際のパスワードは、なんと、これを半角にしたものであるぞ。
 十分注意せい。)

";

                        zip.AddFileSimpleData(new FileContainerEntityParam("Password.txt", new FileMetadata(creationTime: now, lastWriteTime: now),
                             FileContainerEntityFlags.EnableCompression, encryptPassword: pwList[i]),
                             text2._GetBytes_UTF8(true));
                    }

                    zip.AddFileSimpleData(new FileContainerEntityParam(zipInternalFilename, new FileMetadata(creationTime: now, lastWriteTime: now),
                         FileContainerEntityFlags.EnableCompression, encryptPassword: pwList[i]),
                         zipInternalContents);

                    zip.Finish();

                    //Lfs.WriteDataToFile(@"C:\TMP2\190829\zip3.zip", outputMs.ToArray(), FileFlags.AutoCreateDirectory);

                    zipInternalContents = outputMs.ToArray();
                    zipInternalFilename = $"Zip No. {numZip - i - 1}.zip";
                }

                Lfs.WriteDataToFile(@"C:\TMP2\190829\output.zip", zipInternalContents, FileFlags.AutoCreateDirectory);

                return;
            }

            if (true)
            {
                string password = "x";

                DateTimeOffset now = DateTimeOffset.Now;

                using var outFile = Lfs.Create(@"c:\tmp2\190829\zip.zip", flags: FileFlags.AutoCreateDirectory);

                using var zip = new ZipWriter(new ZipContainerOptions(outFile));

                zip.AddFile(new FileContainerEntityParam("1.txt", new FileMetadata(lastWriteTime: now, creationTime: now), FileContainerEntityFlags.EnableCompression),
                    (w, c) =>
                    {
                        w.Append("Hello"._GetBytes_Ascii());
                        w.Append("World"._GetBytes_Ascii());
                        w.Append(Str.MakeCharArray('x', 40000)._GetBytes_Ascii());
                        return true;
                    });

                zip.AddFile(new FileContainerEntityParam("2.txt", new FileMetadata(lastWriteTime: now, creationTime: now), FileContainerEntityFlags.EnableCompression, encryptPassword: password),
                    (w, c) =>
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            w.Append("Hello"._GetBytes_Ascii());
                            w.Append("World"._GetBytes_Ascii());
                            w.Append("Hello"._GetBytes_Ascii());
                            w.Append("World"._GetBytes_Ascii());
                        }
                        return true;
                    });

                if (true)
                {
                    zip.AddFile(new FileContainerEntityParam("3.txt", new FileMetadata(lastWriteTime: now, creationTime: now), FileContainerEntityFlags.EnableCompression | FileContainerEntityFlags.CompressionMode_Fast, encryptPassword: password),
                        (w, c) =>
                        {
                            byte[] tmp = new byte[1_000_000_000];
                            for (int i = 0; i < 5; i++)
                            {
                                w.Append(tmp, c);
                            }
                            return true;
                        });
                }

                zip.AddFile(new FileContainerEntityParam("4.txt", new FileMetadata(lastWriteTime: now, creationTime: now), encryptPassword: password),
                    (w, c) =>
                    {
                        w.Append("Hello"._GetBytes_Ascii());
                        w.Append("World"._GetBytes_Ascii());
                        return true;
                    });

                if (false)
                {
                    for (int i = 10000; i < 90000; i++)
                    {
                        string ns = i.ToString();
                        string fn = $"many/{ns.Substring(0, 2)}/{ns}.txt";

                        zip.AddFile(new FileContainerEntityParam(fn, new FileMetadata(lastWriteTime: now, creationTime: now), flags: FileContainerEntityFlags.None, encryptPassword: password),
                            (w, c) =>
                            {
                                w.Append(i.ToString()._GetBytes_Ascii());
                                return true;
                            });
                    }
                }

                zip.Finish();

                return;
            }

            if (true)
            {
                using var buf = new MemoryOrDiskBuffer(new MemoryOrDiskBufferOptions(5));
                using var w = new StreamWriter(new BufferBasedStream(buf), leaveOpen: true);
                w.AutoFlush = true;

                w.Write("1");
                w.Write("2");
                w.Write("3");
                w.Write("4");
                w.Write("5");
                w.Write("6");
                w.Write("7");

                Con.WriteLine(buf.LongPosition);

                buf.Seek(3, SeekOrigin.Begin);

                Con.WriteLine(buf.LongPosition);

                buf.Write("abc"._GetBytes_Ascii());
                buf.Flush();


                Con.ReadLine("?");

                return;
            }

            if (true)
            {
                using var file = Lfs.CreateDynamicTempFile();
                using var w = new StreamWriter(file.GetStream());
                for (int i = 0; i < 100; i++)
                {
                    w.WriteLine(i.ToString());
                }
                w.Flush();
                //file.Flush();
                Con.ReadLine("?");
                return;
            }

            if (true)
            {
                STest1 t1 = new STest1("Hello");

                string json1 = t1._ObjectToJson();

                //json1._Print();

                json1 = json1._ReplaceStr("Hello", "Bye");

                STest1 t2 = json1._JsonToObject<STest1>()!;

                //t2._PrintAsJson();

                //t2.GetP1()._PrintAsJson();

                string json2 = t1._ObjectToRuntimeJsonStr();

                //STest1 t3 = t1._CloneDeep();

                //t3._PrintAsJson();

                //t3.GetP1()._PrintAsJson();

                json2._Print();

                return;
            }

            if (true)
            {
                var info = Dbg.GetCurrentGitCommitInfo();
                info._PrintAsJson();
                return;
            }

            if (true)
            {
                object? obj = null;

                string jstr = obj._ObjectToJson();

                jstr._Print();

                object obj2 = ""._JsonToObject<object>()!;

                return;
            }

            if (true)
            {
                while (true)
                {
                    string line = Con.ReadLine(">");

                    line._ParseUrl(out Uri uri, out QueryStringList qs);

                    qs._DebugAsJson();

                    Con.WriteDebug(qs.ToString());

                    Con.WriteDebug();
                }
                return;
            }

            if (true)
            {
                JsonRpcTest.jsonrpc_client_server_both_test();
                return;
            }

            if (true)
            {
                OneLineParams ol = new OneLineParams();

                ol._SetSingle("a", "1");
                ol._SetSingle("b", "2");
                ol._SetSingle("c", "3");
                ol.Add("b", "9");
                ol._SetSingle("b", "4");

                ol.ToString()._Print();

                return;
            }

            if (true)
            {
                while (true)
                {
                    string line = Con.ReadLine(">");

                    OneLineParams ol = new OneLineParams(line);

                    ol._DebugAsJson();

                    ol.ToString()._Print();

                }
                return;
            }

            if (true)
            {
                var ca = DevTools.CoresDebugCACert;

                CertificateStore x = ca.PkiCertificateStore;

                return;
            }

            if (true)
            {
                var ca = DevTools.CoresDebugCACert;

                var mem = ca.ExportCertificateAndKeyAsP12();

                mem._DataToFile(PP.Combine(Env.AppRootDir, "test.p12"));

                mem = @"\\10.40.0.110\vmdata1\containers\dn-lxd-vm2-test1\rootfs\root\Copy-IPA-DN-Cores\Cores.NET\TestDev\test.p12"._FileToData();

                ////var x = new CertificateStore(mem.Span, "");

                //mem = CertificateUtil.NormalizePkcs12MemoryData(mem.Span, "");

                //mem._DataToFile(PP.Combine(Env.AppRootDir, "test_fix1.p12"));

                CertificateStore x = ca.PkiCertificateStore;

                x.ExportPkcs12()._DataToFile(PP.Combine(Env.AppRootDir, "test2.p12"));

                x.ExportChainedPem(out ReadOnlyMemory<byte> cert, out _, "");

                cert._DataToFile(PP.Combine(Env.AppRootDir, "test3.txt"));

                return;
            }

            if (true)
            {
                Env.AppRootDir._Print();
                CoresRes["190812codestest"].String._Print();
                return;
            }

            if (true)
            {
                List<SslCertCollectorItem> o = new List<SslCertCollectorItem>();

                o.Add(new SslCertCollectorItem { CertFqdnList = "a" });
                o.Add(new SslCertCollectorItem { CertFqdnList = "b" });
                o.Add(new SslCertCollectorItem { CertFqdnList = "c" });

                XmlAndXsd xmlData = Util.GenerateXmlAndXsd(o);

                string dir = @"c:\tmp\190811_a";

                Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, xmlData.XmlFileName), xmlData.XmlData, flags: FileFlags.AutoCreateDirectory);
                Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, xmlData.XsdFileName), xmlData.XsdData, flags: FileFlags.AutoCreateDirectory);
                return;
            }

            if (true)
            {
                List<SniHostnameIpAddressPair> list = @"c:\tmp\list1.txt"._FileToObject<List<SniHostnameIpAddressPair>>();

                SslCertCollectorUtil col = new SslCertCollectorUtil(1000, list.Take(0));

                IReadOnlyList<SslCertCollectorItem> ret = col.ExecuteAsync()._GetResult();

                var x = Util.GenerateXmlAndXsd(ret);

                string dir = @"c:\tmp\190811";

                Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, x.XmlFileName), x.XmlData, flags: FileFlags.AutoCreateDirectory);
                Lfs.WriteDataToFile(Lfs.PathParser.Combine(dir, x.XsdFileName), x.XsdData, flags: FileFlags.AutoCreateDirectory);

                return;
            }

            if (true)
            {
                DnsFlattenUtil flat = new DnsFlattenUtil();

                foreach (FileSystemEntity ent in Lfs.EnumDirectory(@"_______", true))
                {
                    if (ent.IsFile)
                    {
                        string fn = ent.FullPath;
                        fn._Print();

                        flat.InputZoneFile(Lfs.PathParser.GetFileNameWithoutExtension(fn), Lfs.ReadDataFromFile(fn).Span);
                    }
                }

                DnsIpPairGeneratorUtil gen = new DnsIpPairGeneratorUtil(100, flat.FqdnSet);

                List<SniHostnameIpAddressPair> list = gen.ExecuteAsync()._GetResult().ToList();

                list._ObjectToFile(@"c:\tmp\list1.txt");

                return;
            }

            if (true)
            {
                PkiUtil.GenerateRsaKeyPair(2048, out PrivKey priv, out _);

                CertificateStore parent = DevTools.CoresDebugCACert.PkiCertificateStore;

                Certificate cert = new Certificate(priv, parent, new CertificateOptions(PkiAlgorithm.RSA, cn: "test"));
                Lfs.WriteDataToFile(@"c:\tmp\test.cer", cert.Export());
                return;
            }

            if (true)
            {
                DevTools.CoresDebugCACert.HashSHA256._Print();
                return;
            }

            if (true)
            {
                PkiUtil.GenerateRsaKeyPair(4096, out PrivKey priv, out _);

                var cert = new Certificate(priv, new CertificateOptions(PkiAlgorithm.RSA, "Cores.NET Debug Public CA", c: "US", expires: Util.MaxDateTimeOffsetValue));

                CertificateStore store = new CertificateStore(cert, priv);

                Lfs.WriteDataToFile(@"c:\tmp\ca.p12", store.ExportPkcs12());
                return;
            }

            if (true)
            {
                RateLimiter<int> rl = new RateLimiter<int>(new RateLimiterOptions(3, 1, mode: RateLimiterMode.NoPenalty));
                while (true)
                {
                    Con.ReadLine();
                    bool ret = rl.TryInput(1, out RateLimiterEntry e);
                    ret._Print();
                    e.CurrentAmount._Print();
                }
                return;
            }

            if (true)
            {
                TestSt2 s1 = new TestSt2("Hello");
                TestSt2 s2 = new TestSt2("Hello");

                s1._HashMarvin()._Debug();
                s2._HashMarvin()._Debug();

                //s1.Equals(s2)._Debug();

                Util.StructBitEquals(s1, s2)._Debug();

                return;
            }

            if (true)
            {
                Dictionary<ReadOnlyMemory<byte>, int> testDic3 = new Dictionary<ReadOnlyMemory<byte>, int>(MemoryComparers.ReadOnlyMemoryComparer);
                for (int i = 0; i < 65536; i++)
                {
                    MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
                    buf.WriteSInt32(i);
                    //var mem = buf.Memory;
                    //mem._RawWriteValueSInt32(i);
                    testDic3.Add(buf, i);
                }

                MemoryBuffer<byte> targetBuffer = new MemoryBuffer<byte>();
                targetBuffer.WriteSInt32(((int)32767));

                ReadOnlyMemory<byte> rm = targetBuffer.Memory;


                Limbo.Bool = true;

                while (true)
                {
                    Dbg.Where();
                    Limbo.SInt32 = testDic3[rm];
                }

                Limbo.SInt32._Debug();

                return;
            }

            if (true)
            {
                SpanBuffer<byte> buf = new SpanBuffer<byte>();
                buf.WriteSInt64(1234567890L._Endian64_S());
                ReadOnlySpan<byte> span = buf.Span.Slice(0, 1);
                span._RawReadValueUInt64()._Debug();
                return;
            }

            if (true)
            {
                DebugHostUtil.Stop("daemonCenTer")._Debug();
                return;
            }

            if (true)
            {
                bool b = (IsEmpty)" aaa ";
                b._Print();

                return;

                Con.WriteLine((IgnoreCaseTrim)"aa" == "AA");
                Con.WriteLine("aa" == (IgnoreCaseTrim)"AA");

                Con.WriteLine((IgnoreCaseTrim)"aa" == (IgnoreCaseTrim)"AA");
                Con.WriteLine((IgnoreCaseTrim)"aa" == (IgnoreCaseTrim)"AA");

                Con.WriteLine((IgnoreCaseTrim)"AA" == "aa");
                Con.WriteLine("AA" == (IgnoreCaseTrim)"aa");

                Con.WriteLine(null == (IgnoreCaseTrim)null);
                Con.WriteLine((IgnoreCaseTrim)null == null);

                Con.WriteLine((IgnoreCaseTrim)null == "");
                Con.WriteLine((IgnoreCaseTrim)"" == null);

                Con.WriteLine(null == (IgnoreCaseTrim)"");
                Con.WriteLine("" == (IgnoreCaseTrim)null);

                Con.WriteLine((IgnoreCaseTrim)"ab" == "AA");
                Con.WriteLine((IgnoreCaseTrim)"" == "AA");

                //""._IsSamei
                return;
            }

            if (true)
            {
                Con.WriteLine((IgnoreCase)"aa" == "AA");
                Con.WriteLine("aa" == (IgnoreCase)"AA");

                Con.WriteLine((IgnoreCase)"aa" == (IgnoreCase)"AA");
                Con.WriteLine((IgnoreCase)"aa" == (IgnoreCase)"AA");

                Con.WriteLine((IgnoreCase)"AA" == "aa");
                Con.WriteLine("AA" == (IgnoreCase)"aa");

                Con.WriteLine(null == (IgnoreCase)null);
                Con.WriteLine((IgnoreCase)null == null);

                Con.WriteLine((IgnoreCase)null == "");
                Con.WriteLine((IgnoreCase)"" == null);

                Con.WriteLine(null == (IgnoreCase)"");
                Con.WriteLine("" == (IgnoreCase)null);


                //""._IsSamei
                return;
            }


            if (true)
            {
                Dbg.GetCurrentGitCommitId()._Print();

                return;
            }

            if (true)
            {
                var hive = Hive.AppSettingsEx["test01"];

                hive.AccessData(true, x =>
                {
                });

                return;
            }

            while (true)
            {
                string line = Con.ReadLine();

                string r = MasterData.ExtensionToMime.Get(line);
                r._Print();

                ""._Print();
            }

            if (true)
            {
                string a = "/a\"aa/b/c<a>あほ  ばか"._EncodeUrlPath();
                a._Print();

                string b = a._DecodeUrlPath();
                b._Print();

                return;
            }

            if (true)
            {
                EnumDirectoryFlags flag1 = EnumDirectoryFlags.IncludeCurrentDirectory | EnumDirectoryFlags.IncludeParentDirectory | EnumDirectoryFlags.NoGetPhysicalSize;

                flag1 = flag1.BitRemove(EnumDirectoryFlags.IncludeParentDirectory);

                flag1.ToString()._Print();

                return;
            }

            if (true)
            {
                PathParser.Linux.IsRootDirectory(@"/z")._Print();
                return;
            }

            if (true)
            {
                using (var p = Lfs.CreateFileProvider(@"c:\git"))
                {
                    while (true)
                    {
                        string path = Con.ReadLine();

                        if (path._IsEmpty()) return;

                        var info = p.GetFileInfo(path);

                        if (info.Exists == false)
                        {
                            Con.WriteLine(@"Not found.");
                        }
                        else
                        {
                            info._DebugAsJson(EnsurePresentInterface.Yes);
                        }
                    }
                }
                return;
            }

            if (true)
            {
                using (ViewFileSystem fs = new ChrootFileSystem(new ChrootFileSystemParam(Lfs, @"C:\git\", FileSystemMode.ReadOnly)))
                {
                    while (true)
                    {
                        string path = Con.ReadLine();

                        if (path._IsEmpty()) return;

                        path = fs.NormalizePath(path, NormalizePathOption.NormalizeCaseFileName);

                        Con.WriteLine(path);

                        Con.WriteLine();


                    }
                }
                return;
            }

            if (true)
            {
                PhysicalFileProvider p = new PhysicalFileProvider(@"\\fss\share");

                while (true)
                {
                    Event e = new Event(true);

                    var token = p.Watch("**/*");

                    token.RegisterChangeCallback(x =>
                    {
                        Dbg.Where();
                        e.Set();
                    }, null);

                    e.Wait();
                }

                return;
            }

            if (true)
            {
                CoresRes["190714_run_daemon.sh.txt"].String._Print();
                return;
            }

            if (true)
            {
                ManifestEmbeddedFileProvider emb = new ManifestEmbeddedFileProvider(typeof(FileSystem).Assembly);

                using (var fs = new FileProviderBasedFileSystem(new FileProviderFileSystemParams(emb)))
                {
                    var a = fs.EnumDirectory("/", true);

                    a._PrintAsJson();
                }
                return;
            }

            if (true)
            {
                ManifestEmbeddedFileProvider emb = new ManifestEmbeddedFileProvider(typeof(FileSystem).Assembly);

                var i = emb.GetFileInfo("/Common/resource/test/helloworld.txt");

                i.Exists._Print();
                i.PhysicalPath._Print();
                i.LastModified._Print();
                i.Name._Print();

                return;

                var ent = emb.GetDirectoryContents("/Common/resource/test/helloworld.txt");

                foreach (var e in ent)
                {
                    e.Name._Print();
                }

                return;
            }

            int count = 0;
            if (true)
            {
                while (true)
                {
                    try
                    {
                        while (true)
                        {
                            using (WebApi api = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true })))
                            {
                                count++;
                                Con.WriteLine($"Count : {count}");
                                long start = Time.HighResTick64;
                                //var ret = api.SimpleQueryAsync(WebMethods.GET, "http://127.0.0.1/")._GetResult();
                                //var ret = api.SimpleQueryAsync(WebMethods.GET, "http://pktlinux/favicon.ico")._GetResult();
                                var ret = api.SimpleQueryAsync(WebMethods.GET, "http://dn-lxd-vm2-test1/favicon.ico")._GetResult();
                                //var ret = api.SimpleQueryAsync(WebMethods.GET, "http://dn-winprod1/favicon.ico")._GetResult();
                                //var ret = api.SimpleQueryAsync(WebMethods.GET, "http://lxd-vm2.lab.coe.ad.jp/favicon.ico")._GetResult();
                                long end = Time.HighResTick64;

                                Con.WriteLine(end - start);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                        Thread.Sleep(10);
                    }
                }
                return;
            }

            if (true)
            {
                SourceCodeCounter c = new SourceCodeCounter(@"C:\git\IPA-DN-Cores\Cores.NET");

                c._PrintAsJson();

                return;
            }

            if (false)
            {
                PkiUtil.GenerateKeyPair(PkiAlgorithm.ECDSA, 256, out PrivKey privateKey, out PubKey publicKey);

                var signer = privateKey.GetSigner();
                var verifier = publicKey.GetVerifier();

                publicKey.EcdsaParameters.Q.AffineXCoord.GetEncoded()._GetHexString()._Print();

                byte[] target = "Hello"._GetBytes_Ascii();

                byte[] sign = signer.Sign(target);

                signer.AlgorithmName._Print();
                sign._GetHexString()._Print();

                target[2] = 0;

                verifier.Verify(sign, target)._Print();
            }
            else
            {
            }
        }

        static void Test_Vault_With_Kestrel()
        {
            var httpServerOpt = new HttpServerOptions
            {
                HttpPortsList = 80._SingleList(),
                HttpsPortsList = 443._SingleList(),
            };

            using (var httpServer = new HttpServer<AcmeTestHttpServerBuilder>(httpServerOpt))
            {
                Con.ReadLine("quit>");
            }
        }

        public static void Test_Vault()
        {
            var httpServerOpt = new HttpServerOptions
            {
                HttpPortsList = 80._SingleList(),
                HttpsPortsList = 443._SingleList(),
            };

            using (var httpServer = new HttpServer<AcmeTestHttpServerBuilder>(httpServerOpt))
            {
                using (CertVault vault = new CertVault(@"C:\tmp\190617vault", isGlobalVault: true))
                {
                    while (true)
                    {
                        string? fqdn = Con.ReadLine(">");

                        if (fqdn._IsEmpty())
                            break;

                        CertificateStore s = vault.SelectBestFitCertificate(fqdn, out _);

                        Certificate cert = s.PrimaryContainer.CertificateList[0];

                        cert.CertData.SubjectDN.ToString()._Print();
                    }
                }
            }
        }

        public static void Test_Acme_Junk()
        {
            LetsEncryptClient c = new LetsEncryptClient("https://acme-staging-v02.api.letsencrypt.org/directory");
            c.Init("da.190615@softether.co.jp")._GetResult();
        }

        public static void Test_Acme()
        {
            var httpServerOpt = new HttpServerOptions
            {
                HttpPortsList = 80._SingleList(),
                HttpsPortsList = 443._SingleList(),
            };

            using (var httpServer = new HttpServer<AcmeTestHttpServerBuilder>(httpServerOpt))
            {
                string keyFileName = @"c:\tmp\190615_acme\account.key";
                PrivKey? key = null;

                string certKeyFileName = @"c:\tmp\190615_acme\cert.key";
                PrivKey? certKey = null;

                try
                {
                    Memory<byte> data = Lfs.ReadDataFromFile(keyFileName);
                    key = new PrivKey(data.Span);
                }
                catch
                {
                    PkiUtil.GenerateKeyPair(PkiAlgorithm.ECDSA, 256, out key, out _);
                    Lfs.WriteDataToFile(keyFileName, key.Export(), flags: FileFlags.AutoCreateDirectory);
                }

                try
                {
                    Memory<byte> data = Lfs.ReadDataFromFile(certKeyFileName);
                    certKey = new PrivKey(data.Span);
                }
                catch
                {
                    PkiUtil.GenerateKeyPair(PkiAlgorithm.RSA, 2048, out certKey, out _);
                    Lfs.WriteDataToFile(certKeyFileName, certKey.Export(), flags: FileFlags.AutoCreateDirectory);
                }

                AcmeClientOptions o = new AcmeClientOptions();


                //while (true)
                //{
                //    Dbg.Where();
                //    using (AcmeClient acme = new AcmeClient(o))
                //    {
                //        AcmeAccount ac = acme.LoginAccountAsync(key, "mailto:da.190614@softether.co.jp"._SingleArray())._GetResult();
                //    }
                //}

                using (AcmeClient acme = new AcmeClient(o))
                {
                    AcmeAccount ac = acme.LoginAccountAsync(key, "mailto:da.190614@softether.co.jp"._SingleArray())._GetResult();

                    AcmeTestHttpServerBuilder.AcmeAccount = ac;

                    ac.AccountUrl._Print();

                    AcmeOrder order = ac.NewOrderAsync("012.pc34.sehosts.com")._GetResult();

                    CertificateStore store = order.FinalizeAsync(certKey)._GetResult();

                    Lfs.WriteDataToFile(@"c:\tmp\190615_acme\out.p12", store.ExportPkcs12());
                }
            }
        }

        public static void Test_PersistentCache()
        {
            PersistentLocalCache<TestHiveData1> c = new PersistentLocalCache<TestHiveData1>("cacheTest1", new TimeSpan(0, 0, 15), true,
                async (can) =>
                {
                    throw new ApplicationException("a");
                    await Task.CompletedTask;
                    return new TestHiveData1() { Date = DateTime.Now._ToDtStr(true) };
                }
                );

            var d = c.GetAsync().Result;

            Con.WriteLine(d.Date);
        }

        public static void Test_HiveLock()
        {
            int num = 10;
            HiveData<HiveKeyValue> test = Hive.LocalAppSettings["testlock"];

            for (int i = 0; i < num; i++)
            {
                test.AccessData(true, kv =>
                {
                    int value = kv.GetSInt32("value");
                    value++;
                    kv.SetSInt32("value", value);
                });
            }
        }
        public static void Test_ECDSA_Cert()
        {
            string tmpDir = @"c:\tmp\190614_ecdsa";

            PrivKey privateKey;
            PubKey publicKey;

            Lfs.CreateDirectory(tmpDir);

            if (true)
            {
                PkiUtil.GenerateEcdsaKeyPair(256, out privateKey, out publicKey);

                Lfs.WriteDataToFile(tmpDir._CombinePath("root_private.key"), privateKey.Export());
                Lfs.WriteDataToFile(tmpDir._CombinePath("root_private_encrypted.key"), privateKey.Export("microsoft"));

                Lfs.WriteDataToFile(tmpDir._CombinePath("root_public.key"), publicKey.Export());
            }

            privateKey = new PrivKey(Lfs.ReadDataFromFile(tmpDir._CombinePath("root_private_encrypted.key")).Span, "microsoft");
            publicKey = new PubKey(Lfs.ReadDataFromFile(tmpDir._CombinePath("root_public.key")).Span);

            Certificate cert;

            if (true)
            {
                cert = new Certificate(privateKey, new CertificateOptions(PkiAlgorithm.ECDSA, "www.abc", serial: new byte[] { 1, 2, 3 }, shaSize: PkiShaSize.SHA512));

                Lfs.WriteDataToFile(tmpDir._CombinePath("root_cert.crt"), cert.Export());
            }

            cert = new Certificate(Lfs.ReadDataFromFile(tmpDir._CombinePath("root_cert.crt")).Span);

            //Csr csr = new Csr(PkiAlgorithm.ECDSA, new CertificateOptions(PkiAlgorithm.ECDSA, "www.softether.com"), 256);
            //Lfs.WriteDataToFile(@"C:\TMP\190614_ecdsa\testcsr.txt", csr.ExportPem());

            DoNothing();
        }

        public static void Test_RSA_Cert()
        {
            string tmpDir = @"c:\tmp\190613_cert";

            PrivKey privateKey;
            PubKey publicKey;

            Lfs.CreateDirectory(tmpDir);

            if (true)
            {
                PkiUtil.GenerateRsaKeyPair(1024, out privateKey, out publicKey);

                Lfs.WriteDataToFile(tmpDir._CombinePath("root_private.key"), privateKey.Export());
                Lfs.WriteDataToFile(tmpDir._CombinePath("root_private_encrypted.key"), privateKey.Export("microsoft"));

                Lfs.WriteDataToFile(tmpDir._CombinePath("root_public.key"), publicKey.Export());
            }

            privateKey = new PrivKey(Lfs.ReadDataFromFile(tmpDir._CombinePath("root_private_encrypted.key")).Span, "microsoft");
            publicKey = new PubKey(Lfs.ReadDataFromFile(tmpDir._CombinePath("root_public.key")).Span);

            Certificate cert;

            if (true)
            {
                cert = new Certificate(privateKey, new CertificateOptions(PkiAlgorithm.RSA, "www.abc", serial: new byte[] { 1, 2, 3 }));

                Lfs.WriteDataToFile(tmpDir._CombinePath("root_cert.crt"), cert.Export());
            }

            cert = new Certificate(Lfs.ReadDataFromFile(tmpDir._CombinePath("root_cert.crt")).Span);

            //CertificateStore store = new CertificateStore(Lfs.ReadDataFromFile(@"C:\TMP\190613_cert\p12test.p12").Span);
            CertificateStore store = new CertificateStore(Lfs.ReadDataFromFile(@"H:\Crypto\all.open.ad.jp\cert.pfx").Span, "microsoft");

            Lfs.WriteDataToFile(@"C:\TMP\190613_cert\export_test.pfx", store.ExportPkcs12());

            store.ExportChainedPem(out ReadOnlyMemory<byte> exportPemCert, out ReadOnlyMemory<byte> exportPemKey);
            Lfs.WriteDataToFile(@"C:\TMP\190613_cert\export_test_chained.cer", exportPemCert);
            Lfs.WriteDataToFile(@"C:\TMP\190613_cert\export_test_chained.key", exportPemKey);

            CertificateStore store2 = new CertificateStore(Lfs.ReadDataFromFile(@"C:\TMP\190613_cert\export_test_chained.cer").Span, Lfs.ReadDataFromFile(@"C:\TMP\190613_cert\export_test_chained.key").Span, "");

            Lfs.WriteDataToFile(@"C:\TMP\190613_cert\export_test2.pfx", store2.ExportPkcs12());

            //Csr csr = new Csr(PkiAlgorithm.RSA, new CertificateOptions(PkiAlgorithm.ECDSA, "www.softether.com", shaSize: PkiShaSize.SHA512), 1024);
            //Lfs.WriteDataToFile(@"C:\TMP\190613_cert\testcsr.txt", csr.ExportPem());

            DoNothing();
        }

        public static void Test_Logger_Server_And_Client()
        {
            // Logger Tester
            PalSslServerAuthenticationOptions svrSsl = new PalSslServerAuthenticationOptions(DevTools.TestSampleCert, true, null);
            PalSslClientAuthenticationOptions cliSsl = new PalSslClientAuthenticationOptions(false, null, DevTools.TestSampleCert.HashSHA1);

            using (LogClient client = new LogClient(new LogClientOptions(null, cliSsl, "127.0.0.1")))
            {
                using (LogServer server = new LogServer(new LogServerOptions(null, @"c:\tmp\190612", FileFlags.OnCreateSetCompressionFlag, null, null, svrSsl, ports: Consts.Ports.LogServerDefaultServicePort._SingleArray())))
                {
                    CancellationTokenSource cts = new CancellationTokenSource();

                    Task testTask = TaskUtil.StartAsyncTaskAsync(async () =>
                    {
                        for (int i = 0; ; i++)
                        {
                            if (cts.IsCancellationRequested) return;

                            client.WriteLog(new LogJsonData()
                            {
                                AppName = "App",
                                //Data = "Hello World " + i.ToString(),
                                Data = new { X = 123, Y = 456, Z = "Hello" },
                                Guid = Str.NewGuid(),
                                Kind = LogKind.Default,
                                MachineName = "Neko",
                                Priority = LogPriority.Info.ToString(),
                                Tag = "TagSan",
                                TimeStamp = DateTimeOffset.Now,
                                TypeName = "xyz"
                            }
                            );

                            await Task.Delay(100);
                        }
                    });

                    Con.ReadLine("Exit>");

                    cts.Cancel();

                    testTask._TryWait();
                }
            }
        }

        public static unsafe void Test01()
        {

            if (true)
            {
                using (PCapPacketRecorder r = new PCapPacketRecorder(new TcpPseudoPacketGeneratorOptions(TcpDirectionType.Client, IPAddress.Parse("192.168.0.1"), 1, IPAddress.Parse("192.168.0.2"), 2)))
                {
                    r.RegisterEmitter(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190608\test.pcapng", flags: FileFlags.AutoCreateDirectory),
        false)));

                    var g = r.TcpGen;

                    g.EmitConnected();

                    g.EmitData("1aa1"._GetBytes_Ascii(), Direction.Send);
                    g.EmitData("4d4"._GetBytes_Ascii(), Direction.Recv);
                    g.EmitData("2bbbb2"._GetBytes_Ascii(), Direction.Send);
                    g.EmitData("5eeeeeeeee5"._GetBytes_Ascii(), Direction.Recv);
                    g.EmitData("3cccccc3"._GetBytes_Ascii(), Direction.Send);
                    g.EmitData("6fffffffffffffff6"._GetBytes_Ascii(), Direction.Recv);

                    //g.EmitReset(Direction.Send);

                    g.EmitFinish(Direction.Send);
                }
                return;
            }


            Packet p = PCapUtil.NewEmptyPacketForPCap(PacketSizeSets.NormalTcpIpPacket_V4 + 5, 0);

            ref byte payloadDest = ref p.PrependSpan<byte>(5);
            "Hello"._GetBytes_Ascii().CopyTo(new Span<byte>(Unsafe.AsPointer(ref payloadDest), 5));

            PacketSpan<TCPHeader> tcp = p.PrependSpanWithData<TCPHeader>(
                new TCPHeader()
                {
                    AckNumber = 123U._Endian32_U(),
                    SeqNumber = 456U._Endian32_U(),
                    SrcPort = 80U._Endian16_U(),
                    DstPort = 443U._Endian16_U(),
                    Flag = TCPFlags.Ack | TCPFlags.Psh,
                    HeaderLen = (byte)((sizeof(TCPHeader)) / 4),
                    WindowSize = 1234U._Endian16_U(),
                },
                sizeof(TCPHeader));

            PacketSpan<IPv4Header> ip = tcp.PrependSpanWithData<IPv4Header>(ref p,
                new IPv4Header()
                {
                    SrcIP = 0x12345678,
                    DstIP = 0xdeadbeef,
                    Flags = IPv4Flags.DontFragment,
                    HeaderLen = (byte)(sizeof(IPv4Header) / 4),
                    Identification = 0x1234U._Endian16_U(),
                    Protocol = IPProtocolNumber.TCP,
                    TimeToLive = 12,
                    TotalLength = ((ushort)(sizeof(IPv4Header) + tcp.GetTotalPacketSize(ref p)))._Endian16_U(),
                    Version = 4,
                });

            ref IPv4Header v4Header = ref ip.GetRefValue(ref p);

            ref TCPHeader tcpHeader = ref tcp.GetRefValue(ref p);

            v4Header.Checksum = v4Header.CalcIPv4Checksum();

            //tcpHeader.Checksum = v4Header.CalcTcpUdpPseudoChecksum(Unsafe.AsPointer(ref tcpHeader), ip.GetPayloadSize(ref p));
            tcpHeader.Checksum = tcpHeader.CalcTcpUdpPseudoChecksum(ref v4Header, "Hello"._GetBytes_Ascii());

            PacketSpan<VLanHeader> vlan = ip.PrependSpanWithData<VLanHeader>(ref p,
                new VLanHeader()
                {
                    VLanId_EndianSafe = (ushort)1234,
                    Protocol = EthernetProtocolId.IPv4._Endian16(),
                });

            EthernetHeader etherHeaderData = new EthernetHeader()
            {
                Protocol = EthernetProtocolId.VLan._Endian16(),
            };

            etherHeaderData.SrcAddress[0] = 0x00; etherHeaderData.SrcAddress[1] = 0xAC; etherHeaderData.SrcAddress[2] = 0x01;
            etherHeaderData.SrcAddress[3] = 0x23; etherHeaderData.SrcAddress[4] = 0x45; etherHeaderData.SrcAddress[5] = 0x47;

            etherHeaderData.DestAddress[0] = 0x00; etherHeaderData.DestAddress[1] = 0x98; etherHeaderData.DestAddress[2] = 0x21;
            etherHeaderData.DestAddress[3] = 0x33; etherHeaderData.DestAddress[4] = 0x89; etherHeaderData.DestAddress[5] = 0x01;

            PacketSpan<EthernetHeader> ether = vlan.PrependSpanWithData<EthernetHeader>(ref p, in etherHeaderData);

            /*var spanBuffer = PCapUtil.GenerateStandardPCapNgHeader();
            p._PCapEncapsulateEnhancedPacketBlock(0, 0, "Hello");
            spanBuffer.SeekToEnd();
            spanBuffer.Write(p.Span);

            Lfs.WriteDataToFile(@"c:\tmp\190604\test1.pcapng", spanBuffer.Span.ToArray(), FileOperationFlags.AutoCreateDirectory);*/

            using (PCapBuffer pcap = new PCapBuffer(new PCapFileEmitter(new PCapFileEmitterOptions(new FilePath(@"c:\tmp\190607\pcap1.pcapng", flags: FileFlags.AutoCreateDirectory),
                appendMode: false))))
            {
                pcap.WritePacket(ref p, 0, "");
                //pcap.WritePacket(p.Span, 0, "");

                Con.WriteLine($"{p.MemStat_NumRealloc}  {p.MemStat_PreAllocSize}  {p.MemStat_PostAllocSize}");
            }
        }

        public static unsafe void Test__()
        {
            Con.WriteLine(Unsafe.SizeOf<PacketParsed>());

            //var packetMem = Res.AppRoot["190527_novlan_simple_tcp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190527_novlan_simple_udp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190527_vlan_simple_tcp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190527_vlan_simple_udp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190531_vlan_pppoe_tcp.txt"].HexParsedBinary;
            //var packetMem = Res.AppRoot["190531_vlan_pppoe_udp.txt"].HexParsedBinary;
            var packetMem = Res.AppRoot["190531_vlan_pppoe_l2tp_tcp.txt"].HexParsedBinary;

            Packet packet = new Packet(default, packetMem._CloneSpan());

            PacketParsed parsed = new PacketParsed(ref packet);

            //Con.WriteLine(packet.Parsed.L2_TagVLan1.TagVlan.RefValueRead.VLanId);

            NoOp();
        }
    }

    partial class TestDevCommands
    {
        // Data vault server
        [ConsoleCommand]
        static void DataVaultServerTest(ConsoleService c, string cmdName, string str)
        {
            using (DataVaultServerApp server = new DataVaultServerApp())
            {
                Con.ReadLine("Exit>");
            }
        }

        [ConsoleCommand]
        static void DataVaultClientTest(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[arg]", null, null, null, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string serverHostname = vl.DefaultParam.StrValue._FilledOrDefault("127.0.0.1");

            PalSslClientAuthenticationOptions cliSsl = new PalSslClientAuthenticationOptions(false, null, "d471b9675b3d374d7af8828ab4276711c2a2c601");

            using (DataVaultClient client = new DataVaultClient(new DataVaultClientOptions(serverHostname, "3xvTXIkPJmYoNVVzoNHgDvzQpIyffE4z", cliSsl)))
            {
                CancellationTokenSource cts = new CancellationTokenSource();

                Task testTask = TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    await Task.CompletedTask;

                    string bigData = Str.MakeCharArray('x', 100_000);

                    for (int i = 0; i < 20; i++)
                    {
                        if (cts.IsCancellationRequested) return;

                        await client.WriteDataAsync(new DataVaultData
                        {
                            SystemName = "tcpip_test",
                            LogName = "alog",
                            KeyType = "by_server_ip",
                            KeyShortValue = "127.0.",
                            KeyFullValue = "127.0.0.1",
                            TimeStamp = DateTimeOffset.Now,
                            WithTime = false,
                            Data = new int[] { i, 1, 2, 3, 4, 5 },
                        }
                        );

                        //Dbg.Where();
                    }

                    await client.WriteCompleteAsync(new DataVaultData
                    {
                        SystemName = "tcpip_test",
                        LogName = "alog",
                        KeyType = "by_server_ip",
                        KeyShortValue = "127.0",
                        KeyFullValue = "127.0.0.1",
                        TimeStamp = DateTimeOffset.Now,
                        WithTime = false,
                        Data = new int[] { 0, 1, 2, 3, 4, 5 },
                    });
                });

                Con.ReadLine("Exit>");

                cts.Cancel();
                client._CancelSafeAsync();

                testTask._TryWait();
            }
        }


        // Log server
        [ConsoleCommand]
        static void LogServerTest(ConsoleService c, string cmdName, string str)
        {
            using (LogServerApp server = new LogServerApp())
            {
                Con.ReadLine("Exit>");
            }
        }

        [ConsoleCommand]
        static void LogClientTest(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[arg]", null, null, null, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string serverHostname = vl.DefaultParam.StrValue._FilledOrDefault("127.0.0.1");

            PalSslClientAuthenticationOptions cliSsl = new PalSslClientAuthenticationOptions(false, null, DevTools.TestSampleCert.HashSHA1);

            using (LogClient client = new LogClient(new LogClientOptions(null, cliSsl, serverHostname)))
            {
                CancellationTokenSource cts = new CancellationTokenSource();

                Task testTask = TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    await Task.CompletedTask;

                    string bigData = Str.MakeCharArray('x', 100_000);

                    for (int i = 0; ; i++)
                    {
                        if (cts.IsCancellationRequested) return;

                        client.WriteLog(new LogJsonData()
                        {
                            AppName = "App",
                            //Data = "Hello World " + i.ToString(),
                            Data = new { X = 123, Y = 456, Z = bigData },
                            Guid = Str.NewGuid(),
                            Kind = LogKind.Default,
                            MachineName = "Neko",
                            Priority = LogPriority.Info.ToString(),
                            Tag = "TagSan",
                            TimeStamp = DateTimeOffset.Now,
                            TypeName = "xyz"
                        }
                        );

                        //await Task.Delay(100);
                    }
                });

                Con.ReadLine("Exit>");

                cts.Cancel();

                testTask._TryWait();
            }
        }
    }
}


