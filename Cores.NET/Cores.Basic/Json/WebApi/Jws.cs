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

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY

using System;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;

using Newtonsoft.Json;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class JwsPacket
    {
        [JsonProperty("protected")]
        public string Protected;

        public string payload;

        public string signature;
    }

    class JwsRsaJwk
    {
        public string kty;
        public string crv;
        public string x;
        public string y;
    }

    class JwsProtected
    {
        public string alg;
        public JwsRsaJwk jwk;
        public string nonce;
        public string url;
    }

    static class JwsUtil
    {
        public static JwsPacket Encapsulate(PrivKey key, string nonce, string url, object payload)
        {
            if (key.Algorithm != PkiAlgorithm.ECDSA) throw new ArgumentException("key.Algorithm != PkiAlgorithm.ECDSA");
            if (key.BitsSize != 256) throw new ArgumentException("key.BitsSize != 256");

            JwsRsaJwk jwk = new JwsRsaJwk()
            {
                kty = "EC",
                crv = "P-" + key.PublicKey.BitsSize,
                x = key.PublicKey.EcdsaParameters.Q.AffineXCoord.GetEncoded()._Base64UrlEncode(),
                y = key.PublicKey.EcdsaParameters.Q.AffineYCoord.GetEncoded()._Base64UrlEncode(),
            };

            JwsProtected protect = new JwsProtected()
            {
                alg = "ES256",
                jwk = jwk,
                nonce = nonce,
                url = url,
            };

            JwsPacket ret = new JwsPacket()
            {
                Protected = protect._ObjectToJson(base64url: true),
                payload = payload._ObjectToJson(base64url: true),
            };

            var signer = key.GetSigner();

            ret.signature = signer.Sign((ret.Protected + "." + ret.payload)._GetBytes_Ascii())._Base64UrlEncode();

            return ret;
        }
    }

    partial class WebApi
    {
        public virtual async Task<WebRet> RequestWithJwsObject(WebApiMethods method, PrivKey privKey, string nonce, string url, object payload)
        {
            JwsPacket reqPacket = JwsUtil.Encapsulate(privKey, nonce, url, payload);

            return await this.RequestWithJsonObject(method, url, reqPacket);
        }
    }
}

#endif  // CORES_BASIC_SECURITY
#endif  // CORES_BASIC_JSON

