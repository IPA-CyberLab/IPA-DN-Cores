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

#if CORES_BASIC_JSON && CORES_BASIC_WEBSERVER && CORES_BASIC_DAEMON
#pragma warning disable CS1998

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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.App.DaemonCenterLib;
using Microsoft.AspNetCore.Builder;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IPA.Cores.Basic.App.DaemonCenterLib
{
    // サーバー
    public class Server : JsonRpcServerApi, IRpc
    {
        readonly JsonRpcHttpServer JsonRpcServer;

        readonly SingleInstance SingleInstance;

        // Hive ベースのデータベース
        readonly HiveData<DbHive> HiveData;

        // データベースへのアクセスを容易にするための自動プロパティ
        CriticalSection DbLock => HiveData.DataLock;
        DbHive Db => HiveData.ManagedData;
        DbHive DbSnapshot => HiveData.GetManagedDataSnapshot();

        public Server(CancellationToken cancel = default) : base(cancel)
        {
            try
            {
                this.SingleInstance = new SingleInstance("Cores.Basic.App.DaemonCenter");

                // データベース
                this.HiveData = new HiveData<DbHive>(Hive.SharedLocalConfigHive, "DaemonCenterServer/Database",
                    getDefaultDataFunc: () => new DbHive(),
                    policy: HiveSyncPolicy.AutoReadWriteFile,
                    serializer: HiveSerializerSelection.RichJson);

                this.JsonRpcServer = new JsonRpcHttpServer(this);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public void RegisterRoutesToHttpServer(IApplicationBuilder appBuilder, string path = "/rpc")
        {
            this.JsonRpcServer.RegisterRoutesToHttpServer(appBuilder, path);
        }

        protected override void DisposeImpl(Exception ex)
        {
            try
            {
                // データベース
                this.HiveData._DisposeSafe();

                this.SingleInstance._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }

        public Preference GetPreference()
        {
            return DbSnapshot.Preference;
        }

        public void SetPreference(Preference preference)
        {
            preference.Normalize();

            lock (DbLock)
            {
                Db.Preference = preference;
            }
        }

        // App の追加
        public string AppAdd(AppSettings settings)
        {
            settings.Normalize();
            settings.Validate();

            lock (DbLock)
            {
                string appId = Str.NewFullId("APP");

                App app = new App
                {
                    Settings = settings,
                };

                if (Db.AppList.Values.Where(x => x != app && (IgnoreCaseTrim)x.Settings.AppName == settings.AppName).Any())
                {
                    throw new ApplicationException($"アプリケーション名 '{app.Settings.AppName}' が重複しています。");
                }

                Db.AppList.Add(appId, app);

                // デフォルト設定を保存
                Preference pref = this.GetPreference();
                pref.DefaultDeadIntervalSecs = settings.DeadIntervalSecs;
                pref.DefaultKeepAliveIntervalSecs = settings.KeepAliveIntervalSecs;
                pref.DefaultInstanceKeyType = settings.InstanceKeyType;
                pref.Normalize();
                this.SetPreference(pref);

                return appId;
            }
        }

        // App の削除
        public void AppDelete(string appId)
        {
            lock (DbLock)
            {
                if (Db.AppList.Remove(appId) == false)
                {
                    throw new KeyNotFoundException(nameof(appId));
                }
            }
        }

        // App の取得
        public App AppGet(string appId)
        {
            return DbSnapshot.AppList[appId];
        }

        // App の設定更新
        public void AppSet(string appId, AppSettings settings)
        {
            settings.Normalize();
            settings.Validate();

            lock (DbLock)
            {
                App app = Db.AppList[appId];

                if (Db.AppList.Values.Where(x => x != app && (IgnoreCaseTrim)x.Settings.AppName == settings.AppName).Any())
                {
                    throw new ApplicationException($"アプリケーション名 '{app.Settings.AppName}' が重複しています。");
                }

                // デフォルト設定を保存
                Preference pref = this.GetPreference();
                pref.DefaultDeadIntervalSecs = settings.DeadIntervalSecs;
                pref.DefaultKeepAliveIntervalSecs = settings.KeepAliveIntervalSecs;
                pref.DefaultInstanceKeyType = settings.InstanceKeyType;
                pref.Normalize();
                this.SetPreference(pref);

                app.Settings = settings;
            }
        }

        // App の列挙
        public IReadOnlyList<KeyValuePair<string, App>> AppEnum()
        {
            return DbSnapshot.AppList.ToArray();
        }


        //////////// 以下 RPC API の実装

        // Client からの KeepAlive の受信と対応処理
        public async Task<ResponseMsg> KeepAliveAsync(RequestMsg req)
        {
            lock (DbLock)
            {
                DateTimeOffset now = DateTimeOffset.Now;

                App app = Db.AppList[req.AppId];

                ResponseMsg ret = new ResponseMsg();

                // 応答メッセージの準備を開始
                // 次回の KeepAlive 間隔を指定
                ret.NextKeepAliveMsec = app.Settings.KeepAliveIntervalSecs * 1000;

                // インスタンスを検索
                Instance inst = app.InstanceList.Where(x => x.IsMatchForHost(app.Settings.InstanceKeyType, req.HostName, req.Guid)).SingleOrDefault();

                if (inst == null)
                {
                    // インスタンスがまだ無いので作成する
                    inst = new Instance
                    {
                        SrcIpAddress = this.ClientInfo.RemoteIP,
                        HostName = req.HostName,
                        Guid = req.Guid,

                        FirstAlive = now,
                        LastAlive = now,
                        LastCommitIdChanged = now,
                        LastInstanceArgumentsChanged = now,

                        LastStat = req.Stat,

                        // 初期 Commit ID および Arguments を書き込む
                        NextCommitId = app.Settings.DefaultCommitId._NonNullTrim(),
                        NextInstanceArguments = app.Settings.DefaultInstanceArgument._NonNullTrim(),
                    };

                    app.InstanceList.Add(inst);
                }
                else
                {
                    // すでにインスタンスが存在するので更新する
                    inst.SrcIpAddress = this.ClientInfo.RemoteIP;
                    inst.Guid = req.Guid;
                    inst.HostName = req.HostName;
                }

                // AcceptableIpList の生成
                HashSet<string> acceptableIpList = new HashSet<string>(StrComparer.IpAddressStrComparer);
                acceptableIpList.Add(this.ClientInfo.RemoteIP);
                req.Stat.GlobalIpList._DoForEach(x => acceptableIpList.Add(x));
                req.Stat.AcceptableIpList = acceptableIpList.ToArray();

                if ((IsFilled)req.Stat.CommitId && (IsFilled)inst.NextCommitId && (IgnoreCaseTrim)Str.NormalizeGitCommitId(inst.NextCommitId) != Str.NormalizeGitCommitId(req.Stat.CommitId))
                {
                    // クライアントから現在の CommitId が送付されてきて、
                    // インスタンス設定の Next Commit ID が指定されている場合で、
                    // 2 つの Commit ID の値が異なる場合は、
                    // クライアントに対して更新指示を返送する
                    ret.NextCommitId = Str.NormalizeGitCommitId(inst.NextCommitId);
                }
                
                if ((IsFilled)req.Stat.InstanceArguments && (IsFilled)inst.NextInstanceArguments && (Trim)inst.NextInstanceArguments != req.Stat.InstanceArguments)
                {
                    // クライアントから現在の InstanceArguments が送付されてきて、
                    // インスタンス設定の Next InstanceArguments が指定されている場合で、
                    // 2 つの InstanceArguments の値が異なる場合は、
                    // クライアントに対して更新指示を返送する
                    ret.NextInstanceArguments = inst.NextInstanceArguments._NonNullTrim();
                }

                // ステータスを更新する
                Dbg.Where();
                inst.LastAlive = now;
                inst.LastStat = req.Stat;
                inst.NumAlive++;

                return ret;
            }
        }
    }
}

#endif

