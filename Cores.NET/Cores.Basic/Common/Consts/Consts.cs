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
        public static partial class GoldenRatioPrime
        {
            // From https://github.com/torvalds/linux/blob/88c5083442454e5e8a505b11fa16f32d2879651e/include/linux/hash.h
            public const uint U32 = 0x61C88647;
            public const ulong U64 = 0x61C8864680B583EB;

            public const int S32 = unchecked((int)(U32));
            public const long S64 = unchecked((long)(U64));
        }

        public static partial class Values
        {
            public const long MaxMatchPoint = 1_0000_0000_0000_0000;
            public const long MaxMatchPoint2 = 1_0000_0000;
        }

        public static partial class Ports
        {
            public const int TelnetLogWatcher = 8023;
            public const int LogServerDefaultServicePort = 7003;
            public const int LogServerDefaultHttpPort = 80;
            public const int LogServerDefaultHttpsPort = 443;

            public const int Http = 80;
            public const int Https = 443;
        }

        public static partial class Strings
        {
            public const string DefaultCertCN = "DefaultCertificate";
            public const string DefaultSplitStr = " ,\t\r\n";

            public static readonly IEnumerable<string> CommentStartString = new string[] { "#", "//" };
        }

        public static partial class MimeTypes
        {
            public const string Json = "application/json";
            public const string JoseJson = "application/jose+json";
            public const string FormUrlEncoded = "application/x-www-form-urlencoded";
            public const string OctetStream = "application/octet-stream";
            public const string Directory = "text/directory";
            public const string DirectoryOpening = "text/directory-open";

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

            public const string DefaultStopRootSearchFileExtsForSafety = ".sln .git";

            public static readonly IEnumerable<string> AppRootMarkerFileNames = new string[] { "approot", "appsettings.json", "appsettings.Development.json" };

            public const string ResourceRootAbsoluteDirName = "/ResourceRoot";

            public const string RootMarker_Resource = "resource_root";
            public const string RootMarker_Library_CoresBasic = "cores_basic_root";
            public const string RootMarker_Library_AspNet = "cores_aspnet_root";
        }

        public static partial class Extensions
        {
            public const string Certificate = ".crt";
            public const string Pkcs12 = ".pfx";
            public const string GenericKey = ".key";

            public const string Filter_Pkcs12s = "*.p12;*.pfx";
            public const string Filter_Certificates = "*.crt;*.cer";
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

        public static partial class OAuthScopes
        {
            public const string Slack_Client = "client";
            public const string Google_Gmail = "https://www.googleapis.com/auth/gmail.readonly";
        }

        public static partial class HtmlTarget
        {
            public const string Blank = "_blank";
        }

        public partial class InboxProviderNames
        {
            public const string Gmail = "Gmail";
            public const string Slack_App = "Slack_as_Registered_App";
            public const string Slack_User = "Slack_as_Per_User_Token";

            public const string Slack_Old = "Slack";
        }

        public partial class HttpProtocolSchemes
        {
            public const string Http = "http";
            public const string Https = "https";
        }
    }
}
