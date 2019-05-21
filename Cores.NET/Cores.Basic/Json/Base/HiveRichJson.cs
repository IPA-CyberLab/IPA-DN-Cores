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
using System.Threading.Tasks;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class RichJsonHiveSerializerOptions : HiveSerializerOptions
    {
        public const int DefaultMaxDepth = 12;
        public JsonSerializerSettings JsonSettings { get; }

        public RichJsonHiveSerializerOptions(JsonSerializerSettings settings = null)
        {
            if (settings == null)
            {
                settings = new JsonSerializerSettings()
                {
                    MaxDepth = Json.DefaultMaxDepth,
                    NullValueHandling = NullValueHandling.Ignore,
                    ReferenceLoopHandling = ReferenceLoopHandling.Error,
                    PreserveReferencesHandling = PreserveReferencesHandling.None,
                    StringEscapeHandling = StringEscapeHandling.Default,
                };
            }

            this.JsonSettings = settings;
        }
    }

    class RichJsonHiveSerializer : HiveSerializer
    {
        public new RichJsonHiveSerializerOptions Options => (RichJsonHiveSerializerOptions)base.Options;

        public RichJsonHiveSerializer(RichJsonHiveSerializerOptions options = null) : base(options ?? new RichJsonHiveSerializerOptions()) { }

        protected override Memory<byte> SerializeImpl<T>(T obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            MemoryBuffer<byte> ret = new MemoryBuffer<byte>();

            ret.Write(Str.NewLine_Bytes_Local);

            string str = JsonConvert.SerializeObject(obj, Formatting.Indented, Options.JsonSettings);

            ret.Write(str._GetBytes_UTF8());

            ret.Write(Str.NewLine_Bytes_Local);
            ret.Write(Str.NewLine_Bytes_Local);

            return ret.Memory;
        }

        protected override T DeserializeImpl<T>(ReadOnlyMemory<byte> memory)
        {
            string str = memory._GetString_UTF8();

            return (T)JsonConvert.DeserializeObject(str, typeof(T), Options.JsonSettings);
        }
    }
}

#endif  // CORES_BASIC_JSON

