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
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
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
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;

namespace IPA.Cores.Basic;

public class LtsOpenSslTool
{
    // -- 定数部分  https://lts.dn.ipantt.net/d/211117_002_lts_openssl_exesuite_09221/ を参照のこと --
    public const string BaseUrl = "https://lts.dn.ipantt.net/d/211117_002_lts_openssl_exesuite_09221/";

    public const string VersionYymmdd = "211117";

    public const string ExeNames = @"
lts_openssl_exesuite_0.9.8zh
lts_openssl_exesuite_1.0.2u
lts_openssl_exesuite_1.1.1l
lts_openssl_exesuite_3.0.0
";

    public enum Arch
    {
        linux_arm64,
        linux_x64,
        windows_x64,
    }

    public record Version(string ExeName, Arch Arch);

    public static readonly IReadOnlyList<Version> VersionList = new List<Version>();

    static LtsOpenSslTool()
    {
        List<Version> tmp = new();

        var tokens = ExeNames._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, '\r', '\n', ' ', '\t', '　');
        var archList = Arch.linux_arm64.GetEnumValuesList();

        foreach (var arch in archList)
        {
            foreach (var exe in tokens)
            {
                tmp.Add(new Version(exe, arch));
            }
        }

        VersionList = tmp;
    }

    public static Arch GetCurrentArchiecture()
    {
        if (Env.IsWindows) return Arch.windows_x64;
        if (Env.IsLinux)
        {
            if (Env.CpuInfo == Architecture.Arm64) return Arch.linux_arm64;
            if (Env.CpuInfo == Architecture.X64) return Arch.linux_x64;
        }

        throw new CoresLibException("Suitable architecture not found.");
    }

    public static IEnumerable<Version> GetCurrentSuitableVersions()
    {
        var arch = GetCurrentArchiecture();

        return VersionList.Where(x => x.Arch == arch).OrderBy(x => x.ExeName, StrComparer.IgnoreCaseTrimComparer);
    }

    // OpenSSL コマンドの実行
    public static async Task ExecOpenSslCommandAsync(Version ver, string cmd, CancellationToken cancel = default)
    {
        // URL を生成する
        string exeName = ver.ExeName;
        if (ver.Arch == Arch.windows_x64) exeName += ".exe";
        string url = BaseUrl._CombineUrlDir(VersionYymmdd)._CombineUrlDir("lts_openssl_exesuite")._CombineUrlDir(ver.Arch.ToString())._CombineUrl(exeName).ToString();

        // 一時ディレクトリ名を生成する
        string tmpDirPath = PP.Combine(Env.AppLocalDir, "tmp", "lts_openssl_cache", VersionYymmdd);
        string tmpExePath = PP.Combine(tmpDirPath, exeName);

        string lockName = tmpDirPath.ToLower() + "_lock_" + ver._ObjectToJson()._HashSHA1()._GetHexString();

        GlobalLock lockObject = new GlobalLock(lockName);

        // 以下の操作はロック下で実施する
        using (lockObject.Lock())
        {
            bool exists = false;

            // ファイルが存在しているか?
            if (Lfs.IsFileExists(tmpExePath, cancel))
            {
                var info = Lfs.GetFileMetadata(tmpExePath, cancel: cancel);
                if (info.Size >= 1_000_000)
                {
                    exists = true;
                }
            }

            if (exists == false)
            {
                // ファイルをダウンロードする
                var res = await SimpleHttpDownloader.DownloadAsync(url);
                if (res.DataSize < 1_000_000)
                {
                    throw new CoresLibException($"URL {url} file size too small: {res.DataSize} bytes");
                }

                // ダウンロードしたファイルを一時ファイルに保存する
                string tmpExePath2 = tmpExePath + ".tmp";

                Lfs.WriteDataToFile(tmpExePath2, res.Data, FileFlags.AutoCreateDirectory, cancel: cancel);

                // 保存が完了したらリネームする
                try
                {
                    Lfs.DeleteFile(tmpExePath, cancel: cancel);
                }
                catch { }

                Lfs.MoveFile(tmpExePath2, tmpExePath, cancel);

                tmpExePath._Print();
            }
        }
    }
}

#endif

