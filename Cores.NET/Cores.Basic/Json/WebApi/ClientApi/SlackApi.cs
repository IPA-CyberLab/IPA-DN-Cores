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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using IPA.Cores.Basic.HttpClientCore;
using static IPA.Cores.Globals.Basic;

//using System.Net.Http;
//using System.Net.Http.Headers;

#pragma warning disable CS0649

namespace IPA.Cores.ClientApi.SlackApi
{
    static class SlackApiHelper
    {
        public static DateTimeOffset _ToDateTimeOfSlack(this decimal value) => Util.UnixTimeToDateTime((uint)value);
        public static DateTimeOffset _ToDateTimeOfSlack(this long value) => Util.UnixTimeToDateTime((uint)value);

        public static long _ToLongDateTimeOfSlack(this DateTimeOffset dt) => Util.DateTimeToUnixTime(dt.UtcDateTime);
    }

    class SlackApi : WebApi
    {
        public class ResponseMetadata
        {
            public string next_cursor;
        }

        public abstract class SlackResponseBase : IErrorCheckable
        {
            public bool ok;
            public string error;
            public ResponseMetadata response_metadata;

            public void CheckError()
            {
                if (ok == false)
                    throw new ApplicationException(error._FilledOrDefault("Slack response unknown error"));
            }
        }

        public string ClientId { get; set; }
        public string AccessTokenStr { get; set; }

        public SlackApi(string clientId = "", string accessToken = "") : base()
        {
            this.ClientId = clientId;
            this.AccessTokenStr = accessToken;
        }

