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
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic;

public static class EasyDnsTest
{
    public static void Test1()
    {
        EasyDnsResponderSettings st = new EasyDnsResponderSettings();

        {
            var zone = new EasyDnsResponderZone { DomainName = "test1.com" };

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "", Contents = "9.3.1.7" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "", Contents = "2001::8181" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "1.2.3.4" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "1.9.8.4" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "www", Contents = "2001::1234" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "ftp", Contents = "5.6.7.8" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.SOA, Contents = "ns1.test1.com nobori.softether.com 123 50 100 200 25" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "", Contents = "ns1.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "", Contents = "ns2.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain1", Contents = "ns3.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain1", Contents = "ns4.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain2.subdomain1", Contents = "ns5.ipa.go.jp" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.NS, Name = "subdomain2.subdomain1", Contents = "ns6.ipa.go.jp" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www.subdomain3", Contents = "8.9.4.5" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.kgb.abc123.subdomain5", Contents = "4.9.8.9" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.subdomain5", Contents = "5.9.6.3" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*subdomain6*", Contents = "6.7.8.9" });

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "*.subdomain7", Contents = "proc0001", Attribute = EasyDnsResponderRecordAttribute.DynamicRecord });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "*.subdomain7", Contents = "proc0002", Attribute = EasyDnsResponderRecordAttribute.DynamicRecord });

            st.ZoneList.Add(zone);
        }

        {
            var zone = new EasyDnsResponderZone { DomainName = "abc.test1.com" };

            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "", Contents = "1.8.1.8" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.A, Name = "www", Contents = "8.9.3.1" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "ftp", Contents = "2001::abcd" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.AAAA, Name = "aho.baka.manuke", Contents = "2001::abcd" });
            zone.RecordList.Add(new EasyDnsResponderRecord { Type = EasyDnsResponderRecordType.SOA, Contents = "ns2.test1.com daiyuu.softether.com 894 895 896 897 898" });

            st.ZoneList.Add(zone);
        }

        EasyDnsResponder r = new EasyDnsResponder();

        r.LoadSetting(st);

        r.Callback = (req) =>
        {
            switch (req.CallbackId)
            {
                case "proc0001":
                    var res1 = new EasyDnsResponderDynamicRecordCallbackResult();
                    res1.IPAddressList = new List<IPAddress>();
                    res1.IPAddressList.Add(IPAddress.Parse("1.0.0.1"));
                    res1.IPAddressList.Add(IPAddress.Parse("2.0.0.2"));
                    return res1;

                case "proc0002":
                    var res2 = new EasyDnsResponderDynamicRecordCallbackResult();
                    res2.IPAddressList = new List<IPAddress>();
                    res2.IPAddressList.Add(IPAddress.Parse("2001::1"));
                    res2.IPAddressList.Add(IPAddress.Parse("2001::2"));
                    return res2;
            }

            return null;
        };

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "9.3.1.7");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::8181");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns1.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns2.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain3.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList != null);
            Dbg.TestTrue(res.RecordList!.Count == 0);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain4.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList == null);
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "5.9.6.3");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "def456.abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "5.9.6.3");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "def456.kgb.abc123.subdomain5.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "4.9.8.9");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain6.test1.com" }, EasyDnsResponderRecordType.Any);
            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().Single().IPv4Address.ToString() == "6.7.8.9");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "1.2.3.4.subdomain7.test1.com" }, EasyDnsResponderRecordType.Any);
            res._PrintAsJson();

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "1.0.0.1");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(1).IPv4Address.ToString() == "2.0.0.2");

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::1");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(1).IPv6Address.ToString() == "2001::2");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 0);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns3.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns4.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "subdomain2.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain2.subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns5.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns6.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "xyz.abc.subdomain2.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain2.subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns5.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain2.subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns6.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "test123.subdomain1.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "subdomain1").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type != EasyDnsResponderRecordType.NS).Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS).Count() == 2);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(0).ServerName.ToString() == "ns3.ipa.go.jp.");
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "subdomain1").Where(x => x.Type == EasyDnsResponderRecordType.NS)
                .Cast<EasyDnsResponder.Record_NS>().OrderBy(x => x.ServerName.ToString(), StrCmpi).ElementAt(1).ServerName.ToString() == "ns4.ipa.go.jp.");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "www.subdomain3.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name != "www.subdomain3").Count() == 0);

            Dbg.TestTrue(res!.RecordList!.Where(x => x.Name == "www.subdomain3").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 1);
            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www.subdomain3").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "8.9.4.5");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "www.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.SOARecord.MasterName.ToString() == "ns1.test1.com.");
            Dbg.TestTrue(res.SOARecord.ResponsibleName.ToString() == "nobori.softether.com.");
            Dbg.TestTrue(res.SOARecord.SerialNumber == 123);
            Dbg.TestTrue(res.SOARecord.RefreshIntervalSecs == 50);
            Dbg.TestTrue(res.SOARecord.RetryIntervalSecs == 100);
            Dbg.TestTrue(res.SOARecord.ExpireIntervalSecs == 200);
            Dbg.TestTrue(res.SOARecord.NegativeCacheTtlSecs == 25);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name != "www").Count() == 0);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A).Count() == 2);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(0).IPv4Address.ToString() == "1.2.3.4");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.A)
                .Cast<EasyDnsResponder.Record_A>().OrderBy(x => x.IPv4Address, IpComparer.Comparer).ElementAt(1).IPv4Address.ToString() == "1.9.8.4");

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.AAAA).Count() == 1);

            Dbg.TestTrue(res.RecordList!.Where(x => x.Name == "www").Where(x => x.Type == EasyDnsResponderRecordType.AAAA)
                .Cast<EasyDnsResponder.Record_AAAA>().OrderBy(x => x.IPv6Address, IpComparer.Comparer).ElementAt(0).IPv6Address.ToString() == "2001::1234");
        }

        {
            var res = r.Query(new EasyDnsResponder.SearchRequest { FqdnNormalized = "abc.test1.com" }, EasyDnsResponderRecordType.Any);

            Dbg.TestTrue(res!.SOARecord.MasterName.ToString() == "ns2.test1.com.");
            Dbg.TestTrue(res.SOARecord.ResponsibleName.ToString() == "daiyuu.softether.com.");
            Dbg.TestTrue(res.SOARecord.SerialNumber == 894);
            Dbg.TestTrue(res.SOARecord.RefreshIntervalSecs == 895);
            Dbg.TestTrue(res.SOARecord.RetryIntervalSecs == 896);
            Dbg.TestTrue(res.SOARecord.ExpireIntervalSecs == 897);
            Dbg.TestTrue(res.SOARecord.NegativeCacheTtlSecs == 898);

            Dbg.TestTrue(((EasyDnsResponder.Record_A)res.RecordList!.Single(x => x.Type == EasyDnsResponderRecordType.A)).IPv4Address.ToString() == "1.8.1.8");
        }

    }
}


