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
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
using Newtonsoft.Json;

namespace IPA.Cores.Codes
{
    public class ThinDbSvc
    {
        [EasyManualKey]
        public string SVC_NAME { get; set; } = "";
        public string SVC_TITLE { get; set; } = "";
    }

    public class ThinDbMachine
    {
        public int MACHINE_ID { get; set; }
        public string SVC_NAME { get; set; } = "";
        [EasyManualKey]
        public string MSID { get; set; } = "";
        public string PCID { get; set; } = "";
        public byte[] CERT { get; set; } = new byte[0];
        public string CERT_HASH { get; set; } = "";
        public string HOST_SECRET { get; set; } = "";
        public string HOST_SECRET2 { get; set; } = "";
        public DateTime CREATE_DATE { get; set; } = Util.ZeroDateTimeValue;
        public DateTime UPDATE_DATE { get; set; } = Util.ZeroDateTimeValue;
        public DateTime LAST_SERVER_DATE { get; set; } = Util.ZeroDateTimeValue;
        public DateTime LAST_CLIENT_DATE { get; set; } = Util.ZeroDateTimeValue;
        public int NUM_SERVER { get; set; }
        public int NUM_CLIENT { get; set; }
        public string CREATE_IP { get; set; } = "";
        public string CREATE_HOST { get; set; } = "";
        public string LAST_IP { get; set; } = "";
        public string REAL_PROXY_IP { get; set; } = "";
        public string LAST_FLAG { get; set; } = "";
        public int SE_LANGUAGE { get; set; }
        public bool RESET_CERT_FLAG { get; set; }
        public bool FLAG_BETA2MSG { get; set; }
        public DateTime FIRST_CLIENT_DATE { get; set; } = Util.ZeroDateTimeValue;
        public DateTime EXPIRE { get; set; } = Util.ZeroDateTimeValue;
        public int MAX_CLIENTS { get; set; }
        public bool MAC_ENABLE { get; set; }
        public int NUM_MACCLIENT { get; set; }
        public string WOL_MACLIST { get; set; } = "";
        public long SERVERMASK64 { get; set; }
    }

    public class ThinMemoryDb
    {
        // データベースからもらってきたデータ
        public List<ThinDbSvc> SvcList = new List<ThinDbSvc>();
        public List<ThinDbMachine> MachineList = new List<ThinDbMachine>();

