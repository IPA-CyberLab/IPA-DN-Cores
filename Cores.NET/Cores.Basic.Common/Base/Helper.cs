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
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;

using IPA.Cores.Basic;

namespace IPA.Cores.Helper.Basic
{
    static class HelperBasic
    {
        public static byte[] GetBytes_UTF8(this string s, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(s));
        public static byte[] GetBytes_UTF16LE(this string s, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.UniEncoding.GetBytes(s));
        public static byte[] GetBytes_ShiftJis(this string s) => Str.ShiftJisEncoding.GetBytes(s);
        public static byte[] GetBytes_Ascii(this string s) => Str.AsciiEncoding.GetBytes(s);
        public static byte[] GetBytes_Euc(this string s) => Str.EucJpEncoding.GetBytes(s);
        public static byte[] GetBytes(this string s, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(s));
        public static byte[] GetBytes(this string s, Encoding enc) => enc.GetBytes(s);

        public static string GetString_UTF8(this byte[] b) => Str.DecodeString(b, Str.Utf8Encoding, out _);
        public static string GetString_UTF16LE(this byte[] b) => Str.DecodeString(b, Str.UniEncoding, out _);
        public static string GetString_ShiftJis(this byte[] b) => Str.DecodeString(b, Str.ShiftJisEncoding, out _);
        public static string GetString_Ascii(this byte[] b) => Str.DecodeString(b, Str.AsciiEncoding, out _);
        public static string GetString_Euc(this byte[] b) => Str.DecodeString(b, Str.EucJpEncoding, out _);
        public static string GetString(this byte[] b, Encoding default_encoding) => Str.DecodeString(b, default_encoding, out _);
        public static string GetString(this byte[] b) => Str.DecodeStringAutoDetect(b, out _);

        public static string GetHexString(this byte[] b, string padding = "") => Str.ByteToHex(b, padding);
        public static byte[] GetHexBytes(this string s) => Str.HexToByte(s);

