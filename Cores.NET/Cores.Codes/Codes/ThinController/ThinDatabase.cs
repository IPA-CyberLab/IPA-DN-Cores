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
    public class ThinDbVar : INormalizable
    {
        [EasyKey]
        public int VAR_ID { get; set; }
        public string VAR_NAME { get; set; } = "";
        public string VAR_VALUE1 { get; set; } = "";
        public string? VAR_VALUE2 { get; set; }
        public string? VAR_VALUE3 { get; set; }
        public string? VAR_VALUE4 { get; set; }
        public string? VAR_VALUE5 { get; set; }
        public string? VAR_VALUE6 { get; set; }

        public void Normalize()
        {
            this.VAR_NAME = this.VAR_NAME._NonNullTrim();
            this.VAR_VALUE1 = this.VAR_VALUE1._NonNullTrim();
            this.VAR_VALUE2 = this.VAR_VALUE2._TrimIfNonNull();
            this.VAR_VALUE3 = this.VAR_VALUE3._TrimIfNonNull();
            this.VAR_VALUE4 = this.VAR_VALUE4._TrimIfNonNull();
            this.VAR_VALUE5 = this.VAR_VALUE5._TrimIfNonNull();
            this.VAR_VALUE6 = this.VAR_VALUE6._TrimIfNonNull();
        }
    }

    public class ThinDbSvc
    {
        [EasyManualKey]
        public string SVC_NAME { get; set; } = "";
        public string SVC_TITLE { get; set; } = "";
    }

    public class ThinDbMachine
    {
        [SimpleTableOrder(1)]
        public int MACHINE_ID { get; set; }
        [SimpleTableIgnore]
        public string SVC_NAME { get; set; } = "";
        [EasyManualKey]
        [SimpleTableOrder(3)]
        public string MSID { get; set; } = "";
        [SimpleTableOrder(2)]
        public string PCID { get; set; } = "";
        [SimpleTableIgnore]
        public int PCID_VER { get; set; }
        [SimpleTableIgnore]
        public DateTime PCID_UPDATE_DATE { get; set; } = Util.ZeroDateTimeValue;
        [SimpleTableIgnore]
        public byte[] CERT { get; set; } = new byte[0];
        [SimpleTableIgnore]
        public string CERT_HASH { get; set; } = "";
        [SimpleTableIgnore]
        [JsonIgnore]
        [NoDebugDump]
        public string HOST_SECRET { get; set; } = "";
        [SimpleTableIgnore]
        [JsonIgnore]
        [NoDebugDump]
        public string HOST_SECRET2 { get; set; } = "";
        [SimpleTableOrder(5)]
        public DateTime CREATE_DATE { get; set; } = Util.ZeroDateTimeValue;
        [SimpleTableIgnore]
        public DateTime UPDATE_DATE { get; set; } = Util.ZeroDateTimeValue;
        [SimpleTableOrder(7)]
        public DateTime LAST_SERVER_DATE { get; set; } = Util.ZeroDateTimeValue;
        [SimpleTableOrder(8)]
        public DateTime LAST_CLIENT_DATE { get; set; } = Util.ZeroDateTimeValue;
        [SimpleTableOrder(9)]
        public int NUM_SERVER { get; set; }
        [SimpleTableOrder(10)]
        public int NUM_CLIENT { get; set; }
        [SimpleTableOrder(4)]
        public string CREATE_IP { get; set; } = "";
        [SimpleTableIgnore]
        public string CREATE_HOST { get; set; } = "";
        [SimpleTableOrder(6)]
        public string LAST_IP { get; set; } = "";
        [SimpleTableIgnore]
        public string REAL_PROXY_IP { get; set; } = "";
        [SimpleTableIgnore]
        public string LAST_FLAG { get; set; } = "";
        [SimpleTableIgnore]
        public int SE_LANGUAGE { get; set; }
        [SimpleTableIgnore]
        public bool RESET_CERT_FLAG { get; set; }
        [SimpleTableIgnore]
        public bool FLAG_BETA2MSG { get; set; }
        [SimpleTableIgnore]
        public DateTime FIRST_CLIENT_DATE { get; set; } = Util.ZeroDateTimeValue;
        [SimpleTableIgnore]
        public DateTime EXPIRE { get; set; } = Util.ZeroDateTimeValue;
        [SimpleTableIgnore]
        public int MAX_CLIENTS { get; set; }
        [SimpleTableIgnore]
        public bool MAC_ENABLE { get; set; }
        [SimpleTableIgnore]
        public int NUM_MACCLIENT { get; set; }
        [SimpleTableIgnore]
        public string WOL_MACLIST { get; set; } = "";
        [SimpleTableIgnore]
        public long SERVERMASK64 { get; set; }
        [SimpleTableIgnore]
        public string JSON_ATTRIBUTES { get; set; } = "";
    }

    public class ThinMemoryDb
    {
        // データベースからもらってきたデータ
        public List<ThinDbSvc> SvcList = new List<ThinDbSvc>();
        public List<ThinDbVar> VarList = new List<ThinDbVar>();
        public List<ThinDbMachine> MachineList = new List<ThinDbMachine>();

        // 上記データをもとにハッシュ化したデータ
        [JsonIgnore]
        public Dictionary<string, ThinDbSvc> SvcBySvcName = new Dictionary<string, ThinDbSvc>(StrComparer.IgnoreCaseComparer);

        [JsonIgnore]
        public Dictionary<string, List<ThinDbVar>> VarByName = new Dictionary<string, List<ThinDbVar>>(StrComparer.IgnoreCaseComparer);

        [JsonIgnore]
        public Dictionary<string, ThinDbMachine> MachineByPcidAndSvcName = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);
        [JsonIgnore]
        public Dictionary<string, ThinDbMachine> MachineByMsid = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);
        [JsonIgnore]
        public Dictionary<string, ThinDbMachine> MachineByCertHashAndHostSecret2 = new Dictionary<string, ThinDbMachine>(StrComparer.IgnoreCaseComparer);

        [JsonIgnore]
        public int MaxSessionsPerGate;
        [JsonIgnore]
        public string ControllerGateSecretKey = "";

        public ThinMemoryDb() { }

        // データベースから構築
        public ThinMemoryDb(IEnumerable<ThinDbSvc> svcTable, IEnumerable<ThinDbMachine> machineTable, IEnumerable<ThinDbVar> varTable)
        {
            this.SvcList = svcTable.OrderBy(x => x.SVC_NAME, StrComparer.IgnoreCaseComparer).ToList();
            this.VarList = varTable.OrderBy(x => x.VAR_NAME).ThenBy(x => x.VAR_ID).ToList();
            this.MachineList = machineTable.OrderBy(x => x.MACHINE_ID).ToList();

            BuildOnMemoryData();
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
            this.VarList = tmp.VarList;

            BuildOnMemoryData();
        }

        // オンメモリデータの構築
        void BuildOnMemoryData()
        {
            this.SvcList.ForEach(x => this.SvcBySvcName.TryAdd(x.SVC_NAME, x));

            this.VarList.ForEach(v =>
            {
                v.Normalize();

                this.VarByName._GetOrNew(v.VAR_NAME, () => new List<ThinDbVar>()).Add(v);
            });

            this.MachineList.ForEach(x =>
            {
                var svc = this.SvcBySvcName[x.SVC_NAME];

                this.MachineByPcidAndSvcName.TryAdd(x.PCID + "@" + svc.SVC_NAME, x);
                this.MachineByMsid.TryAdd(x.MSID, x);
                this.MachineByCertHashAndHostSecret2.TryAdd(x.CERT_HASH + "@" + x.HOST_SECRET2, x);
            });

            // 頻繁にアクセスされる変数を予め読み出しておく
            this.MaxSessionsPerGate = this.VarByName._GetOrDefault("MaxSessionsPerGate")?.FirstOrDefault()?.VAR_VALUE1._ToInt() ?? 0;
            this.ControllerGateSecretKey = this.VarByName._GetOrDefault("ControllerGateSecretKey")?.FirstOrDefault()?.VAR_VALUE1._NonNullTrim() ?? "";
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

        public bool IsLoaded => MemDb != null; // 一度でもメモリロードされたら true

        public bool IsDatabaseConnected { get; private set; } // 読み出しメインループからの最後のデータベース接続に成功していれば true、それ以外の場合は false。DB で不具合が発生していることを検出するため

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

        public async Task<Database> OpenDatabaseForWriteAsync(CancellationToken cancel = default)
        {
            Database db = new Database(this.Controller.SettingsFastSnapshot.DbConnectionString_Write, defaultIsolationLevel: IsolationLevel.Serializable);

            await db.EnsureOpenAsync(cancel);

            return db;
        }

        long LastBackupSaveTick = 0;

        // データベースから最新の情報取得メイン
        async Task ReadCoreAsync(CancellationToken cancel)
        {
            try
            {

                IEnumerable<ThinDbSvc> allSvcs = null!;
                IEnumerable<ThinDbMachine> allMachines = null!;
                IEnumerable<ThinDbVar> allVars = null!;

                try
                {
                    await using var db = await OpenDatabaseForReadAsync(cancel);

                    await db.TranReadSnapshotIfNecessaryAsync(async () =>
                    {
                        allSvcs = await db.EasySelectAsync<ThinDbSvc>("select * from SVC", cancel: cancel);
                        allMachines = await db.EasySelectAsync<ThinDbMachine>("select * from MACHINE", cancel: cancel);
                        allVars = await db.EasySelectAsync<ThinDbVar>("select * from VAR", cancel: cancel);
                    });

                    IsDatabaseConnected = true;
                    $"ThinDatabase.ReadCoreAsync Read All Records from DB: {allMachines.Count()}"._Debug();
                }
                catch (Exception ex)
                {
                    IsDatabaseConnected = false;
                    $"ThinDatabase.ReadCoreAsync Database Connect Error: {ex.ToString()}"._Error();
                    throw;
                }

                // メモリデータベースを構築
                ThinMemoryDb mem = new ThinMemoryDb(allSvcs, allMachines, allVars);

                this.MemDb = mem;

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

        // 便利な Var 取得ルーチン集
        public IEnumerable<ThinDbVar>? GetVars(string name)
        {
            var db = this.MemDb;
            if (db == null) return null;

            if (db.VarByName.TryGetValue(name, out List<ThinDbVar>? list) == false)
            {
                return new ThinDbVar[0];
            }

            return list;
        }
        public ThinDbVar? GetVar(string name)
            => GetVars(name)?.FirstOrDefault();

        public string? GetVarString(string name)
            => GetVar(name)?.VAR_VALUE1;

        public int GetVarInt(string name, int defaultValue = 0)
            => GetVarString(name)?._ToInt() ?? defaultValue;

        public bool GetVarBool(string name, bool defaultValue = false)
            => GetVarString(name)?._ToBool(defaultValue) ?? defaultValue;

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

        // PCID 変更実行
        public async Task<VpnErrors> RenamePcidAsync(string msid, string newPcid, DateTime now, CancellationToken cancel = default)
        {
            msid = msid._NonNullTrim();
            newPcid = newPcid._NonNullTrim();

            VpnErrors err2 = ThinController.CheckPCID(newPcid);
            if (err2 != VpnErrors.ERR_NO_ERROR) return err2;

            VpnErrors err = VpnErrors.ERR_INTERNAL_ERROR;

            await using var db = await OpenDatabaseForWriteAsync(cancel);

            // トランザクションを確立し厳格なチェックを実施
            // (DB サーバー側で一意インデックスによりチェックするが、インデックスが間違っていた場合に備えて、トランザクションでも厳密にチェックするのである)
            if (await db.TranAsync(async () =>
            {
                // MACHINE を取得
                var machine = await db.EasySelectSingleAsync<ThinDbMachine>("SELECT * FROM MACHINE WHERE MSID = @MSID", new { MSID = msid }, false, true, cancel);
                if (machine == null)
                {
                    // おかしいな
                    err = VpnErrors.ERR_SECURITY_ERROR;
                    return false;
                }

                // 同一 PCID が存在しないかどうかチェック
                if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE PCID = @ AND SVC_NAME = @", newPcid, machine.SVC_NAME)).Int != 0)
                {
                    err = VpnErrors.ERR_PCID_ALREADY_EXISTS;
                    return false;
                }

                // 変更の実行
                await db.QueryWithNoReturnAsync("UPDATE MACHINE SET PCID = @, UPDATE_DATE = @, PCID_UPDATE_DATE = @, PCID_VER = PCID_VER + 1 WHERE MSID = @",
                    newPcid, now, now, msid);

                return true;
            }) == false)
            {
                return err;
            }

            Controller.AddPcidToRecentPcidCandidateCache(newPcid);

            // ローカルメモリデータベース上の PCID 情報を変更
            var machine = this.MemDb?.MachineByMsid._GetOrDefault(msid);

            if (machine != null)
            {
                machine.PCID = newPcid;
            }

            return VpnErrors.ERR_NO_ERROR;
        }

        // サーバー登録実行
        public async Task<VpnErrors> RegisterMachineAsync(string svcName, string msid, string pcid, string hostKey, string hostSecret2, DateTime now, string ip, string fqdn, CancellationToken cancel = default)
        {
            svcName = svcName._NonNullTrim();
            msid = msid._NonNullTrim();
            pcid = pcid._NonNullTrim();
            hostKey = hostKey._NonNullTrim();
            hostSecret2 = hostSecret2._NonNullTrim();
            ip = ip._NonNullTrim();
            fqdn = fqdn._NonNullTrim();

            VpnErrors err2 = ThinController.CheckPCID(pcid);
            if (err2 != VpnErrors.ERR_NO_ERROR) return err2;

            VpnErrors err = VpnErrors.ERR_INTERNAL_ERROR;

            await using var db = await OpenDatabaseForWriteAsync(cancel);

            // トランザクションを確立し厳格なチェックを実施
            // (DB サーバー側で一意インデックスによりチェックするが、インデックスが間違っていた場合に備えて、トランザクションでも厳密にチェックするのである)
            if (await db.TranAsync(async () =>
            {
                // 同一 hostKey が存在しないかどうか確認
                if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE CERT_HASH = @", hostKey)).Int != 0)
                {
                    err = VpnErrors.ERR_SECURITY_ERROR;
                    return false;
                }

                // 同一シークレットが存在しないかどうかチェック
                if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE HOST_SECRET2 = @", hostSecret2)).Int != 0)
                {
                    err = VpnErrors.ERR_SECURITY_ERROR;
                    return false;
                }

                // 同一 PCID が存在しないかどうかチェック
                if ((await db.QueryWithValueAsync("SELECT COUNT(MACHINE_ID) FROM MACHINE WHERE PCID = @ AND SVC_NAME = @", pcid, svcName)).Int != 0)
                {
                    err = VpnErrors.ERR_PCID_ALREADY_EXISTS;
                    return false;
                }

                // 登録の実行
                await db.QueryWithNoReturnAsync("INSERT INTO MACHINE (SVC_NAME, MSID, PCID, CERT, CERT_HASH, CREATE_DATE, UPDATE_DATE, LAST_SERVER_DATE, LAST_CLIENT_DATE, NUM_SERVER, NUM_CLIENT, CREATE_IP, CREATE_HOST, HOST_SECRET, HOST_SECRET2) " +
                                    "VALUES (@, @, @, @, @, @, @, @, @, @, @, @, @, @, @)",
                                    svcName, msid, pcid, new byte[0], hostKey,
                                    now, now, now, now,
                                    0, 0,
                                    ip, fqdn,
                                    hostKey, hostSecret2);

                return true;
            }) == false)
            {
                return err;
            }

            Controller.AddPcidToRecentPcidCandidateCache(pcid);

            return VpnErrors.ERR_NO_ERROR;
        }

        // SvcName および Pcid による Machine の検索試行
        public async Task<ThinDbMachine?> SearchMachineByPcidAsync(string svcName, string pcid, CancellationToken cancel = default)
        {
            // まずローカルメモリデータベースを検索する
            var mem = this.MemDb;
            if (mem != null)
            {
                var foundMachine = mem.MachineByPcidAndSvcName._GetOrDefault(pcid + "@" + svcName);
                if (foundMachine != null)
                {
                    // 発見
                    return foundMachine;
                }
            }

            if (IsDatabaseConnected == false)
            {
                // データベース読み込みメインループでエラーが発生している場合は、データベース接続を試行しない (大量の試行がなされ不具合が発生するおそれがあるため)
                return null;
            }

            cancel.ThrowIfCancellationRequested();

            // ローカルメモリデータベースの検索でヒットしなかった場合 (たとえば、最近作成されたホストの場合) は、マスタデータベースを物理的に検索する
            await using var db = await OpenDatabaseForReadAsync(cancel);

            var foundMachine2 = await db.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                return await db.EasySelectSingleAsync<ThinDbMachine>("select * from MACHINE where PCID = @PCID and SVC_NAME = @SVC_NAME",
                    new
                    {
                        PCID = pcid,
                        SVC_NAME = svcName,
                    },
                    throwErrorIfMultipleFound: true, throwErrorIfNotFound: false, cancel: cancel);
            });

            if (foundMachine2 != null)
            {
                // 発見
                return foundMachine2;
            }

            // 未発見
            return null;
        }

        // HostKey および HostSecret2 による認証試行
        public async Task<ThinDbMachine?> AuthMachineAsync(string hostKey, string hostSecret2, CancellationToken cancel = default)
        {
            // まずローカルメモリデータベースを検索する
            var mem = this.MemDb;
            if (mem != null)
            {
                var foundMachine = mem.MachineByCertHashAndHostSecret2._GetOrDefault(hostKey + "@" + hostSecret2);
                if (foundMachine != null)
                {
                    // 発見
                    return foundMachine;
                }
            }

            if (IsDatabaseConnected == false)
            {
                // データベース読み込みメインループでエラーが発生している場合は、データベース接続を試行しない (大量の試行がなされ不具合が発生するおそれがあるため)
                return null;
            }

            cancel.ThrowIfCancellationRequested();

            // ローカルメモリデータベースの検索でヒットしなかった場合 (たとえば、最近作成されたホストの場合) は、マスタデータベースを物理的に検索する
            await using var db = await OpenDatabaseForReadAsync(cancel);

            var foundMachine2 = await db.TranReadSnapshotIfNecessaryAsync(async () =>
            {
                return await db.EasySelectSingleAsync<ThinDbMachine>("select * from MACHINE where CERT_HASH = @CERT_HASH and HOST_SECRET2 = @HOST_SECRET2",
                    new
                    {
                        CERT_HASH = hostKey,
                        HOST_SECRET2 = hostSecret2,
                    },
                    throwErrorIfMultipleFound: true, throwErrorIfNotFound: false, cancel: cancel);
            });

            if (foundMachine2 != null)
            {
                // 発見
                return foundMachine2;
            }

            // 未発見
            return null;
        }
    }
}

#endif

