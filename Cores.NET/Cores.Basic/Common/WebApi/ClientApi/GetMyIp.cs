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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static partial class CoresConfig
    {
        public static partial class GetMyIpClientSettings
        {
            public static readonly Copenhagen<int> NumRetry = 3;
            public static readonly Copenhagen<int> Timeout = 5 * 1000;
        }
    }

    public class GetMyIpClient : IDisposable
    {
        public TcpIpSystem TcpIp;
        WebApi Web;

        public GetMyIpClient(TcpIpSystem tcpIp = null)
        {
            this.TcpIp = tcpIp ?? LocalNet;
            this.Web = new WebApi(new WebApiOptions(new WebApiSettings { SslAcceptAnyCerts = true, UseProxy = false, Timeout = CoresConfig.GetMyIpClientSettings.Timeout }, tcpIp));
        }

        public async Task<IPAddress> GetMyIpAsync(IPVersion ver = IPVersion.IPv4, CancellationToken cancel = default)
        {
            string url;

            if (ver == IPVersion.IPv4)
                url = Consts.Urls.GetMyIpUrl_IPv4;
            else
                url = Consts.Urls.GetMyIpUrl_IPv6;

            Exception error = null;

            IPAddress ret = null;

            for (int i = 0; i < CoresConfig.GetMyIpClientSettings.NumRetry; i++)
            {
                try
                {
                    WebRet webRet = await Web.SimpleQueryAsync(WebMethods.GET, url, cancel);

                    string str = webRet.Data._GetString_Ascii()._OneLine("");

                    if (str.StartsWith("IP=", StringComparison.OrdinalIgnoreCase))
                    {
                        ret = IPAddress.Parse(str.Substring(3));
                        break;
                    }
                    else
                    {
                        throw new ApplicationException($"Invalid IP str: \"{str}\"");
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }

            if (ret == null)
            {
                throw error;
            }

            return ret;
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;
            Web._DisposeSafe();
        }
    }
}