public class EasyDnsResponderBasedDnsServerSettings
{
    public int UdpPort { init; get; }
}

public class EasyDnsResponderBasedDnsServer : AsyncService
{
    public EasyDnsServer DnsServer { get; }
    public EasyDnsResponder DnsResponder { get; }

    public EasyDnsResponderBasedDnsServer(EasyDnsResponderBasedDnsServerSettings settings)
    {
        try
        {
            this.DnsResponder = new EasyDnsResponder();
            this.DnsServer = new EasyDnsServer(new EasyDnsServerSetting(this.DnsQueryResponseCallback, settings.UdpPort));
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            await this.DnsServer._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public void LoadSetting(EasyDnsResponderSettings setting)
        => this.DnsResponder.LoadSetting(setting);

    // DNS サーバーから呼ばれるコールバック関数。ここでクエリに対する応答を作る。
    List<DnsUdpPacket> DnsQueryResponseCallback(EasyDnsServer svr, List<DnsUdpPacket> requestPackets)
    {
        List<DnsUdpPacket> responsePackets = new List<DnsUdpPacket>(requestPackets.Count);

        foreach (var request in requestPackets)
        {
            try
            {
                var res = RequestPacketToResponsePacket(request);

                if (res != null)
                {
                    responsePackets.Add(res);
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        return responsePackets;
    }

    // ある 1 つの DNS リクエストパケットに対して DNS レスポンスパケットを作る関数
    DnsUdpPacket? RequestPacketToResponsePacket(DnsUdpPacket request)
    {
        DnsMessage? q = request.Message as DnsMessage;

        if (q == null) return null;

        DnsMessage? r = null;

        try
        {
            r = QueryToResponse(q);
        }
        catch (Exception ex)
        {
            r = q;
            r.IsQuery = false;
            r.ReturnCode = ReturnCode.ServerFailure;
            ex._Debug();
        }

        if (r == null) return null;

        DnsUdpPacket response = new DnsUdpPacket(request.RemoteEndPoint, request.LocalEndPoint, r);

        return response;
    }

    // ある 1 つの DNS クエリメッセージに対して応答メッセージを作る関数
    // CPU 時間の節約のため、届いたクエリメッセージ構造体をそのまま応答メッセージ構造体として書き換えて応答する
    DnsMessage? QueryToResponse(DnsMessage? q)
    {
        if (q == null || q.IsQuery == false) return null;

        q.IsQuery = false;
        q.ReturnCode = ReturnCode.NoError;

        if (q.Questions.Count == 0)
        {
            // 質問が付いていない
            q.ReturnCode = ReturnCode.FormatError;
            return q;
        }

        if (q.Questions.Count >= 2)
        {
            // DNS クエリが複数付いている場合でも、最初の 1 個目だけに応答する。
            q.Questions.RemoveRange(1, q.Questions.Count - 1);
        }

        var question = q.Questions[0];
        string questionFqdn = question.Name.ToNormalizedFqdnFast();
        if (questionFqdn._IsEmpty())
        {
            // 質問の FQDN 名が不正である
            q.ReturnCode = ReturnCode.FormatError;
            return q;
        }

        var questionType = DnsUtil.DnsLibRecordTypeToEasyDnsResponderRecordType(question.RecordType);

        try
        {
            var searchRequest = new EasyDnsResponder.SearchRequest
            {
                FqdnNormalized = questionFqdn,
            };

            var searchResponse = this.DnsResponder.Query(searchRequest, questionType);

            if (searchResponse == null)
            {
                // Zone 不存在。Refuse する。
                q.ReturnCode = ReturnCode.Refused;
                return q;
            }

            if (searchResponse.ResultFlags.Bit(EasyDnsResponder.SearchResultFlags.NotFound) || searchResponse.RecordList == null)
            {
                // レコード不存在
                q.ReturnCode = ReturnCode.NxDomain;
                return q;
            }

            List<DnsRecordBase> answersList = new List<DnsRecordBase>(searchResponse.RecordList.Count);

            foreach (var ans in searchResponse.RecordList)
            {
                var a = ans.ToDnsLibRecordBase(question);
                if (a != null)
                {
                    answersList.Add(a);
                }
            }

            if (searchResponse.ResultFlags.Bit(EasyDnsResponder.SearchResultFlags.SubDomainIsDelegated))
            {
                // 他サブドメインへの委譲
                q.AuthorityRecords = answersList;
                q.IsAuthoritiveAnswer = false;
            }
            else
            {
                // 権威ある回答
                q.AnswerRecords = answersList;
                q.IsAuthoritiveAnswer = true;

                // 回答権威者の SOA レコード
                q.AuthorityRecords.Add(searchResponse.SOARecord.ToDnsLibRecordBase(searchResponse.ZoneDomainName));
            }
        }
        catch
        {
            q.ReturnCode = ReturnCode.ServerFailure;
        }

        return q;
    }
}


#endif

