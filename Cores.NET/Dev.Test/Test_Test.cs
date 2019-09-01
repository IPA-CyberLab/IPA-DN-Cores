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
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Diagnostics;

using IPA.Cores.Basic;
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

using Microsoft.Extensions.FileProviders;
using System.Web;
using System.Text;
using IPA.Cores.Basic.App.DaemonCenterLib;



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

                await response._SendStringContents(retStr, Consts.MimeTypes.OctetStream);
            }
            catch (Exception ex)
            {
                ex._Debug();
                throw;
            }
        }

        protected override void ConfigureImpl_BeforeHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
        {
            RouteBuilder rb = new RouteBuilder(app);

            rb.MapGet("/.well-known/acme-challenge/{token}", AcmeGetChallengeFileRequestHandler);

            IRouter router = rb.Build();
            app.UseRouter(router);
        }

        protected override void ConfigureImpl_AfterHelper(HttpServerStartupConfig cfg, IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime)
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
            TcpIpHostDataJsonSafe hostData = new TcpIpHostDataJsonSafe(getThisHostInfo: EnsureSpecial.Yes);

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

        public static void Test_Generic()
        {
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
                int port = Util.GenerateDynamicListenableTcpPortWithSeed("aaa")._Print();

                LocalNet.CreateListener(new TcpListenParam((l, s) =>
                {
                    return Task.CompletedTask;
                },
                port));

                ThreadObj.Sleep(Timeout.Infinite);
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

            if (false)
            {
                using (var fs = new ChrootFileSystem(new ChrootFileSystemParam(Lfs, @"D:\tmp\190724", FileSystemMode.ReadOnly)))
                {
                    using (var w = fs.CreateFileSystemEventWatcher("/"))
                    {
                        w.EventListeners.RegisterCallback((x, y, z) =>
                        {
                            Dbg.Where();
                        });

                        Con.ReadLine();
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

            }

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


