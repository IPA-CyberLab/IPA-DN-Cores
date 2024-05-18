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

#if CORES_BASIC_JSON && (CORES_BASIC_WEBAPP || CORES_BASIC_HTTPSERVER) && CORES_BASIC_SECURITY

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

namespace IPA.Cores.Basic;

public static partial class CoresConfig
{
    public static partial class FntpMainteDaemonHost
    {
        public static readonly Copenhagen<string> _Test = "Hello";
    }
}

public class FntpMainteDaemonSettings : INormalizable
{
    public string _TestStr = "";

    public int ClockCheckIntervalMsecs = 0;

    public bool SkipCheckRtcCorrect = false;
    public bool SkipCheckNtpClock = false;
    public bool SkipCheckSystemClock = false;
    public bool SkipCheckNtpServerResult = false;
    public bool SkipCheckDateTimeNow = false;

    public int RtcCorrectAllowDiffMsecs = 0;
    public int NtpClockCorrectAllowDiffMsecs = 0;
    public int SystemClockAllowDiffMsecs = 0;
    public int DateTimeNowAllowDiffMsecs = 0;
    public int NtpServerResultAllowDiffMsecs = 0;

    public int CheckDateInternelCommTimeoutMsecs = 0;

    public string CheckTargetNtpServerAddress = "";

    public string StopFileName = "";
    public int HealthTcpPort;

    public string RunCommandWhileOk = "";
    public string RunCommandWhileError = "";

    public void Normalize()
    {
        if (this._TestStr._IsFilled() == false)
        {
            this._TestStr = "Hello";
        }

        if (ClockCheckIntervalMsecs <= 0) ClockCheckIntervalMsecs = 5 * 1000;
        if (RtcCorrectAllowDiffMsecs <= 0) RtcCorrectAllowDiffMsecs = 2 * 1000;
        if (NtpClockCorrectAllowDiffMsecs <= 0) NtpClockCorrectAllowDiffMsecs = 2 * 1000;
        if (SystemClockAllowDiffMsecs <= 0) SystemClockAllowDiffMsecs = 2 * 1000;
        if (NtpServerResultAllowDiffMsecs <= 0) NtpServerResultAllowDiffMsecs = 2 * 1000;
        if (CheckDateInternelCommTimeoutMsecs <= 0) CheckDateInternelCommTimeoutMsecs = 2 * 1000;
        if (DateTimeNowAllowDiffMsecs <= 0) DateTimeNowAllowDiffMsecs = 2 * 1000;
        if (CheckTargetNtpServerAddress._IsEmpty()) CheckTargetNtpServerAddress = "127.0.0.1";
        if (StopFileName._IsEmpty()) StopFileName = "/etc/fntp_stop.txt";
        if (RunCommandWhileOk._IsEmpty()) RunCommandWhileOk = "/etc/fntp_exec_ok.sh";
        if (RunCommandWhileError._IsEmpty()) RunCommandWhileError = "/etc/fntp_exec_error.sh";
        if (HealthTcpPort <= 0) HealthTcpPort = Consts.Ports.FntpMainteDaemonHealthPort;
    }
}

public class FntpMainteHealthStatus
{
    public bool? HasUnknownError;
    public bool? HasTimeDateCtlCommandError;
    public bool? HasStopFile;
    public bool? IsNtpDaemonActive;
    public bool? IsNtpDaemonSynced;
    public bool? IsRtcCorrect;
    public bool? IsNtpClockCorrect;
    public bool? IsSystemClockCorrect;
    public bool? IsDateTimeNowCorrect;
    public DateTimeOffset TimeStamp = DtOffsetNow;

    public bool IsOk()
    {
        if (HasUnknownError ?? false) return false;
        if (HasTimeDateCtlCommandError ?? false) return false;
        if (HasStopFile ?? false) return false;
        if ((IsNtpDaemonActive ?? false) == false) return false;
        if ((IsNtpDaemonSynced ?? false) == false) return false;
        if ((IsRtcCorrect ?? false) == false) return false;
        if ((IsNtpClockCorrect ?? false) == false) return false;
        if ((IsSystemClockCorrect ?? false) == false) return false;
        if ((IsDateTimeNowCorrect ?? false) == false) return false;
        return true;
    }

