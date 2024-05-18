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
    }
}

public class FntpMainteHealthStatus
{
    public bool HasUnknownError;
    public bool? HasTimeDateCtlCommandError;
    public bool? IsNtpDaemonActive;
    public bool? IsNtpDaemonSynced;
    public bool? IsRtcCorrect;
    public bool? IsNtpClockCorrect;
    public bool? IsSystemClockCorrect;
    public bool? IsDateTimeNowCorrect;

    public string ToInternalCompareStr() => $"{HasUnknownError}_{HasTimeDateCtlCommandError}_{IsNtpDaemonActive}_{IsNtpDaemonSynced}_{IsRtcCorrect}_{IsNtpClockCorrect}_{IsSystemClockCorrect}_{IsDateTimeNowCorrect}";

    public override string ToString()
    {
        return this._ObjectToJson(includeNull: true, compact: true);
    }

    public List<string> ErrorList = new List<string>();
}


public class FntpMainteDaemonApp : AsyncServiceWithMainLoop
{
    readonly HiveData<FntpMainteDaemonSettings> SettingsHive;

    // 'Config\FntpMainteDaemon' のデータ
    public FntpMainteDaemonSettings Settings => SettingsHive.GetManagedDataSnapshot();

    readonly CriticalSection LockList = new CriticalSection<FntpMainteDaemonApp>();

    public FntpMainteDaemonApp()
    {
        try
        {
            // Settings を読み込む
            this.SettingsHive = new HiveData<FntpMainteDaemonSettings>(Hive.SharedLocalConfigHive, $"FntpMainteDaemon", null, HiveSyncPolicy.AutoReadFromFile);


            // TODO: ここでサーバーを立ち上げるなどの初期化処理を行なう
            this.StartMainLoop(MainLoopAsync);
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    // 健康状態チェック実行
    public async Task<FntpMainteHealthStatus> CheckHealthAsync(CancellationToken cancel = default)
    {
        FntpMainteHealthStatus ret = new FntpMainteHealthStatus();

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
            if (ret.IsDateTimeNowCorrect == false)
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
                        // 127.0.0.1 の NTPd からの応答結果を取得
                        var dt = await TaskUtil.RetryAsync(async () =>
                        {
                            return await LinuxTimeDateCtlUtil.ExecuteNtpDigAndReturnResultDateTimeAsync("127.0.0.1", 2000, cancel);
                        },
                        250, 10, cancel, true);

                        if (dt._IsZeroDateTime())
                        {
                            throw new CoresException($"ntpdig: Returned datetime was invalid: {dt._ToDtStr()}");
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
                                // 比較に成功したが、時差が許容範囲を超えていた
                                return new OkOrExeption(new CoresException($"ntpdig Health Check Failed: {res.Exception.Message}"));
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

    // 定期的に実行されるチェック処理の実装
    async Task MainProcAsync(CancellationToken cancel = default)
    {
        Where();

        var res = await CheckHealthAsync(cancel: cancel);

        res._PrintAsJson();
    }

    // メインループ
    async Task MainLoopAsync(CancellationToken cancel = default)
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


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            // TODO: ここでサーバーを終了するなどのクリーンアップ処理を行なう

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

