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
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IPA.Cores.Helper.Basic
{
    static class BasicHelper
    {
        public static byte[] GetBytes_UTF8(this string str, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(str));
        public static byte[] GetBytes_UTF16LE(this string str, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.UniEncoding.GetBytes(str));
        public static byte[] GetBytes_ShiftJis(this string str) => Str.ShiftJisEncoding.GetBytes(str);
        public static byte[] GetBytes_Ascii(this string str) => Str.AsciiEncoding.GetBytes(str);
        public static byte[] GetBytes_Euc(this string str) => Str.EucJpEncoding.GetBytes(str);
        public static byte[] GetBytes(this string str, bool appendBom = false) => Util.CombineByteArray(appendBom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(str));
        public static byte[] GetBytes(this string str, Encoding encoding) => encoding.GetBytes(str);

        public static string GetString_UTF8(this byte[] byteArray) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _);
        public static string GetString_UTF16LE(this byte[] byteArray) => Str.DecodeString(byteArray, Str.UniEncoding, out _);
        public static string GetString_ShiftJis(this byte[] byteArray) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _);
        public static string GetString_Ascii(this byte[] byteArray) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _);
        public static string GetString_Euc(this byte[] byteArray) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _);
        public static string GetString(this byte[] byteArray, Encoding defaultEncoding) => Str.DecodeString(byteArray, defaultEncoding, out _);
        public static string GetString(this byte[] byteArray) => Str.DecodeStringAutoDetect(byteArray, out _);

        public static string GetString_UTF8(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _);
        public static string GetString_UTF16LE(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.UniEncoding, out _);
        public static string GetString_ShiftJis(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _);
        public static string GetString_Ascii(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _);
        public static string GetString_Euc(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _);
        public static string GetString(this ReadOnlySpan<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray, default_encoding, out _);
        public static string GetString(this ReadOnlySpan<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray, out _);

        public static string GetString_UTF8(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _);
        public static string GetString_UTF16LE(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.UniEncoding, out _);
        public static string GetString_ShiftJis(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _);
        public static string GetString_Ascii(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _);
        public static string GetString_Euc(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _);
        public static string GetString(this Span<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray, default_encoding, out _);
        public static string GetString(this Span<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray, out _);


        public static string GetString_UTF8(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.Utf8Encoding, out _);
        public static string GetString_UTF16LE(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.UniEncoding, out _);
        public static string GetString_ShiftJis(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.ShiftJisEncoding, out _);
        public static string GetString_Ascii(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.AsciiEncoding, out _);
        public static string GetString_Euc(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.EucJpEncoding, out _);
        public static string GetString(this ReadOnlyMemory<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray.Span, default_encoding, out _);
        public static string GetString(this ReadOnlyMemory<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray.Span, out _);

        public static string GetString_UTF8(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.Utf8Encoding, out _);
        public static string GetString_UTF16LE(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.UniEncoding, out _);
        public static string GetString_ShiftJis(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.ShiftJisEncoding, out _);
        public static string GetString_Ascii(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.AsciiEncoding, out _);
        public static string GetString_Euc(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.EucJpEncoding, out _);
        public static string GetString(this Memory<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray.Span, default_encoding, out _);
        public static string GetString(this Memory<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray.Span, out _);

        public static string GetHexString(this byte[] byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string GetHexString(this Span<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string GetHexString(this ReadOnlySpan<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static byte[] GetHexBytes(this string str) => Str.HexToByte(str);

        public static bool ToBool(this string str) => Str.StrToBool(str);
        public static byte[] ToByte(this string str) => Str.StrToByte(str);
        public static DateTime ToDate(this string str, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToDate(str, toUtc, emptyToZeroDateTime);
        public static DateTime ToTime(this string s, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToTime(s, toUtc, emptyToZeroDateTime);
        public static DateTime ToDateTime(this string s, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToDateTime(s, toUtc, emptyToZeroDateTime);
        public static object ToEnum(this string s, object defaultValue) => Str.StrToEnum(s, defaultValue);
        public static int ToInt(this string s) => Str.StrToInt(s);
        public static long ToLong(this string s) => Str.StrToLong(s);
        public static uint ToUInt(this string s) => Str.StrToUInt(s);
        public static ulong ToULong(this string s) => Str.StrToULong(s);
        public static double ToDouble(this string s) => Str.StrToDouble(s);
        public static decimal ToDecimal(this string s) => Str.StrToDecimal(s);
        public static bool IsSame(this string s, string t, bool ignoreCase = false) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (ignoreCase ? Str.StrCmpi(s, t) : Str.StrCmp(s, t))));
        public static bool IsSame(this string s, string t, StringComparison comparison) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : string.Equals(s, t, comparison)));
        public static bool IsSamei(this string s, string t) => IsSame(s, t, true);
        public static bool IsSameiIgnoreUnderscores(this string s, string t) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (s.Replace("_", "").IsSamei(t.Replace("_", "")))));
        public static int Cmp(this string s, string t, bool ignoreCase = false) => ((s == null && t == null) ? 0 : ((s == null ? 1 : t == null ? -1 : (ignoreCase ? Str.StrCmpiRetInt(s, t) : Str.StrCmpRetInt(s, t)))));
        public static int Cmp(this string s, string t, StringComparison comparison) => ((s == null && t == null) ? 0 : ((s == null ? 1 : t == null ? -1 : string.Compare(s, t, comparison))));
        public static int Cmpi(this string s, string t, bool ignoreCase = false) => Cmp(s, t, true);
        public static string[] GetLines(this string s) => Str.GetLines(s);
        public static bool GetKeyAndValue(this string s, out string key, out string value, string splitStr = "") => Str.GetKeyAndValue(s, out key, out value, splitStr);
        public static bool IsDouble(this string s) => Str.IsDouble(s);
        public static bool IsLong(this string s) => Str.IsLong(s);
        public static bool IsInt(this string s) => Str.IsInt(s);
        public static bool IsNumber(this string s) => Str.IsNumber(s);
        public static bool InStr(this string s, string keyword, bool ignoreCase = false) => Str.InStr(s, keyword, !ignoreCase);
        public static string[] ParseCmdLine(this string s) => Str.ParseCmdLine(s);
        public static object Old_XmlToObjectPublic(this string s, Type t) => Str.XMLToObjectSimple_PublicLegacy(s, t);
        public static StrToken ToToken(this string s, string splitStr = " ,\t\r\n") => new StrToken(s, splitStr);
        public static string OneLine(this string s) => Str.OneLine(s);
        public static string FormatC(this string s) => Str.FormatC(s);
        public static string FormatC(this string s, params object[] args) => Str.FormatC(s, args);
        public static void Printf(this string s) => Str.Printf(s, new object[0]);
        public static void Printf(this string s, params object[] args) => Str.Printf(s, args);
        public static string Print(this string s) { Con.WriteLine(s); return s; }
        public static string Debug(this string s) { Dbg.WriteLine(s); return s; }
        public static int Search(this string s, string keyword, int start = 0, bool caseSenstive = false) => Str.SearchStr(s, keyword, start, caseSenstive);
        public static string TrimCrlf(this string s) => Str.TrimCrlf(s);
        public static string TrimStartWith(this string s, string key, bool caseSensitive = false) { Str.TrimStartWith(ref s, key, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string TrimEndsWith(this string s, string key, bool caseSensitive = false) { Str.TrimEndsWith(ref s, key, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string NonNull(this string s) { if (s == null) return ""; else return s; }
        public static string NonNullTrim(this string s) { if (s == null) return ""; else return s.Trim(); }
        public static string TrimNonNull(this string s) => s.NonNullTrim();
        public static string NoSpace(this string s, string replaceWith = "_") => s.NonNull().ReplaceStr(" ", replaceWith, false);
        public static string NormalizeSoftEther(this string s, bool trim = false) => Str.NormalizeStrSoftEther(s, trim);
        public static string[] DivideStringByMultiKeywords(this string str, bool caseSensitive, params string[] keywords) => Str.DivideStringMulti(str, caseSensitive, keywords);
        public static bool IsSuitableEncodingForString(this string s, Encoding encoding) => Str.IsSuitableEncodingForString(s, encoding);
        public static bool IsStringNumOrAlpha(this string s) => Str.IsStringNumOrAlpha(s);
        public static string GetLeft(this string str, int len) => Str.GetLeft(str, len);
        public static string[] SplitStringForSearch(this string str) => Str.SplitStringForSearch(str);
        public static void WriteTextFile(this string s, string filename, Encoding encoding = null, bool writeBom = false) { if (encoding == null) encoding = Str.Utf8Encoding; Str.WriteTextFile(filename, s, encoding, writeBom); }
        public static bool StartsWithMulti(this string str, StringComparison comparison, params string[] keys) => Str.StartsWithMulti(str, comparison, keys);
        public static int FindStringsMulti(this string str, int findStartIndex, StringComparison comparison, out int foundKeyIndex, params string[] keys) => Str.FindStrings(str, findStartIndex, comparison, out foundKeyIndex, keys);
        public static int GetCountSearchKeywordInStr(this string str, string keyword, bool caseSensitive = false) => Str.GetCountSearchKeywordInStr(str, keyword, caseSensitive);
        public static int[] FindStringIndexes(this string str, string keyword, bool caseSensitive = false) => Str.FindStringIndexes(str, keyword, caseSensitive);
        public static string RemoveSpace(this string str) { Str.RemoveSpace(ref str); return str; }
        public static string Normalize(this string str, bool space = true, bool toHankaku = true, bool toZenkaku = false, bool toZenkakuKana = true) { Str.NormalizeString(ref str, space, toHankaku, toZenkaku, toZenkakuKana); return str; }
        public static string EncodeUrl(this string str, Encoding e) => Str.ToUrl(str, e);
        public static string DecodeUrl(this string str, Encoding e) => Str.FromUrl(str, e);
        public static string EncodeHtml(this string str, bool forceAllSpaceToTag) => Str.ToHtml(str, forceAllSpaceToTag);
        public static string DecodeHtml(this string str) => Str.FromHtml(str);
        public static bool IsPrintableAndSafe(this string str, bool crlfIsOk = true, bool html_tag_ng = false) => Str.IsPrintableAndSafe(str, crlfIsOk, html_tag_ng);
        public static string Unescape(this string s) => Str.Unescape(s);
        public static string Escape(this string s) => Str.Escape(s);
        public static int GetWidth(this string s) => Str.GetStrWidth(s);
        public static bool IsAllUpperStr(this string s) => Str.IsAllUpperStr(s);
        public static string ReplaceStr(this string str, string oldKeyword, string newKeyword, bool caseSensitive = false) => Str.ReplaceStr(str, oldKeyword, newKeyword, caseSensitive);
        public static bool CheckStrLen(this string str, int len) => Str.CheckStrLen(str, len);
        public static bool CheckMailAddress(this string str) => Str.CheckMailAddress(str);
        public static bool IsSafeAsFileName(this string str, bool pathCharsAreNotGood = false) => Str.IsSafe(str, pathCharsAreNotGood);
        public static string MakeSafePath(this string str) => Str.MakeSafePathName(str);
        public static string MakeSafeFileName(this string str) => Str.MakeSafeFileName(str);
        public static string TruncStr(this string str, int len, string appendCode = "") => Str.TruncStrEx(str, len, appendCode.NonNull());
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
        public static bool IsExtensionMatch(this string str, string extensionsList) => IO.IsExtensionsMatch(str, extensionsList);
        public static string ReplaceStrWithReplaceClass(this string str, object replaceClass, bool caseSensitive = false) => Str.ReplaceStrWithReplaceClass(str, replaceClass, caseSensitive);

        public static byte[] NonNull(this byte[] b) { if (b == null) return new byte[0]; else return b; }

        public static string LinesToStr(this string[] lines) => Str.LinesToStr(lines);
        public static string[] UniqueToken(this string[] t) => Str.UniqueToken(t);
        public static List<string> ToStrList(this string[] t, bool removeEmpty = false, bool distinct = false, bool distinctCaseSensitive = false) => Str.StrArrayToList(t, removeEmpty, distinct, distinctCaseSensitive);
        public static string Combine(this string[] t, string sepstr) => Str.CombineStringArray(t, sepstr);

        public static string MakeCharArray(this char c, int len) => Str.MakeCharArray(c, len);
        public static bool IsZenkaku(this char c) => Str.IsZenkaku(c);
        public static bool IsCharNumOrAlpha(this char c) => Str.IsCharNumOrAlpha(c);
        public static bool IsPrintableAndSafe(this char c, bool crlIsOk = true, bool html_tag_ng = false) => Str.IsPrintableAndSafe(c, crlIsOk, html_tag_ng);

        public static string NormalizeCrlf(this string str, CrlfStyle style) => Str.NormalizeCrlf(str, style);

        //public static byte[] NormalizeCrlfWindows(this Span<byte> s) => Str.NormalizeCrlfWindows(s);
        //public static byte[] NormalizeCrlfUnix(this Span<byte> s) => Str.NormalizeCrlfUnix(s);
        //public static byte[] NormalizeCrlfThisPlatform(this Span<byte> s) => Str.NormalizeCrlfThisPlatform(s);

        //public static byte[] NormalizeCrlfWindows(this ReadOnlySpan<byte> s) => Str.NormalizeCrlfWindows(s);
        //public static byte[] NormalizeCrlfUnix(this ReadOnlySpan<byte> s) => Str.NormalizeCrlfUnix(s);
        //public static byte[] NormalizeCrlfThisPlatform(this ReadOnlySpan<byte> s) => Str.NormalizeCrlfThisPlatform(s);

        //public static byte[] NormalizeCrlfWindows(this byte[] s) => Str.NormalizeCrlfWindows(s);
        //public static byte[] NormalizeCrlfUnix(this byte[] s) => Str.NormalizeCrlfUnix(s);
        //public static byte[] NormalizeCrlfThisPlatform(this byte[] s) => Str.NormalizeCrlfThisPlatform(s);

        public static byte[] CloneByte(this byte[] a) => Util.CopyByte(a);
        public static byte[] CombineByte(this byte[] a, byte[] b) => Util.CombineByteArray(a, b);
        public static byte[] ExtractByte(this byte[] a, int start, int len) => Util.ExtractByteArray(a, start, len);
        public static byte[] ExtractByte(this byte[] a, int start) => Util.ExtractByteArray(a, start, a.Length - start);
        public static bool IsSameByte(this byte[] a, byte[] b) => Util.CompareByte(a, b);
        public static int MemCmp(this byte[] a, byte[] b) => Util.CompareByteRetInt(a, b);

        public static void SaveToFile(this byte[] data, string filename, int offset = 0, int size = 0, bool doNothingIfSameContents = false)
            => IO.SaveFile(filename, data, offset, (size == 0 ? data.Length - offset : size), doNothingIfSameContents);

        public static void DebugObject(this object o) => Dbg.DebugObject(o);
        public static void PrintObject(this object o) => Dbg.PrintObject(o);
        public static string GetObjectDump(this object o, string instanceBaseName = "", string separatorString = ", ", bool hideEmpty = true, bool jsonIfPossible = false)
            => Dbg.GetObjectDump(o, instanceBaseName, separatorString, hideEmpty, jsonIfPossible);
        public static string Old_ObjectToXmlPublic(this object o, Type t = null) => Str.ObjectToXMLSimple_PublicLegacy(o, t ?? o.GetType());
        public static T CloneDeep<T>(this T o) => (T)Util.CloneObject_UsingBinary(o);
        public static byte[] ObjectToBinary(this object o) => Util.ObjectToBinary(o);
        public static object BinaryToObject(this ReadOnlySpan<byte> b) => Util.BinaryToObject(b);
        public static object BinaryToObject(this Span<byte> b) => Util.BinaryToObject(b);
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
            Con.WriteLine(o);
            return o;
        }
        public static object Debug(this object o)
        {
            Dbg.WriteLine(o);
            return o;
        }

        public static T[] SingleArray<T>(this T t) => new T[] { t };

        public static string ToString3(this long s) => Str.ToString3(s);
        public static string ToString3(this int s) => Str.ToString3(s);
        public static string ToString3(this ulong s) => Str.ToString3(s);
        public static string ToString3(this uint s) => Str.ToString3(s);

        public static string ToDtStr(this DateTime dt, bool withMSecs = false, DtstrOption option = DtstrOption.All, bool withNanoSecs = false) => Str.DateTimeToDtstr(dt, withMSecs, option, withNanoSecs);
        public static string ToDtStr(this DateTimeOffset dt, bool withMSsecs = false, DtstrOption option = DtstrOption.All, bool withNanoSecs = false) => Str.DateTimeToDtstr(dt, withMSsecs, option, withNanoSecs);

        public static string ToTsStr(this TimeSpan timeSpan, bool withMSecs = false, bool withNanoSecs = false) => Str.TimeSpanToTsStr(timeSpan, withMSecs, withNanoSecs);

        public static bool IsZeroDateTime(this DateTime dt) => Util.IsZero(dt);
        public static bool IsZeroDateTime(this DateTimeOffset dt) => Util.IsZero(dt);

        public static DateTime NormalizeDateTime(this DateTime dt) => Util.NormalizeDateTime(dt);
        public static DateTimeOffset NormalizeDateTimeOffset(this DateTimeOffset dt) => Util.NormalizeDateTime(dt);

        public static byte[] ReadToEnd(this Stream s, int maxSize = 0) => IO.ReadStreamToEnd(s, maxSize);
        public static async Task<byte[]> ReadToEndAsync(this Stream s, int maxSize = 0, CancellationToken cancel = default(CancellationToken)) => await IO.ReadStreamToEndAsync(s, maxSize, cancel);

        public static long SeekToBegin(this Stream s) => s.Seek(0, SeekOrigin.Begin);
        public static long SeekToEnd(this Stream s) => s.Seek(0, SeekOrigin.End);

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

        public static TValue GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key, Func<TValue> newProc) where TValue : new()
        {
            if (d.ContainsKey(key)) return d[key];
            TValue n = newProc();
            d.Add(key, n);
            return n;
        }

        public static IPAddress ToIPAddress(this string s) => IPUtil.StrToIP(s);

        public static IPAddress UnmapIPv4(this IPAddress a) => IPUtil.UnmapIPv6AddressToIPv4Address(a);

        public static void ParseUrl(this string urlString, out Uri uri, out NameValueCollection queryString) => Str.ParseUrl(urlString, out uri, out queryString);

        public static string TryGetContentType(this System.Net.Http.Headers.HttpContentHeaders h) => (h == null ? "" : h.ContentType == null ? "" : h.ContentType.ToString().NonNull());
        public static string TryGetContentType(this IPA.Cores.Basic.HttpClientCore.HttpContentHeaders h) => (h == null ? "" : h.ContentType == null ? "" : h.ContentType.ToString().NonNull());

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

        public static void LaissezFaire(this Task task, bool noDebugMessage = false)
        {
            if (noDebugMessage == false)
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
        }

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
                    tcs.TrySetResult(t.GetResult());

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

        public static async Task ConnectAsync(this TcpClient tcpClient, string host, int port,
            int timeout = Timeout.Infinite, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
        {
            await TaskUtil.DoAsyncWithTimeout(
            mainProc: async c =>
            {
                await tcpClient.ConnectAsync(host, port);
                return 0;
            },
            cancelProc: () =>
            {
                tcpClient.DisposeSafe();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BitAny<T>(this T value, T flags) where T : Enum
        {
            ulong value1 = Convert.ToUInt64(value);
            ulong value2 = Convert.ToUInt64(flags);
            if (value2 == 0) return true;

            return ((value1 & value2) == 0) ? false : true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAnyOfThem<T>(T value, params T[] flags) where T : Enum
        {
            if (flags == null || flags.Length == 0) return false;
            ulong value1 = Convert.ToUInt64(value);
            foreach (T flag in flags)
            {
                ulong value2 = Convert.ToUInt64(flag);
                if (value1 == value2) return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1) where T : Enum
            => IsAnyOfThem(value, v1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2) where T : Enum
            => IsAnyOfThem(value, v1, v2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3, v4);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8, T v9) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8, v9);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8, T v9, T v10) where T : Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8, v9, v10);

        public static Exception GetSingleException(this Exception ex)
        {
            if (ex == null) return null;

            var tex = ex as TargetInvocationException;
            if (tex != null) ex = tex.InnerException;

            var aex = ex as AggregateException;
            if (aex != null) ex = aex.Flatten().InnerExceptions[0];

            return ex;
        }

        public static void ReThrow(this Exception exception)
        {
            if (exception == null) throw exception;
            ExceptionDispatchInfo.Capture(exception.GetSingleException()).Throw();
        }

        public static CertSelectorCallback GetStaticServerCertSelector(this X509Certificate2 cert) => Secure.StaticServerCertSelector(cert);

        public static bool IsZero(this byte[] data) => Util.IsZero(data);
        public static bool IsZero(this byte[] data, int offset, int size) => Util.IsZero(data, offset, size);
        public static bool IsZero(this ReadOnlySpan<byte> data) => Util.IsZero(data);
        public static bool IsZero(this Span<byte> data) => Util.IsZero(data);
        public static bool IsZero(this ReadOnlyMemory<byte> data) => Util.IsZero(data);
        public static bool IsZero(this Memory<byte> data) => Util.IsZero(data);

        public static bool IsEmpty<T>(this T data, bool zeroValueIsEmpty = false) => Util.IsEmpty(data, zeroValueIsEmpty);
        public static bool IsFilled<T>(this T data, bool zeroValueIsEmpty = false) => Util.IsFilled(data, zeroValueIsEmpty);

        public static T FilledOrDefault<T>(this T obj, T defaultValue = default, bool zeroValueIsEmpty = true) => (obj.IsFilled(zeroValueIsEmpty) ? obj : defaultValue);
        public static T FilledOrException<T>(this T obj, Exception exception = null, bool zeroValueIsEmpty = true)
        {
            if (obj.IsFilled(zeroValueIsEmpty))
                return obj;

            throw exception ?? new NotImplementedException();
        }

        public static T DbOverwriteValues<T>(this T baseData, T overwriteData) where T : new() => Util.DbOverwriteValues(baseData, overwriteData);
        public static void DbEnforceNonNull(this object obj) => Util.DbEnforceNonNull(obj);
        public static T DbEnforceNonNullSelf<T>(this T obj)
        {
            obj.DbEnforceNonNull();
            return obj;
        }

        public static T DequeueOrNull<T>(this Queue<T> queue) => (queue.TryDequeue(out T ret) ? ret : default);

        public static async Task<bool> WaitUntilCanceledAsync(this CancellationToken cancel, int timeout = Timeout.Infinite)
        {
            if (cancel.IsCancellationRequested)
                return true;
            await TaskUtil.WaitObjectsAsync(cancels: cancel.SingleArray(), timeout: timeout);
            return cancel.IsCancellationRequested;
        }
        public static Task<bool> WaitUntilCanceledAsync(this CancellationTokenSource cancel, int timeout = Timeout.Infinite)
            => WaitUntilCanceledAsync(cancel.Token, timeout);

        public static DateTime OverwriteKind(this DateTime dt, DateTimeKind kind) => new DateTime(dt.Ticks, kind);

        public static DateTimeOffset AsDateTimeOffset(this DateTime dt, bool isLocalTime) => new DateTimeOffset(dt.OverwriteKind(isLocalTime ? DateTimeKind.Local : DateTimeKind.Utc));

        public static async Task LeakCheck(this Task t, bool noCheck = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null)
        {
            if (noCheck)
            {
                await t;
                return;
            }

            using (LeakChecker.Enter(filename, line, caller))
            {
                await t;
            }
        }

        public static async Task<T> LeakCheck<T>(this Task<T> t, bool noCheck = false, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null)
        {
            if (noCheck)
            {
                return await t;
            }

            using (LeakChecker.Enter(filename, line, caller))
            {
                return await t;
            }
        }

        public static T DoAction<T>(this T obj, Action action)
        {
            action();
            return obj;
        }

        public static T DoAction<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }

        public static T DoFunc<T>(this T obj, Func<T, T> func) => func(obj);

        public static T2 DoFunc<T1, T2>(this T1 obj, Func<T1, T2> func) => func(obj);

        public static T CloneIfClonable<T>(this T obj) => Util.CloneIfClonable(obj);

        public static bool IsAnonymousType(this Type type)
        {
            if (type.IsGenericType)
            {
                var d = type.GetGenericTypeDefinition();
                if (d.IsClass && d.IsSealed && d.Attributes.HasFlag(TypeAttributes.NotPublic))
                {
                    var attributes = d.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false);
                    if (attributes != null && attributes.Length > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsAnonymousType<T>(this T instance) => IsAnonymousType(instance.GetType());

        public static void PostData(this object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
            => LocalLogRouter.PostData(obj, tag, copyToDebug, priority);

        public static void PostAccessLog(this object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
            => LocalLogRouter.PostAccessLog(obj, tag, copyToDebug, priority);

        public static void DoForEach<T>(this IEnumerable<T> list, Action<T> action) => list.ToList().ForEach(action);


        public static T GetResult<T>(this Task<T> task) => task.GetAwaiter().GetResult();

        public static void GetResult(this Task task) => task.GetAwaiter().GetResult();

        public static T GetResult<T>(this ValueTask<T> task) => task.GetAwaiter().GetResult();

        public static void GetResult(this ValueTask task) => task.GetAwaiter().GetResult();


        public static T TryGetResult<T>(this Task<T> task, bool noDebugMessage = false)
        {
            if (task == null) return default;
            try
            {
                return GetResult(task);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    Con.WriteDebug("TryGetResult error");
                    Con.WriteDebug(ex);
                }

                return default;
            }
        }

        public static void TryGetResult(this Task task, bool noDebugMessage = false)
        {
            if (task == null) return;
            try
            {
                GetResult(task);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    Con.WriteDebug("TryGetResult error");
                    Con.WriteDebug(ex);
                }
            }
        }

        public static T TryGetResult<T>(this ValueTask<T> task, bool noDebugMessage = false)
        {
            if (task == default) return default;
            try
            {
                return GetResult(task);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    Con.WriteDebug("TryGetResult error");
                    Con.WriteDebug(ex);
                }

                return default;
            }
        }

        public static void TryGetResult(this ValueTask task, bool noDebugMessage = false)
        {
            if (task == default) return;
            try
            {
                GetResult(task);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    Con.WriteDebug("TryGetResult error");
                    Con.WriteDebug(ex);
                }
            }
        }

        readonly static PropertyInfo PInfo_SafeFileHandle_IsAsyncGet = typeof(SafeFileHandle).GetProperty("IsAsync", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetProperty);
        readonly static PropertyInfo PInfo_SafeFileHandle_IsAsyncSet = typeof(SafeFileHandle).GetProperty("IsAsync", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty);

        public static bool IsAsync(this SafeFileHandle handle)
        {
            try
            {
                return ((bool?)PInfo_SafeFileHandle_IsAsyncGet.GetValue(handle)) ?? false;
            }
            catch
            {
                return false;
            }
        }

        public static void SetAsync(this SafeFileHandle handle, bool isAsync)
        {
            PInfo_SafeFileHandle_IsAsyncSet.SetValue(handle, isAsync);
        }

        public static string ToPointerHexString(this IntPtr ptr) => $"0x{ptr.ToInt64():X}";

        public static ConcurrentRandomAccess<T> CreateConcurrentRandomAccess<T>(this IRandomAccess<T> randomAccess) => new ConcurrentRandomAccess<T>(randomAccess);

        public static string ToIPv4v6String(this AddressFamily family) => ToIPv4v6String(family);
        public static string ToIPv4v6String(this AddressFamily? family)
        {
            switch (family)
            {
                case null:
                    return "IPv4 / IPv6";

                case AddressFamily.InterNetwork:
                    return "IPv4";

                case AddressFamily.InterNetworkV6:
                    return "IPv6";

                default:
                    return "Unknown";
            }
        }
    }
}

