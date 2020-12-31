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
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Diagnostics;

namespace IPA.Cores.Helper.Basic
{
    public static class HashMarvinHelper
    {
        [MethodImpl(Inline)]
        public static int _HashMarvin(this ReadOnlySpan<byte> data)
            => Marvin.ComputeHash32(data);

        [MethodImpl(Inline)]
        public static int _HashMarvin(this Span<byte> data)
            => Marvin.ComputeHash32(data);

        [MethodImpl(Inline)]
        public static int _HashMarvin(this byte[] data, int offset, int size)
            => Marvin.ComputeHash32(data._AsReadOnlySpan(offset, size));

        [MethodImpl(Inline)]
        public static int _HashMarvin(this byte[] data, int offset)
            => Marvin.ComputeHash32(data._AsReadOnlySpan(offset));

        [MethodImpl(Inline)]
        public static int _HashMarvin(this byte[] data)
            => Marvin.ComputeHash32(data._AsReadOnlySpan());

        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this ref TStruct data) where TStruct : unmanaged
        {
            unsafe
            {
                void* ptr = Unsafe.AsPointer(ref data);
                Span<byte> span = new Span<byte>(ptr, sizeof(TStruct));
                return _HashMarvin(span);
            }
        }

        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this ReadOnlySpan<TStruct> data) where TStruct : unmanaged
        {
            ReadOnlySpan<byte> span = MemoryMarshal.Cast<TStruct, byte>(data);
            return _HashMarvin(span);
        }
        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this ReadOnlyMemory<TStruct> data) where TStruct : unmanaged
            => _HashMarvin(data.Span);

        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this Span<TStruct> data) where TStruct : unmanaged
        {
            Span<byte> span = MemoryMarshal.Cast<TStruct, byte>(data);
            return _HashMarvin(span);
        }
        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this Memory<TStruct> data) where TStruct : unmanaged
            => _HashMarvin(data.Span);

        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this TStruct[] data, int offset, int size) where TStruct : unmanaged
            => _HashMarvin(data._AsReadOnlySpan(offset, size));

        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this TStruct[] data, int offset) where TStruct : unmanaged
            => _HashMarvin(data._AsReadOnlySpan(offset));

        [MethodImpl(Inline)]
        public static int _HashMarvin<TStruct>(this TStruct[] data) where TStruct : unmanaged
            => _HashMarvin(data._AsReadOnlySpan());
    }

    public static class BasicHelper
    {
        public static ReadOnlySpan<byte> _UntilNullByte(this ReadOnlySpan<byte> src)
        {
            int r = src.IndexOf((byte)0);
            if (r == -1) return src;
            return src.Slice(0, r);
        }
        public static ReadOnlySpan<byte> _UntilNullByte(this Span<byte> src) => _UntilNullByte(src._AsReadOnlySpan());

        public static ReadOnlyMemory<byte> _UntilNullByte(this ReadOnlyMemory<byte> src)
        {
            int r = src.Span.IndexOf((byte)0);
            if (r == -1) return src;
            return src.Slice(0, r);
        }
        public static ReadOnlyMemory<byte> _UntilNullByte(this Memory<byte> src) => _UntilNullByte(src._AsReadOnlyMemory());

        public static byte[] _UntilNullByte(this byte[] src) => src.AsSpan()._UntilNullByte().ToArray();

        public static byte[] _GetBytes_UTF8(this string str, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(str));
        public static byte[] _GetBytes_UTF16LE(this string str, bool bom = false) => Util.CombineByteArray(bom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.UniEncoding.GetBytes(str));
        public static byte[] _GetBytes_ShiftJis(this string str) => Str.ShiftJisEncoding.GetBytes(str);
        public static byte[] _GetBytes_Ascii(this string str) => Str.AsciiEncoding.GetBytes(str);
        public static byte[] _GetBytes_Euc(this string str) => Str.EucJpEncoding.GetBytes(str);
        public static byte[] _GetBytes(this string str, bool appendBom = false) => Util.CombineByteArray(appendBom ? Str.GetBOM(Str.Utf8Encoding) : null, Str.Utf8Encoding.GetBytes(str));
        public static byte[] _GetBytes(this string str, Encoding encoding) => encoding.GetBytes(str);

        public static string _GetString_UTF8(this byte[] byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _, untilNullByte);
        public static string _GetString_UTF16LE(this byte[] byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.UniEncoding, out _, untilNullByte);
        public static string _GetString_ShiftJis(this byte[] byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _, untilNullByte);
        public static string _GetString_Ascii(this byte[] byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _, untilNullByte);
        public static string _GetString_Euc(this byte[] byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _, untilNullByte);
        public static string _GetString(this byte[] byteArray, Encoding defaultEncoding, bool untilNullByte = false) => Str.DecodeString(byteArray, defaultEncoding, out _, untilNullByte);
        public static string _GetString(this byte[] byteArray, bool untilNullByte = false) => Str.DecodeStringAutoDetect(byteArray, out _, untilNullByte);

        public static string _GetString_UTF8(this ReadOnlySpan<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _, untilNullByte);
        public static string _GetString_UTF16LE(this ReadOnlySpan<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.UniEncoding, out _, untilNullByte);
        public static string _GetString_ShiftJis(this ReadOnlySpan<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _, untilNullByte);
        public static string _GetString_Ascii(this ReadOnlySpan<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _, untilNullByte);
        public static string _GetString_Euc(this ReadOnlySpan<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _, untilNullByte);
        public static string _GetString(this ReadOnlySpan<byte> byteArray, Encoding defaultEncoding, bool untilNullByte = false) => Str.DecodeString(byteArray, defaultEncoding, out _, untilNullByte);
        public static string _GetString(this ReadOnlySpan<byte> byteArray, bool untilNullByte = false) => Str.DecodeStringAutoDetect(byteArray, out _, untilNullByte);

        public static string _GetString_UTF8(this Span<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.Utf8Encoding, out _, untilNullByte);
        public static string _GetString_UTF16LE(this Span<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.UniEncoding, out _, untilNullByte);
        public static string _GetString_ShiftJis(this Span<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.ShiftJisEncoding, out _, untilNullByte);
        public static string _GetString_Ascii(this Span<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.AsciiEncoding, out _, untilNullByte);
        public static string _GetString_Euc(this Span<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray, Str.EucJpEncoding, out _, untilNullByte);
        public static string _GetString(this Span<byte> byteArray, Encoding defaultEncoding, bool untilNullByte = false) => Str.DecodeString(byteArray, defaultEncoding, out _, untilNullByte);
        public static string _GetString(this Span<byte> byteArray, bool untilNullByte = false) => Str.DecodeStringAutoDetect(byteArray, out _, untilNullByte);


        public static string _GetString_UTF8(this ReadOnlyMemory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.Utf8Encoding, out _, untilNullByte);
        public static string _GetString_UTF16LE(this ReadOnlyMemory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.UniEncoding, out _, untilNullByte);
        public static string _GetString_ShiftJis(this ReadOnlyMemory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.ShiftJisEncoding, out _, untilNullByte);
        public static string _GetString_Ascii(this ReadOnlyMemory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.AsciiEncoding, out _, untilNullByte);
        public static string _GetString_Euc(this ReadOnlyMemory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.EucJpEncoding, out _, untilNullByte);
        public static string _GetString(this ReadOnlyMemory<byte> byteArray, Encoding default_encoding, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, default_encoding, out _, untilNullByte);
        public static string _GetString(this ReadOnlyMemory<byte> byteArray, bool untilNullByte = false) => Str.DecodeStringAutoDetect(byteArray.Span, out _, untilNullByte);

        public static string _GetString_UTF8(this Memory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.Utf8Encoding, out _, untilNullByte);
        public static string _GetString_UTF16LE(this Memory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.UniEncoding, out _, untilNullByte);
        public static string _GetString_ShiftJis(this Memory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.ShiftJisEncoding, out _, untilNullByte);
        public static string _GetString_Ascii(this Memory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.AsciiEncoding, out _, untilNullByte);
        public static string _GetString_Euc(this Memory<byte> byteArray, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, Str.EucJpEncoding, out _, untilNullByte);
        public static string _GetString(this Memory<byte> byteArray, Encoding defaultEncoding, bool untilNullByte = false) => Str.DecodeString(byteArray.Span, defaultEncoding, out _, untilNullByte);
        public static string _GetString(this Memory<byte> byteArray, bool untilNullByte = false) => Str.DecodeStringAutoDetect(byteArray.Span, out _, untilNullByte);

        public static string _GetHexString(this byte[] byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string _GetHexString(this Span<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string _GetHexString(this ReadOnlySpan<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray, padding);
        public static string _GetHexString(this Memory<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray.Span, padding);
        public static string _GetHexString(this ReadOnlyMemory<byte> byteArray, string padding = "") => Str.ByteToHex(byteArray.Span, padding);
        public static byte[] _GetHexBytes(this string? str) => Str.HexToByte(str);

        public static byte[] _GetHexOrString(this string? str, Encoding ?encoding = null)
        {
            if (str._IsNullOrZeroLen()) return new byte[0];

            string tag = "0x";

            string trimmed = str.Trim();

            if (trimmed.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(tag.Length)._GetHexBytes();
            }

            if (encoding == null) encoding = Str.Utf8Encoding;

            return encoding.GetBytes(str);
        }

        public static string _NormalizeHexString(this string? src, bool lowerCase = false, string padding = "") => Str.NormalizeHexString(src, lowerCase, padding);

        public static bool _ToBool(this bool b) => b;
        public static bool _ToBool(this sbyte i) => (i != 0);
        public static bool _ToBool(this byte i) => (i != 0);
        public static bool _ToBool(this short i) => (i != 0);
        public static bool _ToBool(this ushort i) => (i != 0);
        public static bool _ToBool(this int i) => (i != 0);
        public static bool _ToBool(this uint i) => (i != 0);
        public static bool _ToBool(this long i) => (i != 0);
        public static bool _ToBool(this ulong i) => (i != 0);

        public static int _BoolToInt(this bool b) => b ? 1 : 0;
        public static uint _BoolToUInt(this bool b) => (uint)(b ? 1 : 0);
        public static byte _BoolToByte(this bool b) => (byte)(b ? 1 : 0);

        public static double _NonNegative(this double i) => (double)(i >= 0.0 ? i : 0.0);
        public static float _NonNegative(this float i) => (float)(i >= 0 ? i : 0);

        public static double _Max(this double i, double target) => Math.Max(i, target);
        public static float _Max(this float i, float target) => Math.Max(i, target);

        public static double _Min(this double i, double target) => Math.Min(i, target);
        public static float _Min(this float i, float target) => Math.Min(i, target);

        public static sbyte _NonNegative(this sbyte i) => (sbyte)(i >= 0 ? i : 0);
        public static short _NonNegative(this short i) => (short)(i >= 0 ? i : 0);
        public static int _NonNegative(this int i) => (int)(i >= 0 ? i : 0);
        public static long _NonNegative(this long i) => (long)(i >= 0 ? i : 0);

        public static byte _NonNegative(this byte i) => (byte)(i >= 0 ? i : 0);
        public static ushort _NonNegative(this ushort i) => (ushort)(i >= 0 ? i : 0);
        public static uint _NonNegative(this uint i) => (uint)(i >= 0 ? i : 0);
        public static ulong _NonNegative(this ulong i) => (ulong)(i >= 0 ? i : 0);

        public static sbyte _Min(this sbyte i, sbyte target) => Math.Min(i, target);
        public static short _Min(this short i, short target) => Math.Min(i, target);
        public static int _Min(this int i, int target) => Math.Min(i, target);
        public static long _Min(this long i, long target) => Math.Min(i, target);

        public static byte _Min(this byte i, byte target) => Math.Min(i, target);
        public static ushort _Min(this ushort i, ushort target) => Math.Min(i, target);
        public static uint _Min(this uint i, uint target) => Math.Min(i, target);
        public static ulong _Min(this ulong i, ulong target) => Math.Min(i, target);

        public static sbyte _Max(this sbyte i, sbyte target) => Math.Max(i, target);
        public static short _Max(this short i, short target) => Math.Max(i, target);
        public static int _Max(this int i, int target) => Math.Max(i, target);
        public static long _Max(this long i, long target) => Math.Max(i, target);

        public static byte _Max(this byte i, byte target) => Math.Max(i, target);
        public static ushort _Max(this ushort i, ushort target) => Math.Max(i, target);
        public static uint _Max(this uint i, uint target) => Math.Max(i, target);
        public static ulong _Max(this ulong i, ulong target) => Math.Max(i, target);



        public static void _SetNonNegative(this ref double i) => i = (double)(i >= 0.0 ? i : 0.0);
        public static void _SetNonNegative(this ref float i) => i = (float)(i >= 0 ? i : 0);

        public static void _SetMax(this ref double i, double target) => i = Math.Max(i, target);
        public static void _SetMax(this ref float i, float target) => i = Math.Max(i, target);

        public static void _SetMin(this ref double i, double target) => i = Math.Min(i, target);
        public static void _SetMin(this ref float i, float target) => i = Math.Min(i, target);

        public static void _SetNonNegative(this ref sbyte i) => i = (sbyte)(i >= 0 ? i : 0);
        public static void _SetNonNegative(this ref short i) => i = (short)(i >= 0 ? i : 0);
        public static void _SetNonNegative(this ref int i) => i = (int)(i >= 0 ? i : 0);
        public static void _SetNonNegative(this ref long i) => i = (long)(i >= 0 ? i : 0);

        public static void _SetNonNegative(this ref byte i) => i = (byte)(i >= 0 ? i : 0);
        public static void _SetNonNegative(this ref ushort i) => i = (ushort)(i >= 0 ? i : 0);
        public static void _SetNonNegative(this ref uint i) => i = (uint)(i >= 0 ? i : 0);
        public static void _SetNonNegative(this ref ulong i) => i = (ulong)(i >= 0 ? i : 0);

        public static void _SetMin(this ref sbyte i, sbyte target) => i = Math.Min(i, target);
        public static void _SetMin(this ref short i, short target) => i = Math.Min(i, target);
        public static void _SetMin(this ref int i, int target) => i = Math.Min(i, target);
        public static void _SetMin(this ref long i, long target) => i = Math.Min(i, target);

        public static void _SetMin(this ref byte i, byte target) => i = Math.Min(i, target);
        public static void _SetMin(this ref ushort i, ushort target) => i = Math.Min(i, target);
        public static void _SetMin(this ref uint i, uint target) => i = Math.Min(i, target);
        public static void _SetMin(this ref ulong i, ulong target) => i = Math.Min(i, target);

        public static void _SetMax(this ref sbyte i, sbyte target) => i = Math.Max(i, target);
        public static void _SetMax(this ref short i, short target) => i = Math.Max(i, target);
        public static void _SetMax(this ref int i, int target) => i = Math.Max(i, target);
        public static void _SetMax(this ref long i, long target) => i = Math.Max(i, target);

        public static void _SetMax(this ref byte i, byte target) => i = Math.Max(i, target);
        public static void _SetMax(this ref ushort i, ushort target) => i = Math.Max(i, target);
        public static void _SetMax(this ref uint i, uint target) => i = Math.Max(i, target);
        public static void _SetMax(this ref ulong i, ulong target) => i = Math.Max(i, target);




        public static bool _ToBool(this string? str, bool defaultValue = false) => Str.StrToBool(str, defaultValue);
        public static byte[] _ToByte(this string? str) => Str.StrToByte(str);
        public static DateTime _ToDate(this string? str, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToDate(str, toUtc, emptyToZeroDateTime);
        public static DateTime _ToTime(this string? s, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToTime(s, toUtc, emptyToZeroDateTime);
        public static DateTime _ToDateTime(this string? s, bool toUtc = false, bool emptyToZeroDateTime = false) => Str.StrToDateTime(s, toUtc, emptyToZeroDateTime);
        public static object _ToEnum(this string? s, object defaultValue, bool exactOnly = false) => Str.StrToEnum(s, defaultValue, exactOnly);
        public static int _ToInt(this string? s) => Str.StrToInt(s);
        public static long _ToLong(this string? s) => Str.StrToLong(s);
        public static uint _ToUInt(this string? s) => Str.StrToUInt(s);
        public static ulong _ToULong(this string? s) => Str.StrToULong(s);
        public static double _ToDouble(this string? s) => Str.StrToDouble(s);
        public static decimal _ToDecimal(this string? s) => Str.StrToDecimal(s);
        public static bool _IsSame(this string? s, string? t, bool ignoreCase = false) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (ignoreCase ? Str.StrCmpi(s, t) : Str.StrCmp(s, t))));
        public static bool _IsSame(this string? s, string? t, StringComparison comparison) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : string.Equals(s, t, comparison)));
        public static bool _IsSamei(this string? s, string? t) => _IsSame(s, t, true);
        public static bool _IsSameiIgnoreUnderscores(this string? s, string? t) => ((s == null && t == null) ? true : ((s == null || t == null) ? false : (s.Replace("_", "")._IsSamei(t.Replace("_", "")))));
        public static int _Cmp(this string? s, string? t, bool ignoreCase = false) => ((s == null && t == null) ? 0 : ((s == null ? 1 : t == null ? -1 : (ignoreCase ? Str.StrCmpiRetInt(s, t) : Str.StrCmpRetInt(s, t)))));
        public static int _Cmp(this string? s, string? t, StringComparison comparison) => ((s == null && t == null) ? 0 : ((s == null ? 1 : t == null ? -1 : string.Compare(s, t, comparison))));
        public static int _Cmpi(this string? s, string? t, bool ignoreCase = false) => _Cmp(s, t, true);

        public static bool _IsSameIPAddress(this string? ip1, string? ip2) => IPUtil.CompareIPAddress(ip1, ip2);
        public static int _CmpIPAddress(this string? ip1, string? ip2) => IPUtil.CompareIPAddressRetInt(ip1, ip2);


        public static int _CmpTrim(this string? s, string? t, StringComparison comparison = StringComparison.Ordinal)
        {
            s = s._NonNullTrim();
            t = t._NonNullTrim();

            return string.Compare(s, t, comparison);
        }
        public static int _CmpTrimi(this string? s, string? t) => _CmpTrim(s, t, StringComparison.OrdinalIgnoreCase);

        public static bool _IsSameTrim(this string? s, string? t, StringComparison comparison = StringComparison.Ordinal)
        {
            s = s._NonNullTrim();
            t = t._NonNullTrim();

            return string.Equals(s, t, comparison);
        }
        public static bool _IsSameTrimi(this string? s, string? t) => _IsSameTrim(s, t, StringComparison.OrdinalIgnoreCase);

        public static bool _IsAscii(this char c) => Str.IsAscii(c);
        public static bool _IsAscii(this string str) => Str.IsAscii(str);

        public static string[] _GetLines(this string s, bool removeEmpty = false, bool stripCommentsFromLine = false, IEnumerable<string>? commentStartStrList = null)
            => Str.GetLines(s, removeEmpty, stripCommentsFromLine, commentStartStrList);
        public static bool _GetKeyAndValue(this string s, out string key, out string value, string splitStr = Consts.Strings.DefaultKeyAndValueSplitStr) => Str.GetKeyAndValue(s, out key, out value, splitStr);
        public static void _SplitUrlAndQueryString(this string src, out string url, out string queryString) => Str.SplitUrlAndQueryString(src, out url, out queryString);
        public static bool _IsDouble(this string s) => Str.IsDouble(s);
        public static bool _IsLong(this string s) => Str.IsLong(s);
        public static bool _IsInt(this string s) => Str.IsInt(s);
        public static bool _IsNumber(this string s) => Str.IsNumber(s);
        public static bool _InStr(this string s, string keyword, bool ignoreCase = false) => Str.InStr(s, keyword, !ignoreCase);
        public static string[] _ParseCmdLine(this string s) => Str.ParseCmdLine(s);
        public static object? _Old_XmlToObjectPublic(this string s, Type t) => Str.XMLToObjectSimple_PublicLegacy(s, t);
        public static StrToken _ToToken(this string s, string splitStr = " ,\t\r\n") => new StrToken(s, splitStr);
        public static string _OneLine(this string? s, string splitter = " / ") => Str.OneLine(s, splitter);
        public static string _OneLine(this IEnumerable<string> s, string splitter = " / ") => Str.OneLine(s._Combine(Str.NewLine_Str_Local), splitter);
        public static string _GetFirstFilledLineFromLines(this string src) => Str.GetFirstFilledLineFromLines(src);
        public static string _GetFirstFilledLineFromLines(this IEnumerable<string> src) => Str.GetFirstFilledLineFromLines(src._Combine(Str.NewLine_Str_Local));
        public static string _FormatC(this string s) => Str.FormatC(s);
        public static string _FormatC(this string s, params object[] args) => Str.FormatC(s, args);
        public static void _Printf(this string s) => Str.Printf(s, new object[0]);
        public static void _Printf(this string s, params object[] args) => Str.Printf(s, args);
        public static string? _Print(this string? s) { Con.WriteLine(s); return s; }
        public static string? _Debug(this string? s) { Dbg.WriteLine(s); return s; }

        [return: NotNullIfNotNull("s")]
        public static string? _MaskPassword(this string? s, char maskedChar = '*')
        {
            if (s == null) return null;
            return maskedChar._MakeCharArray(s.Length);
        }

        [MethodImpl(NoInline | NoOptimization)]
        public static string? _ErrorFunc(this string? s, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
        {
            $"{Dbg.GetCurrentExecutingPositionInfoString(1, filename, line, caller, onlyClassName: false)}: {s._NonNull()}"._Error();
            return s;
        }
        [MethodImpl(NoInline | NoOptimization)]
        public static string? _ErrorClass(this string? s, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
        {
            $"{Dbg.GetCurrentExecutingPositionInfoString(1, filename, line, caller, onlyClassName: true)}: {s._NonNull()}"._Error();
            return s;
        }
        [MethodImpl(NoInline | NoOptimization)]
        public static string? _PrintFunc(this string? s, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
        {
            $"{Dbg.GetCurrentExecutingPositionInfoString(1, filename, line, caller, onlyClassName: false)}: {s._NonNull()}"._Print();
            return s;
        }
        [MethodImpl(NoInline | NoOptimization)]
        public static string? _PrintClass(this string? s, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
        {
            $"{Dbg.GetCurrentExecutingPositionInfoString(1, filename, line, caller, onlyClassName: true)}: {s._NonNull()}"._Print();
            return s;
        }
        [MethodImpl(NoInline | NoOptimization)]
        public static string? _DebugFunc(this string? s, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
        {
            $"{Dbg.GetCurrentExecutingPositionInfoString(1, filename, line, caller, onlyClassName: false)}: {s._NonNull()}"._Debug();
            return s;
        }
        [MethodImpl(NoInline | NoOptimization)]
        public static string? _DebugClass(this string? s, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
        {
            $"{Dbg.GetCurrentExecutingPositionInfoString(1, filename, line, caller, onlyClassName: true)}: {s._NonNull()}"._Debug();
            return s;
        }

        public static int _Search(this string s, string keyword, int start = 0, bool caseSenstive = false) => Str.SearchStr(s, keyword, start, caseSenstive);
        public static long _CalcKeywordMatchPoint(this string targetStr, string keyword, StringComparison comparison = StringComparison.OrdinalIgnoreCase) => Str.CalcKeywordMatchPoint(targetStr, keyword, comparison);
        public static string _TrimCrlf(this string? s) => Str.TrimCrlf(s);
        public static string _TrimStartWith(this string s, string key, bool caseSensitive = false) { Str.TrimStartWith(ref s, key, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string _TrimEndsWith(this string s, string key, bool caseSensitive = false) { Str.TrimEndsWith(ref s, key, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); return s; }
        public static string _NonNull(this string? s) { if (s == null) return ""; else return s; }
        public static string _NonNullTrim(this string? s) { if (s == null) return ""; else return s.Trim(); }
        public static string? _TrimIfNonNull(this string ?s) { if (s == null) return null; else return s.Trim(); }
        public static string _NonNullTrimSe(this string? s) { return Str.NormalizeStrSoftEther(s._NonNullTrim(), true); }
        public static string _TrimNonNull(this string? s) => s._NonNullTrim();
        public static bool _TryTrimStartWith(this string srcStr, out string outStr, StringComparison comparison, params string[] keys) => Str.TryTrimStartWith(srcStr, out outStr, comparison, keys);
        public static string _NoSpace(this string? s, string replaceWith = "_") => s._NonNull()._ReplaceStr(" ", replaceWith, false);
        public static string _NormalizeSoftEther(this string? s, bool trim = false) => Str.NormalizeStrSoftEther(s, trim);
        public static string[] _DivideStringByMultiKeywords(this string str, bool caseSensitive, params string[] keywords) => Str.DivideStringMulti(str, caseSensitive, keywords);
        public static bool _IsSuitableEncodingForString(this string s, Encoding encoding) => Str.IsSuitableEncodingForString(s, encoding);
        public static Encoding _GetBestSuitableEncoding(this string str, IEnumerable<Encoding?>? canditateList = null) => Str.GetBestSuitableEncoding(str, canditateList);
        public static bool _IsStringNumOrAlpha(this string s) => Str.IsStringNumOrAlpha(s);
        public static string _GetLeft(this string str, int len) => Str.GetLeft(str, len);
        public static string[] _SplitStringForSearch(this string str) => Str.SplitStringForSearch(str);
        public static void _WriteTextFile(this string s, string filename, Encoding? encoding = null, bool writeBom = false) { if (encoding == null) encoding = Str.Utf8Encoding; Str.WriteTextFile(filename, s, encoding, writeBom); }
        public static bool _StartsWithMulti(this string str, StringComparison comparison, params string[] keys) => Str.StartsWithMulti(str, comparison, keys);
        public static int _FindStringsMulti(this string str, int findStartIndex, StringComparison comparison, out int foundKeyIndex, params string[] keys) => Str.FindStrings(str, findStartIndex, comparison, out foundKeyIndex, keys);
        public static int _FindStringsMulti2(this string str, int findStartIndex, StringComparison comparison, out string foundString, params string[] keys) => Str.FindStrings(str, findStartIndex, comparison, out foundString, keys);
        public static int _GetCountSearchKeywordInStr(this string str, string keyword, bool caseSensitive = false) => Str.GetCountSearchKeywordInStr(str, keyword, caseSensitive);
        public static int[] _FindStringIndexes(this string str, string keyword, bool caseSensitive = false) => Str.FindStringIndexes(str, keyword, caseSensitive);
        public static string _StripCommentFromLine(this string str, IEnumerable<string>? commentStartStrList = null) => Str.StripCommentFromLine(str, commentStartStrList);
        public static string _RemoveSpace(this string str) { Str.RemoveSpace(ref str); return str; }
        public static string _Normalize(this string? str, bool space = true, bool toHankaku = true, bool toZenkaku = false, bool toZenkakuKana = true) { Str.NormalizeString(ref str, space, toHankaku, toZenkaku, toZenkakuKana); return str; }
        public static string _EncodeUrl(this string? str, Encoding? e = null) => Str.EncodeUrl(str, e);
        public static string _DecodeUrl(this string? str, Encoding? e = null) => Str.DecodeUrl(str, e);
        public static byte[] _DecodeUrlToBytes(this string? str) => Str.DecodeUrlToBytes(str);
        public static string _EncodeUrlPath(this string? str, Encoding? e = null) => Str.EncodeUrlPath(str, e);
        public static string _DecodeUrlPath(this string? str, Encoding? e = null) => Str.DecodeUrlPath(str, e);
        public static string _EncodeHtml(this string? str, bool forceAllSpaceToTag = false, bool spaceIfEmpty = false) => Str.EncodeHtml(str, forceAllSpaceToTag, spaceIfEmpty);
        public static string _DecodeHtml(this string? str) => Str.DecodeHtml(str);

        public static string _EncodeEasy(this string? str) => Str.EncodeEasy(str);
        public static string _DecodeEasy(this string? str) => Str.DecodeEasy(str);

        //public static bool _IsSafeAndPrintable(this string str, bool crlfIsOk = true, bool html_tag_ng = false) => Str.IsSafeAndPrintable(str, crlfIsOk, html_tag_ng);
        public static string _EncodeCEscape(this string s) => Str.EncodeCEscape(s);
        public static string _DecodeCEscape(this string s) => Str.DecodeCEscape(s);
        public static int _GetWidth(this string? s) => Str.GetStrWidth(s);
        public static bool _IsAllUpperStr(this string? s) => Str.IsAllUpperStr(s);
        public static string _ReplaceStr(this string str, string oldKeyword, string newKeyword, bool caseSensitive = false) => Str.ReplaceStr(str, oldKeyword, newKeyword, caseSensitive);
        public static bool _CheckStrLen(this string? str, int maxLen) => Str.CheckStrLen(str, maxLen);

        public static void _CheckStrLenException(this string? str, int maxLen, Exception? ex = null)
        {
            if (str._CheckStrLen(maxLen) == false) throw new ArgumentOutOfRangeException(nameof(str), $"The string length is too long. Allowed maximum length is {maxLen}.");
        }

        public static bool _CheckStrSize(this string? str, int maxSize, Encoding encoding) => Str.CheckStrSize(str, maxSize, encoding);
        public static void _CheckStrSizeException(this string? str, int maxSize, Encoding encoding, Exception? ex = null)
        {
            if (str._CheckStrSize(maxSize, encoding) == false) throw new ArgumentOutOfRangeException(nameof(str), $"The string size is too big. Allowed maximum size is {maxSize}.");
        }

        public static bool _CheckMailAddress(this string? str) => Str.CheckMailAddress(str);
        public static bool _IsSafeAsFileName(this string str, bool pathCharsAreNotGood = false) => Str.IsSafe(str, pathCharsAreNotGood);
        public static string _MakeSafePath(this string str, PathParser? pathParser = null) => Str.MakeSafePathName(str, pathParser);
        public static string _MakeSafeFileName(this string str) => Str.MakeSafeFileName(str);
        public static string _TruncStr(this string? str, int len) => Str.TruncStr(str, len);
        public static string _TruncStrEx(this string? str, int len, string? appendCode = "...") => Str.TruncStrEx(str, len, appendCode);
        public static string _TruncStrMiddle(this string? str, int maxLen, string appendCode = "..") => Str.TruncStrMiddle(str, maxLen, appendCode);
        public static string? _NullIfEmpty(this string? str) => Str.IsFilledStr(str) ? str : null;
        public static string? _NullIfZeroLen(this string? str) => str == null ? null : (str.Length == 0 ? null : str);

        [return: MaybeNull]
        public static T _NullIfEmpty<T>(this T obj) => Util.IsFilled(obj) ? obj : default;

        public static byte[] _HashSHA256(this string? str) => Str.HashStrSHA256(str);
        public static string _CombinePath(this string str, string p1) => Path.Combine(str, p1);
        public static string _CombinePath(this string str, string p1, string p2) => Path.Combine(str, p1, p2);
        public static string _CombinePath(this string str, string p1, string p2, string p3) => Path.Combine(str, p1, p2, p3);
        //public static string _NormalizePath(this string str) => BasicFile.NormalizePath(str);
        //public static string _InnerFilePath(this string str) => BasicFile.InnerFilePath(str);
        //public static string _RemoteLastEnMark(this string str) => BasicFile.RemoteLastEnMark(str);
        public static string? _GetDirectoryName(this string? str) => Path.GetDirectoryName(str);
        public static string? _GetFileName(this string? str) => Path.GetFileName(str);
        public static bool _IsExtensionMatch(this string str, string extensionsList) => IPA.Cores.Basic.Legacy.IO.IsExtensionsMatch(str, extensionsList);
        public static bool _IsExtensionMatch(this string str, IEnumerable<string> extensionsList) => IPA.Cores.Basic.Legacy.IO.IsExtensionsMatch(str, extensionsList);
        public static string _ReplaceStrWithReplaceClass(this string str, object replaceClass, bool caseSensitive = false) => Str.ReplaceStrWithReplaceClass(str, replaceClass, caseSensitive);

        public static byte[] _NonNull(this byte[] b) { if (b == null) return new byte[0]; else return b; }

        public static string _LinesToStr(this IEnumerable<string> lines, string? newLineStr = null) => Str.LinesToStr(lines, newLineStr);
        public static string[] _UniqueToken(this IEnumerable<string> t) => Str.UniqueToken(t);
        public static List<string> _ToStrList(this IEnumerable<string> t, bool removeEmpty = false, bool distinct = false, bool distinctCaseSensitive = false)
            => Str.StrArrayToList(t, removeEmpty, distinct, distinctCaseSensitive);
        public static string _Combine(this IEnumerable<string?> t, string? sepstr = "", bool removeEmpty = false, int maxItems = int.MaxValue, string? ommitStr = "...") => Str.CombineStringArray(t, sepstr, removeEmpty, maxItems, ommitStr);
        public static string _Combine(this IEnumerable<string?> t, char sepChar, bool removeEmpty = false, int maxItems = int.MaxValue, string? ommitStr = "...") => Str.CombineStringArray(t, sepChar.ToString(), removeEmpty, maxItems, ommitStr);

        public static string _MakeAsciiOneLinePrintableStr(this string? src, char alternativeChar = ' ') => Str.MakeAsciiOneLinePrintableStr(src, alternativeChar);

        public static string _MakeCharArray(this char c, int len) => Str.MakeCharArray(c, len);
        public static string _MakeStrArray(this string str, int count, string sepstr = "") => Str.MakeStrArray(str, count, sepstr);
        public static bool _IsZenkaku(this char c) => Str.IsZenkaku(c);
        public static bool _IsCharNumOrAlpha(this char c) => Str.IsCharNumOrAlpha(c);
        //public static bool _IsSafeAndPrintable(this char c, bool crlIsOk = true, bool html_tag_ng = false) => Str.IsSafeAndPrintable(c, crlIsOk, html_tag_ng);

        [return: NotNullIfNotNull("str")]
        public static string _NormalizeCrlf(this string str, CrlfStyle style = CrlfStyle.LocalPlatform, bool ensureLastLineCrlf = false) => Str.NormalizeCrlf(str, style, ensureLastLineCrlf);

        [return: NotNullIfNotNull("str")]
        public static string _NormalizeCrlf(this string str, bool ensureLastLineCrlf) => Str.NormalizeCrlf(str, CrlfStyle.LocalPlatform, ensureLastLineCrlf);

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

        public static bool _MemEquals(this byte[] a, byte[] b) => Util.MemEquals(a, b);
        public static bool _MemEquals(this ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b) => Util.MemEquals(a, b);
        public static bool _MemEquals(this Memory<byte> a, ReadOnlyMemory<byte> b) => Util.MemEquals(a, b);
        public static bool _MemEquals(this ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => Util.MemEquals(a, b);
        public static bool _MemEquals(this Span<byte> a, ReadOnlySpan<byte> b) => Util.MemEquals(a, b);

        public static bool _MemEquals<T>(this T[] a, T[] b) where T : IEquatable<T> => Util.MemEquals(a, b);
        public static bool _MemEquals<T>(this ReadOnlyMemory<T> a, ReadOnlyMemory<T> b) where T : IEquatable<T> => Util.MemEquals(a, b);
        public static bool _MemEquals<T>(this Memory<T> a, ReadOnlyMemory<T> b) where T : IEquatable<T> => Util.MemEquals(a, b);
        public static bool _MemEquals<T>(this ReadOnlySpan<T> a, ReadOnlySpan<T> b) where T : IEquatable<T> => Util.MemEquals(a, b);
        public static bool _MemEquals<T>(this Span<T> a, ReadOnlySpan<T> b) where T : IEquatable<T> => Util.MemEquals(a, b);

        public static int _MemCompare(this byte[] a, byte[] b) => Util.MemCompare(a, b);
        public static int _MemCompare(this ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b) => Util.MemCompare(a, b);
        public static int _MemCompare(this Memory<byte> a, ReadOnlyMemory<byte> b) => Util.MemCompare(a, b);
        public static int _MemCompare(this ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => Util.MemCompare(a, b);
        public static int _MemCompare(this Span<byte> a, ReadOnlySpan<byte> b) => Util.MemCompare(a, b);

        public static int _MemCompare<T>(this T[] a, T[] b) where T : IComparable<T> => Util.MemCompare(a, b);
        public static int _MemCompare<T>(this ReadOnlyMemory<T> a, ReadOnlyMemory<T> b) where T : IComparable<T> => Util.MemCompare(a, b);
        public static int _MemCompare<T>(this Memory<T> a, ReadOnlyMemory<T> b) where T : IComparable<T> => Util.MemCompare(a, b);
        public static int _MemCompare<T>(this ReadOnlySpan<T> a, ReadOnlySpan<T> b) where T : IComparable<T> => Util.MemCompare(a, b);
        public static int _MemCompare<T>(this Span<T> a, ReadOnlySpan<T> b) where T : IComparable<T> => Util.MemCompare(a, b);

        public static bool _StructBitEquals<T>(this ref T t1, in T t2) where T : unmanaged => Util.StructBitEquals(in t1, in t2);
        public static int _StructBitCompare<T>(this ref T t1, in T t2) where T : unmanaged => Util.StructBitCompare(in t1, in t2);


        //public static void _SaveToFile(this byte[] data, string filename, int offset = 0, int size = 0, bool doNothingIfSameContents = false)
        //    => BasicFile.SaveFile(filename, data, offset, (size == 0 ? data.Length - offset : size), doNothingIfSameContents);

        public static void _DebugObject(this object? o) => Dbg.DebugObject(o);
        public static void _PrintObject(this object? o) => Dbg.PrintObject(o);
        public static string _GetObjectDump(this object? o, string? instanceBaseName = "", string? separatorString = ", ", bool hideEmpty = true, bool jsonIfPossible = false, Type? type = null, string stringClosure = "\"")
            => Dbg.GetObjectDump(o, instanceBaseName, separatorString, hideEmpty, jsonIfPossible, type, stringClosure);
        public static string _GetObjectDumpForJsonFriendly(this object? o, string? instanceBaseName = "", Type? type = null)
            => _GetObjectDump(o, instanceBaseName, type: type, stringClosure: "'");
        public static string _Old_ObjectToXmlPublic(this object o, Type? t = null) => Str.ObjectToXMLSimple_PublicLegacy(o, t ?? o.GetType());

        [return: NotNullIfNotNull("o")]
        public static T? _CloneDeep<T>(this T? o) where T : class => (T?)Util.CloneObject_UsingBinary(o);

        [return: NotNullIfNotNull("o")]
        public static T? _CloneDeepWithNormalize<T>(this T? o) where T : class, INormalizable
        {
            T? obj = (T?)Util.CloneObject_UsingBinary(o);
            if (obj != null) obj.Normalize();
            return obj;
        }
        public static byte[] _ObjectToBinary(this object o) => Util.ObjectToBinary(o);
        public static object _BinaryToObject(this ReadOnlySpan<byte> b) => Util.BinaryToObject(b);
        public static object _BinaryToObject(this Span<byte> b) => Util.BinaryToObject(b);
        public static object _BinaryToObject(this byte[] b) => Util.BinaryToObject(b);

        public static void _ObjectToXml(this object obj, MemoryBuffer<byte> dst, DataContractSerializerSettings? settings = null) => Util.ObjectToXml(obj, dst, settings);
        public static byte[] _ObjectToXml(this object obj, DataContractSerializerSettings? settings = null) => Util.ObjectToXml(obj, settings);
        public static object? _XmlToObject(this MemoryBuffer<byte> src, Type type, DataContractSerializerSettings? settings = null) => Util.XmlToObject(src, type, settings);
        public static T? _XmlToObject<T>(this MemoryBuffer<byte> src, DataContractSerializerSettings? settings = null) => Util.XmlToObject<T>(src, settings);
        public static object? _XmlToObject(this byte[] src, Type type, DataContractSerializerSettings? settings = null) => Util.XmlToObject(src, type, settings);
        public static T? _XmlToObject<T>(this byte[] src, DataContractSerializerSettings? settings = null) => Util.XmlToObject<T>(src, settings);

        public static string _ObjectToXmlStr(this object obj, DataContractSerializerSettings? settings = null) => Str.ObjectToXmlStr(obj, settings);
        public static object? _XmlStrToObject(this string src, Type type, DataContractSerializerSettings? settings = null) => Str.XmlStrToObject(src, type, settings);
        public static T? _XmlStrToObject<T>(this string src, DataContractSerializerSettings? settings = null) => Str.XmlStrToObject<T>(src, settings);

        public static void _ObjectToRuntimeJson(this object obj, MemoryBuffer<byte> dst, DataContractJsonSerializerSettings? settings = null) => Util.ObjectToRuntimeJson(obj, dst, settings);
        public static byte[] _ObjectToRuntimeJson(this object obj, DataContractJsonSerializerSettings? settings = null) => Util.ObjectToRuntimeJson(obj, settings);
        public static object? _RuntimeJsonToObject(this MemoryBuffer<byte> src, Type type, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject(src, type, settings);
        public static T? _RuntimeJsonToObject<T>(this MemoryBuffer<byte> src, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject<T>(src, settings);
        public static object? _RuntimeJsonToObject(this byte[] src, Type type, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject(src, type, settings);
        public static T? _RuntimeJsonToObject<T>(this byte[] src, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject<T>(src, settings);

        public static string _ObjectToRuntimeJsonStr(this object obj, DataContractJsonSerializerSettings? settings = null) => Str.ObjectToRuntimeJsonStr(obj, settings);
        public static object? _RuntimeJsonStrToObject(this string src, Type type, DataContractJsonSerializerSettings? settings = null) => Str.RuntimeJsonStrToObject(src, type, settings);
        public static T? _RuntimeJsonStrToObject<T>(this string src, DataContractJsonSerializerSettings? settings = null) => Str.RuntimeJsonStrToObject<T>(src, settings);

        public static T _Print<T>(this T o) => (T)o._Print(typeof(T))!;

        public static object? _Print(this object? o, Type type)
        {
            if (o is Exception ex)
            {
                string? s = o.ToString();
                if (s == null) s = "null";
                Con.WriteLine(s);
            }
            else
            {
                Con.WriteLine(o, type);
            }

            return o;
        }
        public static object? _Debug(this object? o)
        {
            if (o is Exception ex) o = o.ToString();

            Dbg.WriteLine(o);
            return o;
        }
        public static object? _Error(this object? o)
        {
            if (o is Exception ex) o = o.ToString();

            Dbg.WriteError(o);
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
        public static string _ToLocalDtStr(this DateTimeOffset dt, bool withMSsecs = false, DtStrOption option = DtStrOption.All, bool withNanoSecs = false)
            => dt.LocalDateTime._ToDtStr(withMSsecs, option, withNanoSecs);

        public static string _ToFullDateTimeStr(this DateTime dt, bool toLocalTime = false, CoreLanguage lang = CoreLanguage.Japanese, FullDateTimeStrFlags flags = FullDateTimeStrFlags.None)
            => Str.DateTimeToStr(dt, toLocalTime, lang, flags);

        public static string _ToFullDateTimeStr(this DateTimeOffset dt, CoreLanguage lang = CoreLanguage.Japanese, FullDateTimeStrFlags flags = FullDateTimeStrFlags.None)
            => dt.LocalDateTime._ToFullDateTimeStr(false, lang, flags);

        public static string _ToTsStr(this TimeSpan timeSpan, bool withMSecs = false, bool withNanoSecs = false) => Str.TimeSpanToTsStr(timeSpan, withMSecs, withNanoSecs);

        public static bool _IsZeroDateTime(this DateTime dt) => Util.IsZero(dt);
        public static bool _IsZeroDateTime(this DateTimeOffset dt) => Util.IsZero(dt);

        public static DateTime _NormalizeDateTime(this DateTime dt) => Util.NormalizeDateTime(dt);
        public static DateTimeOffset _NormalizeDateTimeOffset(this DateTimeOffset dt) => Util.NormalizeDateTime(dt);

        public static byte[] _ReadToEnd(this Stream s, int maxSize = 0) => Util.ReadStreamToEnd(s, maxSize);
        public static async Task<byte[]> _ReadToEndAsync(this Stream s, int maxSize = 0, CancellationToken cancel = default(CancellationToken)) => await Util.ReadStreamToEndAsync(s, maxSize, cancel);

        public static long _SeekToBegin(this Stream s) => s.Seek(0, SeekOrigin.Begin);
        public static long _SeekToEnd(this Stream s) => s.Seek(0, SeekOrigin.End);

        public static void _TryCancelNoBlock(this CancellationTokenSource? cts) => TaskUtil.TryCancelNoBlock(cts);
        public static void _TryCancel(this CancellationTokenSource? cts) => TaskUtil.TryCancel(cts);
        public static async Task _CancelAsync(this CancellationTokenSource cts, bool throwOnFirstException = false) => await TaskUtil.CancelAsync(cts, throwOnFirstException);
        public static async Task _TryCancelAsync(this CancellationTokenSource? cts) => await TaskUtil.TryCancelAsync(cts);

        public static void _TryWait(this Task? t, bool noDebugMessage = false) => TaskUtil.TryWait(t, noDebugMessage);
        public static Task _TryWaitAsync(this Task? t, bool noDebugMessage = false) => TaskUtil.TryWaitAsync(t, noDebugMessage);
        public static Task<T> _TryWaitAsync<T>(this Task<T>? t, bool noDebugMessage = false) => TaskUtil.TryWaitAsync(t, noDebugMessage);
        public static Task _TryAwait(this Task? t, bool noDebugMessage = false) => _TryWaitAsync(t, noDebugMessage);
        public static Task<T> _TryAwait<T>(this Task<T>? t, bool noDebugMessage = false) => _TryWaitAsync(t, noDebugMessage);

        public static async Task<ResultOrExeption<T>> _TryAwaitAndRetBool<T>(this Task<T>? t, bool noDebugMessage = false)
        {
            if (t == null) return new ResultOrExeption<T>(new NullReferenceException());
            try
            {
                T ret = await t;

                return new ResultOrExeption<T>(ret);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                    Dbg.WriteLine("Task exception: " + ex._GetSingleException().ToString());

                return new ResultOrExeption<T>(ex);
            }
        }

        public static async Task<ResultOrExeption<None>> _TryAwaitAndRetBool(this Task? t, bool noDebugMessage = false)
        {
            if (t == null) return new ResultOrExeption<None>(new NullReferenceException());
            try
            {
                await t;

                return new ResultOrExeption<None>(new None());
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                    Dbg.WriteLine("Task exception: " + ex._GetSingleException().ToString());

                return new ResultOrExeption<None>(ex);
            }
        }

        public static T[] _ToArrayList<T>(this IEnumerable<T> i) => Util.IEnumerableToArrayList<T>(i);

        public static T? _GetFirstOrNull<T>(this List<T> list) where T : class => (list == null ? null : (list.Count == 0 ? null : list[0]));
        public static T? _GetFirstOrNull<T>(this T[] list) where T : class => (list == null ? null : (list.Length == 0 ? null : list[0]));

        public static T _GetFirstOrDefault<T>(this List<T> list) where T : struct => (list == null ? default(T) : (list.Count == 0 ? default(T) : list[0]));
        public static T _GetFirstOrDefault<T>(this T[] list) where T : struct => (list == null ? default(T) : (list.Length == 0 ? default(T) : list[0]));

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
            byte[] data = Util.ObjectToXml_PublicLegacy(o!);

            return (T)Util.XmlToObject_PublicLegacy(data, o!.GetType())!;
        }

        public static TValue _GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key)
            where TValue : new()
            where TKey : notnull
        {
            if (d.TryGetValue(key, out TValue value))
            {
                return value;
            }

            TValue n = new TValue();
            d.Add(key, n);
            return n;
        }

        public static TValue? _GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key, TValue? defaultValue = default)
            where TKey : notnull
        {
            if (d.TryGetValue(key, out TValue value))
            {
                return value;
            }

            TValue n = defaultValue;
            return n;
        }

        public static TValue _GetOrNew<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key, Func<TValue> newProc)
            where TKey : notnull
        {
            if (d.TryGetValue(key, out TValue value))
            {
                return value;
            }

            TValue n = newProc();
            d.Add(key, n);
            return n;
        }

        public static TValue _GetOrNew<TKey, TValue>(this SortedDictionary<TKey, TValue> d, TKey key)
            where TValue : new()
            where TKey : notnull
        {
            if (d.TryGetValue(key, out TValue value))
            {
                return value;
            }

            TValue n = new TValue();
            d.Add(key, n);
            return n;
        }

        public static TValue? _GetOrDefault<TKey, TValue>(this SortedDictionary<TKey, TValue> d, TKey key, TValue? defaultValue = default)
            where TKey : notnull
        {
            if (d.TryGetValue(key, out TValue value))
            {
                return value;
            }

            TValue n = defaultValue;
            return n;
        }

        public static TValue _GetOrNew<TKey, TValue>(this SortedDictionary<TKey, TValue> d, TKey key, Func<TValue> newProc)
            where TKey : notnull
        {
            if (d.TryGetValue(key, out TValue value))
            {
                return value;
            }

            TValue n = newProc();
            d.Add(key, n);
            return n;
        }

        public static Task<string> _GetHostnameFromIpAsync(this IPAddress? ip, CancellationToken cancel = default) => LocalNet.GetHostNameSingleOrIpAsync(ip, cancel);
        public static string _GetHostnameFromIp(this IPAddress? ip, CancellationToken cancel = default) => _GetHostnameFromIpAsync(ip, cancel)._GetResult();

        public static Task<string> _GetHostnameFromIpAsync(this string? ip, CancellationToken cancel = default) => LocalNet.GetHostNameSingleOrIpAsync(ip._ToIPAddress(noExceptionAndReturnNull: true), cancel);
        public static string _GetHostnameFromIp(this string? ip, CancellationToken cancel = default) => _GetHostnameFromIpAsync(ip, cancel)._GetResult();

        public static IPAddress? _ToIPAddress(this string? s, AllowedIPVersions allowed = AllowedIPVersions.All, bool noExceptionAndReturnNull = false) => IPUtil.StrToIP(s, allowed, noExceptionAndReturnNull);

        [return: NotNullIfNotNull("a")]
        public static IPAddress? _UnmapIPv4(this IPAddress? a) => IPUtil.UnmapIPv6AddressToIPv4Address(a);

        public static IPAddressType _GetIPAddressType(this IPAddress ip) => IPUtil.GetIPAddressType(ip);
        public static IPAddressType _GetIPAddressType(this string ip) => IPUtil.GetIPAddressType(ip);

        //        public static void _ParseUrl(this string urlString, out Uri uri, out NameValueCollection queryString) => Str.ParseUrl(urlString, out uri, out queryString);
        public static void _ParseUrl(this string urlString, out Uri uri, out QueryStringList queryString, Encoding? encoding = null) => Str.ParseUrl(urlString, out uri, out queryString, encoding);
        public static Uri _ParseUrl(this string urlString, out QueryStringList queryString, Encoding? encoding = null)
        {
            _ParseUrl(urlString, out Uri uri, out queryString, encoding);
            return uri;
        }
        public static Uri _ParseUrl(this string urlString, Encoding? encoding = null) => _ParseUrl(urlString, out _, encoding);
        public static bool _TryParseUrl(this string urlString, out Uri? uri, out QueryStringList queryString, Encoding? encoding = null) => Str.TryParseUrl(urlString, out uri, out queryString, encoding);
        public static QueryStringList _ParseQueryString(this string src, Encoding? encoding = null) => Str.ParseQueryString(src, encoding);

        public static Uri _CombineUrl(this string uri, string relativeUriOrAbsolutePath) => new Uri(uri._ParseUrl(), relativeUriOrAbsolutePath);
        public static Uri _CombineUrl(this string uri, Uri relativeUri) => new Uri(uri._ParseUrl(), relativeUri);
        public static Uri _CombineUrl(this Uri uri, string relativeUriOrAbsolutePath) => new Uri(uri, relativeUriOrAbsolutePath);
        public static Uri _CombineUrl(this Uri uri, Uri relativeUri) => new Uri(uri, relativeUri);

        public static string _TryGetContentType(this System.Net.Http.Headers.HttpContentHeaders h) => (h == null ? "" : h.ContentType == null ? "" : h.ContentType.ToString()._NonNull());
        public static string _TryGetContentType(this IPA.Cores.Basic.HttpClientCore.HttpContentHeaders h) => (h == null ? "" : h.ContentType == null ? "" : h.ContentType.ToString()._NonNull());

        public static void _DebugHeaders(this IPA.Cores.Basic.HttpClientCore.HttpHeaders h) => h._DoForEach(x => (x.Key + ": " + x.Value._Combine(", "))._Debug());

        public static string _GetStr(this NameValueCollection d, string key, string defaultStr = "")
        {
            try
            {
                if (d == null) return defaultStr;
                return d[key]._NonNull();
            }
            catch { return defaultStr; }
        }

        public static string _GetStr<T>(this IDictionary<string, T> d, string key, string defaultStr = "")
        {
            try
            {
                if (d == null) return defaultStr;
                if (d.ContainsKey(key) == false) return defaultStr;
                object? o = d[key];
                if (o == null) return defaultStr;
                if (o is string) return (string)o;
                return o.ToString()._NonNull();
            }
            catch { return defaultStr; }
        }

        public static string _GetStrFirst<T>(this IEnumerable<KeyValuePair<string, T>> d, string key, string defaultStr = "", StringComparison comparison = StringComparison.OrdinalIgnoreCase, bool autoTrim = true)
        {
            if (key._IsEmpty()) throw new ArgumentNullException(nameof(key));

            if (d == null) return defaultStr;

            T value = d.Where(x => x.Key._IsSameTrim(key, comparison)).FirstOrDefault().Value;

            if (value._IsNullOrDefault())
            {
                return defaultStr;
            }

            string? ret = value.ToString();

            if (ret._IsEmpty())
            {
                return defaultStr;
            }

            if (autoTrim)
            {
                ret = ret._NonNullTrim();
            }

            return ret!;
        }

        public static string _QStr<T>(this IEnumerable<KeyValuePair<string, T>> d, string key, string defaultStr = "", StringComparison comparison = StringComparison.OrdinalIgnoreCase, bool autoTrim = true)
            => _GetStrFirst(d, key, defaultStr, comparison, autoTrim);

        public static E _QEnum<T, E>(this IEnumerable<KeyValuePair<string, T>> d, string key, E defaultValue = default, StringComparison comparison = StringComparison.OrdinalIgnoreCase,
            bool exactOnly = false, bool noMatchError = false)
            where E : unmanaged, Enum
        {
            string strValue = _QStr(d, key, "", comparison, true);

            return strValue._ParseEnum<E>(defaultValue, exactOnly, noMatchError);
        }

        public static E _QEnumBits<T, E>(this IEnumerable<KeyValuePair<string, T>> d, string key, E defaultValue = default, StringComparison comparison = StringComparison.OrdinalIgnoreCase,
            params char[] separaters)
            where E : unmanaged, Enum
        {
            string strValue = _QStr(d, key, "", comparison, true);

            return strValue._ParseEnumBits<E>(defaultValue, separaters);
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

        public static async Task<Memory<byte>> _ReadAsync(this Stream stream, int bufferSize = Consts.Numbers.DefaultSmallBufferSize, CancellationToken cancel = default)
        {
            Memory<byte> tmp = new byte[bufferSize];

            int sz = await stream.ReadAsync(tmp, cancel);

            return tmp.Slice(0, sz);
        }
        public static Memory<byte> _Read(this Stream stream, int bufferSize = Consts.Numbers.DefaultSmallBufferSize, CancellationToken cancel = default)
            => _ReadAsync(stream, bufferSize, cancel)._GetResult();

        // 指定したサイズを超えないデータを切断されるまでに受信する
        // 必要に応じて受信中のデータをリアルタイムで指定されたコールバック関数に提供する
        public static async Task<Memory<byte>> _ReadWithMaxBufferSizeAsync(this Stream stream, int maxBufferSize, CancellationToken cancel = default,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task>? receivedDataMonitorAsync = null)
        {
            maxBufferSize._SetMax(1);

            MemoryBuffer<byte> buffer = new MemoryBuffer<byte>();

            Memory<byte> tmp = new byte[Consts.Numbers.DefaultSmallBufferSize];

            while (true)
            {
                int readSize = Math.Min(tmp.Length, maxBufferSize - buffer.Length);
                if (readSize <= 0)
                {
                    break;
                }

                int sz;

                try
                {
                    sz = await stream.ReadAsync(tmp, cancel);
                }
                catch (Exception ex)
                {
                    if (ex._IsCancelException())
                    {
                        break;
                    }
                    else
                    {
                        throw;
                    }
                }

                if (sz == 0)
                {
                    break;
                }

                var writeData = tmp.Slice(0, sz);
                if (receivedDataMonitorAsync != null)
                {
                    try
                    {
                        await receivedDataMonitorAsync(writeData, cancel);
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }
                buffer.Write(writeData);
            }

            if (receivedDataMonitorAsync != null)
            {
                // 最後まで読み終わった旨を通知するためにコールバックには空データを渡す
                try
                {
                    await receivedDataMonitorAsync(default, cancel);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }

            return buffer.Memory;
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

        public static async Task _WriteAsyncWithTimeout(this Stream stream, ReadOnlyMemory<byte> buffer, int? timeout = null, CancellationToken cancel = default, params CancellationToken[] cancelTokens)
        {
            if (timeout == null) timeout = stream.WriteTimeout;
            if (timeout <= 0) timeout = Timeout.Infinite;
            int targetWriteSize = buffer.Length;
            if (targetWriteSize == 0) return;

            try
            {
                await TaskUtil.DoAsyncWithTimeout(async (cancelLocal) =>
                {
                    await stream.WriteAsync(buffer, cancelLocal);
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

        public static void _DisposeSafe(this IAsyncService? obj, Exception? ex = null)
        {
            try
            {
                if (obj != null) obj.Dispose(ex);
            }
            catch { }
        }

        public static async Task _DisposeSafeAsync2(this IDisposable? obj)
        {
            try
            {
                if (obj != null)
                {
                    if (obj is IAsyncDisposable)
                    {
                        await ((IAsyncDisposable)obj)._DisposeSafeAsync();
                    }
                    else
                    {
                        obj._DisposeSafe();
                    }
                }
            }
            catch { }
        }

        public static async Task _DisposeSafeAsync(this IAsyncDisposable? obj)
        {
            try
            {
                if (obj != null) await obj.DisposeAsync();
            }
            catch { }
        }

        public static void _DisposeSafeSync(this IAsyncDisposable? obj)
        {
            try
            {
                if (obj != null) obj.DisposeAsync()._GetResult();
            }
            catch { }
        }

        public static void _DisposeSafe(this IDisposable? obj)
        {
            try
            {
                if (obj != null) obj.Dispose();
            }
            catch { }
        }

        public static void _CancelSafe(this IAsyncService? obj, Exception? ex = null)
        {
            try
            {
                if (obj != null) obj.Cancel(ex);
            }
            catch { }
        }

        public static Task _DisposeWithCleanupSafeAsync(this IAsyncService? obj, Exception? ex = null)
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

        public static Task _CleanupSafeAsync(this IAsyncService? obj, Exception? ex = null)
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
                                    AsyncCallback? callback,
                                    object? state)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            var tcs = new TaskCompletionSource<T>(state, CoresConfig.TaskAsyncSettings.AsyncEventTaskCreationOption);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception!.InnerExceptions);
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
                                            AsyncCallback? callback,
                                            object? state)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            var tcs = new TaskCompletionSource<int>(state, CoresConfig.TaskAsyncSettings.AsyncEventTaskCreationOption);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception!.InnerExceptions);
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
            if (task.IsFaulted) task.Exception!._ReThrow();
            if (task.IsCanceled) throw new TaskCanceledException();
        }


        [MethodImpl(Inline)]
        public static T If<T>(this T value, bool condition) where T : unmanaged, Enum
            => (condition ? value : default);

        [MethodImpl(Inline)]
        public static bool Bit<T>(this T value, T flag) where T : unmanaged, Enum
            => value.HasFlag(flag);

        [MethodImpl(Inline)]
        public static bool BitAny<T>(this T value, T flags) where T : unmanaged, Enum
        {
            ulong value1 = value._RawReadValueUInt64();
            ulong value2 = flags._RawReadValueUInt64();
            if (value2 == 0) return true;

            return ((value1 & value2) == 0) ? false : true;
        }

        [MethodImpl(Inline)]
        public static T BitRemove<T>(this T value, T removingBit) where T : unmanaged, Enum
        {
            ulong value1 = value._RawReadValueUInt64();
            ulong value2 = removingBit._RawReadValueUInt64();

            value1 &= ~value2;

            T ret = default;
            ret._RawWriteValueUInt64(value1);

            return ret;
        }

        public static T ParseAsDefault<T>(this T defaultValue, string str, bool exactOnly = false, bool noMatchError = false) where T : unmanaged, Enum
        {
            return Str.ParseEnum<T>(str, defaultValue, exactOnly, noMatchError);
        }

        public static T _ParseEnum<T>(this string? str, T defaultValue, bool exactOnly = false, bool noMatchError = false) where T : unmanaged, Enum
        {
            return Str.ParseEnum<T>(str, defaultValue, exactOnly, noMatchError);
        }

        public static T _ParseEnumBits<T>(this string? str, T defaultValue, params char[] separators) where T : unmanaged, Enum
        {
            if (separators == null || separators.Length == 0)
            {
                separators = Consts.Strings.DefaultEnumBitsSeparaters.ToArray();
            }

            return Str.StrToEnumBits<T>(str, defaultValue, separators);
        }

        [MethodImpl(Inline)]
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

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8, T v9) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8, v9);

        [MethodImpl(Inline)]
        public static bool EqualsAny<T>(this T value, T v1, T v2, T v3, T v4, T v5, T v6, T v7, T v8, T v9, T v10) where T : unmanaged, Enum
            => IsAnyOfThem(value, v1, v2, v3, v4, v5, v6, v7, v8, v9, v10);

        [MethodImpl(Inline)]
        public static T[] GetEnumValuesList<T>(this T sampleValue) where T : unmanaged, Enum
            => Util.GetEnumValuesList<T>();

        [MethodImpl(Inline)]
        public static T GetMaxEnumValue<T>(this T sampleValue) where T : unmanaged, Enum
            => Util.GetMaxEnumValue<T>();

        [MethodImpl(Inline)]
        public static int GetMaxEnumValueSInt32<T>(this T sampleValue) where T : unmanaged, Enum
            => Util.GetMaxEnumValueSInt32<T>();

        [return: NotNullIfNotNull("ex")]
        public static Exception? _GetSingleException(this Exception? ex)
        {
            if (ex == null) return null;

            var tex = ex as TargetInvocationException;
            if (tex != null)
                ex = tex.InnerException;

            var aex = ex as AggregateException;
            if (aex != null) ex = aex.Flatten().InnerExceptions[0];

            return ex;
        }

        public static void _ReThrow(this Exception? exception)
        {
            if (exception == null) throw exception!;
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

        public static bool _IsNullOrZeroLen([NotNullWhen(false)] this string? str) => string.IsNullOrEmpty(str);

        public static bool _IsNullOrZeroLen<T>([NotNullWhen(false)] this T[]? array) => !(array != null && array.Length != 0);

        public static bool _IsEmpty<T>([NotNullWhen(false)] this T data, bool zeroValueIsEmpty = false) => Util.IsEmpty(data, zeroValueIsEmpty);
        public static bool _IsFilled<T>([NotNullWhen(true)] this T data, bool zeroValueIsEmpty = false) => Util.IsFilled(data, zeroValueIsEmpty);

        public static bool _IsEmpty([NotNullWhen(false)] this string? str) => Str.IsEmptyStr(str);
        public static bool _IsFilled([NotNullWhen(true)] this string? str) => Str.IsFilledStr(str);

        public static string[] _Split(this string str, StringSplitOptions options, params string[] separators) => str.Split(separators, options);
        public static string[] _Split(this string str, StringSplitOptions options, params char[] separators) => str.Split(separators, options);

        [MethodImpl(Inline)]
        [return: NotNullIfNotNull("defaultValue")]
        public static string? _FilledOrDefault(this string? str, string? defaultValue = null) => (str._IsFilled() ? str : defaultValue);

        [MethodImpl(Inline)]
        [return: NotNullIfNotNull("defaultValue")]
        [return: MaybeNull]
        public static T _FilledOrDefault<T>(this T obj, T defaultValue = default, bool zeroValueIsEmpty = true) => (obj._IsFilled(zeroValueIsEmpty) ? obj : defaultValue);

        [MethodImpl(Inline)]
        public static int _ZeroOrDefault(this int i, int defaultValue, int min = 0, int max = int.MaxValue)
        {
            if (i == 0) i = defaultValue;
            i = Math.Max(i, min);
            i = Math.Min(i, max);
            return i;
        }

        [MethodImpl(Inline)]
        public static int _ZeroOrDefault(this int? i, int defaultValue, int min = 0, int max = int.MaxValue)
            => _ZeroOrDefault(i ?? 0, defaultValue, min, max);

        [MethodImpl(Inline)]
        public static long _ZeroOrDefault(this long i, long defaultValue, long min = 0, long max = long.MaxValue)
        {
            if (i == 0) i = defaultValue;
            i = Math.Max(i, min);
            i = Math.Min(i, max);
            return i;
        }

        [MethodImpl(Inline)]
        public static long _ZeroOrDefault(this long? i, long defaultValue, long min = 0, long max = long.MaxValue)
            => _ZeroOrDefault(i ?? 0, defaultValue, min, max);

        [MethodImpl(Inline)]
        [return: NotNull]
        public static T _FilledOrException<T>(this T obj, Exception? exception = null, bool zeroValueIsEmpty = true)
        {
            if (obj._IsFilled(zeroValueIsEmpty))
                return obj;

            throw exception ?? new CoresEmptyException();
        }

        [MethodImpl(Inline)]
        public static string _FilledOrException(this string? str, Exception? exception = null, bool zeroValueIsEmpty = true)
        {
            if (str._IsFilled(zeroValueIsEmpty))
                return str;

            throw exception ?? new CoresEmptyException();
        }

        [MethodImpl(Inline)]
        public static T _NullCheck<T>([NotNull] this T? obj, string? paramName = null, Exception? exception = null)
            where T : class
        {
            if (obj == null)
            {
                if (exception == null)
                {
                    if (paramName._IsEmpty() == false)
                    {
                        exception = new NullReferenceException(paramName);
                    }
                    else
                    {
                        exception = new NullReferenceException();
                    }
                }
                throw exception;
            }

            return obj;
        }

        [MethodImpl(Inline)]
        public static T _NotEmptyCheck<T>([NotNull] this T? obj, string? paramName = null, Exception? exception = null)
            where T : class
        {
            if (obj == null)
            {
                if (exception == null)
                {
                    if (paramName._IsEmpty() == false)
                    {
                        exception = new NullReferenceException(paramName);
                    }
                    else
                    {
                        exception = new NullReferenceException();
                    }
                }
                throw exception;
            }

            if (obj._IsEmpty())
            {
                if (exception == null)
                {
                    if (paramName._IsEmpty() == false)
                    {
                        exception = new CoresEmptyException(paramName);
                    }
                    else
                    {
                        exception = new CoresEmptyException();
                    }
                }
                throw exception;
            }

            return obj;
        }

        [MethodImpl(Inline)]
        public static string _NotEmptyCheck([NotNull] string obj, Exception? ex = null)
        {
            if (obj == null)
            {
                if (ex == null) ex = new NullReferenceException();
                throw ex;
            }

            if (obj._IsEmpty())
            {
                if (ex == null) ex = new CoresEmptyException();
                throw ex;
            }

            return obj;
        }

        [MethodImpl(Inline)]
        public static T _MarkNotNull<T>([NotNull] this T? obj)
            where T : class
        {
            Debug.Assert(obj != null);
            return obj!;
        }

        public static T _DbOverwriteValues<T>(this T baseData, T overwriteData) where T : new() => Util.DbOverwriteValues(baseData, overwriteData);
        public static void _DbEnforceNonNull(this object obj) => Util.DbEnforceNonNull(obj);
        public static T _DbEnforceNonNullSelf<T>(this T obj)
        {
            obj!._DbEnforceNonNull();
            return obj;
        }

        [return: MaybeNull]
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

        [MethodImpl(Inline)]
        public static DateTime _OverwriteKind(this DateTime dt, DateTimeKind kind) => new DateTime(dt.Ticks, kind);

        [MethodImpl(Inline)]
        public static DateTimeOffset _AsDateTimeOffset(this DateTime dt, bool isLocalTime) => new DateTimeOffset(dt._OverwriteKind(isLocalTime ? DateTimeKind.Local : DateTimeKind.Utc));

        public static async Task _LeakCheck(this Task t, bool noCheck = false, LeakCounterKind kind = LeakCounterKind.TaskLeak, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
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

        public static async Task<T> _LeakCheck<T>(this Task<T> t, bool noCheck = false, LeakCounterKind kind = LeakCounterKind.TaskLeak, [CallerFilePath] string filename = "", [CallerLineNumber] int line = 0, [CallerMemberName] string? caller = null)
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

        public static bool _IsAnonymousType<T>(this T instance) => _IsAnonymousType(instance!.GetType());

        public static void _PostData(this object obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info, CancellationToken cancel = default, bool noWait = false)
            => LocalLogRouter.PostData(obj, tag, copyToDebug, priority, cancel, noWait);

        public static Task _PostDataAsync(this object obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info, CancellationToken cancel = default, bool noWait = false)
            => LocalLogRouter.PostDataAsync(obj, tag, copyToDebug, priority, cancel, noWait);

        public static void _PostAccessLog(this object obj, string? tag = null, bool copyToDebug = false, LogPriority priority = LogPriority.Info)
            => LocalLogRouter.PostAccessLog(obj, tag, copyToDebug, priority);

        public static int _IndexOf<T>(this IEnumerable<T> list, Func<T, bool> predict)
            => _IndexOf(list, 0, predict);
        public static int _IndexOf<T>(this IEnumerable<T> list, int startIndex, Func<T, bool> predict)
        {
            int index = 0;
            foreach (var item in list)
            {
                if (index >= startIndex)
                {
                    if (predict(item))
                    {
                        return index;
                    }
                }
                index++;
            }
            return -1;
        }

        public static void _DoForEach<T>(this IEnumerable<T> list, Action<T> action)
        {
            list._NullCheck();
            list.ToList().ForEach(action);
        }

        public static void _DoForEach<T>(this IEnumerable<T> list, Action<T, int> action)
        {
            list._NullCheck();
            List<T> list2 = list.ToList();
            for (int i = 0; i < list2.Count; i++)
            {
                T t = list2[i];
                action(t, i);
            }
        }

        public static async Task _DoForEachAsync<T>(this IEnumerable<T> list, Func<T, Task> action, CancellationToken cancel = default)
        {
            list._NullCheck();
            List<T> list2 = list.ToList();
            for (int i = 0; i < list2.Count; i++)
            {
                T t = list2[i];
                await action(t);
            }
        }

        public static async Task _DoForEachAsync<T>(this IEnumerable<T> list, Func<T, int, Task> action, CancellationToken cancel = default)
        {
            list._NullCheck();
            List<T> list2 = list.ToList();
            for (int i = 0; i < list2.Count; i++)
            {
                T t = list2[i];
                await action(t, i);
            }
        }


        public static T _GetResult<T>(this Task<T> task) => task.GetAwaiter().GetResult();

        public static void _GetResult(this Task task) => task.GetAwaiter().GetResult();

        public static T _GetResult<T>(this ValueTask<T> task) => task.GetAwaiter().GetResult();

        public static void _GetResult(this ValueTask task) => task.GetAwaiter().GetResult();


        public static T _TryGetResult<T>(this Task<T>? task, bool noDebugMessage = false)
        {
            if (task == null) return default!;
            try
            {
                return _GetResult(task);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    Con.WriteDebug("TryGetResult error");
                    Con.WriteDebug(ex._GetSingleException());
                }

                return default!;
            }
        }

        public static void _TryGetResult(this Task? task, bool noDebugMessage = false)
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
                    Con.WriteDebug(ex._GetSingleException());
                }
            }
        }

        public static T _TryGetResult<T>(this ValueTask<T> task, bool noDebugMessage = false)
        {
            if (task == default) return default!;
            try
            {
                return _GetResult(task);
            }
            catch (Exception ex)
            {
                if (noDebugMessage == false)
                {
                    Con.WriteDebug("TryGetResult error");
                    Con.WriteDebug(ex._GetSingleException());
                }

                return default!;
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
                    Con.WriteDebug(ex._GetSingleException());
                }
            }
        }

        readonly static PropertyInfo PInfo_SafeFileHandle_IsAsyncGet = typeof(SafeFileHandle).GetProperty("IsAsync", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetProperty)!;
        readonly static PropertyInfo PInfo_SafeFileHandle_IsAsyncSet = typeof(SafeFileHandle).GetProperty("IsAsync", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty)!;

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

        public static FieldReaderWriter _GetFieldReaderWriter(this Type type, bool includePrivate = false) => includePrivate ? FieldReaderWriter.GetCachedPrivate(type) : FieldReaderWriter.GetCached(type);
        public static FieldReaderWriter _GetFieldReaderWriter<T>(this T typeSample, bool includePrivate = false) => includePrivate ? FieldReaderWriter.GetCachedPrivate<T>() : FieldReaderWriter.GetCached<T>();

        public static object? _PrivateGet(this object obj, string name) => FieldReaderWriter.GetCachedPrivate(obj.GetType()).GetValue(obj, name);
        public static object? _PrivateGet<T>(this object obj, string name) => FieldReaderWriter.GetCachedPrivate(typeof(T)).GetValue(obj, name);

        public static void _PrivateSet(this object obj, string name, object? value) => FieldReaderWriter.GetCachedPrivate(obj.GetType()).SetValue(obj, name, value);
        public static void _PrivateSet<T>(this object obj, string name, object? value) => FieldReaderWriter.GetCachedPrivate(typeof(T)).SetValue(obj, name, value);

        public static object? _PrivateInvoke(this object obj, string name, params object[] parameters) => FieldReaderWriter.GetCachedPrivate(obj.GetType()).Invoke(obj, name, parameters);

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

        [MethodImpl(Inline)]
        public static int _DefaultSize(this int target, int defaultValue) => target != DefaultSize ? target : defaultValue;

        [MethodImpl(Inline)]
        public static int _ComputeGoldenHash32(this int src) => Util.ComputeGoldenHash32(src);

        [MethodImpl(Inline)]
        public static uint _ComputeGoldenHash32(this uint src) => Util.ComputeGoldenHash32(src);

        [MethodImpl(Inline)]
        public static long _ComputeGoldenHash64(this long src) => Util.ComputeGoldenHash64(src);

        [MethodImpl(Inline)]
        public static ulong _ComputeGoldenHash64(this ulong src) => Util.ComputeGoldenHash64(src);

        [MethodImpl(Inline)]
        public static int _ComputeGoldenHash32(this int src, int bits) => Util.ComputeGoldenHash32(src, bits);

        [MethodImpl(Inline)]
        public static uint _ComputeGoldenHash32(this uint src, int bits) => Util.ComputeGoldenHash32(src, bits);

        [MethodImpl(Inline)]
        public static long _ComputeGoldenHash64(this long src, int bits) => Util.ComputeGoldenHash64(src, bits);

        [MethodImpl(Inline)]
        public static ulong _ComputeGoldenHash64(this ulong src, int bits) => Util.ComputeGoldenHash64(src, bits);

        [MethodImpl(Inline)]
        public static unsafe int _ComputeGoldenHash<T>(this ref T data, int size = DefaultSize) where T : unmanaged
            => Util.ComputeGoldenHash(in data, size);

        [MethodImpl(Inline)]
        public static unsafe int _ComputeGoldenHash<T>(ReadOnlySpan<T> span) where T : unmanaged
            => Util.ComputeGoldenHash(span);

        [MethodImpl(Inline)]
        public static unsafe int _ComputeGoldenHash<T>(this T[] array, int start, int length) where T : unmanaged
            => Util.ComputeGoldenHash(array._AsReadOnlySpan(start, length));

        [MethodImpl(Inline)]
        public static unsafe int _ComputeGoldenHash<T>(this T[] array) where T : unmanaged
            => Util.ComputeGoldenHash(array._AsReadOnlySpan());

        [MethodImpl(Inline)]
        public static unsafe int _ComputeGoldenHash<T>(Span<T> span) where T : unmanaged
            => Util.ComputeGoldenHash(span._AsReadOnlySpan());

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

        public static Task<T> FlushOtherStreamIfPending<T>(this Task<T> recvTask, PipeStream otherStream) => TaskUtil.FlushOtherStreamIfRecvPendingAsync(recvTask, otherStream);

        public static IReadOnlyList<string> _GetCertSHAHashStrList(this X509Certificate cert)
        {
            List<string> ret = new List<string>();

            ret.Add(cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA1));
            ret.Add(cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256));
            ret.Add(cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA512));

            return ret;
        }

        public static bool _IsStrIP(this string? str) => IPUtil.IsStrIP(str);
        public static bool _IsStrIPv4(this string? str) => IPUtil.IsStrIPv4(str);
        public static bool _IsStrIPv6(this string? str) => IPUtil.IsStrIPv6(str);

        public static IPAddress? _StrToIP(this string? str, AllowedIPVersions allowed = AllowedIPVersions.All, bool noException = false)
            => IPUtil.StrToIP(str, allowed, noException);

        public static int _DataToFile(this byte[] data, string path, FileSystem? fs = null, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => (fs ?? Lfs).WriteDataToFile(path, data, flags, doNotOverwrite, cancel);

        public static int _DataToFile(this Memory<byte> data, string path, FileSystem? fs = null, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => (fs ?? Lfs).WriteDataToFile(path, data, flags, doNotOverwrite, cancel);

        public static int _DataToFile(this ReadOnlyMemory<byte> data, string path, FileSystem? fs = null, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, CancellationToken cancel = default)
            => (fs ?? Lfs).WriteDataToFile(path, data, flags, doNotOverwrite, cancel);

        public static Memory<byte> _FileToData(this string path, FileSystem? fs = null, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
            => (fs ?? Lfs).ReadDataFromFile(path, maxSize, flags, cancel);

        public static bool _SetSingle<TKey, TValue>(this IList<KeyValuePair<TKey, TValue>> target, TKey key, TValue value, IEqualityComparer<TKey>? comparer = null)
        {
            if (comparer == null) comparer = EqualityComparer<TKey>.Default;

            List<KeyValuePair<TKey, TValue>> deleteTarget = new List<KeyValuePair<TKey, TValue>>();

            int numMatch = 0;

            for (int i = 0; i < target.Count; i++)
            {
                KeyValuePair<TKey, TValue> current = target[i];

                if (comparer.Equals(current.Key, key))
                {
                    if (numMatch == 0)
                    {
                        target[i] = new KeyValuePair<TKey, TValue>(key, value);
                    }
                    else
                    {
                        deleteTarget.Add(current);
                    }

                    numMatch++;
                }
            }

            deleteTarget.ForEach(x => target.Remove(x));

            if (numMatch == 0)
            {
                target.Add(new KeyValuePair<TKey, TValue>(key, value));
            }

            return numMatch >= 1;
        }

        public static bool _RemoveAll<TKey, TValue>(this IList<KeyValuePair<TKey, TValue>> target, TKey key, TValue value, IEqualityComparer<TKey>? comparer = null)
        {
            if (comparer == null) comparer = EqualityComparer<TKey>.Default;

            List<KeyValuePair<TKey, TValue>> deleteTarget = new List<KeyValuePair<TKey, TValue>>();

            int numMatch = 0;

            for (int i = 0; i < target.Count; i++)
            {
                KeyValuePair<TKey, TValue> current = target[i];

                if (comparer.Equals(current.Key, key))
                {
                    deleteTarget.Add(current);

                    numMatch++;
                }
            }

            deleteTarget.ForEach(x => target.Remove(x));

            return numMatch >= 1;
        }


        public static bool _HasKey<TKey, TValue>(this IList<KeyValuePair<TKey, TValue>> target, TKey key, IEqualityComparer<TKey>? comparer = null)
        {
            if (comparer == null) comparer = EqualityComparer<TKey>.Default;

            return target.Where(x => comparer.Equals(x.Key, key)).Any();
        }

        public static bool _TryGetFirstValue<TKey, TValue>(this IList<KeyValuePair<TKey, TValue>> target, TKey key, [NotNullWhen(true)] out TValue value, IEqualityComparer<TKey>? comparer = null)
        {
            if (comparer == null) comparer = EqualityComparer<TKey>.Default;

            var tmp = target.Where(x => comparer.Equals(x.Key, key));

            if (tmp.Any() == false)
            {
                value = default!;
                return false;
            }
            else
            {
                value = tmp.First().Value!;
                return true;
            }
        }

        public static TValue _GetFirstValueOrDefault<TKey, TValue>(this IList<KeyValuePair<TKey, TValue>> target, TKey key, IEqualityComparer<TKey>? comparer = null)
        {
            if (_TryGetFirstValue(target, key, out TValue ret, comparer))
            {
                return ret;
            }

            return default!;
        }

        public static bool _IsUtf8Encoding(this Encoding enc) => enc.WebName._IsSamei("utf-8");
        public static bool _IsShiftJisEncoding(this Encoding enc) => enc.WebName._IsSamei("shift_jis");

        public static SeekableStreamBasedRandomAccess _CreateSeekableRandomAccess(this Stream stream, bool autoDisposeBase = false)
            => new SeekableStreamBasedRandomAccess(stream, autoDisposeBase);

        public static Task<TResult> _TaskResult<TResult>(this TResult result) => Task.FromResult(result);

        public static IOrderedEnumerable<TSource> _Shuffle<TSource>(this IEnumerable<TSource> source)
        {
            return source.OrderBy(x => Util.RandSInt63());
        }

        // 重み付きシャッフル (getWeigth の戻り値で重みを指定する。2 以上を指定するべきである。)
        public static IOrderedEnumerable<TSource> _ShuffleWithWeight<TSource>(this IEnumerable<TSource> source, Func<TSource, int> getWeigth)
        {
            return source.OrderByDescending(x => Util.RandDouble0To1() * (double)(getWeigth(x)._Max(1)));
        }

        public static byte[] _StrToMac(this string src) => Str.StrToMac(src);
        public static string _NormalizeMac(this string src, string paddingStr = "-", bool lowerStr = false) => Str.NormalizeMac(src, paddingStr, lowerStr);
        public static string _NormalizeMac(this string src, MacAddressStyle style) => Str.NormalizeMac(src, style);
        public static string _NormalizeIp(this string src) => IPUtil.NormalizeIPAddress(src);

        public static string _GetIPv4PtrRecord(this IPAddress ip) => IPUtil.GetIPv4PtrRecord(ip);

        public static int _NormalizeTimeout(this int? argValue, int defaultValue = Timeout.Infinite)
        {
            if (argValue.HasValue)
            {
                int val = argValue.Value;
                if (val <= 0)
                    return Timeout.Infinite;
                else
                    return val;
            }
            else
            {
                if (defaultValue <= 0)
                    return Timeout.Infinite;
                else
                    return defaultValue;
            }
        }

        public static int _CompareHex(this string? hex1, string? hex2) => Str.CompareHex(hex1, hex2);
        public static bool _IsSameHex(this string? hex1, string? hex2) => Str.IsSameHex(hex1, hex2);

        public static string _ObjectArrayToCsv<T>(this IEnumerable<T> array, bool withHeader = false) where T : notnull
            => Str.ObjectArrayToCsv(array, withHeader);

        public static string _ObjectHeaderToCsv(this Type objType)
            => Str.ObjectHeaderToCsv(objType);

        public static string _ObjectHeaderToCsv<T>(this T objSample) where T : notnull
            => Str.ObjectHeaderToCsv<T>();

        public static string _ObjectDataToCsv<T>(this T obj) where T : notnull
            => Str.ObjectDataToCsv(obj);

        public static string _CombineStringArrayForCsv(this IEnumerable<string?>? strs, string prefix = "")
            => Str.CombineStringArrayForCsv(strs, prefix);

        [MethodImpl(Inline)]
        public static bool _IsNullOrDefault<T>([NotNullWhen(false)] this T data)
        {
            return EqualityComparer<T>.Default.Equals(data, default(T)!);
        }

        [MethodImpl(Inline)]
        public static bool _IsNullOrDefault2<T>([NotNullWhen(false)] this T data)
        {
            return (data == null || data.Equals(default(T)!));
        }

        public static Memory<byte> _Load(this string path, int maxSize = int.MaxValue, FileFlags flags = FileFlags.None, FileSystem? fs = null, CancellationToken cancel = default) =>
            (fs ?? Lfs).ReadDataFromFile(path, maxSize, flags, cancel);

        public static void _Save(this ReadOnlyMemory<byte> data, string path, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, FileSystem? fs = null, CancellationToken cancel = default) =>
            (fs ?? Lfs).WriteDataToFile(path, data, flags, doNotOverwrite, cancel);

        public static void _Save(this Memory<byte> data, string path, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, FileSystem? fs = null, CancellationToken cancel = default) =>
            _Save((ReadOnlyMemory<byte>)data, path, flags, doNotOverwrite, fs, cancel);

        public static void _Save(this byte[] data, string path, FileFlags flags = FileFlags.None, bool doNotOverwrite = false, FileSystem? fs = null, CancellationToken cancel = default) =>
            _Save((ReadOnlyMemory<byte>)data, path, flags, doNotOverwrite, fs, cancel);

        public static IEnumerable<WebMethods> _GetWebMethodListFromBits(this WebMethodBits bits)
        {
            List<WebMethods> ret = new List<WebMethods>();

            if (bits.Bit(WebMethodBits.GET)) ret.Add(WebMethods.GET);
            if (bits.Bit(WebMethodBits.DELETE)) ret.Add(WebMethods.DELETE);
            if (bits.Bit(WebMethodBits.POST)) ret.Add(WebMethods.POST);
            if (bits.Bit(WebMethodBits.PUT)) ret.Add(WebMethods.PUT);
            if (bits.Bit(WebMethodBits.HEAD)) ret.Add(WebMethods.HEAD);

            return ret;
        }

        public static bool _WildcardMatch(this string targetStr, string wildcard, bool ignoreCase = false) => Str.WildcardMatch(targetStr, wildcard, ignoreCase);

        static bool NoFixProcessObjectHandleLeak = false;

        public static void _FixProcessObjectHandleLeak(this Process proc)
        {
            if (NoFixProcessObjectHandleLeak) return;

            try
            {
                if ((bool)proc._PrivateGet("_haveProcessHandle")!)
                {
                    Microsoft.Win32.SafeHandles.SafeProcessHandle? processHandle = (Microsoft.Win32.SafeHandles.SafeProcessHandle?)proc._PrivateGet("_processHandle");
                    if (processHandle != null)
                    {
                        bool ownsHandle = (bool)processHandle._PrivateGet<SafeHandle>("_ownsHandle")!;
                        if (ownsHandle == false)
                        {
                            processHandle._PrivateSet<SafeHandle>("_ownsHandle", true);
                        }
                    }
                }
            }
            catch
            {
                // エラーが発生した場合は次回から実施しない
                NoFixProcessObjectHandleLeak = true;
            }
        }

        public static long _ToTime64(this DateTime dt) => Time.DateTimeToTime64(dt);
        public static long _ToTime64(this DateTimeOffset dt) => Time.DateTimeToTime64(dt.UtcDateTime);
        public static DateTime _Time64ToDateTime(this long value) => Time.Time64ToDateTime(value);
        public static DateTimeOffset _Time64ToDateTimeOffsetUtc(this long value) => Time.Time64ToDateTimeOffsetUtc(value);
        public static DateTimeOffset _Time64ToDateTimeOffsetLocal(this long value) => Time.Time64ToDateTimeOffsetLocal(value);

        public static Task<long> CopyBetweenStreamAsync(this Stream src, Stream dest, CopyFileParams? param = null, ProgressReporterBase? reporter = null,
            long estimatedSize = -1, CancellationToken cancel = default, Ref<uint>? srcZipCrc = null, long truncateSize = -1, bool flush = false, int readTimeout = Timeout.Infinite, int writeTimeout = Timeout.Infinite)
            => Util.CopyBetweenStreamAsync(src, dest, param, reporter, estimatedSize, cancel, srcZipCrc, truncateSize, flush, readTimeout, writeTimeout);

        public static RandomAccessBasedStream GetStream(this IRandomAccess<byte> target, bool disposeTarget = false)
            => new RandomAccessBasedStream(target, disposeTarget);

        [MethodImpl(Inline)]
        [return: NotNullIfNotNull("src")]
        public static string? _Slice(this string? src, int start, int length)
        {
            checked
            {
                if (src == null) return null;
                length = Math.Min(length, src.Length - start);
                if (length <= 0) return "";
                return src.Substring(start, length);
            }
        }

        [MethodImpl(Inline)]
        [return: NotNullIfNotNull("src")]
        public static string? _Slice(this string? src, int start)
        {
            if (src == null) return null;
            return src.Substring(start);
        }

        [MethodImpl(Inline)]
        [return: NotNullIfNotNull("src")]
        public static string? _SliceHead(this string? src, int length) => src._Slice(0, length);

        [MethodImpl(Inline)]
        [return: NotNullIfNotNull("src")]
        public static string? _SliceTail(this string? src, int length)
        {
            checked
            {
                if (src == null) return null;
                length = Math.Min(length, src.Length);
                if (length <= 0) return "";
                return src.Substring(src.Length - length);
            }
        }

        public static string _GetFileSizeStr(this long size) => Str.GetFileSizeStr(size);
        public static string _GetFileSizeStr(this int size) => Str.GetFileSizeStr(size);
        public static string _GetFileSizeStr(this ulong size) => Str.GetFileSizeStr((long)Math.Min(size, long.MaxValue));
        public static string _GetFileSizeStr(this uint size) => Str.GetFileSizeStr(size);

        [return: NotNullIfNotNull("memoryList")]
        public static List<string>? _ToStringList(this List<Memory<byte>>? memoryList, Encoding? encoding = null)
        {
            if (memoryList == null) return null;

            if (encoding == null) encoding = Str.Utf8Encoding;

            List<string> ret = new List<string>();

            memoryList._DoForEach(x => ret.Add(encoding.GetString(x.Span)));

            return ret;
        }

        public static string _GetIpStrForSort(this IPAddress ip)
        {
            return ip.AddressFamily.ToString() + ":" + ip.GetAddressBytes()._GetHexString();
        }

        public static string _AddSpacePadding(this string? srcStr, int totalWidth, bool addOnLeft = false, char spaceChar = ' ')
            => Str.AddSpacePadding(srcStr, totalWidth, addOnLeft, spaceChar);

        public static TimeSpan _ToTimeSpanMSecs(this double msecs) => TimeSpan.FromMilliseconds(msecs);
        public static TimeSpan _ToTimeSpanMSecs(this int msecs) => TimeSpan.FromMilliseconds(msecs);
        public static TimeSpan _ToTimeSpanMSecs(this uint msecs) => TimeSpan.FromMilliseconds(msecs);
        public static TimeSpan _ToTimeSpanMSecs(this long msecs) => TimeSpan.FromMilliseconds(msecs);
        public static TimeSpan _ToTimeSpanMSecs(this ulong msecs) => TimeSpan.FromMilliseconds(msecs);

        [MethodImpl(Inline)]
        public static long _ScopeIdSafe(this IPAddress? ip)
        {
            if (ip == null) return 0;
            if (ip.AddressFamily != AddressFamily.InterNetworkV6) return 0;
            return ip.ScopeId;
        }

        [MethodImpl(Inline)]
        public static int _IndexOfAfter<T>(this Span<T> span, ReadOnlySpan<T> value, int startOffset) where T : IEquatable<T>
        {
            checked
            {
                if (startOffset < 0) return -1;
                int spanSize = span.Length;
                if (spanSize <= 0) return -1;
                if (startOffset >= spanSize) return -1;
                var subSpan = span.Slice(startOffset);
                int r = subSpan.IndexOf(value);
                if (r < 0) return -1;
                return r + startOffset;
            }
        }

        public static string _TrimBothSideChar(this string? src, char c1, char c2) => Str.TrimBothSideChar(src, c1, c2);

        public static IPEndPoint? _ToIPEndPoint(this string? str, int defaultPort, AllowedIPVersions allowed = AllowedIPVersions.All, bool noExceptionAndReturnNull = false)
            => IPUtil.StrToIPEndPoint(str, defaultPort, allowed, noExceptionAndReturnNull);

        public static bool IsCommunicationError(this VpnErrors error)
        {
            if (error == VpnErrors.ERR_SSL_X509_UNTRUSTED || error == VpnErrors.ERR_CERT_NOT_TRUSTED ||
                error == VpnErrors.ERR_SSL_X509_EXPIRED ||
                error == VpnErrors.ERR_PROTOCOL_ERROR || error == VpnErrors.ERR_CONNECT_FAILED ||
                error == VpnErrors.ERR_TIMEOUTED || error == VpnErrors.ERR_DISCONNECTED)
            {
                return true;
            }

            return false;
        }

        public static bool _IsVpnCommuncationError(this Exception exception)
        {
            if (exception == null) return true;
            if (exception is VpnException vpnEx) return vpnEx.IsCommuncationError;
            return true;
        }
    }
}



