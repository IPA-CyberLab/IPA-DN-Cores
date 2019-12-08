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
using System.Threading;
using System.Threading.Tasks;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Helper.Basic
{
    public static class JsonHelper
    {
        public static string _ObjectToJson(this object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null)
            => Json.Serialize(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type);

        public static string _ObjectToJson<T>(this T obj, EnsurePresentInterface yes, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false)
            => _ObjectToJson(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, typeof(T));

        [return: MaybeNull]
        public static T _JsonToObject<T>(this string str, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false)
            => Json.Deserialize<T>(str, includeNull, maxDepth, base64url);

        public static object? _JsonToObject(this string str, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false)
            => Json.Deserialize(str, type, includeNull, maxDepth, base64url);

        [return: MaybeNull]
        public static T _ConvertJsonObject<T>(this object obj, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false)
            => Json.ConvertObject<T>(obj, includeNull, maxDepth, referenceHandling);

        public static object? _ConvertJsonObject(this object obj, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false)
            => Json.ConvertObject(obj, type, includeNull, maxDepth, referenceHandling);

        public static dynamic? _JsonToDynamic(this string str)
            => Json.DeserializeDynamic(str);

        public static ulong _CalcObjectHashByJson(this object o)
            => Util.CalcObjectHashByJson(o);

        public static string _JsonNormalize(this string s)
            => Json.Normalize(s);

        public static int _ObjectToFile<T>(this T obj, string path, FileSystem? fs = null, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
            => (fs ?? Lfs).WriteJsonToFile<T>(path, obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling);

        [return: MaybeNull]
        public static T _FileToObject<T>(this string path, FileSystem? fs = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false)
            => (fs ?? Lfs).ReadJsonFromFile<T>(path, maxSize, flags, cancel, includeNull, maxDepth, nullIfError);

        [return: NotNullIfNotNull("obj")]
        public static T _CloneWithJson<T>(this T obj, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, Type? type = null)
            => Json.CloneWithJson(obj, escapeHtml, maxDepth, referenceHandling, type);

        [return: NotNullIfNotNull("obj")]
        public static object? _CloneObjectWithJson(this object? obj, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, Type? type = null)
            => Json.CloneObjectWithJson(obj, escapeHtml, maxDepth, referenceHandling, type);
    }

    public static class JsonConsoleHelper
    {
        [return: NotNullIfNotNull("o")]
        public static object? _PrintAsJson(this object? o, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null)
        {
            Con.WriteJsonLine(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type);
            return o;
        }

        [return: NotNullIfNotNull("o")]
        public static T _PrintAsJson<T>([AllowNull] this T o, EnsurePresentInterface yes, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
            => (T)_PrintAsJson(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, typeof(T))!;

        [return: NotNullIfNotNull("o")]
        public static object? _DebugAsJson(this object? o, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null)
        {
            Con.WriteJsonDebug(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type);
            return o;
        }

        [return: NotNullIfNotNull("o")]
        public static T _DebugAsJson<T>([AllowNull] this T o, EnsurePresentInterface yes, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
            => (T)_DebugAsJson(o, includeNull, escapeHtml, maxDepth, compact, referenceHandling, typeof(T))!;

        public static void _JsonNormalizeAndPrint(this string s)
        {
            Json.Normalize(s)._Print();
        }
        public static void _JsonNormalizeAndDebug(this string s)
        {
            Json.Normalize(s)._Debug();
        }
    }
}


namespace IPA.Cores.Basic
{
    public static partial class Con
    {
        public static void WriteJsonLine(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null)
            => Con.WriteLine(obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, type: type));

        public static void WriteJsonError(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null)
            => Con.WriteError(obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, type: type));

        public static void WriteJsonDebug(object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null)
            => Con.WriteDebug(obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling, type: type));
    }

    public abstract partial class FileSystem
    {
        public Task<int> WriteJsonToFileAsync<T>(string path, [AllowNull] T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
        {
            string jsonStr = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling);

            return this.WriteStringToFileAsync(path, jsonStr, flags, doNotOverwrite, writeBom: true, cancel: cancel);
        }
        public int WriteJsonToFile<T>(string path, T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
            => WriteJsonToFileAsync(path, obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling)._GetResult();

        public async Task<T> ReadJsonFromFileAsync<T>(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false)
        {
            try
            {
                if (nullIfError)
                {
                    if ((await this.IsFileExistsAsync(path, cancel)) == false)
                    {
                        return default!;
                    }
                }

                string jsonStr = await this.ReadStringFromFileAsync(path, null, maxSize, flags, false, cancel);

                return jsonStr._JsonToObject<T>(includeNull, maxDepth, false);
            }
            catch
            {
                if (nullIfError)
                    return default!;

                throw;
            }
        }
        public T ReadJsonFromFile<T>(string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false)
            => ReadJsonFromFileAsync<T>(path, maxSize, flags, cancel, includeNull, maxDepth, nullIfError)._GetResult();
    }
}

#endif // CORES_BASIC_JSON


