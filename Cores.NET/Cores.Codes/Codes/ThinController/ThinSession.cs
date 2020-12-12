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
// Description

#if CORES_CODES_THINCONTROLLER

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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

using IPA.Cores.Web;
using IPA.Cores.Helper.Web;
using static IPA.Cores.Globals.Web;

using static IPA.App.ThinControllerApp.AppGlobal;

namespace IPA.Cores.Codes
{
    public class ThinSession : IValidatable, INormalizable
    {
        public string Msid = "";
        public string SessionId = "";
        public DateTime EstablishedDateTime = Util.ZeroDateTimeValue;
        public string IpAddress = "";
        public string HostName = "";
        public int NumClients;
        public long ServerMask64;
        public int NumClientsUnique;

        public void Normalize()
        {
            Msid = Msid._NonNull().ToUpper();
            SessionId = SessionId._NonNull();
            IpAddress = IpAddress._NonNull();
            HostName = HostName._NonNull();
        }

        public void Validate()
        {
            if (Msid._IsNullOrZeroLen()) throw new CoresLibException("Msid._IsNullOrZeroLen()");
            if (SessionId.Length != 40) throw new CoresLibException("SessionId.Length != 40");
        }
    }

    public class ThinGate : IValidatable, INormalizable
    {
        public string GateId = "";
        public string IpAddress = "";
        public string HostName = "";
        public int Performance;
        public int NumSessions;
        public int Build;
        public string MacAddress = "";
        public string OsInfo = "";
        public DateTime EstablishedDateTime = Util.ZeroDateTimeValue;
        public DateTime LastCommDateTime = Util.ZeroDateTimeValue;
        public DateTime Expires = Util.ZeroDateTimeValue;
        public long NumComm;

        public ImmutableDictionary<string, ThinSession> SessionTable = ImmutableDictionary<string, ThinSession>.Empty;

        public void Normalize()
        {
            GateId = GateId._NonNull().ToUpper();
            IpAddress = IpAddress._NonNull();
            HostName = HostName._NonNull();
            MacAddress = MacAddress._NonNull();
            OsInfo = OsInfo._NonNull();
        }

        public void Validate()
        {
            if (GateId.Length != 40) throw new CoresLibException("GateId.Length != 40");
            IpAddress._NotEmptyCheck(nameof(IpAddress));
        }
    }

    public class ThinGateDb
    {
        public ImmutableDictionary<string, ThinGate> GateTable = ImmutableDictionary<string, ThinGate>.Empty;

        public void UpdateGateAndDeleteSession(DateTime now, DateTime expires, ThinGate gate, ThinSession session)
        {
            gate.Normalize();
            gate.Validate();
            session.Normalize();
            session.Validate();

            if (this.GateTable.TryGetValue(gate.GateId, out ThinGate? currentGate))
            {
                // Gate が存在するかどうかチェック。存在しない場合は、単発セッション削除指令は無視する
                Interlocked.Increment(ref currentGate.NumComm);

                if (currentGate.LastCommDateTime < now) currentGate.LastCommDateTime = now;
                if (currentGate.Expires < expires) currentGate.Expires = expires;

                currentGate.NumSessions = Math.Max(currentGate.NumSessions - 1, 0);

                // MSID でセッションを検索
                if (currentGate.SessionTable.TryGetValue(session.Msid, out ThinSession? existSession))
                {
                    // セッション ID が一致する場合のみ削除する
                    ImmutableInterlocked.TryRemove(ref currentGate.SessionTable, existSession.Msid, out _);
                }
            }

            // 古い Gate を削除
            DeleteOldGate(now, gate);
        }

        public void UpdateGateAndAddSession(DateTime now, DateTime expires, ThinGate gate, ThinSession session)
        {
            gate.Normalize();
            gate.Validate();
            session.Normalize();
            session.Validate();

            if (this.GateTable.TryGetValue(gate.GateId, out ThinGate? currentGate))
            {
                // Gate が存在するかどうかチェック。存在しない場合は、単発セッション追加指令は無視する
                Interlocked.Increment(ref currentGate.NumComm);

                if (currentGate.LastCommDateTime < now) currentGate.LastCommDateTime = now;
                if (currentGate.Expires < expires) currentGate.Expires = expires;

                ImmutableInterlocked.AddOrUpdate(ref currentGate.SessionTable, session.Msid,
                    addValueFactory: msid =>
                    {
                        currentGate.NumSessions++;
                        return session;
                    },
                    updateValueFactory: (msid, existSession) =>
                    {
                        // 同じ MSID のセッションがすでに存在する場合、AddSession で追加されようとしているセッション
                        // のほうが新しい場合は置き換える。そうでない場合は何もしない。
                        if (session.EstablishedDateTime >= existSession.EstablishedDateTime)
                        {
                            return session;
                        }
                        else
                        {
                            return existSession;
                        }
                    });
            }

            // 古い Gate を削除
            DeleteOldGate(now, gate);
        }

        public void UpdateGateAndReportSessions(DateTime now, DateTime expires, ThinGate gate, IEnumerable<ThinSession> sessionList)
        {
            gate.Normalize();
            gate.Validate();
            sessionList._DoForEach(x => { x.Normalize(); x.Validate(); });

            Dictionary<string, ThinSession> newSessionDictionary = new Dictionary<string, ThinSession>();

            foreach (ThinSession sess in sessionList)
            {
                if (newSessionDictionary.TryGetValue(sess.Msid, out ThinSession? current) == false ||
                    sess.EstablishedDateTime >= current.EstablishedDateTime)
                {
                    // MSID が重複している場合は、EstablishedDateTime が最も最近のもの 1 つだけを入れる
                    newSessionDictionary[sess.Msid] = sess;
                }
            }

            gate.SessionTable = gate.SessionTable.AddRange(newSessionDictionary);
            gate.NumSessions = gate.SessionTable.Count;

            ImmutableInterlocked.AddOrUpdate(ref this.GateTable, gate.GateId,
                addValueFactory: gateId =>
                {
                    gate.NumComm = 1;
                    return gate;
                },
                updateValueFactory: (gateId, current) =>
                {
                    gate.NumComm = current.NumComm + 1;
                    return gate;
                });

            // 古い Gate を削除
            DeleteOldGate(now, gate);
        }

        // 古い Gate を削除
        public void DeleteOldGate(DateTime now, ThinGate? latestGate = null)
        {
            var currentTable = this.GateTable;

            // 有効期限切れの Gate を削除する
            foreach (var item in currentTable)
            {
                if (now > item.Value.Expires)
                {
                    ImmutableInterlocked.TryRemove(ref this.GateTable, item.Key, out _);
                }
            }

            // 同一の IP Address を持った古い Gate が存在している場合はこれを削除する
            if (latestGate != null)
            {
                foreach (var item in currentTable)
                {
                    if (latestGate.IpAddress._IsSamei(item.Value.IpAddress) && item.Key != latestGate.GateId)
                    {
                        ImmutableInterlocked.TryRemove(ref this.GateTable, item.Key, out _);
                    }
                }
            }
        }
    }
}

#endif

