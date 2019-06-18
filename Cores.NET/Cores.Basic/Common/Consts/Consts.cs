// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
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
    static partial class Consts
    {
        public static partial class Strings
        {
            public const string DefaultCertCN = "DefaultCertificate";
        }

        public static partial class MediaTypes
        {
            public const string Json = "application/json";
            public const string JoseJson = "application/jose+json";
            public const string FormUrlEncoded = "application/x-www-form-urlencoded";
            public const string OctetStream = "application/octet-stream";
        }

        public static partial class FileNames
        {
            public const string CertVault_Settings = "settings.json";
            public const string CertVault_Password = "password.txt";

            public const string CertVault_AcmeAccountKey = "acme_account.key";
            public const string CertVault_AcmeCertKey = "acme_cert.key";

            public const string CertVault_DefaultCert = "default.pfx";
        }

        public static partial class Extensions
        {
            public const string Certificate = ".crt";
            public const string Pkcs12 = ".pfx";
            public const string GenericKey = ".key";

            public const string Filter_Pkcs12s = "*.p12;*.pfx";
            public const string Filter_Certificates = "*.crt;*.cer";
            public const string Filter_Keys = "*.key;*.pem";
        }

        public static partial class Addresses
        {
            public const string GetMyIpUrl_IPv4 = "http://get-my-ip.ddns.softether-network.net/ddns/getmyip.ashx";
            public const string GetMyIpUrl_IPv6 = "http://get-my-ip-v6.ddns.softether-network.net/ddns/getmyip.ashx";
        }
    }
}
