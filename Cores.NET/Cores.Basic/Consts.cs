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
        }

        public static partial class MaxLens
        {
            public const int MaxAutoCertGeneratingFqdnLen = 64;

            public const int GitCommitIdTruncateLen = 8;
            public const int StandardTruncateLen = 32;
        }

        public static partial class Ports
        {
            public const int TelnetLogWatcher = 8023;
            public const int LogServerDefaultServicePort = 7003;
            public const int LogServerDefaultHttpPort = 80;
            public const int LogServerDefaultHttpsPort = 443;

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

            public const string LogBrowserDefaultSystemTitle = "HTTP Log Browser";

            public static readonly IEnumerable<string> CommentStartString = new string[] { "#", "//", ";" };

            public static readonly IEnumerable<string> AutoEnrollCertificateSubjectInStrList = new string[] { "Let's Encrypt", "Google Internet Authority", "Google Trust Services" };

            public const string EncodeEasyPrefix = "_E_";

            public const string HidePassword = "********";

            public const string RootUsername = "root";

            public const string DaemonDefFileMarker = "hTNdwaKmxL4MNPAyyes2qsgT";
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

            public const string Filter_SourceCodes = "*.c;*.cpp;*.h;*.rc;*.stb;*.cs;*.fx;*.hlsl;*.cxx;*.cc;*.hh;*.hpp;*.hxx;*.hh;*.txt";
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

        public static partial class HttpProtocolSchemes
        {
            public const string Http = "http";
            public const string Https = "https";
        }

        public static partial class RateLimiter
        {
            public const double DefaultBurst = 100;
            public const double DefaultLimitPerSecond = 10;
            public const int DefaultExpiresMsec = 1000;
            public const int DefaultMaxEntries = 65536;
            public const int DefaultGcInterval = 10000;
        }

        public static partial class Intervals
        {
            public const int MinKeepAliveIntervalsMsec = 1 * 1000;
            public const int MaxKeepAliveIntervalsMsec = 24 * 60 * 60 * 1000;

            public const int DaemonCenterRebootRequestTimeout = 15 * 1000;

            public const int JsonRpcClientEndPointInfoUpdateInterval = 60 * 1000;
        }

        public static partial class Timeouts
        {
            public const int Rapid = 5 * 1000;
        }
    }
}
