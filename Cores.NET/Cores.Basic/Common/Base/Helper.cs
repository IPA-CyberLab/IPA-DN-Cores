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
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;

namespace IPA.Cores.Helper.Basic
{
    static class FastHashHelper
    {
        public static int _ComputeHash32(this string data, StringComparison cmp = StringComparison.Ordinal)
            => data.GetHashCode(cmp);

        public static int _ComputeHash32(this ReadOnlySpan<byte> data)
            => Marvin.ComputeHash32(data);

        public static int _ComputeHash32(this Span<byte> data)
            => Marvin.ComputeHash32(data);

        public static int _ComputeHash32(this byte[] data, int offset, int size)
            => Marvin.ComputeHash32(data._AsReadOnlySpan(offset, size));

        public static int _ComputeHash32(this byte[] data, int offset)
            => Marvin.ComputeHash32(data._AsReadOnlySpan(offset));

        public static int _ComputeHash32(this byte[] data)
            => Marvin.ComputeHash32(data._AsReadOnlySpan());

        public static int _ComputeHash32<TStruct>(this ref TStruct data) where TStruct : unmanaged
        {
            unsafe
            {
                void* ptr = Unsafe.AsPointer(ref data);
                Span<byte> span = new Span<byte>(ptr, sizeof(TStruct));
                return _ComputeHash32(span);
            }
        }

        public static int _ComputeHash32<TStruct>(this ReadOnlySpan<TStruct> data) where TStruct : unmanaged
        {
            var span = MemoryMarshal.Cast<TStruct, byte>(data);
            return _ComputeHash32(span);
        }

        public static int _ComputeHash32<TStruct>(this Span<TStruct> data) where TStruct : unmanaged
        {
            var span = MemoryMarshal.Cast<TStruct, byte>(data);
            return _ComputeHash32(span);
        }

        public static int _ComputeHash32<TStruct>(this TStruct[] data, int offset, int size) where TStruct : unmanaged
            => _ComputeHash32(data._AsReadOnlySpan(offset, size));

        public static int _ComputeHash32<TStruct>(this TStruct[] data, int offset) where TStruct : unmanaged
            => _ComputeHash32(data._AsReadOnlySpan(offset));

        public static int _ComputeHash32<TStruct>(this TStruct[] data) where TStruct : unmanaged
            => _ComputeHash32(data._AsReadOnlySpan());
    }

    static class BasicHelper
    {
        public static byte[] _GetBytes_UTF8(this string str, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(str));
        public static byte[] _GetBytes_UTF16LE(this string str, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.UniEncoding.GetBytes(str));
        public static byte[] _GetBytes_ShiftJis(this string str) => Str.ShiftJisEncoding.GetBytes(str);
        public static byte[] _GetBytes_Ascii(this string str) => Str.AsciiEncoding.GetBytes(str);
        public static byte[] _GetBytes_Euc(this string str) => Str.EucJpEncoding.GetBytes(str);
        public static byte[] _GetBytes(this string str, bool appendBom = false) => Util.CombineByteArray(appendBom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(str));
        public static byte[] _GetBytes(this string str, Encoding encoding) => encoding.GetBytes(str);

