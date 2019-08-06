// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

#if CORES_BASIC_JSON && CORES_BASIC_DAEMON

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.App.DaemonCenterLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using System.ComponentModel.DataAnnotations;

namespace IPA.Cores.Basic.App.DaemonCenterLib
{
    public enum InstanceKeyType
    {
        Hostname = 0,
        Guid = 1,
    }

    public class AppSettings : INormalizable, IErrorCheckable, IValidatableObject
    {
        [Required]
        public string AppName { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public InstanceKeyType InstanceKeyType { get; set; }

        public int KeepAliveIntervalSecs { get; set; }
        public int DeadIntervalSecs { get; set; }

        public void CheckError()
        {
            if (AppName._IsEmpty())
                throw new ArgumentNullException(nameof(AppName));
        }

        public void Normalize()
        {
            this.KeepAliveIntervalSecs = this.KeepAliveIntervalSecs._Max(1);
            this.DeadIntervalSecs = this.DeadIntervalSecs._Max(3);
        }

        public override string ToString() => this.AppName;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            => this._Validate(validationContext);
    }

    public class App
    {
        public AppSettings Settings;

        public List<Instance> InstanceList = new List<Instance>();
    }

    [Flags]
    public enum StatFlag
    {
        None = 0,
        IsOnGit = 1,
    }

    public class InstanceStat
    {
        public string CommitId;
        public string InstanceArguments;
        public CoresRuntimeStat RuntimeStat;
        public EnvInfoSnapshot EnvInfo;

        [JsonConverter(typeof(StringEnumConverter))]
        public StatFlag StatFlag;
    }

    public class Instance
    {
        public string Key;
        public string SrcIpAddress;
        public string HostName;

        public DateTimeOffset FirstAlive;
        public DateTimeOffset LastAlive;
        public DateTimeOffset LastCommitIdChanged;
        public DateTimeOffset LastInstanceArgumentsChanged;

        public int NumAlive;
        public string NextCommitId;
        public string NextInstanceArguments;
    }

    public class DbHive
    {
        public Dictionary<string, App> AppList = new Dictionary<string, App>(StrComparer.IgnoreCaseTrimComparer);
    }

    public class RequestMsg
    {
        public string AppId;
        public string HostName;
        public InstanceStat Stat;
    }

    public class ResponseMsg
    {
        public int NextKeepAliveMsec;

        public string NextCommitId;
        public string NextInstanceArguments;
    }

    [RpcInterface]
    public interface IRpc
    {
        Task<ResponseMsg> KeepAlive(RequestMsg req);
    }
}

#endif