    public string ToInternalCompareStr() => $"{HasStopFile}_{HasUnknownError}_{HasTimeDateCtlCommandError}_{IsNtpDaemonActive}_{IsNtpDaemonSynced}_{IsRtcCorrect}_{IsNtpClockCorrect}_{IsSystemClockCorrect}_{IsDateTimeNowCorrect}";

    public override string ToString()
    {
        return this._ObjectToJson(compact: true);
    }

    public List<string> ErrorList = new List<string>();
}


public class FntpMainteDaemonApp : AsyncServiceWithMainLoop
{
    readonly HiveData<FntpMainteDaemonSettings> SettingsHive;

    // 'Config\FntpMainteDaemon' のデータ
    public FntpMainteDaemonSettings Settings => SettingsHive.GetManagedDataSnapshot();

    readonly CriticalSection LockList = new CriticalSection<FntpMainteDaemonApp>();

    public CgiHttpServer Cgi { get; }

    public FntpMainteDaemonApp()
    {
        try
        {
            // Settings を読み込む
            this.SettingsHive = new HiveData<FntpMainteDaemonSettings>(Hive.SharedLocalConfigHive, $"FntpMainteDaemon", null, HiveSyncPolicy.AutoReadFromFile);

            // ここでサーバーを立ち上げるなどの初期化処理を行なう
            this.StartMainLoop(MainLoopAsync);

            // HTTP サーバーを立ち上げる
            this.Cgi = new CgiHttpServer(new CgiHandler(this), new HttpServerOptions()
            {
                AutomaticRedirectToHttpsIfPossible = false,
                UseKestrelWithIPACoreStack = false,
                HttpPortsList = new int[] { Consts.Ports.FntpMainteDaemonHttpAdminPort }.ToList(),
                HttpsPortsList = new int[] {  }.ToList(),
                UseStaticFiles = false,
                MaxRequestBodySize = 32 * 1024,
                ReadTimeoutMsecs = 30 * 1000,
                DenyRobots = true,
                UseSimpleBasicAuthentication = true,
            },
            true);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public class CgiHandler : CgiHandlerBase
    {
        public readonly FntpMainteDaemonApp App;

        public CgiHandler(FntpMainteDaemonApp app)
        {
            this.App = app;
        }

        protected override void InitActionListImpl(CgiActionList noAuth, CgiActionList reqAuth)
        {
            try
            {
                reqAuth.AddAction("/", WebMethodBits.GET | WebMethodBits.HEAD, async (ctx) =>
                {
                    var status = App.LastStatus;

                    StringWriter w = new StringWriter();

                    w.WriteLine($"Welcome to FntpDaemon Status Screen !!!");
                    w.WriteLine();
                    w.WriteLine($"Current datetime: {DtOffsetNow._ToDtStr()}");
                    w.WriteLine($"This Machine name: {Env.DnsFqdnHostName}");
                    w.WriteLine();
                    w.WriteLine($"IsOK: {status?.IsOk() ?? false}");
                    w.WriteLine();
                    w.WriteLine();

                    var banner_result = await EasyExec.ExecAsync("/bin/se_generate_login_banner");

                    w.WriteLine("--- Linux Status Begin ---");
                    w.WriteLine(banner_result.ErrorAndOutputStr);
                    w.WriteLine("--- Linux Status End ---");

                    w.WriteLine();
                    w.WriteLine();

                    if (status == null)
                    {
                        w.WriteLine($"There is no LastStatus.");
                        w.WriteLine();
                        w.WriteLine();
                    }
                    else
                    {
                        w.WriteLine($"--- FNTP Status Begin ---");
                        w.WriteLine($"IsOK: {status.IsOk()}");
                        w.WriteLine($"TimeStamp: {status.TimeStamp._ToLocalDtStr()}");
                        w.WriteLine();
                        w.WriteLine(status._ObjectToJson(includeNull: true));
                        w.WriteLine($"--- FNTP Status End ---");
                        w.WriteLine();
                        w.WriteLine();
                    }

                    w.WriteLine("--- Linux Status Begin ---");
                    w.WriteLine((new EnvInfoSnapshot())._GetObjectDump());
                    w.WriteLine("--- Linux Status End ---");

                    w.WriteLine();
                    w.WriteLine();

                    return new HttpStringResult(w.ToString(), Consts.MimeTypes.TextUtf8);
                });
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }
    }

    // 健康状態チェック実行
    public async Task<FntpMainteHealthStatus> CheckHealthAsync(CancellationToken cancel = default)
    {
        FntpMainteHealthStatus ret = new FntpMainteHealthStatus();

        // stopfile を検査
        string stopFileName = Settings.StopFileName;
        if (stopFileName._IsFilled())
        {
            try
            {
                if (await Lfs.IsFileExistsAsync(stopFileName, cancel))
                {
                    ret.HasStopFile = true;
                }
            }
            catch { }
        }

        if (ret.HasStopFile ?? false)
        {
            return ret; // stopfile が設置されていれば、これ以降の検査を省略
        }

        try
        {
            // timedatectl の実行結果を取得
            try
            {
                var ctlResult = await TaskUtil.RetryAsync(async () =>
                {
                    return await LinuxTimeDateCtlUtil.GetStateFromTimeDateCtlCommandAsync(cancel);
                }, 1000, 5);

                ret.IsNtpDaemonActive = ctlResult.NtpServiceActive;
                ret.IsNtpDaemonSynced = ctlResult.SystemClockSynchronized;
            }
            catch (Exception ex)
            {
                ret.HasTimeDateCtlCommandError = true;
                ret.ErrorList.Add($"timedatectl command exec error. message = {ex.Message}");
                return ret; // ここで失敗したら、これ以降の検査を省略
            }

            if (ret.IsNtpDaemonActive == false || ret.IsNtpDaemonSynced == false)
            {
                return ret; // ここで失敗したら、これ以降の検査を省略
            }

            // システム時計を検査 (DateTime.Now 報告値)
            if (Settings.SkipCheckDateTimeNow)
            {
                ret.IsDateTimeNowCorrect = true;
            }
            else
            {
                RefBool commError = new RefBool();
                var res = await MiscUtil.CompareLocalClockToInternetServersClockAsync(Settings.SystemClockAllowDiffMsecs._ToTimeSpanMSecs(), true, Settings.CheckDateInternelCommTimeoutMsecs,
                    commError,
                    () =>
                    {
                        return DateTimeOffset.Now._TR();
                    },
                    cancel);

                if (commError)
                {
                    // 比較対象の HTTP サーバーとの間でのインターネット通信エラー発生時は成功と見なす (ただし、ログを吐き出す)
                    $"DateTime.Now Clock: CompareLocalClockToInternetServersClockAsync error. Error = {res.Exception.ToString()}"._Error();
                    ret.IsDateTimeNowCorrect = true;
                }
                else
                {
                    if (res.IsOk)
                    {
                        // 比較に成功し、時差は許容範囲内であった
                        ret.IsDateTimeNowCorrect = true;
                    }
                    else
                    {
                        // 比較に成功したが、時差が許容範囲を超えていた
                        ret.IsDateTimeNowCorrect = false;
                        ret.ErrorList.Add($"DateTime.Now Health Check Failed: {res.Exception.Message}");
                    }
                }
            }
            if (ret.IsDateTimeNowCorrect == false || ret.IsNtpDaemonActive == false || ret.IsNtpDaemonSynced == false)
            {
                return ret; // ここで失敗したら、これ以降の検査を省略
            }

            // NTP 応答値を検査
            if (Settings.SkipCheckNtpClock)
            {
                ret.IsNtpClockCorrect = true;
            }
            else
            {
                // 数回リトライを実施する
                try
                {
                    var res = await TaskUtil.RetryAsync(async () =>
                    {
                        DateTimeOffset dt;
                        try
                        {
                            // 127.0.0.1 の NTPd からの応答結果を取得
                            dt = await TaskUtil.RetryAsync(async () =>
                            {
                                return await LinuxTimeDateCtlUtil.ExecuteNtpDigAndReturnResultDateTimeAsync(Settings.CheckTargetNtpServerAddress, 2000, cancel);
                            },
                            250, 10, cancel, true);
                        }
                        catch (Exception ex)
                        {
                            // 127.0.0.1 の NTPd が動作していないようである
                            return new CoresException($"ntpdig: NTP server {Settings.CheckTargetNtpServerAddress} down? Exception: {ex.Message}");
                        }

                        if (dt._IsZeroDateTime())
                        {
                            return new CoresException($"ntpdig: Returned datetime from NTP Server {Settings.CheckTargetNtpServerAddress} was invalid: {dt._ToDtStr()}");
                        }

                        // この結果を元にインターネット上の HTTP サーバーと比較
                        RefBool commError = new RefBool();
                        var res = await MiscUtil.CompareLocalClockToInternetServersClockAsync(Settings.SystemClockAllowDiffMsecs._ToTimeSpanMSecs(), true, Settings.CheckDateInternelCommTimeoutMsecs,
                            commError,
                            () =>
                            {
                                return DateTimeOffset.Now._TR();
                            },
                            cancel);

                        if (commError)
                        {
                            // 比較対象の HTTP サーバーとの間でのインターネット通信エラー発生時は成功と見なす (ただし、ログを吐き出す)
                            $"ntpdig: CompareLocalClockToInternetServersClockAsync error. Error = {res.Exception.ToString()}"._Error();
                            return new OkOrExeption();
                        }
                        else
                        {
                            if (res.IsOk)
                            {
                                // 比較に成功し、時差は許容範囲内であった
                                return new OkOrExeption();
                            }
                            else
                            {
                                // 比較に成功したが、時差が許容範囲を超えていた。この場合、再試行をする。
                                throw new CoresException($"ntpdig Health Check Failed: {res.Exception.Message}");
                            }
                        }
                    },
                    250, 5, cancel, randomInterval: true);

                    if (res.IsOk)
                    {
                        ret.IsNtpClockCorrect = true;
                    }
                    else
                    {
                        ret.IsNtpClockCorrect = false;
                        ret.ErrorList.Add(res.Exception.Message);
                    }
                }
                catch (Exception ex)
                {
                    ret.IsNtpClockCorrect = false;
                    ret.ErrorList.Add($"NTP Health Check Failed (try-catch): {ex.Message}");
                }
            }
            if (ret.IsNtpClockCorrect == false)
            {
                return ret; // ここで失敗したら、これ以降の検査を省略
            }

            // RTC を検査
            if (Settings.SkipCheckRtcCorrect)
            {
                ret.IsRtcCorrect = true;
            }
            else
            {
                RefBool commError = new RefBool();
                var res = await MiscUtil.CompareLocalClockToInternetServersClockAsync(Settings.RtcCorrectAllowDiffMsecs._ToTimeSpanMSecs(), true, Settings.CheckDateInternelCommTimeoutMsecs,
                    commError,
                    async () =>
                    {
                        var ctlResult = await TaskUtil.RetryAsync(async () =>
                        {
                            return await LinuxTimeDateCtlUtil.GetStateFromTimeDateCtlCommandAsync(cancel);
                        }, 250, 5, cancel, true);

                        return ctlResult.RtcTime._AsDateTimeOffset(false, true);
                    },
                    cancel);

                if (commError)
                {
                    // 比較対象の HTTP サーバーとの間でのインターネット通信エラー発生時は成功と見なす (ただし、ログを吐き出す)
                    $"RTC: CompareLocalClockToInternetServersClockAsync error. Error = {res.Exception.ToString()}"._Error();
                }
                else
                {
                    if (res.IsOk)
                    {
                        // 比較に成功し、時差は許容範囲内であった
                        ret.IsRtcCorrect = true;
                    }
                    else
                    {
                        // 比較に成功したが、時差が許容範囲を超えていた
                        ret.IsRtcCorrect = false;
                        ret.ErrorList.Add($"RTC Health Check Failed: {res.Exception.Message}");
                    }
                }
            }
            if (ret.IsRtcCorrect == false)
            {
                return ret; // ここで失敗したら、これ以降の検査を省略
            }

            // システム時計を検査 (timedatectl 報告値)
            if (Settings.SkipCheckSystemClock)
            {
                ret.IsSystemClockCorrect = true;
            }
            else
            {
                RefBool commError = new RefBool();
                var res = await MiscUtil.CompareLocalClockToInternetServersClockAsync(Settings.SystemClockAllowDiffMsecs._ToTimeSpanMSecs(), true, Settings.CheckDateInternelCommTimeoutMsecs,
                    commError,
                    async () =>
                    {
                        var ctlResult = await TaskUtil.RetryAsync(async () =>
                        {
                            return await LinuxTimeDateCtlUtil.GetStateFromTimeDateCtlCommandAsync(cancel);
                        }, 250, 5, cancel, true);

                        return ctlResult.UniversalTime._AsDateTimeOffset(false, true);
                    },
                    cancel);

                if (commError)
                {
                    // 比較対象の HTTP サーバーとの間でのインターネット通信エラー発生時は成功と見なす (ただし、ログを吐き出す)
                    $"System Clock: CompareLocalClockToInternetServersClockAsync error. Error = {res.Exception.ToString()}"._Error();
                }
                else
                {
                    if (res.IsOk)
                    {
                        // 比較に成功し、時差は許容範囲内であった
                        ret.IsSystemClockCorrect = true;
                    }
                    else
                    {
                        // 比較に成功したが、時差が許容範囲を超えていた
                        ret.IsSystemClockCorrect = false;
                        ret.ErrorList.Add($"System Clock Health Check Failed: {res.Exception.Message}");
                    }
                }
            }
            if (ret.IsSystemClockCorrect == false)
            {
                return ret; // ここで失敗したら、これ以降の検査を省略
            }

            return ret;
        }
        catch (Exception ex)
        {
            ret = new FntpMainteHealthStatus();

            ret.HasUnknownError = true;

            ret.ErrorList.Add($"Unknown exception: {ex.Message}");

            return ret;
        }
    }

    bool LastOk = false;
    NetTcpListener? LastListener = null;
    FntpMainteHealthStatus? LastStatus = null;

    // 定期的に実行されるチェック処理の実装
    async Task MainProcAsync(CancellationToken cancel = default)
    {
        FntpMainteHealthStatus res = await CheckHealthAsync(cancel: cancel);

        bool ok = res.IsOk();

        if (LastOk != ok)
        {
            if (ok)
            {
                "Status changed to OK. Good good."._Error();
            }
            else
            {
                "Warning!! Status changed to Error !!!"._Error();
            }
        }

        if (LastStatus == null || LastStatus.ToInternalCompareStr() != res.ToInternalCompareStr())
        {
            // 状態が変化した
            LastStatus = res;

            // 詳細をログに書き出す
            $"Health status changed: Ok = {ok}, Details: {res.ToString()}"._Error();
        }

        // 外部コマンドを実行
        string cmd = ok ? Settings.RunCommandWhileOk : Settings.RunCommandWhileError;

        if (cmd._IsFilled())
        {
            try
            {
                if (await Lfs.IsFileExistsAsync(cmd, cancel))
                {
                    await EasyExec.ExecAsync(cmd, cancel: cancel);
                }
            }
            catch (Exception ex)
            {
                $"Command '{cmd}' execute error. Message = {ex.Message}"._Error();
            }
        }

        if (LastOk != ok)
        {
            // OK 状態が変化した
            LastOk = ok;

            // 検査用 TCP ポートを開閉する
            if (ok)
            {
                int port = Settings.HealthTcpPort;

                if (port >= 1)
                {
                    try
                    {
                        this.LastListener = LocalNet.CreateTcpListener(new TcpListenParam(async (listener, sock) =>
                        {
                            try
                            {
                                await using var stream = sock.GetStream();
                                stream.ReadTimeout = 5 * 1000;
                                stream.WriteTimeout = 5 * 1000;

                                long endTick = TickNow + 5 * 1000;

                                while (true)
                                {
                                    if (TickNow >= endTick)
                                    {
                                        break;
                                    }
                                    var data = await stream._ReadToEndAsync(1000, cancel);

                                    if (data.Length == 0)
                                    {
                                        break;
                                    }

                                    await Task.Delay(10); // 念のため
                                }
                            }
                            catch { }
                        }, null, port));
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }
            }
            else
            {
                await this.LastListener._DisposeSafeAsync();
                this.LastListener = null;
            }
        }
    }

    // メインループ
    async Task MainLoopAsync(CancellationToken cancel = default)
    {
        try
        {
            while (cancel.IsCancellationRequested == false)
            {
                try
                {
                    await MainProcAsync(cancel);
                }
                catch (Exception ex)
                {
                    ex._Error();
                }

                await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(Settings.ClockCheckIntervalMsecs));
            }

        }
        finally
        {
            // 最後に 1 回外部コマンドを実行 (シャットダウン処理)
            // cancel は無視
            string cmd = Settings.RunCommandWhileError;

            if (cmd._IsFilled())
            {
                try
                {
                    if (await Lfs.IsFileExistsAsync(cmd))
                    {
                        await EasyExec.ExecAsync(cmd);
                    }
                }
                catch (Exception ex)
                {
                    $"Command '{cmd}' execute error. Message = {ex.Message}"._Error();
                }
            }
        }
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            // TODO: ここでサーバーを終了するなどのクリーンアップ処理を行なう
            await this.LastListener._DisposeSafeAsync();

            await this.Cgi._DisposeSafeAsync();

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

