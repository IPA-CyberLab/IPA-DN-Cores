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
using System.Security.Authentication;

namespace IPA.Cores.Basic;

public class LtsOpenSslTool
{
    // -- 定数部分  https://lts.dn.ipantt.net/d/211117_002_lts_openssl_exesuite_09221/ を参照のこと --
    public const string BaseUrl = "https://lts.dn.ipantt.net/d/211117_002_lts_openssl_exesuite_09221/";

    public const string VersionYymmdd = "211117";

    public const int CommandTimeoutMsecs = 5 * 1000;

    public const string Def_OpenSslExeNamesAndSslVers = @"
lts_openssl_exesuite_0.9.8zh                ssl3 tls1 tls1_1
lts_openssl_exesuite_1.0.2u                 ssl3 tls1 tls1_1 tls1_2
lts_openssl_exesuite_1.1.1l                 ssl3 tls1 tls1_1 tls1_2
lts_openssl_exesuite_3.0.0                  ssl3 tls1 tls1_1 tls1_2 tls1_3
";

    public const string Def_Ciphers = @"
RC4-SHA                                     ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DES-CBC3-SHA                                ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES128-SHA                                  ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES256-SHA                                  ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES128-SHA                          ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES256-SHA                          ssl3 tls1 tls1_1 tls1_2     lts_openssl_exesuite_0.9.8zh lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES128-GCM-SHA256                           tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES256-GCM-SHA384                           tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
AES256-SHA256                               tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES128-GCM-SHA256                   tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES128-SHA256                       tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES256-GCM-SHA384                   tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-AES256-SHA256                       tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES128-GCM-SHA256                 tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES128-SHA256                     tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES256-GCM-SHA384                 tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-AES256-SHA384                     tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
DHE-RSA-CHACHA20-POLY1305                   tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
ECDHE-RSA-CHACHA20-POLY1305                 tls1_2                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
TLS_CHACHA20_POLY1305_SHA256                tls1_3                      lts_openssl_exesuite_1.0.2u lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
TLS_AES_256_GCM_SHA384                      tls1_3                      lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
TLS_AES_128_GCM_SHA256                      tls1_3                      lts_openssl_exesuite_1.1.1l lts_openssl_exesuite_3.0.0
";

    public enum Arch
    {
        linux_arm64,
        linux_x64,
        windows_x64,
    }

    public record Version(string ExeName, Arch Arch, IReadOnlySet<string> sslVersions);
    public record Cipher(string name, IReadOnlySet<string> sslVersions, IReadOnlySet<string> exeNames);

    public static readonly IReadOnlyList<Version> VersionList = new List<Version>();