        protected override HttpRequestMessage CreateWebRequest(WebMethods method, string url, params (string name, string value)[] queryList)
        {
            HttpRequestMessage r = base.CreateWebRequest(method, url, queryList);

            if (this.AccessTokenStr._IsFilled())
            {
                r.Headers.Add("Authorization", $"Bearer {this.AccessTokenStr._EncodeUrl(this.RequestEncoding)}");
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

        public class AccessToken : SlackResponseBase
        {
            public string access_token;
            public string scope;
            public string user_id;
            public string team_name;
            public string team_id;
        }

        public async Task<AccessToken> AuthGetAccessTokenAsync(string clientSecret, string code, CancellationToken cancel = default)
        {
            WebRet ret = await this.SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/oauth.access", cancel,
                null,
                ("client_id", this.ClientId),
                ("client_secret", clientSecret),
//                ("redirect_uri", redirectUrl),
                ("code", code));

            AccessToken a = ret.Deserialize<AccessToken>(true);

            return a;
        }

        public class ChannelsList : SlackResponseBase
        {
            public Channel[] channels;
        }

        public class Value
        {
            public string value;
            public string creator;
            public long last_set;
        }
        
        public class ConversationInfo : SlackResponseBase
        {
            public Channel channel;
        }

        public class Channel
        {
            public string id;
            public string name;
            public bool is_channel;
            public bool is_group;
            public bool is_im;
            public decimal created;
            public string creator;
            public bool is_archived;
            public bool is_member;
            public bool is_private;
            public bool is_mpim;
            public string name_normalized;
            public Value purpose;
            public decimal last_read;

            public bool IsTarget()
            {
                if (this.is_archived) return false;
                if (this.is_channel && this.is_member == false) return false;

                return true;
            }
        }

        public class PostMessageData
        {
            public string channel;
            public string text;
            public bool as_user;
        }

        public class RealtimeResponse : SlackResponseBase
        {
            public string url;
        }

        public class Profile
        {
            public string image_512;
            public string real_name;
        }

        public class User
        {
            public string id;
            public string name;
            public Profile profile;
        }

        public class UserListResponse : SlackResponseBase
        {
            public User[] members;
        }

        public class TeamIcon
        {
            public string image_132;
        }

        public class Team
        {
            public string id;
            public string name;
            public TeamIcon icon;
        }

        public class TeamResponse : SlackResponseBase
        {
            public Team team;
        }

        public class File
        {
            public string name;
        }

        public class Message
        {
            public string type;
            public string user;
            public string text;
            public bool upload;
            public decimal ts;

            public File[] files;
        }

        public class HistoryResponse : SlackResponseBase
        {
            public Message[] messages;
        }

        public async Task<WebSocket> RealtimeConnectAsync(CancellationToken cancel = default)
        {
            WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/rtm.connect", cancel, null);

            string url = ret.Deserialize<RealtimeResponse>(true).url;

            return await WebSocket.ConnectAsync(url, cancel: cancel, options: new WebSocketConnectOptions(new WebSocketOptions { RespectMessageDelimiter = true }));
        }

        public async Task<Message[]> GetConversationHistoryAsync(string channelId, decimal oldest = 0, CancellationToken cancel = default)
        {
            string nextCursor = null;

            List<Message> o = new List<Message>();

            do
            {
                WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/conversations.history", cancel, null, ("limit", "100"), ("cursor", nextCursor), ("channel", channelId), ("oldest", oldest.ToString()));

                HistoryResponse data = ret.Deserialize<HistoryResponse>(true);

                ret.Data._GetString_UTF8()._JsonNormalizeAndDebug();

                foreach (Message m in data.messages)
                {
                    o.Add(m);
                }

                nextCursor = data.response_metadata?.next_cursor;
            }
            while (nextCursor._IsFilled());

            return o.ToArray();
        }

        public async Task<Channel> GetConversationInfoAsync(string id, CancellationToken cancel = default)
        {
            WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/conversations.info", cancel, null, ("channel", id));

            return ret.Deserialize<ConversationInfo>(true).channel;
        }

        public async Task<Channel[]> GetConversationsListAsync(CancellationToken cancel = default)
        {
            string nextCursor = null;

            List<Channel> o = new List<Channel>();

            do
            {
                WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/conversations.list", cancel, null, ("limit", "100"), ("cursor", nextCursor));

                ChannelsList data = ret.Deserialize<ChannelsList>(true);

                foreach (Channel c in data.channels)
                {
                    o.Add(c);
                }

                nextCursor = data.response_metadata?.next_cursor;
            }
            while (nextCursor._IsFilled());

            return o.ToArray();
        }

        public async Task<ChannelsList> GetChannelsListAsync(CancellationToken cancel = default)
        {
            WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/channels.list", cancel, queryList: ("unreads", "true"));

            ret.Data._GetString_UTF8()._JsonNormalizeAndDebug();

            return ret.Deserialize<ChannelsList>(true);
        }

        public async Task PostMessageAsync(string channelId, string text, bool asUser, CancellationToken cancel = default)
        {
            PostMessageData m = new PostMessageData()
            {
                channel = channelId,
                text = text,
                as_user = asUser,
            };

            await PostMessageAsync(m, cancel);
        }

        public async Task<Team> GetTeamInfoAsync(CancellationToken cancel = default)
        {
            WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/team.info", cancel);

            return ret.Deserialize<TeamResponse>(true).team;
        }

        public async Task<User[]> GetUsersListAsync(CancellationToken cancel = default)
        {
            string nextCursor = null;

            List<User> o = new List<User>();

            do
            {
                WebRet ret = await SimpleQueryAsync(WebMethods.POST, "https://slack.com/api/users.list", cancel, null, ("limit", "1000"), ("cursor", nextCursor));

                UserListResponse data = ret.Deserialize<UserListResponse>(true);

                foreach (User u in data.members)
                {
                    o.Add(u);
                }

                nextCursor = data.response_metadata?.next_cursor;
            }
            while (nextCursor._IsFilled());

            return o.ToArray();
        }

        public async Task PostMessageAsync(PostMessageData m, CancellationToken cancel = default)
        {
            (await RequestWithJsonObjectAsync(WebMethods.POST, "https://slack.com/api/chat.postMessage", m, cancel)).Deserialize<SlackResponseBase>(true);
        }
    }
}

#endif  // CORES_BASIC_JSON

