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
    public static partial class GitLabMainteDaemonHost
    {
        public static readonly Copenhagen<string> _Test = "Hello";
    }
}

public class GitLabMainteClientSettings : INormalizable
{
    public string GitLabBaseUrl = "";
    public string PrivateToken = "";

    public void Normalize()
    {
        if (GitLabBaseUrl._IsEmpty()) GitLabBaseUrl = "https://git-lab-address-here/";
        if (PrivateToken._IsEmpty()) PrivateToken = "PrivateTokenHere";
    }
}

public class GitLabMainteClient : AsyncService
{
    public class Project
    {
        public int id;
        public string? description;
        public string? name;
        public string? path;
        public string? path_with_namespace;
        public string? default_branch;
        public string? visibility;
        public bool empty_repo;
    }

    public class User
    {
        public int id;
        public string? username;
        public string? name;
        public string? state; // active or blocked_pending_approval
        public string? commit_email;
        public bool bot;
        public bool is_admin;

        public bool IsSystemUser()
        {
            if (this.bot || this.username._IsSamei("ghost"))
            {
                return true;
            }

            return false;
        }
    }

    public class Group
    {
        public int id;
        public string? name;
        public string? path;
    }

    public class GroupMember
    {
        public int id;
        public string? username;
    }

    public GitLabMainteClientSettings Settings { get; }
    public WebApi Web { get; }
    public string GitExe { get; }

