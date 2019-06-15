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

// RFC 8555: Automatic Certificate Management Environment (ACME)
// https://tools.ietf.org/html/rfc8555

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

//using System.Net.Http;
//using System.Net.Http.Headers;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.HttpClientCore;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class AcmeClientSettings
        {
            public static readonly Copenhagen<TimeSpan> AcmeDirectoryCacheLifeTime = new TimeSpan(1, 0, 0);

            public static readonly Copenhagen<int> ShortTimeout = 5 * 1000;
        }
    }
}

namespace IPA.Cores.ClientApi.Acme
{
    class AcmeEntryPoints : IErrorCheckable
    {
        public string keyChange;
        public string newAccount;
        public string newNonce;
        public string newOrder;
        public string revokeCert;

        public void CheckError()
        {
            if (this.keyChange._IsEmpty() ||
                this.newAccount._IsEmpty() ||
                this.newNonce._IsEmpty() ||
                this.newOrder._IsEmpty() ||
                this.revokeCert._IsEmpty())
            {
                throw new ApplicationException("ACME Directory: parameter is missing.");
            }
        }
    }

    class AcmeCreateAccountPayload
    {
        public bool termsOfServiceAgreed;
        public string[] contact;
        public DateTime createdAt;
    }

    static class AcmeWellKnownServiceUrls
    {
        public const string LetsEncryptStaging = "https://acme-staging-v02.api.letsencrypt.org/directory";
        public const string LetsEncryptProduction = "https://acme-v02.api.letsencrypt.org/directory";
    }

    class AcmeClientOptions
    {
        public string DirectoryUrl { get; }
        public TcpIpSystem TcpIpSystem { get; }

        PersistentLocalCache<AcmeEntryPoints> DirectoryWebContentsCache;

        public async Task<AcmeEntryPoints> GetEntryPointsAsync(CancellationToken cancel = default)
        {
            return await this.DirectoryWebContentsCache.GetAsync(cancel);
        }

        public AcmeClientOptions(string directoryUrl = AcmeWellKnownServiceUrls.LetsEncryptStaging, TcpIpSystem tcpIp = null)
        {
            DirectoryUrl = directoryUrl;
            this.TcpIpSystem = tcpIp ?? LocalNet;

            DirectoryWebContentsCache = new PersistentLocalCache<AcmeEntryPoints>($"acme/directory_{PathParser.Windows.MakeSafeFileName(this.DirectoryUrl)}",
                CoresConfig.AcmeClientSettings.AcmeDirectoryCacheLifeTime,
                true,
                async (cancel) =>
                {
                    using (WebApi api = new WebApi(new WebApiOptions(new WebApiSettings() { SslAcceptAnyCerts = true, Timeout = CoresConfig.AcmeClientSettings.ShortTimeout })))
                    {
                        WebRet ret = await api.SimpleQueryAsync(WebApiMethods.GET, this.DirectoryUrl, cancel);

                        return ret.Deserialize<AcmeEntryPoints>(true);
                    }
                });
        }
    }

    class AcmeClient : IDisposable
    {
        public AcmeClientOptions Options { get; }

        WebApi Web;

        public AcmeClient(AcmeClientOptions options)
        {
            this.Options = options;

            this.Web = new WebApi(new WebApiOptions(new WebApiSettings() { SslAcceptAnyCerts = true, Timeout = CoresConfig.AcmeClientSettings.ShortTimeout }, Options.TcpIpSystem));
            this.Web.AddHeader("User-Agent", "AcmeClient/1.0");
        }

        public async Task<string> GetNonceAsync(CancellationToken cancel = default)
        {
            AcmeEntryPoints url = await Options.GetEntryPointsAsync(cancel);

            WebRet response = await Web.SimpleQueryAsync(WebApiMethods.HEAD, url.newNonce, cancel);

            string ret = response.Headers.GetValues("Replay-Nonce").FirstOrDefault();

            if (ret._IsEmpty()) throw new ApplicationException("Replay-Nonce is empty.");

            return ret;
        }

        public async Task<TResponse> RequestAsync<TResponse>(WebApiMethods method, PrivKey key, string url, object request, CancellationToken cancel = default)
        {
            string nonce = await GetNonceAsync(cancel);

            WebRet response = await Web.RequestWithJwsObject(method, key, nonce, url, request, cancel, Consts.MediaTypes.JoseJson);

            response.ToString()._Print();

            TResponse ret = response.Deserialize<TResponse>(true);

            return ret;
        }

        public async Task NewAccountAsync(PrivKey key, string[] contacts, CancellationToken cancel = default)
        {
            AcmeEntryPoints url = await Options.GetEntryPointsAsync(cancel);

            AcmeCreateAccountPayload req = new AcmeCreateAccountPayload()
            {
                contact = contacts,
                termsOfServiceAgreed = true,
            };

            await this.RequestAsync<object>(WebApiMethods.POST, key, url.newAccount, req, cancel);
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            this.Web._DisposeSafe();
        }
    }
}

#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

