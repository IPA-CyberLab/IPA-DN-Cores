﻿// IPA Cores.NET
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
// Description

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
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
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using IPA.Cores.Basic.DnsLib;

namespace IPA.Cores.Basic;


public class EasyDnsResponderBasedDnsServerSettings
{
    public int UdpPort { init; get; }
}

public class EasyDnsResponderBasedDnsServer : AsyncService
{
    public EasyDnsServer DnsServer { get; }
    public EasyDnsResponder DnsResponder { get; }
    public DateTimeOffset LastDatabaseHealtyTimeStamp { get; set; }

    public bool SaveAccessLogForDebug { get; private set; }
    public bool CopyQueryAdditionalRecordsToResponse { get; set; } = false;

    public EasyDnsResponderBasedDnsServer(EasyDnsResponderBasedDnsServerSettings settings)
    {
        try
        {
            this.LastDatabaseHealtyTimeStamp = DnsUtil.DnsDtStartDay;
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

    public void ApplySetting(EasyDnsResponderSettings setting)
    {
        this.DnsResponder.ApplySetting(setting);

        this.SaveAccessLogForDebug = setting.SaveAccessLogForDebug;
        this.CopyQueryAdditionalRecordsToResponse = setting.CopyQueryAdditionalRecordsToResponse;
    }

    public class DnsAccessLog
    {
        public DnsUdpPacket? RequestPacket;
        public DnsUdpPacket? ResponsePacket;
        public string TookSeconds = "";
    }

    // DNS サーバーから呼ばれるコールバック関数。ここでクエリに対する応答を作る。
    List<DnsUdpPacket> DnsQueryResponseCallback(EasyDnsServer svr, List<DnsUdpPacket> requestPackets)
    {
        List<DnsUdpPacket> responsePackets = new List<DnsUdpPacket>(requestPackets.Count);

        bool debug = this.SaveAccessLogForDebug;

        foreach (var request in requestPackets)
        {
            try
            {
                long startTick = 0;
                long endTick = 0;
                DnsUdpPacket? requestCopy = null;

                if (debug)
                {
                    startTick = Time.NowHighResLong100Usecs;
                    requestCopy = request._CloneDeep();
                }

                var response = RequestPacketToResponsePacket(request, out var alternativeSendPacketsList);

                if (debug)
                {
                    endTick = Time.NowHighResLong100Usecs;

                    long timespan = endTick - startTick;

                    DnsAccessLog log = new DnsAccessLog
                    {
                        TookSeconds = ((double)timespan / 10000000.0).ToString("F9"),
                        RequestPacket = requestCopy,
                        ResponsePacket = response,
                    };

                    log._PostAccessLog("DDnsAccessLogForDebug");
                }

                if (response != null)
                {
                    responsePackets.Add(response);
                }

                if (alternativeSendPacketsList != null)
                {
                    responsePackets.AddRange(alternativeSendPacketsList);
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
    DnsUdpPacket? RequestPacketToResponsePacket(DnsUdpPacket request, out List<DnsUdpPacket>? alternativeSendPacketsList)
    {
        alternativeSendPacketsList = null;
        DnsMessage? q = request.Message as DnsMessage;

        if (q == null) return null;

        DnsMessage? r = null;

        try
        {
            r = QueryToResponse(q, request, out alternativeSendPacketsList);
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
    DnsMessage? QueryToResponse(DnsMessage? q, DnsUdpPacket requestPacket, out List<DnsUdpPacket>? alternativeSendPacketsList)
    {
        alternativeSendPacketsList = null;

        if (q == null) return null;
        if (q.IsQuery == false)
        {
            // フォワーダからの戻りパケットの処理
            var responsePacket = this.DnsResponder.TryProcessForwarderResponse(requestPacket);
            if (responsePacket != null)
            {
                alternativeSendPacketsList = responsePacket._SingleList();
                return null;
            }
        }

        q.ReturnCode = ReturnCode.NoError;

        if (this.CopyQueryAdditionalRecordsToResponse == false)
        {
            // クエリに付いてきた Additional Records を削除 (dnsdist キャッシュ対策)
            q.AdditionalRecords.Clear();
        }

        if (q.Questions.Count == 0)
        {
            // 質問が付いていない
            q.IsQuery = false;
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
            q.IsQuery = false;
            q.ReturnCode = ReturnCode.FormatError;
            return q;
        }

        var questionType = DnsUtil.DnsLibRecordTypeToEasyDnsResponderRecordType(question.RecordType);

        try
        {
            var searchRequest = new EasyDnsResponder.SearchRequest
            {
                FqdnNormalized = questionFqdn,
                RequestPacket = requestPacket,
            };

            var searchResponse = this.DnsResponder.Query(searchRequest, questionType);

            if (searchResponse == null)
            {
                // Zone 不存在。Refuse する。
                q.ReturnCode = ReturnCode.Refused;
                q.IsQuery = false;
                return q;
            }

            if (searchResponse.AlternativeSendPackets != null)
            {
                // 代替パケットを応答する (フォワーダ等が生成したパケットである)
                alternativeSendPacketsList = searchResponse.AlternativeSendPackets;
                return null;
            }

            q.IsQuery = false;

            if (searchResponse.RaiseCustomError != ReturnCode.NoError)
            {
                // カスタムエラー
                q.ReturnCode = searchResponse.RaiseCustomError;
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
                var a = ans.ToDnsLibRecordBase(question, this.LastDatabaseHealtyTimeStamp);
                if (a != null)
                {
                    answersList.Add(a);
                }
            }

            if (answersList.Count >= 2)
            {
                answersList = answersList._Shuffle().ToList();
            }

            if (searchResponse.ResultFlags.Bit(EasyDnsResponder.SearchResultFlags.SubDomainIsDelegated))
            {
                string targetSubDomainFqdn = "";
                if (searchResponse.RecordList[0].Name._IsFilled())
                {
                    targetSubDomainFqdn = searchResponse.RecordList[0].Name + ".";
                }
                targetSubDomainFqdn += searchResponse.RecordList[0].ParentZone.DomainFqdn;

                var targetSubDomainFqdnParsed = DomainName.Parse(targetSubDomainFqdn);

                // 他サブドメインへの委譲
                foreach (var ans in answersList)
                {
                    ans.Name = targetSubDomainFqdnParsed;
                }

                // Glue レコードの追記
                HashSet<string> glueRecordDistinctHash = new HashSet<string>();
                foreach (var rec in searchResponse.RecordList)
                {
                    if (rec is EasyDnsResponder.Record_NS ns)
                    {
                        foreach (var glue in ns.GlueRecordList)
                        {
                            string test = glue.Name + "." + glue.ParentZone.DomainFqdn + " = " + glue.ToStringForCompare();

                            if (glueRecordDistinctHash.Contains(test) == false)
                            {
                                glueRecordDistinctHash.Add(test);

                                var r = glue.ToDnsLibRecordBase(DomainName.Parse(glue.Name + "." + glue.ParentZone.DomainFqdn), this.LastDatabaseHealtyTimeStamp);

                                q.AdditionalRecords.Add(r);
                            }
                        }
                    }
                }

                if (q.AdditionalRecords.Count >= 1)
                {
                    q.AdditionalRecords = q.AdditionalRecords._Shuffle().ToList();
                }

                q.AuthorityRecords = answersList;
                q.IsAuthoritiveAnswer = false;
            }
            else
            {
                // 権威ある回答
                q.AnswerRecords = answersList;
                q.IsAuthoritiveAnswer = true;

                // 回答権威者の SOA レコード
                q.AuthorityRecords.Add(searchResponse.SOARecord.ToDnsLibRecordBase(searchResponse.ZoneDomainName, this.LastDatabaseHealtyTimeStamp));
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

