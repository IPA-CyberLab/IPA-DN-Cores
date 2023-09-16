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
    public static partial class LinuxMainteDaemonHost
    {
        public static readonly Copenhagen<string> _Test = "Hello";
    }
}

public class LinuxMainteDaemonSettings : INormalizable
{
    public string ConfigUrl = "";
    public int PollingIntervalMsecs;

    public void Normalize()
    {
        if (this.ConfigUrl._IsFilled() == false)
        {
            this.ConfigUrl = "http://server_name_here/txt_filename_here.txt";
        }

        if (this.PollingIntervalMsecs <= 0)
        {
            this.PollingIntervalMsecs = 1000;
        }
    }
}


public class LinuxMainteDaemonApp : AsyncService
{
    readonly HiveData<LinuxMainteDaemonSettings> SettingsHive;

    // 'Config\LinuxMainteDaemon' のデータ
    public LinuxMainteDaemonSettings Settings => SettingsHive.GetManagedDataSnapshot();

    readonly CriticalSection LockList = new CriticalSection<LinuxMainteDaemonApp>();

    Task MainLoop1Task;

    public LinuxMainteDaemonApp()
    {
        try
        {
            // Settings を読み込む
            this.SettingsHive = new HiveData<LinuxMainteDaemonSettings>(Hive.SharedLocalConfigHive, $"LinuxMainteDaemon", null, HiveSyncPolicy.AutoReadFromFile);


            // TODO: ここでサーバーを立ち上げるなどの初期化処理を行なう
            this.MainLoop1Task = TaskUtil.StartAsyncTaskAsync(MainLoopAsync(this.GrandCancel));
        }
        catch
        {
            this._DisposeSafe();
            throw;
        }
    }

    public class UserDef
    {
        public bool DeleteMode = false;
        public string Username = "";
        public string Password = "";
        public string ForwardMail = "";

        public static bool TryParse(string line, out UserDef def)
        {
            line = line.Trim();

            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t");

            string command = tokens.FirstOrDefault("");

            if (command._IsSamei("+user"))
            {
                UserDef ret = new UserDef
                {
                    DeleteMode = false,
                    Username = tokens.ElementAtOrDefault(1)._NonNullTrim().ToLowerInvariant(),
                    Password = tokens.ElementAtOrDefault(2)._NonNullTrim(),
                    ForwardMail = tokens.ElementAtOrDefault(3)._NonNullTrim(),
                };

                if (Str.CheckMailAddress(ret.ForwardMail) == false)
                {
                    ret.ForwardMail = "";
                }

                if (Str.IsPasswordSafe(ret.Password) == false)
                {
                    ret.Password = "";
                }

                def = ret;

                if (Str.IsUsernameSafe(ret.Username) == false)
                {
                    return false;
                }

                return true;
            }
            else if (command._IsSamei("-user"))
            {
                UserDef ret = new UserDef
                {
                    DeleteMode = true,
                    Username = tokens.ElementAtOrDefault(1)._NonNullTrim().ToLowerInvariant(),
                };

                def = ret;

                if (Str.IsUsernameSafe(ret.Username) == false)
                {
                    return false;
                }

                return true;
            }

            def = new UserDef();
            return false;
        }
    }

