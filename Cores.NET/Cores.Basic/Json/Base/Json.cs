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

#if CORES_BASIC_JSON

using System.Diagnostics.CodeAnalysis;

using System;
using System.Threading.Tasks;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Newtonsoft.Json.Serialization;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IPA.Cores.Basic
{
    public static class Json
    {
        public const int DefaultMaxDepth = 8;

        public static string SerializeLog(IEnumerable itemArray, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth)
        {
            StringWriter w = new StringWriter();
            SerializeLogToTextWriterAsync(w, itemArray, includeNull, escapeHtml, maxDepth)._GetResult();
            return w.ToString();
        }

        public static async Task SerializeLogToTextWriterAsync(TextWriter w, IEnumerable itemArray, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, Type? type = null)
        {
            foreach (var item in itemArray)
            {
                await w.WriteLineAsync(Serialize(item, includeNull, escapeHtml, maxDepth, true, type: type));
            }
        }

        public class InterfaceContractResolver : DefaultContractResolver
        {
            private readonly Type[] _interfaceTypes;

            private readonly ConcurrentDictionary<Type, Type> _typeToSerializeMap;

            public InterfaceContractResolver(params Type[] interfaceTypes)
            {
                _interfaceTypes = interfaceTypes;

                _typeToSerializeMap = new ConcurrentDictionary<Type, Type>();
            }

            protected override IList<JsonProperty> CreateProperties(
                Type type,
                MemberSerialization memberSerialization)
            {
                var typeToSerialize = _typeToSerializeMap.GetOrAdd(
                    type,
                    t => _interfaceTypes.FirstOrDefault(
                        it => it.IsAssignableFrom(t)) ?? t);

                return base.CreateProperties(typeToSerialize, memberSerialization);
            }
        }

        public static string Serialize(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null)
        {
            JsonSerializerSettings setting = new JsonSerializerSettings()
            {
                MaxDepth = maxDepth,
                NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                PreserveReferencesHandling = referenceHandling ? PreserveReferencesHandling.All : PreserveReferencesHandling.None,
                StringEscapeHandling = escapeHtml ? StringEscapeHandling.EscapeHtml : StringEscapeHandling.Default,
            };

            if (type != null)
            {
                setting.ContractResolver = new InterfaceContractResolver(type);
            }

            string ret = JsonConvert.SerializeObject(obj, compact ? Formatting.None : Formatting.Indented, setting);

            if (base64url)
            {
                ret = ret._GetBytes_UTF8()._Base64UrlEncode();
            }

            return ret;
        }

        [return: MaybeNull]
        public static T Deserialize<T>(string str, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false)
            => (T)Deserialize(str, typeof(T), includeNull, maxDepth, base64url)!;

        public static object? Deserialize(string str, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false)
        {
            if (base64url)
            {
                str = str._Base64UrlDecode()._GetString_UTF8();
            }

            JsonSerializerSettings setting = new JsonSerializerSettings()
            {
                MaxDepth = maxDepth,
                NullValueHandling = includeNull ? NullValueHandling.Include : NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
            };
            return JsonConvert.DeserializeObject(str, type, setting);
        }

        [return: MaybeNull]
        public static T ConvertObject<T>(object? src, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false)
            => (T)ConvertObject(src, typeof(T), includeNull, maxDepth, referenceHandling)!;

        public static object? ConvertObject(object? src, Type destType, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false)
        {
            string str = Serialize(src, includeNull, false, maxDepth, true, referenceHandling);
            return Deserialize(str, destType, maxDepth: maxDepth);
        }

        public static async Task<bool> DeserializeLargeArrayAsync<T>(TextReader txt, Func<T?, bool> itemReadCallback,
            Func<string, Exception, bool>? parseErrorCallback = null, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth)
            where T: class
        {
            while (true)
            {
                string? line = await txt.ReadLineAsync();
                if (line == null)
                {
                    return true;
                }
                if (line._IsFilled())
                {
                    object? obj = null;
                    try
                    {
                        obj = (object ?)Deserialize<T>(line, includeNull, maxDepth);
                    }
                    catch (Exception ex)
                    {
                        if (parseErrorCallback != null && parseErrorCallback(line, ex) == false)
                        {
                            return false;
                        }
                    }

                    if (itemReadCallback((T?)obj) == false)
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

        public static dynamic? DeserializeDynamic(string str)
        {
            dynamic? ret = JObject.Parse(str);
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
            dynamic? d = DeserializeDynamic(str);

            return SerializeDynamic(d);
        }
    }

    public static partial class Dbg
    {
        static partial void InternalConvertToJsonStringIfPossible(ref string? ret, object obj, bool includeNull, bool escapeHtml, int? maxDepth, bool compact, bool referenceHandling)
        {
            ret = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling);
        }

        static partial void InternalIsJsonSupported(ref bool ret)
        {
            ret = true;
        }
    }
}

#endif // CORES_BASIC_JSON
