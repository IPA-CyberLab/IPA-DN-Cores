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
    public class Server : AsyncService
    {
        readonly RpcServer RpcServer;

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

                this.RpcServer = new RpcServer(cancel);

                this.JsonRpcServer = new JsonRpcHttpServer(this.RpcServer);
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
                this.RpcServer._DisposeSafe();

                // データベース
                this.HiveData._DisposeSafe();

                this.SingleInstance._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }

        public string AppAdd(AppSettings settings)
        {
            settings.Normalize();
            settings.CheckError();

            lock (DbLock)
            {
                string appId = Str.NewGuid();

                App app = new App
                {
                    Settings = settings,
                };

                if (Db.AppList.Values.Where(x => x != app && (IgnoreCaseTrim)x.Settings.AppName == settings.AppName).Any())
                {
                    throw new ApplicationException($"アプリケーション名 '{app.Settings.AppName}' が重複しています。");
                }

                Db.AppList.Add(appId, app);

                return appId;
            }
        }

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

        public App AppGet(string appId)
        {
            return DbSnapshot.AppList[appId];
        }

        public void AppSet(string appId, AppSettings settings)
        {
            settings.Normalize();
            settings.CheckError();

            lock (DbLock)
            {
                App app = Db.AppList[appId];

                if (Db.AppList.Values.Where(x => x != app && (IgnoreCaseTrim)x.Settings.AppName == settings.AppName).Any())
                {
                    throw new ApplicationException($"アプリケーション名 '{app.Settings.AppName}' が重複しています。");
                }

                app.Settings = settings;
            }
        }

        public IReadOnlyList<App> AppEnum()
        {
            return DbSnapshot.AppList.Values.ToArray();
        }
    }

    public class RpcServer : JsonRpcServerApi, IRpc
    {
        public RpcServer(CancellationToken cancel = default) : base(cancel)
        {
        }

        public async Task<ResponseMsg> KeepAlive(RequestMsg req)
        {
            var ret = new ResponseMsg();
            
            return ret;
        }

    }
}

#pragma warning restore CS1998
#endif