    static LtsOpenSslTool()
    {
        List<Version> verList = new();
        List<Cipher> cipherList = new();

        string[] lines = Def_OpenSslExeNamesAndSslVers._GetLines(true, true);
        var archList = Arch.linux_arm64.GetEnumValuesList();

        foreach (var arch in archList)
        {
            foreach (var line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　");
                if (tokens.Length >= 2)
                {
                    HashSet<string> sslVerList = new HashSet<string>(StrComparer.IgnoreCaseComparer);
                    for (int i = 1; i < tokens.Length; i++)
                    {
                        sslVerList.Add(tokens[i]);
                    }

                    verList.Add(new Version(tokens[0], arch, sslVerList));
                }
            }
        }

        lines = Def_Ciphers._GetLines(true, true);

        foreach (var line in lines)
        {
            string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ", "\t", "　");
            if (tokens.Length >= 3)
            {
                HashSet<string> sslVerList = new HashSet<string>(StrComparer.IgnoreCaseComparer);
                HashSet<string> exeNamesList = new HashSet<string>(StrComparer.IgnoreCaseComparer);

                for (int i = 1; i < tokens.Length; i++)
                {
                    string a = tokens[i];
                    if (a._StartsWithMulti(StringComparison.OrdinalIgnoreCase, "ssl", "tls"))
                    {
                        sslVerList.Add(a);
                    }
                    else if (a.StartsWith("lts_", StringComparison.OrdinalIgnoreCase))
                    {
                        exeNamesList.Add(a);
                    }
                    else
                    {
                        throw new CoresLibException();
                    }

                    cipherList.Add(new Cipher(tokens[0], sslVerList, exeNamesList));
                }
            }
        }

        VersionList = verList;
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

    // OpenSSL での SSL 接続テストの実行
    public static async Task ExecOpenSslClientConnectTest(Version ver, string host, int port, string protoVerStr, string? cipher = null, CancellationToken cancel = default)
    {
        string args = $"s_client -connect {host}:{port} -{protoVerStr}";
        if (cipher._IsFilled())
        {
            if (protoVerStr._IsSamei("tls1_3"))
            {
                args += $" -ciphersuites {cipher}";
            }
            else
            {
                args += $" -cipher {cipher}";
            }
        }

        bool ok = false;
        string error = "";

        try
        {
            var res = await ExecOpenSslCommandAsync(ver, args,
                async line =>
                {
                    // OpenSSL コマンドが出力する標準出力またはエラー文字列をもとに状態を判断する
                    string tmp = line._NonNullTrim();

                    if (tmp._TryTrimStartWith(out string tmp2, StringComparison.OrdinalIgnoreCase, "Session-ID:"))
                    {
                        tmp2 = tmp2._NonNullTrim();
                        byte[] sessionKey = tmp2._GetHexBytes();
                        if (sessionKey._IsZero() == false && sessionKey.Length >= 16)
                        {
                            // OK
                            ok = true;

                            // もうプロセスは終了してよい
                            return false;
                        }
                    }

                    var tokens = tmp._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ":");
                    if (tokens.ElementAtOrDefault(1)._NonNull().StartsWith("error", StringComparison.OrdinalIgnoreCase))
                    {
                        // RC4-MD5 で発生するような SSL エラーが発生している。
                        // その後 Session-ID: が一見正常に届くように見えるが、このエラーが発生している時点で
                        // 通信はうまくできないのである。
                        ok = false;

                        error = tmp;
                        return false;
                    }

                    await Task.CompletedTask;

                    return true;
                },
                cancel);
        }
        catch (Exception ex)
        {
            if (ok == false)
            {
                if (error._IsEmpty())
                {
                    error = ex.Message;
                }
            }
        }

        if (ok == false)
        {
            if (error._IsEmpty())
            {
                error = "Unknown error";
            }
        }

        if (error._IsFilled())
        {
            throw new CoresException(error);
        }
    }

    // OpenSSL コマンドの実行
    public static async Task<EasyExecResult> ExecOpenSslCommandAsync(Version ver, string args,
            Func<string, Task<bool>>? easyOneLineRecvCallbackAsync = null, CancellationToken cancel = default)
    {
        var exePath = await PrepareOpenSslExeAsync(ver, cancel);

        return await EasyExec.ExecAsync(exePath, args, PP.GetDirectoryName(exePath),
            ExecFlags.EasyInputOutputMode /*| ExecFlags.EasyPrintRealtimeStdErr | ExecFlags.EasyPrintRealtimeStdOut*/ | ExecFlags.KillProcessGroup,
            10_000_000,
            cancel: cancel,
            printTag: ver.ExeName,
            timeout: CommandTimeoutMsecs,
            easyOneLineRecvCallbackAsync: easyOneLineRecvCallbackAsync);
    }

    // OpenSSL コマンドの実行
    public static async Task<string> PrepareOpenSslExeAsync(Version ver, CancellationToken cancel = default)
    {
        // URL を生成する
        string exeName = ver.ExeName;
        if (ver.Arch == Arch.windows_x64) exeName += ".exe";
        string url = BaseUrl._CombineUrlDir(VersionYymmdd)._CombineUrlDir("lts_openssl_exesuite")._CombineUrlDir(ver.Arch.ToString())._CombineUrl(exeName).ToString();

        // 一時ディレクトリ名を生成する
        string tmpDirPath = PP.Combine(Env.AppLocalDir, "Temp", "lts_openssl_cache", VersionYymmdd);
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

        return tmpExePath;
    }
}

#endif