        public static string _GetString_UTF8(this byte[] byteArray) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _);
        public static string _GetString_UTF16LE(this byte[] byteArray) => Str.DecodeString(byteArray, Str.UniEncoding, out _);
        public static string _GetString_ShiftJis(this byte[] byteArray) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _);
        public static string _GetString_Ascii(this byte[] byteArray) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _);
        public static string _GetString_Euc(this byte[] byteArray) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _);
        public static string _GetString(this byte[] byteArray, Encoding defaultEncoding) => Str.DecodeString(byteArray, defaultEncoding, out _);
        public static string _GetString(this byte[] byteArray) => Str.DecodeStringAutoDetect(byteArray, out _);

        public static string _GetString_UTF8(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _);
        public static string _GetString_UTF16LE(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.UniEncoding, out _);
        public static string _GetString_ShiftJis(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _);
        public static string _GetString_Ascii(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _);
        public static string _GetString_Euc(this ReadOnlySpan<byte> byteArray) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _);
        public static string _GetString(this ReadOnlySpan<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray, default_encoding, out _);
        public static string _GetString(this ReadOnlySpan<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray, out _);

        public static string _GetString_UTF8(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _);
        public static string _GetString_UTF16LE(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.UniEncoding, out _);
        public static string _GetString_ShiftJis(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _);
        public static string _GetString_Ascii(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _);
        public static string _GetString_Euc(this Span<byte> byteArray) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _);
        public static string _GetString(this Span<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray, default_encoding, out _);
        public static string _GetString(this Span<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray, out _);


        public static string _GetString_UTF8(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.Utf8Encoding, out _);
        public static string _GetString_UTF16LE(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.UniEncoding, out _);
        public static string _GetString_ShiftJis(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.ShiftJisEncoding, out _);
        public static string _GetString_Ascii(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.AsciiEncoding, out _);
        public static string _GetString_Euc(this ReadOnlyMemory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.EucJpEncoding, out _);
        public static string _GetString(this ReadOnlyMemory<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray.Span, default_encoding, out _);
        public static string _GetString(this ReadOnlyMemory<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray.Span, out _);

        public static string _GetString_UTF8(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.Utf8Encoding, out _);
        public static string _GetString_UTF16LE(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.UniEncoding, out _);
        public static string _GetString_ShiftJis(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.ShiftJisEncoding, out _);
        public static string _GetString_Ascii(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.AsciiEncoding, out _);
        public static string _GetString_Euc(this Memory<byte> byteArray) => Str.DecodeString(byteArray.Span, Str.EucJpEncoding, out _);
        public static string _GetString(this Memory<byte> byteArray, Encoding default_encoding) => Str.DecodeString(byteArray.Span, default_encoding, out _);
        public static string _GetString(this Memory<byte> byteArray) => Str.DecodeStringAutoDetect(byteArray.Span, out _);

        public static string _GetHexString(this byte[] byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string _GetHexString(this Span<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string _GetHexString(this ReadOnlySpan<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string _GetHexString(this Memory<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray.Span, padding);
        public static string _GetHexString(this ReadOnlyMemory<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray.Span, padding);
        public static byte[] _GetHexBytes(this string str) => Str.HexToByte(str);

        public static bool _ToBool(this string str) => Str.StrToBool(str);
        public static byte[] _ToByte(this string str) => Str.StrToByte(str);
        public static DateTime _ToDate(this string str, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToDate(str, toUtc, emptyToZeroDateTime);
        public static DateTime _ToTime(this string s, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToTime(s, toUtc, emptyToZeroDateTime);
        public static DateTime _ToDateTime(this string s, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToDateTime(s, toUtc, emptyToZeroDateTime);
        public static object _ToEnum(this string s, object defaultValue, bool exactOnly = false) => Str.StrToEnum(s, defaultValue, exactOnly);
        public static int _ToInt(this string s) => Str.StrToInt(s);
        public static long _ToLong(this string s) => Str.StrToLong(s);
        public static uint _ToUInt(this string s) => Str.StrToUInt(s);
        public static ulong _ToULong(this string s) => Str.StrToULong(s);
        public static double _ToDouble(this string s) => Str.StrToDouble(s);
        public static decimal _ToDecimal(this string s) => Str.StrToDecimal(s);
        public static bool _IsSame(this string s, string t, bool ignoreCase = false) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (ignoreCase ? Str.StrCmpi(s, t) : Str.StrCmp(s, t))));
        public static bool _IsSame(this string s, string t, StringComparison comparison) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : string.Equals(s, t, comparison)));
        public static bool _IsSamei(this string s, string t) => _IsSame(s, t, true);
        public static bool _IsSameiIgnoreUnderscores(this string s, string t) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (s.Replace("_", "")._IsSamei(t.Replace("_", "")))));
        public static int _Cmp(this string s, string t, bool ignoreCase = false) => ((s == null && t == null) ? 0 : ((s == null ? 1 : t == null ? -1 : (ignoreCase ? Str.StrCmpiRetInt(s, t) : Str.StrCmpRetInt(s, t)))));
        public static int _Cmp(this string s, string t, StringComparison comparison) => ((s == null && t == null) ? 0 : ((s == null ? 1 : t == null ? -1 : string.Compare(s, t, comparison))));
        public static int _Cmpi(this string s, string t, bool ignoreCase = false) => _Cmp(s, t, true);
        public static string[] _GetLines(this string s) => Str.GetLines(s);
        public static bool _GetKeyAndValue(this string s, out string key, out string value, string splitStr = "") => Str.GetKeyAndValue(s, out key, out value, splitStr);
        public static bool _IsDouble(this string s) => Str.IsDouble(s);
        public static bool _IsLong(this string s) => Str.IsLong(s);
        public static bool _IsInt(this string s) => Str.IsInt(s);
        public static bool _IsNumber(this string s) => Str.IsNumber(s);
        public static bool _InStr(this string s, string keyword, bool ignoreCase = false) => Str.InStr(s, keyword, !ignoreCase);
        public static string[] _ParseCmdLine(this string s) => Str.ParseCmdLine(s);
        public static object _Old_XmlToObjectPublic(this string s, Type t) => Str.XMLToObjectSimple_PublicLegacy(s, t);
        public static StrToken _ToToken(this string s, string splitStr = " ,\t\r\n") => new StrToken(s, splitStr);
        public static string _OneLine(this string s, string splitter = " / ") => Str.OneLine(s, splitter);
        public static string _FormatC(this string s) => Str.FormatC(s);
        public static string _FormatC(this string s, params object[] args) => Str.FormatC(s, args);
        public static void _Printf(this string s) => Str.Printf(s, new object[0]);
        public static void _Printf(this string s, params object[] args) => Str.Printf(s, args);
        public static string _Print(this string s) { Con.WriteLine(s); return s; }
        public static string _Debug(this string s) { Dbg.WriteLine(s); return s; }
        public static int _Search(this string s, string keyword, int start = 0, bool caseSenstive = false) => Str.SearchStr(s, keyword, start, caseSenstive);
        public static string _TrimCrlf(this string s) => Str.TrimCrlf(s);
        public static string _TrimStartWith(this string s, string key, bool caseSensitive = false) { Str.TrimStartWith(ref s, key, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string _TrimEndsWith(this string s, string key, bool caseSensitive = false) { Str.TrimEndsWith(ref s, key, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string _NonNull(this string s) { if (s == null) return ""; else return s; }
        public static string _NonNullTrim(this string s) { if (s == null) return ""; else return s.Trim(); }
        public static string _TrimNonNull(this string s) => s._NonNullTrim();
        public static bool _TryTrimStartWith(this string srcStr, out string outStr, StringComparison comparison, params string[] keys) => Str.TryTrimStartWith(srcStr, out outStr, comparison, keys);
        public static string _NoSpace(this string s, string replaceWith = "_") => s._NonNull()._ReplaceStr(" ", replaceWith, false);
        public static string _NormalizeSoftEther(this string s, bool trim = false) => Str.NormalizeStrSoftEther(s, trim);
        public static string[] _DivideStringByMultiKeywords(this string str, bool caseSensitive, params string[] keywords) => Str.DivideStringMulti(str, caseSensitive, keywords);
        public static bool _IsSuitableEncodingForString(this string s, Encoding encoding) => Str.IsSuitableEncodingForString(s, encoding);
        public static bool _IsStringNumOrAlpha(this string s) => Str.IsStringNumOrAlpha(s);
        public static string _GetLeft(this string str, int len) => Str.GetLeft(str, len);
        public static string[] _SplitStringForSearch(this string str) => Str.SplitStringForSearch(str);
        public static void _WriteTextFile(this string s, string filename, Encoding encoding = null, bool writeBom = false) { if (encoding == null) encoding = Str.Utf8Encoding; Str.WriteTextFile(filename, s, encoding, writeBom); }
        public static bool _StartsWithMulti(this string str, StringComparison comparison, params string[] keys) => Str.StartsWithMulti(str, comparison, keys);
        public static int _FindStringsMulti(this string str, int findStartIndex, StringComparison comparison, out int foundKeyIndex, params string[] keys) => Str.FindStrings(str, findStartIndex, comparison, out foundKeyIndex, keys);
        public static int _GetCountSearchKeywordInStr(this string str, string keyword, bool caseSensitive = false) => Str.GetCountSearchKeywordInStr(str, keyword, caseSensitive);
        public static int[] _FindStringIndexes(this string str, string keyword, bool caseSensitive = false) => Str.FindStringIndexes(str, keyword, caseSensitive);
        public static string _RemoveSpace(this string str) { Str.RemoveSpace(ref str); return str; }
        public static string _Normalize(this string str, bool space = true, bool toHankaku = true, bool toZenkaku = false, bool toZenkakuKana = true) { Str.NormalizeString(ref str, space, toHankaku, toZenkaku, toZenkakuKana); return str; }
        public static string _EncodeUrl(this string str, Encoding e) => Str.ToUrl(str, e);
        public static string _DecodeUrl(this string str, Encoding e) => Str.FromUrl(str, e);
        public static string _EncodeHtml(this string str, bool forceAllSpaceToTag) => Str.ToHtml(str, forceAllSpaceToTag);
        public static string _DecodeHtml(this string str) => Str.FromHtml(str);
        public static bool _IsSafeAndPrintable(this string str, bool crlfIsOk = true, bool html_tag_ng = false) => Str.IsSafeAndPrintable(str, crlfIsOk, html_tag_ng);
        public static string _Unescape(this string s) => Str.Unescape(s);
        public static string _Escape(this string s) => Str.Escape(s);
        public static int _GetWidth(this string s) => Str.GetStrWidth(s);
        public static bool _IsAllUpperStr(this string s) => Str.IsAllUpperStr(s);
        public static string _ReplaceStr(this string str, string oldKeyword, string newKeyword, bool caseSensitive = false) => Str.ReplaceStr(str, oldKeyword, newKeyword, caseSensitive);
        public static bool _CheckStrLen(this string str, int len) => Str.CheckStrLen(str, len);
        public static bool _CheckMailAddress(this string str) => Str.CheckMailAddress(str);
        public static bool _IsSafeAsFileName(this string str, bool pathCharsAreNotGood = false) => Str.IsSafe(str, pathCharsAreNotGood);
        public static string _MakeSafePath(this string str) => Str.MakeSafePathName(str);
        public static string _MakeSafeFileName(this string str) => Str.MakeSafeFileName(str);
        public static string _TruncStr(this string str, int len) => Str.TruncStr(str, len);
        public static string _TruncStrEx(this string str, int len, string appendCode = "...") => Str.TruncStrEx(str, len, appendCode);
        public static byte[] _HashSHA1(this string str) => Str.HashStr(str);
        public static byte[] _HashSHA256(this string str) => Str.HashStrSHA256(str);
        public static string _CombinePath(this string str, string p1) => Path.Combine(str, p1);
        public static string _CombinePath(this string str, string p1, string p2) => Path.Combine(str, p1, p2);
        public static string _CombinePath(this string str, string p1, string p2, string p3) => Path.Combine(str, p1, p2, p3);
        //public static string _NormalizePath(this string str) => BasicFile.NormalizePath(str);
        //public static string _InnerFilePath(this string str) => BasicFile.InnerFilePath(str);
        //public static string _RemoteLastEnMark(this string str) => BasicFile.RemoteLastEnMark(str);
        public static string _GetDirectoryName(this string str) => Path.GetDirectoryName(str);
        public static string _GetFileName(this string str) => Path.GetFileName(str);
        //public static bool _IsExtensionMatch(this string str, string extensionsList) => BasicFile.IsExtensionsMatch(str, extensionsList);
        public static string _ReplaceStrWithReplaceClass(this string str, object replaceClass, bool caseSensitive = false) => Str.ReplaceStrWithReplaceClass(str, replaceClass, caseSensitive);

        public static byte[] _NonNull(this byte[] b) { if (b == null) return new byte[0]; else return b; }

        public static string _LinesToStr(this IEnumerable<string> lines) => Str.LinesToStr(lines);
        public static string[] _UniqueToken(this IEnumerable<string> t) => Str.UniqueToken(t);
        public static List<string> _ToStrList(this IEnumerable<string> t, bool removeEmpty = false, bool distinct = false, bool distinctCaseSensitive = false) => Str.StrArrayToList(t, removeEmpty, distinct, distinctCaseSensitive);
        public static string _Combine(this IEnumerable<string> t, string sepstr) => Str.CombineStringArray(t, sepstr);

        public static string _MakeCharArray(this char c, int len) => Str.MakeCharArray(c, len);
        public static bool _IsZenkaku(this char c) => Str.IsZenkaku(c);
        public static bool _IsCharNumOrAlpha(this char c) => Str.IsCharNumOrAlpha(c);
        public static bool _IsSafeAndPrintable(this char c, bool crlIsOk = true, bool html_tag_ng = false) => Str.IsSafeAndPrintable(c, crlIsOk, html_tag_ng);

        public static string _NormalizeCrlf(this string str, CrlfStyle style) => Str.NormalizeCrlf(str, style);

        //public static byte[] NormalizeCrlfWindows(this Span<byte> s) => Str.NormalizeCrlfWindows(s);
        //public static byte[] NormalizeCrlfUnix(this Span<byte> s) => Str.NormalizeCrlfUnix(s);
        //public static byte[] NormalizeCrlfThisPlatform(this Span<byte> s) => Str.NormalizeCrlfThisPlatform(s);

        //public static byte[] NormalizeCrlfWindows(this ReadOnlySpan<byte> s) => Str.NormalizeCrlfWindows(s);
        //public static byte[] NormalizeCrlfUnix(this ReadOnlySpan<byte> s) => Str.NormalizeCrlfUnix(s);
        //public static byte[] NormalizeCrlfThisPlatform(this ReadOnlySpan<byte> s) => Str.NormalizeCrlfThisPlatform(s);

        //public static byte[] NormalizeCrlfWindows(this byte[] s) => Str.NormalizeCrlfWindows(s);
        //public static byte[] NormalizeCrlfUnix(this byte[] s) => Str.NormalizeCrlfUnix(s);
        //public static byte[] NormalizeCrlfThisPlatform(this byte[] s) => Str.NormalizeCrlfThisPlatform(s);

        public static byte[] _CloneByte(this byte[] a) => Util.CopyByte(a);
        public static byte[] _CombineByte(this byte[] a, byte[] b) => Util.CombineByteArray(a, b);
        public static byte[] _ExtractByte(this byte[] a, int start, int len) => Util.ExtractByteArray(a, start, len);
        public static byte[] _ExtractByte(this byte[] a, int start) => Util.ExtractByteArray(a, start, a.Length - start);
        public static bool _IsSameByte(this byte[] a, byte[] b) => Util.CompareByte(a, b);
        public static int _MemCmp(this byte[] a, byte[] b) => Util.CompareByteRetInt(a, b);

        //public static void _SaveToFile(this byte[] data, string filename, int offset = 0, int size = 0, bool doNothingIfSameContents = false)
        //    => BasicFile.SaveFile(filename, data, offset, (size == 0 ? data.Length - offset : size), doNothingIfSameContents);

        public static void _DebugObject(this object o) => Dbg.DebugObject(o);
        public static void _PrintObject(this object o) => Dbg.PrintObject(o);
        public static string _GetObjectDump(this object o, string instanceBaseName = "", string separatorString = ", ", bool hideEmpty = true, bool jsonIfPossible = false)
            => Dbg.GetObjectDump(o, instanceBaseName, separatorString, hideEmpty, jsonIfPossible);
        public static string _Old_ObjectToXmlPublic(this object o, Type t = null) => Str.ObjectToXMLSimple_PublicLegacy(o, t ?? o.GetType());
        public static T _CloneDeep<T>(this T o) => (T)Util.CloneObject_UsingBinary(o);
        public static byte[] _ObjectToBinary(this object o) => Util.ObjectToBinary(o);
        public static object _BinaryToObject(this ReadOnlySpan<byte> b) => Util.BinaryToObject(b);
        public static object _BinaryToObject(this Span<byte> b) => Util.BinaryToObject(b);
        public static object _BinaryToObject(this byte[] b) => Util.BinaryToObject(b);

        public static void _ObjectToXml(this object obj, MemoryBuffer<byte> dst, DataContractSerializerSettings settings = null) => Util.ObjectToXml(obj, dst, settings);
        public static byte[] _ObjectToXml(this object obj, DataContractSerializerSettings settings = null) => Util.ObjectToXml(obj, settings);
        public static object _XmlToObject(this MemoryBuffer<byte> src, Type type, DataContractSerializerSettings settings = null) => Util.XmlToObject(src, type, settings);
        public static T _XmlToObject<T>(this MemoryBuffer<byte> src, DataContractSerializerSettings settings = null) => Util.XmlToObject<T>(src, settings);
        public static object _XmlToObject(this byte[] src, Type type, DataContractSerializerSettings settings = null) => Util.XmlToObject(src, type, settings);
        public static T _XmlToObject<T>(this byte[] src, DataContractSerializerSettings settings = null) => Util.XmlToObject<T>(src, settings);

        public static string _ObjectToXmlStr(this object obj, DataContractSerializerSettings settings = null) => Str.ObjectToXmlStr(obj, settings);
        public static object _XmlStrToObject(this string src, Type type, DataContractSerializerSettings settings = null) => Str.XmlStrToObject(src, type, settings);
        public static T _XmlStrToObject<T>(this string src, DataContractSerializerSettings settings = null) => Str.XmlStrToObject<T>(src, settings);

        public static void _ObjectToRuntimeJson(this object obj, MemoryBuffer<byte> dst, DataContractJsonSerializerSettings settings = null) => Util.ObjectToRuntimeJson(obj, dst, settings);
        public static byte[] _ObjectToRuntimeJson(this object obj, DataContractJsonSerializerSettings settings = null) => Util.ObjectToRuntimeJson(obj, settings);
        public static object _RuntimeJsonToObject(this MemoryBuffer<byte> src, Type type, DataContractJsonSerializerSettings settings = null) => Util.RuntimeJsonToObject(src, type, settings);
        public static T _RuntimeJsonToObject<T>(this MemoryBuffer<byte> src, DataContractJsonSerializerSettings settings = null) => Util.RuntimeJsonToObject<T>(src, settings);
        public static object _RuntimeJsonToObject(this byte[] src, Type type, DataContractJsonSerializerSettings settings = null) => Util.RuntimeJsonToObject(src, type, settings);
        public static T _RuntimeJsonToObject<T>(this byte[] src, DataContractJsonSerializerSettings settings = null) => Util.RuntimeJsonToObject<T>(src, settings);

        public static string _ObjectToRuntimeJsonStr(this object obj, DataContractJsonSerializerSettings settings = null) => Str.ObjectToRuntimeJsonStr(obj, settings);
        public static object _RuntimeJsonStrToObject(this string src, Type type, DataContractJsonSerializerSettings settings = null) => Str.RuntimeJsonStrToObject(src, type, settings);
        public static T _RuntimeJsonStrToObject<T>(this string src, DataContractJsonSerializerSettings settings = null) => Str.RuntimeJsonStrToObject<T>(src, settings);

        public static object _Print(this object o)
        {
            Con.WriteLine(o);
            return o;
        }
        public static object _Debug(this object o)
        {
            Dbg.WriteLine(o);
            return o;
        }

        public static T[] _SingleArray<T>(this T t) => new T[] { t };
        public static Span<T> _SingleSpan<T>(this T t) => new T[] { t };
        public static ReadOnlySpan<T> _SingleReadOnlySpan<T>(this T t) => new T[] { t };
        public static List<T> _SingleList<T>(this T t) => (new T[] { t }).ToList();

        public static string _ToString3(this long s) => Str.ToString3(s);
        public static string _ToString3(this int s) => Str.ToString3(s);
        public static string _ToString3(this ulong s) => Str.ToString3(s);
        public static string _ToString3(this uint s) => Str.ToString3(s);

        public static string _ToDtStr(this DateTime dt, bool withMSecs = false, DtStrOption option = DtStrOption.All, bool withNanoSecs = false) => Str.DateTimeToDtstr(dt, withMSecs, option, withNanoSecs);
        public static string _ToDtStr(this DateTimeOffset dt, bool withMSsecs = false, DtStrOption option = DtStrOption.All, bool withNanoSecs = false) => Str.DateTimeToDtstr(dt, withMSsecs, option, withNanoSecs);

        public static string _ToTsStr(this TimeSpan timeSpan, bool withMSecs = false, bool withNanoSecs = false) => Str.TimeSpanToTsStr(timeSpan, withMSecs, withNanoSecs);

        public static bool _IsZeroDateTime(this DateTime dt) => Util.IsZero(dt);
        public static bool _IsZeroDateTime(this DateTimeOffset dt) => Util.IsZero(dt);

        public static DateTime _NormalizeDateTime(this DateTime dt) => Util.NormalizeDateTime(dt);
        public static DateTimeOffset _NormalizeDateTimeOffset(this DateTimeOffset dt) => Util.NormalizeDateTime(dt);

        public static byte[] _ReadToEnd(this Stream s, int maxSize = 0) => Util.ReadStreamToEnd(s, maxSize);
        public static async Task<byte[]> _ReadToEndAsync(this Stream s, int maxSize = 0, CancellationToken cancel = default(CancellationToken)) => await Util.ReadStreamToEndAsync(s, maxSize, cancel);

        public static long _SeekToBegin(this Stream s) => s.Seek(0, SeekOrigin.Begin);
        public static long _SeekToEnd(this Stream s) => s.Seek(0, SeekOrigin.End);

        public static void _TryCancelNoBlock(this CancellationTokenSource cts) => TaskUtil.TryCancelNoBlock(cts);
        public static void _TryCancel(this CancellationTokenSource cts) => TaskUtil.TryCancel(cts);
        public static async Task _CancelAsync(this CancellationTokenSource cts, bool throwOnFirstException = false) => await TaskUtil.CancelAsync(cts, throwOnFirstException);
        public static async Task _TryCancelAsync(this CancellationTokenSource cts) => await TaskUtil.TryCancelAsync(cts);

        public static void _TryWait(this Task t, bool noDebugMessage = false) => TaskUtil.TryWait(t, noDebugMessage);
        public static Task _TryWaitAsync(this Task t, bool noDebugMessage = false) => TaskUtil.TryWaitAsync(t, noDebugMessage);

        public static T[] _ToArrayList<T>(this IEnumerable<T> i) => Util.IEnumerableToArrayList<T>(i);

        public static T _GetFirstOrNull<T>(this List<T> list) => (list == null ? default(T) : (list.Count == 0 ? default(T) : list[0]));
        public static T _GetFirstOrNull<T>(this T[] list) => (list == null ? default(T) : (list.Length == 0 ? default(T) : list[0]));

        public static void _AddStringsByLines(this ISet<string> iset, string strings)
        {
            var lines = Str.GetLines(strings);
            foreach (string line in lines)
            {
                string s = line.Trim();
                if (s._IsFilled())
                {
                    iset.Add(s);
                }
            }
        }

        public static T _Old_ClonePublic<T>(this T o)
        {
            byte[] data = Util.ObjectToXml_PublicLegacy(o);

            return (T)Util.XmlToObject_PublicLegacy(data, o.GetType());
        }

        public static TValue _GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key) where TValue: new()
        {
            if (d.ContainsKey(key)) return d[key];
            TValue n = new TValue();
            d.Add(key, n);
            return n;
        }

        public static TValue _GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key, Func<TValue> newProc) where TValue : new()
        {
            if (d.ContainsKey(key)) return d[key];
            TValue n = newProc();
            d.Add(key, n);
            return n;
        }

        public static IPAddress _ToIPAddress(this string s) => IPUtil.StrToIP(s);

        public static IPAddress _UnmapIPv4(this IPAddress a) => IPUtil.UnmapIPv6AddressToIPv4Address(a);

        public static IPAddressType _GetIPAddressType(this IPAddress ip) => IPUtil.GetIPAddressType(ip);

        public static void _ParseUrl(this string urlString, out Uri uri, out NameValueCollection queryString) => Str.ParseUrl(urlString, out uri, out queryString);

        public static string _TryGetContentType(this System.Net.Http.Headers.HttpContentHeaders h) => (h == null ? "" : h.ContentType == null ? "" : h.ContentType.ToString()._NonNull());
        public static string _TryGetContentType(this IPA.Cores.Basic.HttpClientCore.HttpContentHeaders h) => (h == null ? "" : h.ContentType == null ? "" : h.ContentType.ToString()._NonNull());

        public static void _DebugHeaders(this IPA.Cores.Basic.HttpClientCore.HttpHeaders h) => h._DoForEach(x => (x.Key + ": " + x.Value._Combine(", "))._Debug());

        public static string _GetStrOrEmpty(this NameValueCollection d, string key)
        {
            try
            {
                if (d == null) return "";
                return d[key]._NonNull();
            }
            catch { return ""; }
        }

        public static string _GetStrOrEmpty<T>(this IDictionary<string, T> d, string key)
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

        public static bool _IsNullable(this Type t) => Nullable.GetUnderlyingType(t) != null;

        public static void _TryCloseNonBlock(this Stream stream)
        {
            Task.Run(() =>
            {
                try
                {
                    stream.Close();
                }
                catch { }
            });
        }

        public static async Task<Memory<byte>> _ReadAllAsync(this Stream stream, int size, CancellationToken cancel = default)
        {
            if (stream is PipeStream ps)
            {
                return await ps.ReceiveAllAsync(size, cancel);
            }
            Memory<byte> tmp = MemoryHelper.FastAllocMemory<byte>(size);
            await _ReadAllAsync(stream, tmp, cancel);
            return tmp;
        }

        public static async Task _ReadAllAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancel = default)
        {
            if (stream is PipeStream ps)
            {
                await ps.ReceiveAllAsync(buffer, cancel);
                return;
            }

            if (buffer.Length == 0) return;
            int currentReadSize = 0;

            while (currentReadSize != buffer.Length)
            {
                int sz = await stream.ReadAsync(buffer.Slice(currentReadSize, buffer.Length - currentReadSize), cancel);
                if (sz == 0) throw new DisconnectedException();

                currentReadSize += sz;
            }
        }

        public static async Task<Memory<byte>> _ReadAsyncWithTimeout(this Stream stream, int maxSize = 65536, int? timeout = null, bool? readAll = false, CancellationToken cancel = default)
        {
            Memory<byte> tmp = MemoryHelper.FastAllocMemory<byte>(maxSize);
            int ret = await stream._ReadAsyncWithTimeout(tmp, timeout,
                readAll: readAll,
                cancel: cancel);
            return tmp.Slice(0, ret);
        }

        public static async Task<int> _ReadAsyncWithTimeout(this Stream stream, Memory<byte> buffer, int? timeout = null, bool? readAll = false, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
        {
            if (timeout == null) timeout = stream.ReadTimeout;
            if (timeout <= 0) timeout = Timeout.Infinite;
            if (buffer.Length == 0) return 0;

            try
            {
                int ret = await TaskUtil.DoAsyncWithTimeout(async (cancelLocal) =>
                {
                    if (readAll == false)
                    {
                        int a = await stream.ReadAsync(buffer, cancelLocal);
                        if (a <= 0) throw new DisconnectedException();
                        return a;
                    }
                    else
                    {
                        int currentReadSize = 0;

                        while (currentReadSize != buffer.Length)
                        {
                            int sz = await stream.ReadAsync(buffer.Slice(currentReadSize, buffer.Length - currentReadSize), cancelLocal);
                            if (sz <= 0)
                            {
                                throw new DisconnectedException();
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
                stream._TryCloseNonBlock();
                throw;
            }
        }

        public static async Task _WriteAsyncWithTimeout(this Stream stream, byte[] buffer, int offset = 0, int? count = null, int? timeout = null, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
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
                stream._TryCloseNonBlock();
                throw;
            }
        }

        public static void _LaissezFaire(this Task task, bool noDebugMessage = false)
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

                            ("LaissezFaire: Error: " + err)._Debug();
                        }
                    });
                }
            }
        }

        public static void _DisposeSafe(this IAsyncService obj, Exception ex = null)
        {
            try
            {
                if (obj != null) obj.Dispose(ex);
            }
            catch { }
        }

        public static void _DisposeSafe(this IDisposable obj)
        {
            try
            {
                if (obj != null) obj.Dispose();
            }
            catch { }
        }

        public static void _CancelSafe(this IAsyncService obj, Exception ex = null)
        {
            try
            {
                if (obj != null) obj.Cancel(ex);
            }
            catch { }
        }

        public static Task _DisposeWithCleanupSafeAsync(this IAsyncService obj, Exception ex = null)
        {
            try
            {
                if (obj != null)
                {
                    return obj.DisposeWithCleanupAsync(ex);
                }
            }
            catch { }
            return Task.CompletedTask;
        }

        public static Task _CleanupSafeAsync(this IAsyncService obj, Exception ex = null)
        {
            try
            {
                if (obj != null)
                {
                    return obj.CleanupAsync(ex);
                }
            }
            catch { }
            return Task.CompletedTask;
        }

        public static IAsyncResult _AsApm<T>(this Task<T> task,
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
                    tcs.TrySetResult(t._GetResult());

                if (callback != null)
                    callback(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }

        public static IAsyncResult _AsApm(this Task task,
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

        public static async Task _ConnectAsync(this TcpClient tcpClient, string host, int port,
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
                tcpClient._DisposeSafe();
            },
            timeout: timeout,
            cancel: cancel,
            cancelTokens: cancelTokens);
        }

        public static void _ThrowIfErrorOrCanceled(this Task task)
        {
            if (task == null) return;
            if (task.IsFaulted) task.Exception._ReThrow();
            if (task.IsCanceled) throw new TaskCanceledException();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T If<T>(this T value, bool condition) where T : unmanaged, Enum
            => (condition ? value : default);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Bit<T>(this T value, T flag) where T : unmanaged, Enum
            => value.HasFlag(flag);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BitAny<T>(this T value, T flags) where T : unmanaged, Enum
        {
            ulong value1 = value._RawReadValueUInt64();
            ulong value2 = flags._RawReadValueUInt64();
            if (value2 == 0) return true;

            return ((value1 & value2) == 0) ? false : true;
        }

        public static T ParseAsDefault<T>(this T defaultValue, string str, bool exactOnly = false, bool noMatchError = false) where T : unmanaged, Enum
        {
            return Str.ParseEnum<T>(str, defaultValue, exactOnly, noMatchError);
        }
        
        public static T _ParseEnum<T>(this string str, T defaultValue, bool exactOnly = false, bool noMatchError = false) where T : unmanaged, Enum
        {
            return Str.ParseEnum<T>(str, defaultValue, exactOnly, noMatchError);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAnyOfThem<T>(T value, params T[] flags) where T : unmanaged, Enum
        {
            if (flags == null || flags.Length == 0) return false;
            ulong value1 = value._RawReadValueUInt64();
            foreach (T flag in flags)
            {
                ulong value2 = flag._RawReadValueUInt64();
                if (value1 == value2) return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8, T v9) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8, v9);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8, T v9, T v10) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8, v9, v10);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] GetEnumValuesList<T>(this T sampleValue) where T : unmanaged, Enum
            => Util.GetEnumValuesList<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetMaxEnumValue<T>(this T sampleValue) where T : unmanaged, Enum
            => Util.GetMaxEnumValue<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetMaxEnumValueSInt32<T>(this T sampleValue) where T : unmanaged, Enum
            => Util.GetMaxEnumValueSInt32<T>();

        public static Exception _GetSingleException(this Exception ex)
        {
            if (ex == null) return null;

            var tex = ex as TargetInvocationException;
            if (tex != null)
                ex = tex.InnerException;

            var aex = ex as AggregateException;
            if (aex != null) ex = aex.Flatten().InnerExceptions[0];

            return ex;
        }

        public static void _ReThrow(this Exception exception)
        {
            if (exception == null) throw exception;
            ExceptionDispatchInfo.Capture(exception._GetSingleException()).Throw();
        }

        public static CertSelectorCallback _GetStaticServerCertSelector(this X509Certificate2 cert) => Secure.StaticServerCertSelector(cert);

        public static bool _IsZero(this byte[] data) => Util.IsZero(data);
        public static bool _IsZero(this byte[] data, int offset, int size) => Util.IsZero(data, offset, size);
        public static bool _IsZero(this ReadOnlySpan<byte> data) => Util.IsZero(data);
        public static bool _IsZero(this Span<byte> data) => Util.IsZero(data);
        public static bool _IsZero(this ReadOnlyMemory<byte> data) => Util.IsZero(data);
        public static bool _IsZero(this Memory<byte> data) => Util.IsZero(data);


        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool _IsZeroFast(this byte[] data) => Util.IsZeroFast(data);
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool _IsZeroFast(this byte[] data, int offset, int size) => Util.IsZeroFast(data, offset, size);
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool _IsZeroFast(this ReadOnlySpan<byte> data) => Util.IsZeroFast(data);
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool _IsZeroFast(this Span<byte> data) => Util.IsZeroFast(data);
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool _IsZeroFast(this ReadOnlyMemory<byte> data) => Util.IsZeroFast(data);
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool _IsZeroFast(this Memory<byte> data) => Util.IsZeroFast(data);

        public static bool _IsNullOrZeroLen(this string str) => string.IsNullOrEmpty(str);

        public static bool _IsEmpty<T>(this T data, bool zeroValueIsEmpty = false) => Util.IsEmpty(data, zeroValueIsEmpty);
        public static bool _IsFilled<T>(this T data, bool zeroValueIsEmpty = false) => Util.IsFilled(data, zeroValueIsEmpty);

        public static T _FilledOrDefault<T>(this T obj, T defaultValue = default, bool zeroValueIsEmpty = true) => (obj._IsFilled(zeroValueIsEmpty) ? obj : defaultValue);
        public static T _FilledOrException<T>(this T obj, Exception exception = null, bool zeroValueIsEmpty = true)
        {
            if (obj._IsFilled(zeroValueIsEmpty))
                return obj;

            throw exception ?? new NotImplementedException();
        }

        public static T _DbOverwriteValues<T>(this T baseData, T overwriteData) where T : new() => Util.DbOverwriteValues(baseData, overwriteData);
        public static void _DbEnforceNonNull(this object obj) => Util.DbEnforceNonNull(obj);
        public static T _DbEnforceNonNullSelf<T>(this T obj)
        {
            obj._DbEnforceNonNull();
            return obj;
        }

        public static T _DequeueOrNull<T>(this Queue<T> queue) => (queue.TryDequeue(out T ret) ? ret : default);

        public static async Task<bool> _WaitUntilCanceledAsync(this CancellationToken cancel, int timeout = Timeout.Infinite)
        {
            if (cancel.IsCancellationRequested)
                return true;
            await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), timeout: timeout);
            return cancel.IsCancellationRequested;
        }
        public static Task<bool> _WaitUntilCanceledAsync(this CancellationTokenSource cancel, int timeout = Timeout.Infinite)
            => _WaitUntilCanceledAsync(cancel.Token, timeout);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTime _OverwriteKind(this DateTime dt, DateTimeKind kind) => new DateTime(dt.Ticks, kind);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTimeOffset _AsDateTimeOffset(this DateTime dt, bool isLocalTime) => new DateTimeOffset(dt._OverwriteKind(isLocalTime ? DateTimeKind.Local : DateTimeKind.Utc));

        public static async Task _LeakCheck(this Task t, bool noCheck = false, LeakCounterKind kind = LeakCounterKind.TaskLeak, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null)
        {
            if (noCheck)
            {
                await t;
                return;
            }

            using (LeakChecker.Enter(kind, filename, line, caller))
            {
                await t;
            }
        }

        public static async Task<T> _LeakCheck<T>(this Task<T> t, bool noCheck = false, LeakCounterKind kind = LeakCounterKind.TaskLeak, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string caller = null)
        {
            if (noCheck)
            {
                return await t;
            }

            using (LeakChecker.Enter(kind, filename, line, caller))
            {
                return await t;
            }
        }

        public static T _DoAction<T>(this T obj, Action action)
        {
            action();
            return obj;
        }

        public static T _DoAction<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }

        public static T _DoFunc<T>(this T obj, Func<T, T> func) => func(obj);

        public static T2 _DoFunc<T1, T2>(this T1 obj, Func<T1, T2> func) => func(obj);

        public static T _CloneIfClonable<T>(this T obj) => Util.CloneIfClonable(obj);

        public static bool _IsAnonymousType(this Type type)
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

        public static bool _IsAnonymousType<T>(this T instance) => _IsAnonymousType(instance.GetType());

        public static void _PostData(this object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
            => LocalLogRouter.PostData(obj, tag, copyToDebug, priority);

        public static void _PostAccessLog(this object obj, string tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
            => LocalLogRouter.PostAccessLog(obj, tag, copyToDebug, priority);

        public static void _DoForEach<T>(this IEnumerable<T> list, Action<T> action) => list.ToList().ForEach(action);


        public static T _GetResult<T>(this Task<T> task) => task.GetAwaiter().GetResult();

        public static void _GetResult(this Task task) => task.GetAwaiter().GetResult();

        public static T _GetResult<T>(this ValueTask<T> task) => task.GetAwaiter().GetResult();

        public static void _GetResult(this ValueTask task) => task.GetAwaiter().GetResult();


        public static T _TryGetResult<T>(this Task<T> task, bool noDebugMessage = false)
        {
            if (task == null) return default;
            try
            {
                return _GetResult(task);
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

        public static void _TryGetResult(this Task task, bool noDebugMessage = false)
        {
            if (task == null) return;
            try
            {
                _GetResult(task);
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

        public static T _TryGetResult<T>(this ValueTask<T> task, bool noDebugMessage = false)
        {
            if (task == default) return default;
            try
            {
                return _GetResult(task);
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

        public static void _TryGetResult(this ValueTask task, bool noDebugMessage = false)
        {
            if (task == default) return;
            try
            {
                _GetResult(task);
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

        public static bool _IsAsync(this SafeFileHandle handle)
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

        public static void _SetAsync(this SafeFileHandle handle, bool isAsync)
        {
            PInfo_SafeFileHandle_IsAsyncSet.SetValue(handle, isAsync);
        }

        public static string _ToPointerHexString(this IntPtr ptr) => $"0x{ptr.ToInt64():X}";

        public static ConcurrentRandomAccess<T> GetConcurrentRandomAccess<T>(this IRandomAccess<T> randomAccess) => new ConcurrentRandomAccess<T>(randomAccess);

        public static string _ToIPv4v6String(this AddressFamily family) => _ToIPv4v6String(family);
        public static string _ToIPv4v6String(this AddressFamily? family)
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

        public static object _PrivateGet(this object obj, string name) => FieldReaderWriter.GetCachedPrivate(obj.GetType()).GetValue(obj, name);
        public static object _PrivateGet<T>(this object obj, string name) => FieldReaderWriter.GetCachedPrivate(typeof(T)).GetValue(obj, name);

        public static void _PrivateSet(this object obj, string name, object value) => FieldReaderWriter.GetCachedPrivate(obj.GetType()).SetValue(obj, name, value);
        public static void _PrivateSet<T>(this object obj, string name, object value) => FieldReaderWriter.GetCachedPrivate(typeof(T)).SetValue(obj, name, value);

        public static object _PrivateInvoke(this object obj, string name, params object[] parameters) => FieldReaderWriter.GetCachedPrivate(obj.GetType()).Invoke(obj, name, parameters);

        public static bool _IsSubClassOfOrSame(this Type deriverClass, Type baseClass) => deriverClass == baseClass || deriverClass.IsSubclassOf(baseClass);

        public static bool _IsSocketErrorDisconnected(this SocketException e) => PalSocket.IsSocketErrorDisconnected(e);

        public static async Task<bool> ReceiveBool8Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(1, cancel))._GetBool8();

        public static async Task<byte> ReceiveUInt8Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(1, cancel))._GetUInt8();

        public static async Task<byte> ReceiveByteAsync(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(1, cancel)).Span[0];

        public static async Task<ushort> ReceiveUInt16Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(2, cancel))._GetUInt16();

        public static async Task<uint> ReceiveUInt32Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(4, cancel))._GetUInt32();

        public static async Task<ulong> ReceiveUInt64Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(8, cancel))._GetUInt64();

        public static async Task<sbyte> ReceiveSInt8Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(1, cancel))._GetSInt8();

        public static async Task<short> ReceiveSInt16Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(2, cancel))._GetSInt16();

        public static async Task<int> ReceiveSInt32Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(4, cancel))._GetSInt32();

        public static async Task<long> ReceiveSInt64Async(this Stream stream, CancellationToken cancel = default)
            => (await stream._ReadAllAsync(8, cancel))._GetSInt64();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int _DefaultSize(this int target, int defaultValue) => target != DefaultSize ? target : defaultValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int _Compute32bitMagicHashFast(this int src) => Util.Compute32bitMagicHashFast(src);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int _Compute32bitMagicHashFast(this uint src) => Util.Compute32bitMagicHashFast((int)src);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int _Compute32bitMagicHashFast<T>(this ref T data, int size = DefaultSize) where T : unmanaged
            => Util.Compute32bitMagicHashFast(ref data, size);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int _Compute32bitMagicHashFast<T>(ReadOnlySpan<T> span) where T : unmanaged
            => Util.Compute32bitMagicHashFast(span);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int _Compute32bitMagicHashFast<T>(this T[] array, int start, int length) where T : unmanaged
            => Util.Compute32bitMagicHashFast(array._AsReadOnlySpan(start, length));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int _Compute32bitMagicHashFast<T>(this T[] array) where T : unmanaged
            => Util.Compute32bitMagicHashFast(array._AsReadOnlySpan());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int _Compute32bitMagicHashFast<T>(Span<T> span) where T : unmanaged
            => Util.Compute32bitMagicHashFast(span._AsReadOnlySpan());

        public static unsafe uint _Get_IPv4_UInt32_BigEndian(this IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("address.AddressFamily != AddressFamily.InterNetwork");

            Span<byte> data = address.GetAddressBytes();
            Debug.Assert(data.Length == 4);

            fixed (byte* ptr = data)
            {
                return *((uint*)ptr);
            }
        }

        public static bool _IsCancelException(this Exception ex)
        {
            return (ex is OperationCanceledException || ex is TaskCanceledException);
        }

        public static IPVersion _GetIPVersion(this IPAddress addr)
            => _GetIPVersion(addr.AddressFamily);

        public static IPVersion _GetIPVersion(this AddressFamily af)
        {
            if (af == AddressFamily.InterNetwork)
                return IPVersion.IPv4;
            else if (af == AddressFamily.InterNetworkV6)
                return IPVersion.IPv6;

            throw new ArgumentOutOfRangeException("Invalid AddressFamily");
        }

        public static string _Base64Encode(this byte[] data) => Str.Base64Encode(data);
        public static string _Base64Encode(this ReadOnlySpan<byte> data) => Str.Base64Encode(data.ToArray());
        public static string _Base64Encode(this ReadOnlyMemory<byte> data) => Str.Base64Encode(data.ToArray());
        public static string _Base64Encode(this Span<byte> data) => Str.Base64Encode(data.ToArray());
        public static string _Base64Encode(this Memory<byte> data) => Str.Base64Encode(data.ToArray());
        public static byte[] _Base64Decode(this string str) => Str.Base64Decode(str);
        public static string _Base64UrlEncode(this byte[] data) => Str.Base64UrlEncode(data);
        public static string _Base64UrlEncode(this ReadOnlySpan<byte> data) => Str.Base64UrlEncode(data.ToArray());
        public static string _Base64UrlEncode(this ReadOnlyMemory<byte> data) => Str.Base64UrlEncode(data.ToArray());
        public static string _Base64UrlEncode(this Span<byte> data) => Str.Base64UrlEncode(data.ToArray());
        public static string _Base64UrlEncode(this Memory<byte> data) => Str.Base64UrlEncode(data.ToArray());
        public static byte[] _Base64UrlDecode(this string str) => Str.Base64UrlDecode(str);
    }
}

