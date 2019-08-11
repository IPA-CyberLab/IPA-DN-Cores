// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// DNS および証明書関係ユーティリティ

#if CORES_BASIC_JSON

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

using System.Net;
using System.Net.Sockets;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic.AppLib
{
    // 大解説書
    // 
    // 1. DNS ゾーンファイルを入力してユニークな FQDN レコードの一覧を出力する
    //  by DnsFlatten
    // 
    // ↓ ↓ ↓
    // 
    // 2. FQDN の一覧を入力して FQDN と IP アドレスのペアの一覧を出力する
    //  by DnsIpPairGenerator
    // 
    // ↓ ↓ ↓
    // 
    // 3. FQDN と IP アドレスのペアの一覧を入力して SSL 証明書一覧を出力する
    //  by SslCertCollector


    // 3. FQDN と IP アドレスのペアの一覧を入力して SSL 証明書一覧を出力する
    public class SslCertCollector
    {
        public int MaxConcurrentTasks { get; }
        public TcpIpSystem TcpIp { get; }

        ConcurrentQueue<SslCertEntry> Queue;

        ConcurrentBag<SslCertEntry> ResultList = new ConcurrentBag<SslCertEntry>();

        public SslCertCollector(int maxConcurrentTasks, IEnumerable<DnsIpPair> pairs, TcpIpSystem tcpIp = null)
        {
            this.TcpIp = tcpIp ?? LocalNet;

            this.MaxConcurrentTasks = maxConcurrentTasks._Max(1);

            // 入力リストを整理する
            // (SNI + IP アドレス のほか、IP アドレス + IP アドレスも追加する)
            HashSet<DnsIpPair> tmpList = new HashSet<DnsIpPair>(pairs.Distinct());

            Queue = new ConcurrentQueue<SslCertEntry>();

            foreach (DnsIpPair pair in pairs)
            {
                SslCertEntry e1 = new SslCertEntry
                {
                    FriendName = pair.SniHostName,
                    SniHostName = pair.SniHostName,
                    IpAddress = pair.IpAddress,
                };

                Queue.Enqueue(e1);

                SslCertEntry e2 = new SslCertEntry
                {
                    FriendName = pair.SniHostName,
                    SniHostName = pair.IpAddress,
                    IpAddress = pair.IpAddress,
                };

                Queue.Enqueue(e2);
            }
        }

        Once StartFlag;
        public async Task<IReadOnlyList<SslCertEntry>> ExecuteAsync(CancellationToken cancel = default)
        {
            int totalCount = Queue.Count;

            StartFlag.FirstCallOrThrowException();

            List<Task> taskList = new List<Task>();

            for (int i = 0; i < this.MaxConcurrentTasks; i++)
            {
                Task t = WorkerTaskAsync(cancel);

                taskList.Add(t);
            }

            CancellationTokenSource done = new CancellationTokenSource();

            TaskUtil.StartAsyncTaskAsync(async () =>
            {
                CancellationToken doneCancel = done.Token;

                while (doneCancel.IsCancellationRequested == false)
                {
                    await doneCancel._WaitUntilCanceledAsync(250);

                    int completed = totalCount - Queue.Count;

                    Con.WriteLine("{0} / {1}", completed._ToString3(), totalCount._ToString3());
                }
            })._LaissezFaire(true);

            foreach (Task t in taskList)
            {
                await t._TryWaitAsync(true);
            }

            done._TryCancelNoBlock();

            return ResultList.ToList();
        }

        // ワーカー疑似スレッド
        async Task WorkerTaskAsync(CancellationToken cancel = default)
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                // キューから 1 つ取ります
                if (Queue.TryDequeue(out SslCertEntry e) == false)
                {
                    // もう ありません
                    return;
                }

                foreach (int port in Consts.Ports.PotentialHttpsPorts)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        // 処理を いたします
                        // 3 回トライする
                        try
                        {
                            await PerformOneAsync(e, port, cancel);
                            break;
                        }
                        catch (Exception ex)
                        {
                            $"Error: {e.SniHostName}:{port} => {ex.Message}"._Print();
                        }
                    }
                }
            }
        }

        // 1 つの SSL 接続試行を処理する非同期関数
        async Task PerformOneAsync(SslCertEntry e, int port, CancellationToken cancel = default)
        {
            using (ConnSock sock = await TcpIp.ConnectAsync(new TcpConnectParam(IPAddress.Parse(e.IpAddress), port, connectTimeout: 5000), cancel))
            {
                using (SslSock ssl = await sock.SslStartClientAsync(new PalSslClientAuthenticationOptions(e.SniHostName, true)))
                {
                    ILayerInfoSsl sslInfo = ssl.Info.Ssl;
                    PalX509Certificate cert = sslInfo.RemoteCertificate;

                    Certificate cert2 = cert.PkiCertificate;

                    e.CertIssuer = cert2.CertData.IssuerDN.ToString();
                    e.CertSubject = cert2.CertData.SubjectDN.ToString();
                    e.CertFqdnList = cert2.HostNameList.Select(x => x.HostName)._Combine(", ");
                    e.CertHashSha1 = cert2.DigestSHA1Str;
                    e.CertNotAfter = cert2.CertData.NotAfter;
                    e.CertNotBefore = cert2.CertData.NotBefore;

                    this.ResultList.Add(e);

                    $"OK: {e.SniHostName}:{port} => {e._ObjectToJson(compact: true)}"._Print();
                }
            }
        }
    }

    public class SslCertEntry
    {
        public string FriendName;
        public string SniHostName;
        public string IpAddress;

        public string CertHashSha1;
        public DateTime CertNotBefore;
        public DateTime CertNotAfter;
        public string CertIssuer;
        public string CertSubject;
        public string CertFqdnList;
    }

    // 2. FQDN の一覧を入力して FQDN、IP アドレス、ポート番号のペアの一覧を出力する
    public class DnsIpPairGenerator
    {
        public int MaxConcurrentTasks { get; }
        public TcpIpSystem TcpIp { get; }

        ConcurrentQueue<string> FqdnQueue;

        ConcurrentBag<DnsIpPair> ResultList = new ConcurrentBag<DnsIpPair>();

        public DnsIpPairGenerator(int maxConcurrentTasks, IEnumerable<string> fqdnSet, TcpIpSystem tcpIp = null)
        {
            this.TcpIp = tcpIp ?? LocalNet;

            this.MaxConcurrentTasks = maxConcurrentTasks._Max(1);

            // ひとまずソート
            List<string> fqdnList = fqdnSet.Distinct(StrComparer.IgnoreCaseComparer).OrderBy(x => x, StrComparer.IgnoreCaseComparer).ToList();

            // キューに入れる
            FqdnQueue = new ConcurrentQueue<string>(fqdnList);
        }

        Once StartFlag;
        public async Task<IReadOnlyList<DnsIpPair>> ExecuteAsync(CancellationToken cancel = default)
        {
            StartFlag.FirstCallOrThrowException();

            List<Task> taskList = new List<Task>();

            for (int i = 0; i < this.MaxConcurrentTasks; i++)
            {
                Task t = WorkerTaskAsync(cancel);

                taskList.Add(t);
            }

            foreach (Task t in taskList)
            {
                await t._TryWaitAsync(true);
            }

            return ResultList.ToList();
        }

        // ワーカー疑似スレッド
        async Task WorkerTaskAsync(CancellationToken cancel = default)
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                // キューから 1 つ取ります
                if (FqdnQueue.TryDequeue(out string fqdn) == false)
                {
                    // もう ありません
                    return;
                }

                for (int i = 0; i < 3; i++)
                {
                    // 処理を いたします
                    // 3 回トライする
                    try
                    {
                        await PerformOneAsync(fqdn, cancel);
                        break;
                    }
                    catch { }
                }
            }
        }

        // 1 つの FQDN レコードを処理する非同期関数
        async Task PerformOneAsync(string fqdn, CancellationToken cancel = default)
        {
            // 名前解決を いたします
            DnsResponse response = await TcpIp.QueryDnsAsync(new DnsGetIpQueryParam(fqdn, DnsQueryOptions.Default, 5000), cancel);

            IEnumerable<IPAddress> addressList = response.IPAddressList.Where(x => x.AddressFamily.EqualsAny(AddressFamily.InterNetwork, AddressFamily.InterNetworkV6));

            addressList._DoForEach(addr =>
            {
                $"{fqdn} => {addr}"._Print();
                ResultList.Add(new DnsIpPair { SniHostName = fqdn, IpAddress = addr.ToString() });
            });
        }
    }

    public class DnsIpPair
    {
        const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

        public string SniHostName;
        public string IpAddress;

        public override int GetHashCode()
            => System.HashCode.Combine(SniHostName.GetHashCode(Comparison), IpAddress.GetHashCode(Comparison));

        public override bool Equals(object obj)
            => this.SniHostName.Equals(((DnsIpPair)obj).SniHostName, Comparison) && this.IpAddress.Equals(((DnsIpPair)obj).IpAddress, Comparison);
    }

    // 1. DNS ゾーンファイルを入力してユニークな FQDN レコードの一覧を出力する
    public class DnsFlatten
    {
        HashSet<string> FqdnSetInternal = new HashSet<string>(StrComparer.IgnoreCaseComparer);

        public IReadOnlyCollection<string> FqdnSet => FqdnSetInternal;

        public void InputZoneFile(string domainName, ReadOnlySpan<byte> fileData, Encoding encoding = null)
        {
            encoding = encoding ?? Str.Utf8Encoding;

            string[] lines = Str.GetLines(fileData._GetString(encoding));

            foreach (string line in lines)
            {
                string line2 = line._StripCommentFromLine();

                string[] tokens = line2._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');

                //if (tokens.Length >= 4 && (IgnoreCase)tokens[2] == "SOA")
                //{
                //    string tmp = tokens[3].Trim();
                //    if (tmp.EndsWith("."))
                //    {
                //        currentDomainName = tmp;
                //    }
                //}

                //if (tokens.Length >= 5 && (IgnoreCase)tokens[3] == "SOA")
                //{
                //    string tmp = tokens[4].Trim();
                //    if (tmp.EndsWith("."))
                //    {
                //        currentDomainName = tmp;
                //    }
                //}

                if (tokens.Length >= 3)
                {
                    string hostName = tokens[0];
                    string type = tokens[tokens.Length - 2].ToUpper();

                    if (type == "A" || type == "AAAA" || type == "CNAME")
                    {
                        bool isFull = hostName.EndsWith(".");

                        string fqdn;

                        if (isFull == false)
                        {
                            if (domainName._IsEmpty())
                            {
                                throw new ApplicationException("currentDomainName == null");
                            }

                            if (hostName == "@")
                            {
                                fqdn = domainName;
                            }
                            else
                            {
                                fqdn = hostName + "." + domainName;
                            }
                        }
                        else
                        {
                            fqdn = hostName;
                        }

                        if (fqdn.StartsWith("*."))
                        {
                            fqdn = "_asterisk." + fqdn.Substring(2);
                        }

                        InputFqdn(fqdn);
                    }
                }
            }
        }

        public void InputFqdn(string fqdn)
        {
            fqdn = fqdn._NonNullTrim();
            if (fqdn.EndsWith(".")) fqdn = fqdn.Substring(0, fqdn.Length - 1);
            if (fqdn._IsEmpty()) return;

            FqdnSetInternal.Add(fqdn);
        }
    }
}

#endif