    public GitLabMainteClient(GitLabMainteClientSettings settings)
    {
        try
        {
            this.Settings = settings;

            if (Env.IsWindows)
            {
                this.GitExe = Util.GetGitForWindowsExeFileName();
            }
            else
            {
                this.GitExe = Lfs.UnixGetFullPathFromCommandName("git");
            }

            this.Web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, }));
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
            await this.Web._DisposeSafeAsync();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    public async Task<List<Project>> EnumProjectsAsync(CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrl._CombineUrl($"/api/v4/projects?private_token={this.Settings.PrivateToken}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<Project>>()!;
    }

    public async Task<List<User>> EnumUsersAsync(CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrl._CombineUrl($"/api/v4/users?private_token={this.Settings.PrivateToken}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<User>>()!;
    }

    public async Task<List<Group>> EnumGroupsAsync(CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrl._CombineUrl($"/api/v4/groups?private_token={this.Settings.PrivateToken}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<Group>>()!;
    }

    public async Task<List<GroupMember>> EnumGroupMembersAsync(int groupId, CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrl._CombineUrl($"/api/v4/groups/{groupId}/members?private_token={this.Settings.PrivateToken}").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

        return res.ToString()._JsonToObject<List<GroupMember>>()!;
    }

    public async Task JoinUserToGroupAsync(int userId, int groupId, int accessLevel = 30, CancellationToken cancel = default)
    {
        string url = this.Settings.GitLabBaseUrl._CombineUrl($"/api/v4/groups/{groupId}/members").ToString();

        var res = await this.Web.SimpleQueryAsync(WebMethods.POST, url, cancel, Consts.MimeTypes.FormUrlEncoded,
            ("private_token", this.Settings.PrivateToken),
            ("user_id", userId.ToString()),
            ("expires_at", accessLevel.ToString()),
            ("access_level", accessLevel.ToString()),
            ("expires_at", "9931-12-21")
            );
    }

    public async Task GitPullFromRepositoryAsync(string repositoryPath, string localDir, string branch, CancellationToken cancel = default)
    {
        string gitUrl = this.Settings.GitLabBaseUrl._CombineUrl(repositoryPath + ".git").ToString();

        gitUrl = gitUrl._ReplaceStr("https://", $"https://oauth2:{this.Settings.PrivateToken}@");

        await Lfs.CreateDirectoryAsync(localDir);

        // localDir ディレクトリに .git ディレクトリは存在するか?
        string dotgitDir = localDir._CombinePath(".git");

        bool init = false;

        StrDictionary<string> envVars = new StrDictionary<string>();

        // empty config
        string emptyCfgPath = Env.MyLocalTempDir._CombinePath("empty.txt");
        if (await Lfs.IsFileExistsAsync(emptyCfgPath, cancel) == false)
        {
            await Lfs.WriteStringToFileAsync(emptyCfgPath, "\n\n", cancel: cancel);
        }

        envVars.Add("GIT_CONFIG_GLOBAL", emptyCfgPath);
        envVars.Add("GIT_CONFIG_SYSTEM", emptyCfgPath);

        if (await Lfs.IsDirectoryExistsAsync(dotgitDir, cancel))
        {
            try
            {
                // update を試みる
                await EasyExec.ExecAsync(this.GitExe, $"pull origin {branch}", localDir, cancel: cancel, additionalEnvVars: envVars);
            }
            catch (Exception ex)
            {
                ex._Error();
                init = true;
            }
        }
        else
        {
            init = true;
        }

        if (init)
        {
            // 初期化する
            await Lfs.DeleteDirectoryAsync(localDir, true, cancel: cancel, forcefulUseInternalRecursiveDelete: true);

            // git clone をする
            await EasyExec.ExecAsync(this.GitExe, $"clone {gitUrl} {localDir}", cancel: cancel, additionalEnvVars: envVars);

            // update を試みる
            await EasyExec.ExecAsync(this.GitExe, $"pull origin {branch}", localDir, cancel: cancel, additionalEnvVars: envVars);
        }
    }
}


public class GitLabMainteDaemonSettings : INormalizable
{
    public GitLabMainteClientSettings GitLabClientSettings = null!;

    public SmtpClientSettings SmtpSettings = null!;

    public SmtpBassicSettings MailSettings = null!;

    public List<string> DefaultGroupsAllUsersWillJoin = new List<string>();

    public int UsersPollIntervalMsecs = 0;

    public string GitMirrorDataRootDir = "";
    public string GitWebDataRootDir = "";

    public void Normalize()
    {
        this.GitLabClientSettings ??= new GitLabMainteClientSettings();
        this.GitLabClientSettings.Normalize();

        this.SmtpSettings ??= new SmtpClientSettings();
        this.SmtpSettings.Normalize();

        this.MailSettings ??= new SmtpBassicSettings();
        this.MailSettings.Normalize();

        this.DefaultGroupsAllUsersWillJoin ??= new List<string>();
        if (this.DefaultGroupsAllUsersWillJoin.Count == 0)
        {
            this.DefaultGroupsAllUsersWillJoin.Add("__default_groups_here__");
        }
        this.DefaultGroupsAllUsersWillJoin = this.DefaultGroupsAllUsersWillJoin.Distinct(StrCmpi).ToList();

        if (this.UsersPollIntervalMsecs <= 0) this.UsersPollIntervalMsecs = 5 * 1000;

        if (GitMirrorDataRootDir._IsEmpty())
        {
            if (Env.IsWindows)
            {
                GitMirrorDataRootDir = @"c:\tmp2\git_mirror_root\";
            }
            else
            {
                GitMirrorDataRootDir = @"/data1/git_mirror_root/";
            }
        }

        if (GitWebDataRootDir._IsEmpty())
        {
            if (Env.IsWindows)
            {
                GitWebDataRootDir = @"c:\tmp2\git_web_root\";
            }
            else
            {
                GitWebDataRootDir = @"/data1/git_web_root/";
            }
        }
    }
}

public class GitLabMainteDaemonApp : AsyncService
{
    public class PublishConfigData
    {
        public string Username = "";
        public string Password = "";
    }

    readonly HiveData<GitLabMainteDaemonSettings> SettingsHive;

    // 'Config\GitLabMainteDaemon' のデータ
    public GitLabMainteDaemonSettings Settings => SettingsHive.GetManagedDataSnapshot();

    readonly CriticalSection LockList = new CriticalSection<GitLabMainteDaemonApp>();

    public GitLabMainteClient GitLabClient { get; }

    Task MainLoop1Task;

    public GitLabMainteDaemonApp()
    {
        try
        {
            // Settings を読み込む
            this.SettingsHive = new HiveData<GitLabMainteDaemonSettings>(Hive.SharedLocalConfigHive, $"GitLabMainteDaemon", null, HiveSyncPolicy.AutoReadFromFile);

            this.GitLabClient = new GitLabMainteClient(this.Settings.GitLabClientSettings);

            // TODO: ここでサーバーを立ち上げるなどの初期化処理を行なう

            this.MainLoop1Task = TaskUtil.StartAsyncTaskAsync(Loop1_MainteUsersAsync(this.GrandCancel));
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
            // TODO: ここでサーバーを終了するなどのクリーンアップ処理を行なう
            await this.MainLoop1Task._TryWaitAsync();

            await this.GitLabClient._DisposeSafeAsync(ex);

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
        }
    }

    async Task Loop1_MainteUsersAsync(CancellationToken cancel = default)
    {
        List<GitLabMainteClient.User> lastPendingUsers = new List<GitLabMainteClient.User>();

        while (cancel.IsCancellationRequested == false)
        {
            // 新規申請中のユーザーが増えたらメールで知らせる
            try
            {
                // ユーザーの列挙
                var users = await this.GitLabClient.EnumUsersAsync(cancel);

                var pendingUsers = users.Where(x => x.IsSystemUser() == false && x.state == "blocked_pending_approval").OrderBy(x => x.id);

                var newPendingUsers = pendingUsers.Where(u => lastPendingUsers.Where(a => a.id == u.id).Any() == false);

                StringWriter w = new StringWriter();

                string url = this.Settings.GitLabClientSettings.GitLabBaseUrl._CombineUrl("/admin/users?filter=blocked_pending_approval").ToString();

                string subject = $"{url._ParseUrl().Host} にユーザー {newPendingUsers.Select(x => x.commit_email._NonNullTrim())._Combine(" ,")} の参加申請がありました";

                w.WriteLine(subject + "。");
                w.WriteLine();

                w.WriteLine($"GitLab のアドレス: {url}");
                w.WriteLine();

                w.WriteLine($"現在時刻: {DtOffsetNow._ToDtStr()}");

                w.WriteLine();

                w.WriteLine($"新しい申請中のユーザー ({newPendingUsers.Count()}):");

                int num = 0;

                foreach (var user in newPendingUsers)
                {
                    lastPendingUsers.Add(user._CloneDeep());

                    w.WriteLine("- " + user.name + " " + user.commit_email);

                    num++;
                }

                w.WriteLine();

                w.WriteLine($"現在申請中のユーザー一覧 ({pendingUsers.Count()})");

                foreach (var user in pendingUsers)
                {
                    w.WriteLine("- " + user.name + " " + user.commit_email);
                }

                w.WriteLine();

                w.WriteLine($"GitLab のアドレス: {url}");
                w.WriteLine();
                w.WriteLine();

                //Dbg.Where();
                if (num >= 1)
                {
                    await this.SendMailAsync(subject, w.ToString(), cancel);
                }
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            // すべてのユーザーをデフォルトグループに自動追加する
            try
            {
                await this.JoinAllUsersToSpecificGroupAsync(this.Settings.DefaultGroupsAllUsersWillJoin, cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }

            await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(this.Settings.UsersPollIntervalMsecs));
        }
    }

    public async Task SendMailAsync(string subject, string body, CancellationToken cancel = default)
    {
        foreach (var dest in this.Settings.MailSettings.MailToList.Distinct(StrCmpi).OrderBy(x => x, StrCmpi))
        {
            await SmtpUtil.SendAsync(this.Settings.SmtpSettings, this.Settings.MailSettings.MailFrom, dest, subject, body, true, cancel);
        }
    }

    public async Task JoinAllUsersToSpecificGroupAsync(IEnumerable<string> groupNamesList, CancellationToken cancel = default)
    {
        var groups = await this.GitLabClient.EnumGroupsAsync(cancel);

        var users = await this.GitLabClient.EnumUsersAsync(cancel);

        var activeUsers = users.Where(u => u.state._IsSamei("active") && u.IsSystemUser() == false);

        var targetGroups = groups.Where(g => groupNamesList.Where(s => s._IsSamei(g.name)).Any()).OrderBy(g => g.id);

        foreach (var group in targetGroups)
        {
            var members = await this.GitLabClient.EnumGroupMembersAsync(group.id, cancel);

            var usersNotInThisGroup = activeUsers.Where(u => members.Where(m => m.id == u.id).Any() == false).OrderBy(u => u.id);

            foreach (var user in usersNotInThisGroup)
            {
                Con.WriteLine($"Joining User {user.name} ({user.commit_email}) to Group {group.name} ...");

                try
                {
                    await this.GitLabClient.JoinUserToGroupAsync(user.id, group.id, cancel: cancel);

                    Con.WriteLine("OK.");
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }
        }
    }

    public async Task SyncGitLocalRepositoryDirToWebRootDirAsync(string gitLocalRootDir, string webLocalRootDir, CancellationToken cancel = default)
    {
        string gitCommitId = await Lfs.GetCurrentGitCommitIdAsync(gitLocalRootDir, cancel);

        var param = new CopyDirectoryParams(
              CopyDirectoryFlags.AsyncCopy | CopyDirectoryFlags.Overwrite | CopyDirectoryFlags.Recursive | CopyDirectoryFlags.DeleteNotExistDirs | CopyDirectoryFlags.DeleteNotExistFiles | CopyDirectoryFlags.SilenceSuccessfulReport,
              FileFlags.WriteOnlyIfChanged,
              new FileMetadataCopier(FileMetadataCopyMode.TimeAll),
              new FileMetadataCopier(FileMetadataCopyMode.TimeAll),
              determineToCopyCallback: (d, e) =>
              {
                  if (e.IsFile && e.Name._IsSamei(Consts.FileNames.GitLabMainte_PublishFileName)) return false;
                  if (e.IsDirectory && e.Name._IsSamei(".git")) return false;
                  return true;
              },
              determineToDeleteCallback: (e) =>
              {
                  if (e.IsFile && e.Name._IsSamei(Consts.FileNames.GitLabMainte_CommitIdFileName)) return false;
                  if (e.IsFile && e.Name._IsSamei(Consts.FileNames.LogBrowserSecureJson)) return false;
                  return true;
              }
              );

        string publishPath = gitLocalRootDir._CombinePath(Consts.FileNames.GitLabMainte_PublishFileName);

        await Lfs.CopyDirAsync(gitLocalRootDir, webLocalRootDir, param: param, cancel: cancel);

        await Lfs.WriteStringToFileAsync(webLocalRootDir._CombinePath(Consts.FileNames.GitLabMainte_CommitIdFileName),
            gitCommitId + "\n",
            FileFlags.WriteOnlyIfChanged,
            cancel: cancel);

        PublishConfigData? config = null;

        try
        {
            config = await Lfs.ReadJsonFromFileAsync<PublishConfigData>(publishPath, cancel: cancel, nullIfError: true);
        }
        catch
        {
            if (await Lfs.IsFileExistsAsync(publishPath))
            {
                config = new PublishConfigData();
            }
            else
            {
                config = null;
            }
        }

        string secureJsonPath = webLocalRootDir._CombinePath(Consts.FileNames.LogBrowserSecureJson);

        if (config == null)
        {
            try
            {
                await Lfs.DeleteFileAsync(secureJsonPath, cancel: cancel);
            }
            catch (Exception ex)
            {
                ex._Error();
            }
        }
        else
        {
            bool requireAuth = config.Username._IsFilled() && config.Password._IsFilled();

            LogBrowserSecureJson sj = new LogBrowserSecureJson();

            sj.AuthRequired = requireAuth;

            if (requireAuth)
            {
                sj.AuthDatabase = new KeyValueList<string, string>();
                sj.AuthDatabase.Add(config.Username, config.Password);
                sj.AuthSubDirName = "auth";
            }
            sj.DisableAccessLog = false;
            sj.AllowZipDownload = true;

            await Lfs.WriteJsonToFileAsync(secureJsonPath, sj, FileFlags.WriteOnlyIfChanged, cancel: cancel);
        }
    }
}

#endif
