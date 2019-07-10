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
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.ClientApi.SlackApi;

#pragma warning disable CS0162
#pragma warning disable CS0219

namespace IPA.TestDev
{
    partial class TestDevCommands
    {
        [ConsoleCommand(
        "Inbox command",
        "Inbox",
        "Inbox test")]
        static int Inbox(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args = { };
            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            Inbox_SlackTestAsync()._GetResult();

            return 0;
        }

        static async Task Inbox_SlackTestAsync()
        {
            string accessToken = "xoxp-687264585408-687264586720-675870571299-";

            if (false)
            {
                using (SlackApi slack = new SlackApi("687264585408.675851234162"))
                {
                    if (false)
                    {
                        //string scope = "channels:read groups:read im:read mpim:read channels:history groups:history im:history mpim:history users:read users.profile:read";
                        string scope = "client";
                        string url = slack.AuthGenerateAuthorizeUrl("client", "https://www.google.com/");

                        url._Print();

                    }
                    else
                    {
                        var token = await slack.AuthGetAccessTokenAsync("a092d08d6b399ef42fcab14bdc2df837", "687264585408.678256576146.f0e278b0db71e80ebe432243327ffb565fc09686f8efaf0f0a424b08a9e47820");

                        token._PrintAsJson();
                    }
                }
            }
            else
            {
                using (SlackApi slack = new SlackApi("687264585408.675851234162", accessToken))
                {
                    //var channels = await slack.GetChannelsListAsync();

                    //await slack.GetConversationsListAsync();

                    using (WebSocket ws = await slack.RealtimeConnectAsync())
                    {
                        using (var st = ws.GetStream())
                        {
                            while (true)
                            {
                                IReadOnlyList<ReadOnlyMemory<byte>> segments = await st.FastReceiveAsync();

                                foreach (ReadOnlyMemory<byte> mem in segments)
                                {
                                    string str = mem._GetString_Ascii();

                                    str._Print();
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
