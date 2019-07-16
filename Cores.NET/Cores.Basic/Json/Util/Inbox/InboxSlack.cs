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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.ClientApi.SlackApi;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class InboxSlackAdapterSettings
        {
            public static readonly Copenhagen<int> RefreshAllInterval = 60 * 1000;
        }
    }

    class InboxSlackAdapter : InboxAdapter
    {
        public override string AdapterName => "slack";

        string currentAccountInfoStr = null;

        public override string AccountInfoStr => currentAccountInfoStr;

        SlackApi Api;

        public InboxSlackAdapter(string guid, Inbox inbox, InboxAdapterAppCredential appCredential, InboxOptions adapterOptions = null)
            : base(guid, inbox, appCredential, adapterOptions)
        {
        }

        Once Started;

        protected override void StartImpl(InboxAdapterUserCredential credential)
        {
            if (credential == null) throw new ArgumentNullException("credential");

            if (Started.IsFirstCall())
            {
                this.UserCredential = credential;

                this.Api = new SlackApi(this.AppCredential.ClientId, this.AppCredential.ClientSecret, this.UserCredential.AccessToken);
            }
            else
            {
                throw new ApplicationException("Already started.");
            }
        }

        protected override void CancelImpl(Exception ex)
        {
            base.CancelImpl(ex);
        }

        protected override Task CleanupImplAsync(Exception ex)
        {
            return base.CleanupImplAsync(ex);
        }

        protected override void DisposeImpl(Exception ex)
        {
            try
            {
                this.Api._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }

        public override string AuthStartGetUrl(string redirectUrl, string state = "")
        {
            using (SlackApi tmpApi = new SlackApi(this.AppCredential.ClientId, this.AppCredential.ClientSecret))
            {
                return tmpApi.AuthGenerateAuthorizeUrl(Consts.OAuthScopes.Slack_Client, redirectUrl, state);
            }
        }

        public override async Task<InboxAdapterUserCredential> AuthGetCredentialAsync(string code, string redirectUrl, CancellationToken cancel = default)
        {
            using (SlackApi tmpApi = new SlackApi(this.AppCredential.ClientId, this.AppCredential.ClientSecret))
            {
                SlackApi.AccessToken token = await tmpApi.AuthGetAccessTokenAsync(code, cancel);

                return new InboxAdapterUserCredential { AccessToken = token.access_token };
            }
        }

        SlackApi.User[] UserList;
        SlackApi.Channel[] ConversationList;
        SlackApi.Team TeamInfo;
        Dictionary<string, SlackApi.Message[]> MessageListPerConversation = new Dictionary<string, SlackApi.Message[]>(StrComparer.IgnoreCaseComparer);

        readonly AsyncAutoResetEvent UpdateChannelsEvent = new AsyncAutoResetEvent();
        readonly CriticalSection UpdateChannelsListLock = new CriticalSection();
        readonly HashSet<string> UpdateChannelsList = new HashSet<string>(StrComparer.IgnoreCaseComparer);

        async Task RealtimeRecvLoopAsync(CancellationToken cancel)
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                try
                {
                    using (WebSocket ws = await Api.RealtimeConnectAsync(cancel))
                    {
                        using (PipeStream st = ws.GetStream())
                        {
                            while (true)
                            {
                                ReadOnlyMemory<byte> recvData = await st.ReceiveAsync(cancel: cancel);
                                //recvData._GetString_UTF8()._JsonNormalizeAndDebug();
                                dynamic d = recvData._GetString_UTF8()._JsonToDynamic();

                                string channel = d.channel;
                                string type = d.type;

                                if (type._IsSamei("message") || type._IsSamei("channel_marked") || type._IsSamei("im_marked") || type._IsSamei("group_marked"))
                                {
                                    if (channel._IsFilled())
                                    {
                                        lock (UpdateChannelsListLock)
                                        {
                                            UpdateChannelsList.Add(channel);
                                        }

                                        UpdateChannelsEvent.Set(true);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                cancel.ThrowIfCancellationRequested();

                await cancel._WaitUntilCanceledAsync(1000);
            }
        }

        protected override async Task MainLoopImplAsync(CancellationToken cancel)
        {
            MessageListPerConversation.Clear();

            cancel.ThrowIfCancellationRequested();

            CancellationTokenSource realTimeTaskCancel = new CancellationTokenSource();

            Task realTimeTask = RealtimeRecvLoopAsync(realTimeTaskCancel.Token);

            try
            {
                bool all = true;
                string[] targetChannelIdList = null;

                while (true)
                {
                    cancel.ThrowIfCancellationRequested();

                    InboxMessageBox box = await ReloadInternalAsync(all, targetChannelIdList, cancel);

                    MessageBoxUpdatedCallback(box);

                    ExceptionWhen reason;

                    if (this.UpdateChannelsList.Count == 0)
                    {
                        reason = await TaskUtil.WaitObjectsAsync(
                            cancels: cancel._SingleArray(),
                            events: this.UpdateChannelsEvent._SingleArray(),
                            timeout: Util.GenRandInterval(CoresConfig.InboxSlackAdapterSettings.RefreshAllInterval));
                    }
                    else
                    {
                        reason = ExceptionWhen.None;
                    }

                    if (reason == ExceptionWhen.TimeoutException)
                    {
                        all = true;
                        targetChannelIdList = null;
                        lock (this.UpdateChannelsListLock)
                        {
                            this.UpdateChannelsList.Clear();
                        }
                    }
                    else
                    {
                        all = false;
                        lock (this.UpdateChannelsListLock)
                        {
                            targetChannelIdList = this.UpdateChannelsList.ToArray();
                            this.UpdateChannelsList.Clear();
                        }
                    }
                }
            }
            finally
            {
                realTimeTaskCancel._TryCancel();

                await realTimeTask._TryWaitAsync(true);
            }
        }

        int numReload = 0;

        async Task<InboxMessageBox> ReloadInternalAsync(bool all, IEnumerable<string> targetChannelIDs, CancellationToken cancel)
        {
            try
            {
                List<InboxMessage> msgList = new List<InboxMessage>();

                // Team info
                this.TeamInfo = await Api.GetTeamInfoAsync(cancel);

                currentAccountInfoStr = this.TeamInfo.name;

                if (all)
                {
                    // Enum users
                    this.UserList = await Api.GetUsersListAsync(cancel);

                    // Clear cache
                    this.MessageListPerConversation.Clear();
                }

                // Enum conversations
                this.ConversationList = await Api.GetConversationsListAsync(cancel);

                // Enum messages
                foreach (SlackApi.Channel conv in ConversationList)
                {
                    bool selected = false;

                    if (conv.IsTarget())
                    {
                        if (all)
                        {
                            selected = true;
                        }
                        else
                        {
                            if (targetChannelIDs.Contains(conv.id, StrComparer.IgnoreCaseComparer))
                            {
                                selected = true;
                            }
                        }
                    }

                    if (selected)
                    {
                        // Get the conversation info
                        SlackApi.Channel convInfo = await Api.GetConversationInfoAsync(conv.id, cancel);

                        // Get unread messages
                        SlackApi.Message[] messages = await Api.GetConversationHistoryAsync(conv.id, convInfo.last_read, this.Inbox.Options.MaxMessagesPerAdapter, cancel: cancel);

                        MessageListPerConversation[conv.id] = messages;
                    }

                    if (conv.IsTarget())
                    {
                        foreach (SlackApi.Message message in MessageListPerConversation[conv.id])
                        {
                            var user = GetUser(message.user);

                            string group_name = "";

                            if (conv.is_channel)
                            {
                                group_name = "#" + conv.name_normalized;
                            }
                            else if (conv.is_im)
                            {
                                group_name = "@" + GetUser(conv.user)?.profile?.real_name ?? "unknown";
                            }
                            else
                            {
                                group_name = "@" + conv.name_normalized;
                            }

                            InboxMessage m = new InboxMessage
                            {
                                Id = this.Guid + "_" + message.ts.ToString(),

                                Service = TeamInfo.name._DecodeHtml(),
                                ServiceImage = TeamInfo.icon?.image_132 ?? "",

                                From = (user?.profile?.real_name ?? "Unknown User")._DecodeHtml(),
                                FromImage = user?.profile?.image_512 ?? "",

                                Group = group_name,

                                Body = message.text._DecodeHtml(),
                                Timestamp = message.ts._ToDateTimeOfSlack(),
                            };

                            m.Subject = group_name._DecodeHtml();

                            if (message.upload)
                            {
                                m.Body += $"Filename: '{message.files.FirstOrDefault()?.name ?? "Unknown Filename"}'";
                            }

                            msgList.Add(m);
                        }
                    }
                }

                InboxMessageBox ret = new InboxMessageBox()
                {
                    MessageList = msgList.OrderByDescending(x => x.Timestamp).Take(this.Inbox.Options.MaxMessagesPerAdapter).ToArray(),
                };

                ClearLastError();

                if (numReload == 0)
                {
                    ret.IsFirst = true;
                }

                numReload++;

                return ret;
            }
            catch (Exception ex)
            {
                SetLastError(ex);
                throw;
            }
        }

        SlackApi.User GetUser(string userId)
        {
            SlackApi.User user = this.UserList.Where(x => x.id._IsSamei(userId)).SingleOrDefault();
            return user;
        }
    }
}

#endif  // CORES_BASIC_JSON
