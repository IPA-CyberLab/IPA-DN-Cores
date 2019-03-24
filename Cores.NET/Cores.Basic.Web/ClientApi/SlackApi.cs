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
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

#pragma warning disable CS0649

namespace IPA.Cores.ClientApi.SlackApi
{
    static class SlackApiHelper
    {
        public static DateTime ToDateTimeOfSlack(this decimal value) => Util.UnixTimeToDateTime((uint)value);
        public static DateTime ToDateTimeOfSlack(this long value) => Util.UnixTimeToDateTime((uint)value);
        public static long ToLongDateTimeOfSlack(this DateTime dt) => Util.DateTimeToUnixTime(dt);
    }

    class SlackApi : WebApi
    {
        public class Response : WebResponseBasic
        {
            public bool ok = false;
            public string error = null;

            public override void CheckError()
            {
                if (this.ok == false) throw new WebResponseException(error);
            }
        }

        public string ClientId { get; set; }
        public string AccessTokenStr { get; set; }

        public SlackApi(string clientId = "", string accessToken = "") : base()
        {
            this.ClientId = clientId;
            this.AccessTokenStr = accessToken;
        }

        protected override HttpRequestMessage CreateWebRequest(WebApiMethods method, string url, params (string name, string value)[] queryList)
        {
            HttpRequestMessage r = base.CreateWebRequest(method, url, queryList);

            if (this.AccessTokenStr.IsFilled())
            {
                r.Headers.Add("Authorization", $"Bearer {this.AccessTokenStr.EncodeUrl(this.RequestEncoding)}");
            }

            return r;
        }

        public string AuthGenerateAuthorizeUrl(string scope, string redirectUrl, string state = "")
        {
            return "https://slack.com/oauth/authorize?" +
                BuildQueryString(
                    ("client_id", this.ClientId),
                    ("scope", scope),
                    ("redirect_uri", redirectUrl),
                    ("state", state));
        }

        public class AccessToken : Response
        {
            public string access_token;
            public string scope;
            public string user_id;
            public string team_name;
            public string team_id;
        }

        public async Task<AccessToken> AuthGetAccessTokenAsync(string clientSecret, string code, string redirectUrl)
        {
            WebRet ret = await this.RequestWithQuery(WebApiMethods.POST, "https://slack.com/api/oauth.access",
                null,
                ("client_id", this.ClientId),
                ("client_secret", clientSecret),
                ("redirect_uri", redirectUrl),
                ("code", code));

            AccessToken a = ret.DeserializeAndCheckError<AccessToken>();

            return a;
        }

        public class ChannelsList : Response
        {
            public Channel[] Channels;
        }

        public class Value
        {
            public string value;
            public string creator;
            public long last_set;
        }

        public class Channel
        {
            public string id;
            public string name;
            public bool is_channel;
            public decimal created;
            public string creator;
            public string name_normalized;
            public Value purpose;
        }

        public class PostMessageData
        {
            public string channel;
            public string text;
            public bool as_user;
        }

        public async Task<ChannelsList> GetChannelsListAsync()
        {
            return (await RequestWithQuery(WebApiMethods.POST, "https://slack.com/api/channels.list")).DeserializeAndCheckError<ChannelsList>();
        }

        public async Task PostMessageAsync(string channelId, string text, bool asUser)
        {
            PostMessageData m = new PostMessageData()
            {
                channel = channelId,
                text = text,
                as_user = asUser,
            };

            await PostMessageAsync(m);
        }

        public async Task PostMessageAsync(PostMessageData m)
        {
            (await RequestWithJsonObject(WebApiMethods.POST, "https://slack.com/api/chat.postMessage", m)).DeserializeAndCheckError<Response>();
        }
    }
}
