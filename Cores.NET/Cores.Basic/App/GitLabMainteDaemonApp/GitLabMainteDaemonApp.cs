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

    public GitLabMainteClient(GitLabMainteClientSettings settings)
    {
        try
        {
            this.Settings = settings;

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
}


public class GitLabMainteDaemonSettings : INormalizable
{
    public GitLabMainteClientSettings GitLabClientSettings = null!;

    public void Normalize()
    {
        if (this.GitLabClientSettings == null)
        {
            this.GitLabClientSettings = new GitLabMainteClientSettings();
        }

        this.GitLabClientSettings.Normalize();
    }
}

public class GitLabMainteDaemonApp : AsyncService
{
    readonly HiveData<GitLabMainteDaemonSettings> SettingsHive;

    // 'Config\GitLabMainteDaemon' のデータ
    public GitLabMainteDaemonSettings Settings => SettingsHive.GetManagedDataSnapshot();

    readonly CriticalSection LockList = new CriticalSection<GitLabMainteDaemonApp>();

    readonly GitLabMainteClient GitLabClient;

    public GitLabMainteDaemonApp()
    {
        try
        {
            // Settings を読み込む
            this.SettingsHive = new HiveData<GitLabMainteDaemonSettings>(Hive.SharedLocalConfigHive, $"GitLabMainteDaemon", null, HiveSyncPolicy.AutoReadFromFile);

            this.GitLabClient = new GitLabMainteClient(this.Settings.GitLabClientSettings);

            // TODO: ここでサーバーを立ち上げるなどの初期化処理を行なう
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

            await this.GitLabClient._DisposeSafeAsync(ex);

            this.SettingsHive._DisposeSafe();
        }
        finally
        {
            await base.CleanupImplAsync(ex);
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
}

#endif

