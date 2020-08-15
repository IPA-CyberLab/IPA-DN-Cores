// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static partial class Consts
    {
        public static partial class ExitCodes
        {
            // UNIX の制限のため、0 - 255 に限る。
            public const byte NoError = 0;
            public const byte DaemonCenterRebootRequestd_Normal = 81;
            public const byte DaemonCenterRebootRequestd_GitUpdated = 82;
        }

        public static partial class GoldenRatioPrime
        {
            // From https://github.com/torvalds/linux/blob/88c5083442454e5e8a505b11fa16f32d2879651e/include/linux/hash.h
            public const uint U32 = 0x61C88647;
            public const ulong U64 = 0x61C8864680B583EB;

            public const int S32 = unchecked((int)(U32));
            public const long S64 = unchecked((long)(U64));
        }

        public static partial class Numbers
        {
            public const long MaxMatchPoint = 1_0000_0000_0000_0000;
            public const long MaxMatchPoint2 = 1_0000_0000;
            public const long LogBrowserDefaultTailSize = 10_000;

            public const int DefaultUseStorageThreshold = 1_000_000;

            public const int DefaultSmallBufferSize = 8192;
            public const int DefaultLargeBufferSize = 65536;
            public const int DefaultVeryLargeBufferSize = 400000;

            public static readonly int MaxYear = (Util.MaxDateTimeValue.Year - 1);
            public static readonly int MinYear = Util.ZeroDateTimeValue.Year;

            public const int DefaultSendPingSize = 32;

            public const int DefaultBufferLines = 1024;

            public const int DefaultMaxBytesPerLine = 10 * 1024 * 1024;

            public const int DefaultMaxNetworkRecvSize = 30 * 1000 * 1000; // 30 MB (Kestrel default)

            public const int SignCodeServerMaxFileSize = 300 * 1024 * 1024; // 300 MB

            public const int GcTempFreq = 100;

            public const int DefaultMaxPartialFragments = 4096;

            public const int NormalJsonMaxSize = 1 * 1024 * 1024; // 1MB

            public const int MaxCookieDays = 365 + 366; // 2 Years
        }

        public static partial class MaxLens
        {
            public const int MaxAutoCertGeneratingFqdnLen = 64;

            public const int GitCommitIdTruncateLen = 8;
            public const int StandardTruncateLen = 32;
            public const int NormalStringTruncateLen = 255;

            public const int ExceptionStrTruncateLen = 800;

            public const int MaxCookieSize = 4093;

            public const int DataVaultPathElementMaxLen = 64;
        }

        public static partial class Ports
        {
            public const int TelnetLogWatcher = 8023;

            public const int LogServerDefaultHttpPort = 80;
            public const int LogServerDefaultHttpsPort = 443;

            public const int DataVaultServerDefaultHttpPort = 80;
            public const int DataVaultServerDefaultHttpsPort = 443;

            public const int Http = 80;
            public const int Https = 443;

            public const int Smtp = 25;
            public const int SmtpSubmission = 587;
            public const int Smtps = 465;
            public const int Pop3 = 110;
            public const int Pop3s = 995;
            public const int Imap4 = 143;
            public const int Imap4s = 993;

            public const int DynamicPortMin = 10000;
            public const int DynamicPortMax = 19999;
            public const int DynamicPortCheckRetryMaxCount = 200;

            public static readonly IEnumerable<int> PotentialHttpsPorts = new int[] { Https, Smtps, Pop3s, Imap4s };

            // Unique server ports
            public const int MistPPPoEServerHttp = 7001;
            public const int MistPPPoEServerHttps = 7002;
            public const int LogServerDefaultServicePort = 7003;
            public const int DaemonCenterHttps = 7004;
            public const int CodeSignServer = 7006;
            public const int SnmpWorkHttp = 7007;
            public const int SnmpWorkTelnetStat = 7008;
            public const int DataVaultServerDefaultServicePort = 7009;
        }

        public static partial class DaemonArgKeys
        {
            public const string StartLogFileBrowser = "StartLogFileBrowser";
            public const string LogFileBrowserPort = "LogFileBrowserPort";
        }

        public static partial class DaemonMetaStatKeys
        {
            public const string CurrentLogFileBrowserUrl = "CurrentLogFileBrowserUrl";
            public const string CurrentDaemonClientLocalIp = "CurrentDaemonClientLocalIp";
        }

        public static partial class Strings
        {
            public const string DefaultCertCN = "DefaultCertificate";
            public const string DefaultSplitStr = " ,\t\r\n";

            public static readonly IEnumerable<char> DefaultEnumBitsSeparaters = new char[] { ',', '|', ' ', ';', '+', ':', '.', '/', '-', '　', };

            public const string DefaultKeyAndValueSplitStr = " \t";

            public const string LogBrowserDefaultSystemTitle = "HTTP Log Browser";

            public static readonly IEnumerable<string> CommentStartString = new string[] { "#", "//", ";" };

            public static readonly IEnumerable<string> AutoEnrollCertificateSubjectInStrList = new string[] { "Let's Encrypt", "Google Internet Authority", "Google Trust Services" };

            public const string EncodeEasyPrefix = "_E_";

            public const string HidePassword = "********";

            public const string RootUsername = "root";

            public const string DaemonDefFileMarker = "hTNdwaKmxL4MNPAyyes2qsgT";

            public const string DaemonExecModeLogFileSuffix = "daemon";

            public const string EasyCookieNamePrefix = "Cores_EasyCookie_";
            public const string EasyCookieValuePrefix = "Ec_";
        }

        public static partial class HiveNames
        {
            public const string DefaultWebServer = "WebServer";
        }

        public static partial class MimeTypes
        {
            public const string Json = "application/json";
            public const string JoseJson = "application/jose+json";
            public const string FormUrlEncoded = "application/x-www-form-urlencoded";
            public const string OctetStream = "application/octet-stream";
            public const string Directory = "text/directory";
            public const string DirectoryOpening = "text/directory-open";
            public const string Html = "text/html";

            public const string Text = "text/plain";
            public const string TextUtf8 = "text/plain; charset=UTF-8";
            public const string HtmlUtf8 = "text/html; charset=UTF-8";
        }

        public static partial class FileNames
        {
            public const string CertVault_Settings = "settings.json";
            public const string CertVault_Password = "password.txt";

            public const string CertVault_AcmeAccountKey = "acme_account.key";
            public const string CertVault_AcmeCertKey = "acme_cert.key";

            public const string CertVault_DefaultCert = "default.pfx";
            public const string CertVault_AutoGeneratingCert = "ca.pfx";

            public const string DefaultStopRootSearchFileExtsForSafety = ".sln .git";

            public static readonly IEnumerable<string> AppRootMarkerFileNamesForBinary = new string[] { "approot", "appsettings.json", "appsettings.Development.json" };

            public static readonly IEnumerable<string> AppRootMarkerFileNames = new string[] { "approot", ".csproj" };

            public const string ResourceRootAbsoluteDirName = "/ResourceRoot";

            public const string SystemdConfigDirName = "/etc/systemd/system/";

            public const string RootMarker_Resource = "resource_root";
            public const string RootMarker_Library_CoresBasic = "cores_basic_root";
            public const string RootMarker_Library_CoresCodes = "cores_codes_root";
            public const string RootMarker_Library_CoresWeb = "cores_web_root";

            public const string MyDynamicTempSubDirName = "_dynamic";

            public const string AutoArchiveSubDirName = ".AutoBackup";

            public const string LogBrowserSecureJson = "_secure.json";
            public const string LogBrowserAccessLogDirName = "_accesslog";
        }

        public static partial class BlazorApp
        {
            public const string DummyImageFileName = "/tmp/dummy/webasm.exe";
            public const string DummyImageDirName = "/tmp/dummy";
            public const string DummyBuildConfigurationName = "Debug";

            public const string DummyFqdn = "webasm.example.org";
            public const int DummyProcessId = 12345;
        }

        public static partial class Extensions
        {
            public const string Certificate = ".cer";
            public const string Certificate_Acme = ".crt";
            public const string Pkcs12 = ".pfx";
            public const string GenericKey = ".key";

            public const string Filter_Pkcs12s = "*.p12;*.pfx";
            public const string Filter_Certificates = "*.cer;*.crt";
            public const string Filter_Keys = "*.key;*.pem";

            public const string Zip = ".zip";

            public const string Win32Executable = ".exe";

            public const string Filter_SourceCodes = "*.c;*.cpp;*.h;*.rc;*.stb;*.cs;*.fx;*.hlsl;*.cxx;*.cc;*.hh;*.hpp;*.hxx;*.hh;*.txt";

            public const string EncryptedXtsAes256 = "._encrypted_xtsaes256";
        }

        public static partial class Urls
        {
            public const string GetMyIpUrl_IPv4 = "http://get-my-ip.ddns.softether-network.net/ddns/getmyip.ashx";
            public const string GetMyIpUrl_IPv6 = "http://get-my-ip-v6.ddns.softether-network.net/ddns/getmyip.ashx";
        }

        public static partial class CdnUrls
        {
            public const string GmailIcon = "https://upload.wikimedia.org/wikipedia/commons/4/4e/Gmail_Icon.png";
        }

        public static partial class UrlPaths
        {
            public const string Robots = "/robots.txt";

            public const string LogBrowserMvcPath = "/LogBrowser";
        }

        public static partial class OAuthScopes
        {
            public const string Slack_Client = "client";
            public const string Google_Gmail = "https://www.googleapis.com/auth/gmail.readonly";
        }

        public static partial class HtmlTarget
        {
            public const string Blank = "_blank";
        }

        public static partial class InboxProviderNames
        {
            public const string Gmail = "Gmail";
            public const string Slack_App = "Slack_as_Registered_App";
            public const string Slack_User = "Slack_as_Per_User_Token";

            public const string Slack_Old = "Slack";
        }

        public static partial class HttpHeaders
        {
            public const string WWWAuthenticate = "WWW-Authenticate";
            public const string UserAgent = "User-Agent";
            public const string Referer = "Referer";
        }

        public static partial class HttpStatusCodes
        {
            public const int Continue = 100;
            public const int Ok = 200;
            public const int MovedPermanently = 301;
            public const int Found = 302;
            public const int NotModified = 304;
            public const int TemporaryRedirect = 307;
            public const int BadRequest = 100;
            public const int Unauthorized = 401;
            public const int Forbidden = 403;
            public const int NotFound = 404;
            public const int MethodNotAllowed = 405;
            public const int InternalServerError = 500;
            public const int NotImplemented = 501;
            public const int ServiceUnavailable = 503;
        }

        public static partial class HttpProtocolSchemes
        {
            public const string Http = "http";
            public const string Https = "https";
        }

        public static partial class RateLimiter
        {
            public const int DefaultSrcIPv4SubnetLength = 24;
            public const int DefaultSrcIPv6SubnetLength = 56;

            // RateLimiter
            public const double DefaultBurst = 200;
            public const double DefaultLimitPerSecond = 10;
            public const int DefaultExpiresMsec = 30_000;
            public const int DefaultMaxEntries = 1_000_000; // 100 万セッションまで対応!?
            public const int DefaultGcIntervalMsec = 15_000;

            // ConcurrentLimiter
            public const int DefaultMaxConcurrentRequestsPerSrcSubnet = 40;
        }

        public static partial class Intervals
        {
            public const int MinKeepAliveIntervalsMsec = 1 * 1000;
            public const int MaxKeepAliveIntervalsMsec = 24 * 60 * 60 * 1000;

            public const int JsonRpcClientEndPointInfoUpdateInterval = 60 * 1000;

            public const int AutoArchivePollingInterval = 12 * 60 * 1000;

            public const int UiAutomationDefaultInterval = 50;
        }

        public static partial class Timeouts
        {
            public const int Rapid = 5 * 1000;

            public const int DefaultSendPingTimeout = 1 * 1000;

            public const int GcTempDefaultFileLifeTime = 5 * 60 * 1000;
        }

        public static partial class LinuxCommands
        {
            public const string Bash = "/bin/bash";
            public const string Ip = "/sbin/ip";
            public const string Ifconfig = "/sbin/ifconfig";
            public const string ConnTrack = "/usr/sbin/conntrack";
            public const string PppoeDiscovery = "/usr/sbin/pppoe-discovery";
            public const string PppoeStart = "/usr/sbin/pppoe-start";
            public const string KillAll = "/usr/bin/killall";
            public const string Reboot = "/sbin/reboot";
            public const string Sync = "/bin/sync";
            public const string Ping = "/bin/ping";
            public const string Ping6 = "/bin/ping6";
            public const string IpTables = "/sbin/iptables";
            public const string Sensors = "/usr/bin/sensors";
            public const string Free = "/usr/bin/free";
            public const string Df = "/bin/df";
            public const string Birdc = "/usr/local/sbin/birdc";
            public const string Birdc6 = "/usr/local/sbin/birdc6";
        }

        public static partial class LinuxPaths
        {
            public const string SysThermal = "/sys/class/thermal/";
            public const string SockStat = "/proc/net/sockstat";
            public const string FileNr = "/proc/sys/fs/file-nr";
        }

        public static partial class StrEncodingAutoDetector
        {
            public const string Candidates = "utf-8 euc-jp shift_jis gb2312 euc-kr iso-8859-1 big5 iso-2022-jp";
        }

        public static partial class SnmpOids
        {
            public const string SnmpWorkNames = ".1.3.6.1.4.1.9801.5.29.1.1";
            public const string SnmpWorkValues = ".1.3.6.1.4.1.9801.5.29.1.2";
        }
    }

    public static partial class CoresConfig
    {
        public static partial class Timeouts
        {
            public static readonly Copenhagen<int> DaemonCenterRebootRequestTimeout = 60 * 1000;

            public static readonly Copenhagen<int> DaemonCenterGitUpdateTimeout = 3 * 60 * 1000;

            public static readonly Copenhagen<int> DaemonStopLogFinish = 60 * 1000;

            public static readonly Copenhagen<int> DefaultEasyExecTimeout = 60 * 1000;

            public static readonly Copenhagen<int> RebootDangerous_Sync_Timeout = 20 * 1000;

            public static readonly Copenhagen<int> RebootDangerous_Reboot_Timeout = 60 * 1000;

            public static readonly Copenhagen<int> GitCommandTimeout = 60 * 1000;

            public static readonly Copenhagen<int> DaemonDefaultStopTimeout = 60 * 1000;

            public static readonly Copenhagen<int> DaemonStartExecTimeout = 5 * 60 * 1000;

            public static readonly Copenhagen<int> DaemonSystemdStartTimeoutSecs = 10 * 60;

            public static readonly Copenhagen<int> DaemonSystemdStopTimeoutSecs = 2 * 60;

            // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
            public static void ApplyHeavyLoadServerConfig()
            {
                DaemonStartExecTimeout.TrySet(3 * 60 * 60 * 1000);
                DaemonCenterRebootRequestTimeout.TrySet(15 * 60 * 1000);
                DaemonCenterGitUpdateTimeout.TrySet(3 * 60 * 60 * 1000);
                DaemonStopLogFinish.TrySet(3 * 60 * 1000);
                DefaultEasyExecTimeout.TrySet(1 * 60 * 60 * 1000);
                RebootDangerous_Reboot_Timeout.TrySet(5 * 60 * 1000);
                GitCommandTimeout.TrySet(60 * 60 * 1000);
                DaemonDefaultStopTimeout.TrySet(3 * 60 * 60 * 1000);
                DaemonSystemdStartTimeoutSecs.TrySet(3 * 60 * 60);
                DaemonSystemdStopTimeoutSecs.TrySet(3 * 60 * 60);
            }
        }

        public static partial class BufferSizes
        {
            public static readonly Copenhagen<int> FileCopyBufferSize = 81920;  // .NET の Stream クラスの実装からもらってきた定数

            public static readonly Copenhagen<int> MaxNetworkStreamSendRecvBufferSize = 65536;  // ストリームソケットの送受信バッファの最大サイズ
        }
    }

    public static partial class CoresConfig
    {
        // 重いサーバー (大量のインスタンスや大量のコンテナが稼働、または大量のコネクションを処理) における定数変更
        public static void ApplyHeavyLoadServerConfigAll()
        {
            Timeouts.ApplyHeavyLoadServerConfig();
            PipeConfig.ApplyHeavyLoadServerConfig();
            FileDownloader.ApplyHeavyLoadServerConfig();
            FastBufferConfig.ApplyHeavyLoadServerConfig();
            FastMemoryPoolConfig.ApplyHeavyLoadServerConfig();
        }
    }
}

