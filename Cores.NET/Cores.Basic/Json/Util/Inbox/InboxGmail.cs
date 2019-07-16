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

using IPA.Cores.ClientApi.GoogleApi;
namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class InboxGmailAdapterSettings
        {
            public static readonly Copenhagen<int> ReloadInterval = 1 * 1000;
        }
    }

    class InboxGmailAdapter : InboxAdapter
    {
        public override string AdapterName => "slack";

        GoogleApi Api;

        public InboxGmailAdapter(string guid, Inbox inbox, InboxAdapterAppCredential appCredential, InboxOptions adapterOptions = null)
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

                this.Api = new GoogleApi(this.AppCredential.ClientId, this.AppCredential.ClientSecret, this.UserCredential.AccessToken);
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
            using (GoogleApi tmpApi = new GoogleApi(this.AppCredential.ClientId, this.AppCredential.ClientSecret))
            {
                return tmpApi.AuthGenerateAuthorizeUrl(Consts.OAuthScopes.Google_Gmail, redirectUrl, state);
            }
        }

        public override async Task<InboxAdapterUserCredential> AuthGetCredentialAsync(string code, string redirectUrl, CancellationToken cancel = default)
        {
            using (GoogleApi tmpApi = new GoogleApi(this.AppCredential.ClientId, this.AppCredential.ClientSecret))
            {
                GoogleApi.AccessToken token = await tmpApi.AuthGetAccessTokenAsync(code, redirectUrl, cancel);

                return new InboxAdapterUserCredential { AccessToken = token.access_token };
            }
        }

        GoogleApi.GmailProfile currentProfile = null;

        protected override async Task MainLoopImplAsync(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            GoogleApi.GmailProfile profile = await Api.GmailGetProfileAsync(cancel);

            currentProfile = profile;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();

                InboxMessageBox box = await ReloadInternalAsync(cancel);

                MessageBoxUpdatedCallback(box);

                await TaskUtil.WaitObjectsAsync(
                    cancels: cancel._SingleArray(),
                    timeout: Util.GenRandInterval(CoresConfig.InboxGmailAdapterSettings.ReloadInterval));
            }
        }

        readonly Dictionary<string, GoogleApi.Message> MessageCache = new Dictionary<string, GoogleApi.Message>(StrComparer.IgnoreCaseComparer);

        int numReload = 0;

        async Task<InboxMessageBox> ReloadInternalAsync(CancellationToken cancel)
        {
            GoogleApi.MessageList[] list = await Api.GmailListMessagesAsync("is:unread label:inbox", this.Inbox.Options.MaxMessagesPerAdapter, cancel);

            List<GoogleApi.Message> msgList = new List<GoogleApi.Message>();

            foreach (GoogleApi.MessageList message in list)
            {
                if (MessageCache.TryGetValue(message.id, out GoogleApi.Message m) == false)
                {
                    m = await Api.GmailGetMessageAsync(message.id, cancel);

                    MessageCache[message.id] = m;
                }

                msgList.Add(m);
            }

            // delete old cache
            List<string> deleteList = new List<string>();
            foreach (string id in MessageCache.Keys)
            {
                if (list.Where(x => x.id._IsSamei(id)).Any() == false)
                    deleteList.Add(id);
            }
            foreach (string id in deleteList)
            {
                MessageCache.Remove(id);
            }

            InboxMessageBox box = new InboxMessageBox();

            List<InboxMessage> msgList2 = new List<InboxMessage>();

            foreach (var msg in msgList.OrderByDescending(x => x.internalDate).Take(this.Inbox.Options.MaxMessagesPerAdapter))
            {
                var m = new InboxMessage
                {
                    From = msg.GetFrom()._DecodeHtml(),
                    FromImage = Consts.CdnUrls.GmailIcon,
                    Group = "Inbox",
                    Id = this.Guid + "_" + msg.id,
                    Service = currentProfile.emailAddress,
                    ServiceImage = Consts.CdnUrls.GmailIcon,
                    Subject = msg.GetSubject()._DecodeHtml(),
                    Body = msg.snippet._DecodeHtml(),
                    Timestamp = msg.internalDate._ToDateTimeOfGoogle(),
                };

                msgList2.Add(m);
            }

            box.MessageList = msgList2.ToArray();

            if (numReload == 0)
            {
                box.IsFirst = true;
            }

            numReload++;

            return box;
        }
    }
}

#endif  // CORES_BASIC_JSON