        // 上記データをもとにハッシュ化したデータ
        [JsonIgnore]
        public Dictionary<string, ThinDbSvc> SvcBySvcName = new Dictionary<string, ThinDbSvc>(StrComparer.IgnoreCaseComparer);
        [JsonIgnore]
        public Dictionary<string, ThinDbMachine> MachineByPcidAndSvcName = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);
        [JsonIgnore]
        public Dictionary<string, ThinDbMachine> MachineByMsid = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);
        [JsonIgnore]
        public Dictionary<string, ThinDbMachine> MachineByCertHashAndHostSecret2 = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);

        public ThinMemoryDb() { }

        // データベースから構築
        public ThinMemoryDb(IEnumerable<ThinDbSvc> svcTable, IEnumerable<ThinDbMachine> machineTable)
        {
            this.SvcList = svcTable.OrderBy(x=>x.SVC_NAME, StrComparer.IgnoreCaseComparer).ToList();
            this.MachineList = machineTable.OrderBy(x=>x.MACHINE_ID).ToList();

            BuildDictionary();
        }

        // ファイルから復元
        public ThinMemoryDb(string filePath)
        {
            ThinMemoryDb? tmp = Lfs.ReadJsonFromFile<ThinMemoryDb>(filePath, nullIfError: true);
            if (tmp == null)
            {
                tmp = Lfs.ReadJsonFromFile<ThinMemoryDb>(filePath + ".bak", nullIfError: false);
            }

            this.SvcList = tmp.SvcList;
            this.MachineList = tmp.MachineList;

            BuildDictionary();
        }

        void BuildDictionary()
        {
            this.SvcList.ForEach(x => this.SvcBySvcName.TryAdd(x.SVC_NAME, x));

            this.MachineList.ForEach(x =>
            {
                var svc = this.SvcBySvcName[x.SVC_NAME];

                this.MachineByPcidAndSvcName.TryAdd(x.PCID + "@" + x.SVC_NAME, x);
                this.MachineByMsid.TryAdd(x.MSID, x);
                this.MachineByCertHashAndHostSecret2.TryAdd(x.CERT_HASH + "@" + x.HOST_SECRET2, x);
            });
        }

        public long SaveToFile(string filePath)
        {
            string backupPath = filePath + ".bak";

            try
            {
                if (Lfs.IsFileExists(filePath))
                {
                    Lfs.CopyFile(filePath, backupPath);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            return Lfs.WriteJsonToFile(filePath, this, flags: FileFlags.AutoCreateDirectory | FileFlags.OnCreateSetCompressionFlag);
        }
    }

    public class ThinDatabase : AsyncServiceWithMainLoop
    {
        Task ReadMainLoopTask, WriteMainLoopTask;

        public ThinController Controller { get; }

        public ThinMemoryDb? MemDb { get; private set; }

        public string BackupFileName { get; }

        public ThinDatabase(ThinController controller)
        {
            try
            {
                this.BackupFileName = PP.Combine(Env.AppLocalDir, "Config", "ThinControllerDatabaseBackupCache", "DatabaseBackupCache.json");

                this.Controller = controller;

                this.ReadMainLoopTask = ReadMainLoopAsync(this.GrandCancel)._LeakCheck();
                this.WriteMainLoopTask = WriteMainLoopAsync(this.GrandCancel)._LeakCheck();
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public async Task<Database> OpenDatabaseForReadAsync(CancellationToken cancel = default)
        {
            Database db = new Database(this.Controller.SettingsFastSnapshot.DbConnectionString_Read, defaultIsolationLevel: IsolationLevel.Snapshot);

            await db.EnsureOpenAsync(cancel);

            return db;
        }

        long LastBackupSaveTick = 0;

        // データベースから最新の情報取得メイン
        async Task ReadCoreAsync(CancellationToken cancel)
        {
            try
            {
                await using var db = await OpenDatabaseForReadAsync(cancel);

                var dbRet = await db.TranReadSnapshotIfNecessaryAsync(async () =>
                {
                    IEnumerable<ThinDbSvc> allSvcs = await db.EasySelectAsync<ThinDbSvc>("select * from SVC", cancel: cancel);
                    IEnumerable<ThinDbMachine> allMachines = await db.EasySelectAsync<ThinDbMachine>("select * from MACHINE", cancel: cancel);

                    return new Pair2<IEnumerable<ThinDbSvc>, IEnumerable<ThinDbMachine>>(allSvcs, allMachines);
                });

                $"ThinDatabase.ReadCoreAsync Read All Records from DB: {dbRet.B.Count()}"._Debug();

                // メモリデータベースを構築
                ThinMemoryDb mem = new ThinMemoryDb(dbRet.A, dbRet.B);

                // 構築したメモリデータベースをバックアップファイルに保存
                long now = TickNow;

                if (LastBackupSaveTick == 0 || now >= (LastBackupSaveTick + ThinControllerConsts.DbBackupFileWriteIntervalMsecs))
                {
                    try
                    {
                        long size = mem.SaveToFile(this.BackupFileName);

                        $"ThinDatabase.ReadCoreAsync Save to the backup file: {size._ToString3()} bytes, filename = '{this.BackupFileName}'"._Debug();

                        LastBackupSaveTick = now;
                    }
                    catch (Exception ex)
                    {
                        ex._Error();
                    }
                }
            }
            catch
            {
                // データベースからもバックアップファイルからもまだデータが読み込まれていない場合は、バックアップファイルから読み込む
                if (this.MemDb == null)
                {
                    this.MemDb = new ThinMemoryDb(this.BackupFileName);
                }

                // バックアップファイルの読み込みを行なった上で、DB 例外はちゃんと throw する
                throw;
            }
        }

        // データベースから最新の情報を取得するタスク
        async Task ReadMainLoopAsync(CancellationToken cancel)
        {
            int numCycle = 0;
            int numError = 0;

            while (cancel.IsCancellationRequested == false)
            {
                numCycle++;

                $"ThinDatabase.ReadMainLoopAsync numCycle={numCycle}, numError={numError} Start."._Debug();

                long startTick = Time.HighResTick64;

                try
                {
                    await ReadCoreAsync(cancel);
                }
                catch (Exception ex)
                {
                    ex._Error();
                }

                long endTick = Time.HighResTick64;

                $"ThinDatabase.ReadMainLoopAsync numCycle={numCycle}, numError={numError} End. Took time: {endTick - startTick}"._Debug();

                await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(ThinControllerConsts.DbReadReloadIntervalMsecs));
            }
        }

        // データベースを更新するタスク
        async Task WriteMainLoopAsync(CancellationToken cancel)
        {
            while (cancel.IsCancellationRequested == false)
            {
                await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(ThinControllerConsts.DbWriteIntervalMsecs));
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                await this.ReadMainLoopTask._TryWaitAsync();
                await this.WriteMainLoopTask._TryWaitAsync();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

#endif