        public static bool ToBool(this string s) => Str.StrToBool(s);
        public static byte[] ToByte(this string s) => Str.StrToByte(s);
        public static DateTime ToDate(this string s, bool to_utc = false, bool empty_to_zero_dt = false) => Str.StrToDate(s, to_utc, empty_to_zero_dt);
        public static DateTime ToTime(this string s, bool to_utc = false, bool empty_to_zero_dt = false) => Str.StrToTime(s, to_utc, empty_to_zero_dt);
        public static DateTime ToDateTime(this string s, bool to_utc = false, bool empty_to_zero_dt = false) => Str.StrToDateTime(s, to_utc, empty_to_zero_dt);
        public static object ToEnum(this string s, object default_value) => Str.StrToEnum(s, default_value);
        public static int ToInt(this string s) => Str.StrToInt(s);
        public static long ToLong(this string s) => Str.StrToLong(s);
        public static uint ToUInt(this string s) => Str.StrToUInt(s);
        public static ulong ToULong(this string s) => Str.StrToULong(s);
        public static double ToDouble(this string s) => Str.StrToDouble(s);
        public static decimal ToDecimal(this string s) => Str.StrToDecimal(s);
        public static bool IsSame(this string s, string t, bool ignore_case = false) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (ignore_case ? Str.StrCmpi(s, t) : Str.StrCmp(s, t))));
        public static bool IsSamei(this string s, string t) => IsSame(s, t, true);
        public static bool IsSameiIgnoreUnderscores(this string s, string t) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (s.Replace("_", "").IsSamei(t.Replace("_", "")))));
        public static int Cmp(this string s, string t, bool ignore_case = false) => ((s == null && t == null) ? 0 : ((s == null ? 1 : t == null ? -1 : (ignore_case ? Str.StrCmpiRetInt(s, t) : Str.StrCmpRetInt(s, t)))));
        public static int Cmpi(this string s, string t, bool ignore_case = false) => Cmp(s, t, true);
        public static string[] GetLines(this string s) => Str.GetLines(s);
        public static bool GetKeyAndValue(this string s, out string key, out string value, string split_str = "") => Str.GetKeyAndValue(s, out key, out value, split_str);
        public static bool IsDouble(this string s) => Str.IsDouble(s);
        public static bool IsLong(this string s) => Str.IsLong(s);
        public static bool IsInt(this string s) => Str.IsInt(s);
        public static bool IsNumber(this string s) => Str.IsNumber(s);
        public static bool InStr(this string s, string keyword, bool ignore_case = false) => Str.InStr(s, keyword, !ignore_case);
        public static string NormalizeCrlfWindows(this string s) => Str.NormalizeCrlfWindows(s);
        public static string NormalizeCrlfUnix(this string s) => Str.NormalizeCrlfUnix(s);
        public static string NormalizeCrlfThisPlatform(this string s) => Str.NormalizeCrlfThisPlatform(s);
        public static string[] ParseCmdLine(this string s) => Str.ParseCmdLine(s);
        public static object Old_XmlToObjectPublic(this string s, Type t) => Str.XMLToObjectSimple_PublicLegacy(s, t);
        public static StrToken ToToken(this string s, string split_str = " ,\t\r\n") => new StrToken(s, split_str);
        public static string OneLine(this string s) => Str.OneLine(s);
        public static string FormatC(this string s) => Str.FormatC(s);
        public static string FormatC(this string s, params object[] args) => Str.FormatC(s, args);
        public static void Printf(this string s) => Str.Printf(s, new object[0]);
        public static void Printf(this string s, params object[] args) => Str.Printf(s, args);
        public static string Print(this string s, bool newline = true) { Console.Write((s == null ? "null" : s) + (newline ? Env.NewLine : "")); return s; }
        public static string Debug(this string s) { Dbg.WriteLine(s); return s; }
        public static int Search(this string s, string keyword, int start = 0, bool case_senstive = false) => Str.SearchStr(s, keyword, start, case_senstive);
        public static string TrimCrlf(this string s) => Str.TrimCrlf(s);
        public static string TrimStartWith(this string s, string key, bool case_sensitive = false) { Str.TrimStartWith(ref s, key, case_sensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string TrimEndsWith(this string s, string key, bool case_sensitive = false) { Str.TrimEndsWith(ref s, key, case_sensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string NonNull(this string s) { if (s == null) return ""; else return s; }
        public static string NonNullTrim(this string s) { if (s == null) return ""; else return s.Trim(); }
        public static string TrimNonNull(this string s) => s.NonNullTrim();
        public static string NormalizeSoftEther(this string s, bool trim = false) => Str.NormalizeStrSoftEther(s, trim);
        public static string[] DivideStringByMultiKeywords(this string str, bool caseSensitive, params string[] keywords) => Str.DivideStringMulti(str, caseSensitive, keywords);
        public static bool IsSuitableEncodingForString(this string s, Encoding enc) => Str.IsSuitableEncodingForString(s, enc);
        public static bool IsStringNumOrAlpha(this string s) => Str.IsStringNumOrAlpha(s);
        public static string GetLeft(this string str, int len) => Str.GetLeft(str, len);
        public static string[] SplitStringForSearch(this string str) => Str.SplitStringForSearch(str);
        public static void WriteTextFile(this string s, string filename, Encoding enc = null, bool writeBom = false) { if (enc == null) enc = Str.Utf8Encoding; Str.WriteTextFile(filename, s, enc, writeBom); }
        public static bool StartsWithMulti(this string str, StringComparison comp, params string[] keys) => Str.StartsWithMulti(str, comp, keys);
        public static int FindStringsMulti(this string str, int findStartIndex, StringComparison comp, out int foundKeyIndex, params string[] keys) => Str.FindStrings(str, findStartIndex, comp, out foundKeyIndex, keys);
        public static int GetCountSearchKeywordInStr(this string str, string keyword, bool case_sensitive = false) => Str.GetCountSearchKeywordInStr(str, keyword, case_sensitive);
        public static int[] FindStringIndexes(this string str, string keyword, bool case_sensitive = false) => Str.FindStringIndexes(str, keyword, case_sensitive);
        public static string RemoveSpace(this string str) { Str.RemoveSpace(ref str); return str; }
        public static string Normalize(this string str, bool space = true, bool toHankaku = true, bool toZenkaku = false, bool toZenkakuKana = true) { Str.NormalizeString(ref str, space, toHankaku, toZenkaku, toZenkakuKana); return str; }
        public static string EncodeUrl(this string str, Encoding e) => Str.ToUrl(str, e);
        public static string DecodeUrl(this string str, Encoding e) => Str.FromUrl(str, e);
        public static string EncodeHtml(this string str, bool forceAllSpaceToTag) => Str.ToHtml(str, forceAllSpaceToTag);
        public static string DecodeHtml(this string str) => Str.FromHtml(str);
        public static bool IsPrintableAndSafe(this string str, bool crlf_ok = true, bool html_tag_ng = false) => Str.IsPrintableAndSafe(str, crlf_ok, html_tag_ng);
        public static string Unescape(this string s) => Str.Unescape(s);
        public static string Escape(this string s) => Str.Escape(s);
        public static int GetWidth(this string s) => Str.GetStrWidth(s);
        public static bool IsAllUpperStr(this string s) => Str.IsAllUpperStr(s);
        public static string ReplaceStr(this string str, string oldKeyword, string newKeyword, bool caseSensitive = false) => Str.ReplaceStr(str, oldKeyword, newKeyword, caseSensitive);
        public static bool CheckStrLen(this string str, int len) => Str.CheckStrLen(str, len);
        public static bool CheckMailAddress(this string str) => Str.CheckMailAddress(str);
        public static bool IsSafeAsFileName(this string str, bool path_char_ng = false) => Str.IsSafe(str, path_char_ng);
        public static string MakeSafePath(this string str) => Str.MakeSafePathName(str);
        public static string MakeSafeFileName(this string str) => Str.MakeSafeFileName(str);
        public static string TruncStr(this string str, int len, string append_code = "") => Str.TruncStrEx(str, len, append_code.NonNull());
        public static byte[] HashSHA1(this string str) => Str.HashStr(str);
        public static byte[] HashSHA256(this string str) => Str.HashStrSHA256(str);
        public static string CombinePath(this string str, string p1) => Path.Combine(str, p1);
        public static string CombinePath(this string str, string p1, string p2) => Path.Combine(str, p1, p2);
        public static string CombinePath(this string str, string p1, string p2, string p3) => Path.Combine(str, p1, p2, p3);
        public static string NormalizePath(this string str) => IO.NormalizePath(str);
        public static string InnerFilePath(this string str) => IO.InnerFilePath(str);
        public static string RemoteLastEnMark(this string str) => IO.RemoteLastEnMark(str);
        public static string GetDirectoryName(this string str) => Path.GetDirectoryName(str);
        public static string GetFileName(this string str) => Path.GetFileName(str);
        public static bool IsExtensionMatch(this string str, string ext_list) => IO.IsExtensionsMatch(str, ext_list);
        public static string ReplaceStrWithReplaceClass(this string str, object replace_class, bool case_sensitive = false) => Str.ReplaceStrWithReplaceClass(str, replace_class, case_sensitive);

        public static byte[] NonNull(this byte[] b) { if (b == null) return new byte[0]; else return b; }

        public static string LinesToStr(this string[] lines) => Str.LinesToStr(lines);
        public static string[] UniqueToken(this string[] t) => Str.UniqueToken(t);
        public static List<string> ToList(this string[] t, bool remove_empty = false, bool distinct = false, bool distinct_case_sensitive = false) => Str.StrArrayToList(t, remove_empty, distinct, distinct_case_sensitive);
        public static string Combine(this string[] t, string sepstr) => Str.CombineStringArray(t, sepstr);

        public static string MakeCharArray(this char c, int len) => Str.MakeCharArray(c, len);
        public static bool IsZenkaku(this char c) => Str.IsZenkaku(c);
        public static bool IsCharNumOrAlpha(this char c) => Str.IsCharNumOrAlpha(c);
        public static bool IsPrintableAndSafe(this char c, bool crlf_ok = true, bool html_tag_ng = false) => Str.IsPrintableAndSafe(c, crlf_ok, html_tag_ng);

        public static byte[] NormalizeCrlfWindows(this byte[] s) => Str.NormalizeCrlfWindows(s);
        public static byte[] NormalizeCrlfUnix(this byte[] s) => Str.NormalizeCrlfUnix(s);
        public static byte[] NormalizeCrlfThisPlatform(this byte[] s) => Str.NormalizeCrlfThisPlatform(s);

        public static byte[] CloneByte(this byte[] a) => Util.CopyByte(a);
        public static byte[] CombineByte(this byte[] a, byte[] b) => Util.CombineByteArray(a, b);
        public static byte[] ExtractByte(this byte[] a, int start, int len) => Util.ExtractByteArray(a, start, len);
        public static byte[] ExtractByte(this byte[] a, int start) => Util.ExtractByteArray(a, start, a.Length - start);
        public static bool IsSameByte(this byte[] a, byte[] b) => Util.CompareByte(a, b);
        public static int MemCmp(this byte[] a, byte[] b) => Util.CompareByteRetInt(a, b);

        public static void SaveToFile(this byte[] data, string filename, int offset = 0, int size = 0, bool do_nothing_if_same_contents = false)
            => IO.SaveFile(filename, data, offset, (size == 0 ? data.Length - offset : size), do_nothing_if_same_contents);

        public static void InnerDebug(this object o, string instance_base_name = "") => Dbg.WriteObject(o, instance_base_name);
        public static void InnerPrint(this object o, string instance_base_name = "") => Dbg.PrintObjectInnerString(o, instance_base_name);
        public static string GetInnerStr(this object o, string instance_base_name = "") => Dbg.GetObjectInnerString(o, instance_base_name);
        public static string Old_ObjectToXmlPublic(this object o, Type t = null) => Str.ObjectToXMLSimple_PublicLegacy(o, t ?? o.GetType());
        public static T CloneDeep<T>(this T o) => (T)Util.CloneObject_UsingBinary(o);
        public static byte[] ObjectToBinary(this object o) => Util.ObjectToBinary(o);
        public static object BinaryToObject(this byte[] b) => Util.BinaryToObject(b);
        public static void ObjectToXml(this object obj, MemoryBuffer<byte> dst) => Util.ObjectToXml(obj, dst);
        public static byte[] ObjectToXml(this object obj) => Util.ObjectToXml(obj);
        public static object XmlToObject(this MemoryBuffer<byte> src, Type type) => Util.XmlToObject(src, type);
        public static T XmlToObject<T>(this MemoryBuffer<byte> src) => Util.XmlToObject<T>(src);
        public static object XmlToObject(this byte[] src, Type type) => Util.XmlToObject(src, type);
        public static T XmlToObject<T>(this byte[] src) => Util.XmlToObject<T>(src);

        public static string ObjectToXmlStr(this object obj) => Str.ObjectToXmlStr(obj);
        public static object XmlStrToObject(this string src, Type type) => Str.XmlStrToObject(src, type);
        public static T XmlStrToObject<T>(this string src) => Str.XmlStrToObject<T>(src);

        public static object Print(this object o)
        {
            string str = o.ToString() ?? "null";
            if (o is string) str = (string)o;
            Console.WriteLine(str);
            return o;
        }
        public static object Debug(this object o)
        {
            string str = o.ToString() ?? "null";
            if (o is string) str = (string)o;
            Dbg.WriteLine(str);
            return o;
        }

        public static T[] SingleArray<T>(this T t) => new T[] { t };

        public static string ToStr3(this long s) => Str.ToStr3(s);
        public static string ToStr3(this int s) => Str.ToStr3(s);
        public static string ToStr3(this ulong s) => Str.ToStr3(s);
        public static string ToStr3(this uint s) => Str.ToStr3(s);

        public static string ToDtStr(this DateTime dt, bool with_msecs = false, DtstrOption option = DtstrOption.All, bool with_nanosecs = false) => Str.DateTimeToDtstr(dt, with_msecs, option, with_nanosecs);
        public static string ToDtStr(this DateTimeOffset dt, bool with_msecs = false, DtstrOption option = DtstrOption.All, bool with_nanosecs = false) => Str.DateTimeToDtstr(dt, with_msecs, option, with_nanosecs);

        public static bool IsZeroDateTime(this DateTime dt) => Util.IsZero(dt);
        public static bool IsZeroDateTime(this DateTimeOffset dt) => Util.IsZero(dt);

        public static DateTime NormalizeDateTime(this DateTime dt) => Util.NormalizeDateTime(dt);
        public static DateTimeOffset NormalizeDateTimeOffset(this DateTimeOffset dt) => Util.NormalizeDateTime(dt);

        public static byte[] ReadToEnd(this Stream s, int max_size = 0) => IO.ReadStreamToEnd(s, max_size);
        public static async Task<byte[]> ReadToEndAsync(this Stream s, int max_size = 0, CancellationToken cancel = default(CancellationToken)) => await IO.ReadStreamToEndAsync(s, max_size, cancel);

        public static void TryCancelNoBlock(this CancellationTokenSource cts) => TaskUtil.TryCancelNoBlock(cts);
        public static void TryCancel(this CancellationTokenSource cts) => TaskUtil.TryCancel(cts);
        public static async Task CancelAsync(this CancellationTokenSource cts, bool throwOnFirstException = false) => await TaskUtil.CancelAsync(cts, throwOnFirstException);
        public static async Task TryCancelAsync(this CancellationTokenSource cts) => await TaskUtil.TryCancelAsync(cts);

        public static void TryWait(this Task t, bool noDebugMessage = false) => TaskUtil.TryWait(t, noDebugMessage);
        public static Task TryWaitAsync(this Task t, bool noDebugMessage = false) => TaskUtil.TryWaitAsync(t, noDebugMessage);

        public static T[] ToArrayList<T>(this IEnumerable<T> i) => Util.IEnumerableToArrayList<T>(i);

        public static T GetFirstOrNull<T>(this List<T> list) => (list == null ? default(T) : (list.Count == 0 ? default(T) : list[0]));
        public static T GetFirstOrNull<T>(this T[] list) => (list == null ? default(T) : (list.Length == 0 ? default(T) : list[0]));

        public static void AddStringsByLines(this ISet<string> iset, string strings)
        {
            var lines = Str.GetLines(strings);
            foreach (string line in lines)
            {
                string s = line.Trim();
                if (s.IsFilled())
                {
                    iset.Add(s);
                }
            }
        }

        public static T Old_ClonePublic<T>(this T o)
        {
            byte[] data = Util.ObjectToXml_PublicLegacy(o);

            return (T)Util.XmlToObject_PublicLegacy(data, o.GetType());
        }

        public static TValue GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key) where TValue: new()
        {
            if (d.ContainsKey(key)) return d[key];
            TValue n = new TValue();
            d.Add(key, n);
            return n;
        }

        public static TValue GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key, Func<TValue> new_proc) where TValue : new()
        {
            if (d.ContainsKey(key)) return d[key];
            TValue n = new_proc();
            d.Add(key, n);
            return n;
        }

        //public static List<T> ToList<T>(this IEnumerable<T> i) => new List<T>(i);

        public static IPAddress ToIPAddress(this string s) => IPUtil.StrToIP(s);

        public static IPAddress UnmapIPv4(this IPAddress a) => IPUtil.UnmapIPv6AddressToIPv4Address(a);

        public static void ParseUrl(this string url_string, out Uri uri, out NameValueCollection query_string) => Str.ParseUrl(url_string, out uri, out query_string);

        //public static void AddArrayItemsToList<T>(this IEnumerable<T> items, List<T> list) => Util.AddArrayItemsToList<T>(items, list);
        //public static void AddArrayItemsToList(this IEnumerable items, IList list) => Util.AddArrayItemsToList(items, list);
        //public static void AddArrayItemsToList(this IList list, IEnumerable items) => Util.AddArrayItemsToList(items, list);

        public static string TryGetContentsType(this HttpContentHeaders h) => (h == null ? "" : h.ContentType == null ? "" : h.ContentType.ToString().NonNull());

        public static string GetStrOrEmpty(this NameValueCollection d, string key)
        {
            try
            {
                if (d == null) return "";
                return d[key].NonNull();
            }
            catch { return ""; }
        }

        public static string GetStrOrEmpty<T>(this IDictionary<string, T> d, string key)
        {
            try
            {
                if (d == null) return "";
                if (d.ContainsKey(key) == false) return "";
                object o = d[key];
                if (o == null) return "";
                if (o is string) return (string)o;
                return o.ToString();
            }
            catch { return ""; }
        }

        public static bool IsNullable(this Type t) => Nullable.GetUnderlyingType(t) != null;

        public static void TryCloseNonBlock(this Stream stream)
        {
            BackgroundWorker.Run(param =>
            {
                try
                {
                    stream.Close();
                }
                catch
                {
                }
            }, null);
        }

        public static async Task<byte[]> ReadAsyncWithTimeout(this Stream stream, int maxSize = 65536, int? timeout = null, bool? readAll = false, CancellationToken cancel = default)
        {
            byte[] tmp = new byte[maxSize];
            int ret = await stream.ReadAsyncWithTimeout(tmp, 0, tmp.Length, timeout,
                readAll: readAll,
                cancel: cancel);
            return Util.CopyByte(tmp, 0, ret);
        }

        public static async Task<int> ReadAsyncWithTimeout(this Stream stream, byte[] buffer, int offset = 0, int? count = null, int? timeout = null, bool? readAll = false, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
        {
            if (timeout == null) timeout = stream.ReadTimeout;
            if (timeout <= 0) timeout = Timeout.Infinite;
            int targetReadSize = count ?? (buffer.Length - offset);
            if (targetReadSize == 0) return 0;

            try
            {
                int ret = await TaskUtil.DoAsyncWithTimeout(async (cancelLocal) =>
                {
                    if (readAll == false)
                    {
                        return await stream.ReadAsync(buffer, offset, targetReadSize, cancelLocal);
                    }
                    else
                    {
                        int currentReadSize = 0;

                        while (currentReadSize != targetReadSize)
                        {
                            int sz = await stream.ReadAsync(buffer, offset + currentReadSize, targetReadSize - currentReadSize, cancelLocal);
                            if (sz == 0)
                            {
                                return 0;
                            }

                            currentReadSize += sz;
                        }

                        return currentReadSize;
                    }
                },
                timeout: (int)timeout,
                cancel: cancel,
                cancelTokens: cancelTokens);

                if (ret <= 0)
                {
                    throw new EndOfStreamException("The NetworkStream is disconnected.");
                }

                return ret;
            }
            catch
            {
                stream.TryCloseNonBlock();
                throw;
            }
        }

        public static async Task WriteAsyncWithTimeout(this Stream stream, byte[] buffer, int offset = 0, int? count = null, int? timeout = null, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
        {
            if (timeout == null) timeout = stream.WriteTimeout;
            if (timeout <= 0) timeout = Timeout.Infinite;
            int targetWriteSize = count ?? (buffer.Length - offset);
            if (targetWriteSize == 0) return;

            try
            {
                await TaskUtil.DoAsyncWithTimeout(async (cancelLocal) =>
                {
                    await stream.WriteAsync(buffer, offset, targetWriteSize, cancelLocal);
                    return 0;
                },
                timeout: (int)timeout,
                cancel: cancel,
                cancelTokens: cancelTokens);

            }
            catch
            {
                stream.TryCloseNonBlock();
                throw;
            }
        }

        public static void LaissezFaire(this Task task)
        {
            if (task != null)
            {
                task.ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        string err = t.Exception.ToString();

                        ("LaissezFaire: Error: " + err).Debug();
                    }
                });
            }
        }

        public static byte[] AsciiToByteArray(this object o) => Encoding.ASCII.GetBytes(o.ToString());

        public static string ByteArrayToAscii(this byte[] d) => Encoding.ASCII.GetString(d);

        public static void DisposeSafe(this IDisposable obj)
        {
            try
            {
                if (obj != null) obj.Dispose();
            }
            catch { }
        }

        public static IAsyncResult AsApm<T>(this Task<T> task,
                                    AsyncCallback callback,
                                    object state)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            var tcs = new TaskCompletionSource<T>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(t.Result);

                if (callback != null)
                    callback(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }

        public static IAsyncResult AsApm(this Task task,
                                            AsyncCallback callback,
                                            object state)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            var tcs = new TaskCompletionSource<int>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(0);

                if (callback != null)
                    callback(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }

        public static async Task ConnectAsync(this TcpClient tc, string host, int port,
            int timeout = Timeout.Infinite, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
        {
            await TaskUtil.DoAsyncWithTimeout(
            mainProc: async c =>
            {
                await tc.ConnectAsync(host, port);
                return 0;
            },
            cancelProc: () =>
            {
                tc.DisposeSafe();
            },
            timeout: timeout,
            cancel: cancel,
            cancelTokens: cancelTokens);
        }

        public static void ThrowIfErrorOrCanceled(this Task task)
        {
            if (task == null) return;
            if (task.IsFaulted) task.Exception.ReThrow();
            if (task.IsCanceled) throw new TaskCanceledException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Bit<T>(this T value, T flag) where T : Enum
            => value.HasFlag(flag);

        public static Exception GetSingleException(this Exception ex)
        {
            if (ex == null) return null;

            var tex = ex as TargetInvocationException;
            if (tex != null) ex = tex.InnerException;

            var aex = ex as AggregateException;
            if (aex != null) ex = aex.Flatten().InnerExceptions[0];

            return ex;
        }

        public static void ReThrow(this Exception ex)
        {
            if (ex == null) throw ex;
            ExceptionDispatchInfo.Capture(ex.GetSingleException()).Throw();
        }

        public static CertSelectorCallback GetStaticServerCertSelector(this X509Certificate2 cert) => Secure.StaticServerCertSelector(cert);

        public static bool IsZero(this byte[] data) => Util.IsZero(data);
        public static bool IsZero(this byte[] data, int offset, int size) => Util.IsZero(data, offset, size);
        public static bool IsZero(this Span<byte> data) => Util.IsZero(data);
        public static bool IsZero(this Memory<byte> data) => Util.IsZero(data);

        public static bool IsEmpty<T>(this T data) => Util.IsEmpty(data);
        public static bool IsFilled<T>(this T data) => Util.IsFilled(data);

        public static T DbOverwriteValues<T>(this T baseData, T overwriteData) where T : new() => Util.DbOverwriteValues(baseData, overwriteData);
        public static void DbEnforceNonNull(this object obj) => Util.DbEnforceNonNull(obj);
        public static T DbEnforceNonNullSelf<T>(this T obj)
        {
            obj.DbEnforceNonNull();
            return obj;
        }

        public static T DequeueOrNull<T>(this Queue<T> queue) => (queue.TryDequeue(out T ret) ? ret : default);
    }
}

