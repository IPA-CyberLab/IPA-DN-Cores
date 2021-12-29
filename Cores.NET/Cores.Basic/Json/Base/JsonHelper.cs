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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.IO;

namespace IPA.Cores.Helper.Basic
{
    public static class JsonHelper
    {
        public static string _ObjectToJson(this object? obj, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false, Type? type = null)
            => Json.Serialize(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, type);

        public static string _ObjectToJson<T>([AllowNull] this T obj, EnsurePresentInterface yes, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool base64url = false)
            => _ObjectToJson(obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, base64url, typeof(T));

        public static void _ObjectToJsonTextWriter(this object? obj, TextWriter destTextWriter, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, Type? type = null)
            => Json.Serialize(destTextWriter, obj, includeNull, escapeHtml, maxDepth, compact, referenceHandling, type);

        public static void _ObjectToJsonTextWriter<T>([AllowNull] this T obj, TextWriter destTextWriter, bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
            => _ObjectToJsonTextWriter(obj, destTextWriter, includeNull, escapeHtml, maxDepth, compact, referenceHandling, typeof(T));

        [return: MaybeNull]
        public static T _JsonToObject<T>(this string str, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false)
            => Json.Deserialize<T>(str, includeNull, maxDepth, base64url);

        public static object? _JsonToObject(this string str, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool base64url = false)
            => Json.Deserialize(str, type, includeNull, maxDepth, base64url);

        [return: MaybeNull]
        public static T _JsonToObject<T>(this TextReader srcTextReader, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth)
            => Json.Deserialize<T>(srcTextReader, includeNull, maxDepth);

        public static object? _JsonToObject(this TextReader srcTextReader, Type type, bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth)
            => Json.Deserialize(srcTextReader, type, includeNull, maxDepth);

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

        public static long _ObjectToFile<T>([AllowNull] this T obj, string path, FileSystem? fs = null, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
            => (fs ?? Lfs).WriteJsonToFile<T>(path, obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling);

        [return: MaybeNull]
        public static T _FileToObject<T>(this string path, FileSystem? fs = null, long maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false)
            => (fs ?? Lfs).ReadJsonFromFile<T>(path, maxSize, flags, cancel, includeNull, maxDepth, nullIfError);

        [return: NotNullIfNotNull("obj")]
        [return: MaybeNull]
        public static T _CloneWithJson<T>([AllowNull] this T obj, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool referenceHandling = false, Type? type = null)
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
        public async Task<long> WriteJsonToFileEncryptedAsync<T>(string path, [AllowNull] T obj, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
        {
            string jsonStr = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling);

            return await this.WriteStringToFileEncryptedAsync(path, jsonStr, password, flags, doNotOverwrite, writeBom: true, cancel: cancel);
        }
        public long WriteJsonToFileEncrypted<T>(string path, [AllowNull] T obj, string password, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false)
            => WriteJsonToFileEncryptedAsync(path, obj, password, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling)._GetResult();


        public async Task<T> ReadJsonFromFileEncryptedAsync<T>(string path, string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
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

                string jsonStr = await this.ReadStringFromFileEncryptedAsync(path, password, null, maxSize, flags, false, cancel);

                return jsonStr._JsonToObject<T>(includeNull, maxDepth, false)!;
            }
            catch
            {
                if (nullIfError)
                    return default!;

                throw;
            }
        }
        public T ReadJsonFromFileEncrypted<T>(string path, string password, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false)
            => ReadJsonFromFileEncryptedAsync<T>(path, password, maxSize, flags, cancel, includeNull, maxDepth, nullIfError)._GetResult();

        public async Task<long> WriteJsonToFileAsync<T>(string path, [AllowNull] T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool withBackup = false)
        {
            //string jsonStr = obj._ObjectToJson(includeNull, escapeHtml, maxDepth, compact, referenceHandling);

            //return this.WriteStringToFileAsync(path, jsonStr, flags, doNotOverwrite, writeBom: true, cancel: cancel);

            HugeMemoryBuffer<byte> mem = new HugeMemoryBuffer<byte>();

            await using (BufferBasedStream stream = new BufferBasedStream(mem))
            {
                await using (StreamWriter w = new StreamWriter(stream, new UTF8Encoding(true), Consts.Numbers.DefaultVeryLargeBufferSize))
                {
                    obj._ObjectToJsonTextWriter(w, includeNull, escapeHtml, maxDepth, compact, referenceHandling);
                }
            }

            if (withBackup)
            {
                string backupFilePath = path + Consts.Extensions.Backup;

                try
                {
                    if (await this.IsFileExistsAsync(path, cancel))
                    {
                        await this.CopyFileAsync(path, backupFilePath, new CopyFileParams(flags: flags | FileFlags.AutoCreateDirectory | FileFlags.WriteOnlyIfChanged), cancel: cancel);
                    }
                }
                catch (Exception ex)
                {
                    ex._Error();
                }
            }

            return await this.WriteHugeMemoryBufferToFileAsync(path, mem, flags, doNotOverwrite, cancel);
        }
        public long WriteJsonToFile<T>(string path, [AllowNull] T obj, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default,
            bool includeNull = false, bool escapeHtml = false, int? maxDepth = Json.DefaultMaxDepth, bool compact = false, bool referenceHandling = false, bool withBackup = false)
            => WriteJsonToFileAsync(path, obj, flags, doNotOverwrite, cancel, includeNull, escapeHtml, maxDepth, compact, referenceHandling, withBackup)._GetResult();

        public async Task<T> ReadJsonFromFileAsync<T>(string path, long maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, bool withBackup = false)
        {
            try
            {
                if (nullIfError)
                {
                    if ((await this.IsFileExistsAsync(path, cancel)) == false)
                    {
                        if (withBackup)
                        {
                            if ((await this.IsFileExistsAsync(path + Consts.Extensions.Backup, cancel)) == false)
                            {
                                return default!;
                            }
                        }
                        else
                        {
                            return default!;
                        }
                    }
                }

L_RETRY:
                try
                {
                    HugeMemoryBuffer<byte> mem = await this.ReadHugeMemoryBufferFromFileAsync(path, maxSize, flags, cancel);

                    await using (BufferBasedStream stream = new BufferBasedStream(mem))
                    {
                        using (StreamReader r = new StreamReader(stream, Str.Utf8Encoding, true, Consts.Numbers.DefaultVeryLargeBufferSize))
                        {
                            return r._JsonToObject<T>(includeNull, maxDepth)!;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (withBackup == false)
                    {
                        throw;
                    }
                    else
                    {
                        ex._Error();
                        withBackup = false;
                        path += Consts.Extensions.Backup;
                        $"Trying the backup file '{path}' ..."._Error();
                        goto L_RETRY;
                    }
                }
            }
            catch
            {
                if (nullIfError)
                    return default!;

                throw;
            }
        }
        public T ReadJsonFromFile<T>(string path, long maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default,
            bool includeNull = false, int? maxDepth = Json.DefaultMaxDepth, bool nullIfError = false, bool withBackup = false)
            => ReadJsonFromFileAsync<T>(path, maxSize, flags, cancel, includeNull, maxDepth, nullIfError, withBackup)._GetResult();
    }
}

#endif // CORES_BASIC_JSON