    async Task PerformOneAsync(CancellationToken cancel)
    {
        Dbg.Where();

        var lines = await MiscUtil.ReadIncludesFileLinesAsync(this.Settings.ConfigUrl, cancel: cancel);

        List<UserDef> userDefList = new();

        HashSet<string> existingUsersList = new(StrCmpi);
        HashSet<string> disabledUsersList = new(StrCmpi);

        string[] shadowLines = (await Lfs.ReadStringFromFileAsync(@"/etc/shadow"))._GetLines(true, true, trim: true);

        foreach (var line in shadowLines)
        {
            string[] tokens = line._Split(StringSplitOptions.None, ":");
            string username = tokens.ElementAtOrDefault(0)._NonNull();
            string passwordHash = tokens.ElementAtOrDefault(1)._NonNull();

            if (username._IsFilled() && passwordHash._IsFilled())
            {
                existingUsersList.Add(username);

                if (passwordHash.StartsWith("!"))
                {
                    disabledUsersList.Add(username);
                }
            }
        }

        foreach (var line in lines)
        {
            string line2 = line._StripCommentFromLine();

            if (line2._IsFilled())
            {
                if (UserDef.TryParse(line2, out var def))
                {
                    userDefList.Add(def);
                }
            }
        }

        foreach (var def in userDefList)
        {
            if (def.DeleteMode == false)
            {
                string forwardPath = $"/home/{def.Username}/.forward";

                var forwardMailList = def.ForwardMail._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",").Distinct(StrCmpi);

                string forwardFileBody = $"# [Caution] Remove this comment line before edit manually! otherwise any change will be lost.\n\n{forwardMailList._Combine("\n")}\n\\{def.Username}\n";

                if (existingUsersList.Contains(def.Username) == false)
                {
                    if (def.Password._IsFilled())
                    {
                        Dbg.Where();
                        // まだ存在しないユーザーを只今作成します
                        await EasyExec.ExecBashAsync($"useradd -m -s /bin/bash {def.Username}");
                        await EasyExec.ExecBashAsync($"edquota -p sys_quota_default {def.Username}");
                        await EasyExec.ExecBashAsync($"passwd {def.Username}", easyInputStr: $"{def.Password}\n{def.Password}\n");

                        if (def.ForwardMail._IsFilled())
                        {
                            await Lfs.WriteStringToFileAsync(forwardPath, forwardFileBody);
                            await EasyExec.ExecBashAsync($"chown {def.Username} {forwardPath}");
                            await EasyExec.ExecBashAsync($"chgrp {def.Username} {forwardPath}");
                            await EasyExec.ExecBashAsync($"chmod 644 {forwardPath}");
                        }
                        Dbg.Where();
                    }
                }
                else
                {
                    if (disabledUsersList.Contains(def.Username))
                    {
                        Dbg.Where();
                        // すでに無効化されている既存ユーザーを只今有効化します
                        await EasyExec.ExecBashAsync($"passwd -u {def.Username}");
                    }
                    else
                    {
                        Dbg.Where();
                        // すでに存在するユーザーの .forward ファイルの内容を検査し、必要に応じて再設定します
                        bool needToSave = false;

                        if (await Lfs.IsFileExistsAsync(forwardPath) == false)
                        {
                            needToSave = true;
                        }
                        else
                        {
                            string currentBody = "";
                            try
                            {
                                currentBody = await Lfs.ReadStringFromFileAsync(forwardPath);
                            }
                            catch { }

                            if (currentBody._InStri("# [Caution]"))
                            {
                                needToSave = true;
                            }
                        }

                        if (needToSave)
                        {
                            if (def.ForwardMail._IsFilled())
                            {
                                await Lfs.WriteStringToFileAsync(forwardPath, forwardFileBody, FileFlags.WriteOnlyIfChanged);
                                await EasyExec.ExecBashAsync($"chown {def.Username} {forwardPath}");
                                await EasyExec.ExecBashAsync($"chgrp {def.Username} {forwardPath}");
                                await EasyExec.ExecBashAsync($"chmod 644 {forwardPath}");
                            }
                            else
                            {
                                try
                                {
                                    if (await Lfs.IsFileExistsAsync(forwardPath))
                                    {
                                        await Lfs.DeleteFileAsync(forwardPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ex._Error();
                                }
                            }
                        }
                    }
                }
            }
        }

        foreach (var def in userDefList)
        {
            if (def.DeleteMode)
            {
                if (existingUsersList.Contains(def.Username))
                {
                    if (disabledUsersList.Contains(def.Username) == false)
                    {
                        Dbg.Where();
                        // まだ無効化されていない既存ユーザーを只今無効化します
                        await EasyExec.ExecBashAsync($"passwd -l {def.Username}");
                    }
                }
            }
        }
    }

    async Task MainLoopAsync(CancellationToken cancel)
    {
        int numError = 0;

        while (cancel.IsCancellationRequested == false)
        {
            try
            {
                await PerformOneAsync(cancel);

                numError = 0;
            }
            catch (Exception ex)
            {
                ex._Error();
                numError++;
            }

            int nextWait = Util.GenRandIntervalWithRetry(this.Settings.PollingIntervalMsecs, numError, this.Settings.PollingIntervalMsecs * 30);

            await cancel._WaitUntilCanceledAsync(nextWait);
        }
    }


    protected override async Task CleanupImplAsync(Exception? ex)
    {
        try
        {
            // TODO: ここでサーバーを終了するなどのクリーンアップ処理を行なう
            await this.MainLoop1Task._TryWaitAsync();

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }
}

#endif

