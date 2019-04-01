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
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

namespace IPA.Cores.Basic
{
    static class Json
    {
        public const int DefaultMaxDepth = 8;

        public static string SerializeLog(IEnumerable itemArray, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth)
        {
            StringWriter w = new StringWriter();
            SerializeLogToTextWriterAsync(w, itemArray, includeNull, escapeHtml, maxDepth).GetResult();
            return w.ToString();
        }

        public static async Task SerializeLogToTextWriterAsync(TextWriter w, IEnumerable itemArray, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth)
        {
            foreach (var item in itemArray)
            {
                await w.WriteLineAsync(Serialize(item, includeNull, escapeHtml, maxDepth, true));
            }
        }

        public static string Serialize(object obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
        {
            JsonSerializerSettings setting = new JsonSerializerSettings()
            {
                MaxDepth = maxDepth,
                NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                PreserveReferencesHandling = referenceHandling ? PreserveReferencesHandling.All : PreserveReferencesHandling.None,
                StringEscapeHandling = escapeHtml ? StringEscapeHandling.EscapeHtml : StringEscapeHandling.Default,
                
            };
            return JsonConvert.SerializeObject(obj, compact ? Formatting.None : Formatting.Indented, setting);
        }

        public static T Deserialize<T>(string str, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth)
            => (T)Deserialize(str, typeof(T), includeNull, maxDepth);

        public static object Deserialize(string str, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth)
        {
            JsonSerializerSettings setting = new JsonSerializerSettings()
            {
                MaxDepth = maxDepth,
                NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
            };
            return JsonConvert.DeserializeObject(str, type, setting);
        }

        public static T ConvertObject<T>(object src, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false)
            => (T)ConvertObject(src, typeof(T), includeNull, maxDepth, referenceHandling);

        public static object ConvertObject(object src, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false)
        {
            string str = Serialize(src, includeNull, false, maxDepth, true, referenceHandling);
            return Deserialize(str, type, maxDepth: maxDepth);
        }

        public static async Task<bool> DeserializeLargeArrayAsync<T>(TextReader txt, Func<T, bool> itemReadCallback,
            Func<string, Exception, bool> parseErrorCallback = null, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth)
        {
            while (true)
            {
                string line = await txt.ReadLineAsync();
                if (line == null)
                {
                    return true;
                }
                if (line.IsFilled())
                {
                    object obj = null;
                    try
                    {
                        obj = (object)Deserialize<T>(line, includeNull, maxDepth);
                    }
                    catch (Exception ex)
                    {
                        if (parseErrorCallback(line, ex) == false)
                        {
                            return false;
                        }
                    }

                    if (itemReadCallback((T)obj) == false)
                    {
                        return false;
                    }
                }
            }
        }

        public static string SerializeDynamic(dynamic d)
        {
            JObject o = (JObject)d;

            return o.ToString();
        }

        public static dynamic DeserializeDynamic(string str)
        {
            dynamic ret = JObject.Parse(str);
            return ret;
        }

        public static dynamic NewDynamicObject()
        {
            JObject o = new JObject();

            return o;
        }

        public static JObject NewJsonObject()
        {
            return new JObject();
        }

        public static string Normalize(string str)
        {
            dynamic d = DeserializeDynamic(str);

            return SerializeDynamic(d);
        }
    }

    static partial class Dbg
    {
        static partial void InternalConvertToJsonStringIfPossible(ref string ret, object obj, bool includeNull, bool escapeHtml, int? maxDepth, bool compact, bool referenceHandling)
        {
            ret = obj.ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling);
        }

        static partial void InternalIsJsonSupported(ref bool ret)
        {
            ret = true;
        }
    }
}

