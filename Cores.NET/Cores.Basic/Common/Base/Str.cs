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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Serialization;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    [Flags]
    public enum CrlfStyle
    {
        LocalPlatform,
        Lf,
        CrLf,
        NoChange,
    }

    // DateTime をシンブル文字列に変換
    [Flags]
    public enum DtStrOption
    {
        All,
        DateOnly,
        TimeOnly,
    }

    namespace Legacy
    {
        // キーバリューリスト
        public class KeyValueList
        {
            SortedDictionary<string, string> data = new SortedDictionary<string, string>();

            public KeyValueList()
            {
            }

            public KeyValueList(string fromString)
            {
                KeyValueList v = this;
                StringReader r = new StringReader(fromString);
                while (true)
                {
                    string? line = r.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    int i = Str.SearchStr(line, "=", 0);
                    if (i != -1)
                    {
                        string key = Str.DecodeCEscape(line.Substring(0, i));
                        string value = Str.DecodeCEscape(line.Substring(i + 1));

                        v.Add(key, value);
                    }
                }
            }

            void NormalizeName(ref string name)
            {
                Str.NormalizeString(ref name);
                name = name.ToUpperInvariant();
                if (Str.InStr(name, "="))
                {
                    throw new InvalidDataException(name);
                }
            }

            public string this[string name]
            {
                get
                {
                    return Get(name);
                }

                set
                {
                    Set(name, value);
                }
            }

            public void Add(string name, string value)
            {
                Set(name, value);
            }

            public void Set(string name, string value)
            {
                NormalizeName(ref name);

                if (value == null)
                {
                    value = "";
                }

                if (value == "")
                {
                    Delete(name);
                    return;
                }

                if (data.ContainsKey(name) == false)
                {
                    data.Add(name, value);
                }
                else
                {
                    data[name] = value;
                }
            }

            public string Get(string name)
            {
                NormalizeName(ref name);

                if (data.ContainsKey(name))
                {
                    return data[name];
                }
                else
                {
                    return "";
                }
            }

            public void Delete(string name)
            {
                NormalizeName(ref name);

                if (data.ContainsKey(name))
                {
                    data.Remove(name);
                }
            }

            public string[] EnumKeys()
            {
                List<string> ret = new List<string>();
                foreach (string key in this.data.Keys)
                {
                    ret.Add(key);
                }
                return ret.ToArray();
            }

            public override string ToString()
            {
                StringWriter w = new StringWriter();
                foreach (string name in data.Keys)
                {
                    string line = string.Format("{0}={1}", Str.EncodeCEscape(name), Str.EncodeCEscape(data[name]));

                    w.WriteLine(line);
                }

                return w.ToString();
            }

            public static KeyValueList FromString(string src)
            {
                return new KeyValueList(src);
            }
        }

        // シリアライズされたエラー文字列
        public class SerializedError
        {
            public string Code = null!;
            public string Language = null!;
            public string ErrorMsg = null!;
        }
    }

    // Printf 風フラグ
    [FlagsAttribute]
    public enum PrintFFLags
    {
        Minus = 1,
        Plus = 2,
        Zero = 4,
        Blank = 8,
        Sharp = 16,
    }

    // Printf 風パース結果
    public class PrintFParsedParam
    {
        public bool Ok = false;
        public readonly PrintFFLags Flags = 0;
        public readonly int Width = 0;
        public readonly int Precision = 0;
        public readonly bool NoPrecision = true;
        public readonly string Type = "";

        static PrintFFLags CharToFlag(char c)
        {
            switch (c)
            {
                case '-':
                    return PrintFFLags.Minus;

                case '+':
                    return PrintFFLags.Plus;

                case '0':
                    return PrintFFLags.Zero;

                case ' ':
                    return PrintFFLags.Blank;

                case '#':
                    return PrintFFLags.Sharp;
            }

            return 0;
        }

        public string GetString(object param)
        {
            int i;
            StringBuilder sb;
            string tmp = "(error)";
            double f;
            bool signed = false;

            switch (this.Type)
            {
                case "c":
                case "C":
                    if (param is char)
                    {
                        tmp += (char)param;
                    }
                    else if (param is string)
                    {
                        string s = (string)param;
                        if (s.Length >= 1)
                        {
                            tmp += s[0];
                        }
                    }
                    break;

                case "d":
                case "i":
                    sb = new StringBuilder();
                    int count = this.Width;
                    if (this.Precision != 0)
                    {
                        count = this.Precision;
                    }
                    for (i = 1; i < this.Precision; i++)
                    {
                        sb.Append("#");
                    }
                    sb.Append("0");

                    if (param is int)
                    {
                        tmp = ((int)param).ToString(sb.ToString());
                    }
                    else if (param is long)
                    {
                        tmp = ((long)param).ToString(sb.ToString());
                    }
                    else if (param is uint)
                    {
                        tmp = ((int)((uint)param)).ToString(sb.ToString());
                    }
                    else if (param is ulong)
                    {
                        tmp = ((long)((ulong)param)).ToString(sb.ToString());
                    }
                    else if (param is decimal)
                    {
                        tmp = ((decimal)param).ToString(sb.ToString());
                    }
                    signed = true;

                    break;

                case "u":
                    sb = new StringBuilder();
                    for (i = 1; i < this.Precision; i++)
                    {
                        sb.Append("#");
                    }
                    sb.Append("0");

                    if (param is int)
                    {
                        tmp = ((uint)((int)param)).ToString(sb.ToString());
                    }
                    else if (param is long)
                    {
                        tmp = ((ulong)((long)param)).ToString(sb.ToString());
                    }
                    else if (param is uint)
                    {
                        tmp = ((uint)param).ToString(sb.ToString());
                    }
                    else if (param is ulong)
                    {
                        tmp = ((ulong)param).ToString(sb.ToString());
                    }
                    else if (param is decimal)
                    {
                        tmp = ((decimal)param).ToString(sb.ToString());
                    }

                    break;

                case "x":
                case "X":
                    sb = new StringBuilder();
                    sb.Append(this.Type);
                    sb.Append(this.Precision.ToString());

                    if (param is int)
                    {
                        tmp = ((uint)((int)param)).ToString(sb.ToString());
                    }
                    else if (param is long)
                    {
                        tmp = ((ulong)((long)param)).ToString(sb.ToString());
                    }
                    else if (param is uint)
                    {
                        tmp = ((uint)param).ToString(sb.ToString());
                    }
                    else if (param is ulong)
                    {
                        tmp = ((ulong)param).ToString(sb.ToString());
                    }

                    break;

                case "e":
                case "E":
                case "f":
                    f = 0;

                    if (param is int)
                    {
                        f = (double)((int)param);
                    }
                    else if (param is long)
                    {
                        f = (double)((long)param);
                    }
                    else if (param is uint)
                    {
                        f = (double)((uint)param);
                    }
                    else if (param is ulong)
                    {
                        f = (double)((ulong)param);
                    }
                    else if (param is decimal)
                    {
                        f = (double)((long)param);
                    }
                    else if (param is float)
                    {
                        f = (double)((float)param);
                    }
                    else if (param is double)
                    {
                        f = (double)param;
                    }
                    else
                    {
                        break;
                    }

                    int prectmp = Precision;
                    if (prectmp == 0 && NoPrecision)
                    {
                        prectmp = 6;
                    }

                    tmp = f.ToString(string.Format("{0}{1}", Type, prectmp));

                    break;

                case "s":
                case "S":
                    if (param == null)
                    {
                        tmp = "(null)";
                    }
                    else
                    {
                        tmp = param.ToString()._NonNull();
                    }
                    break;
            }

            int normalWidth = Str.GetStrWidth(tmp);
            int targetWidth = Math.Max(this.Width, normalWidth);

            if ((this.Flags & PrintFFLags.Plus) != 0)
            {
                if (signed)
                {
                    if (tmp.StartsWith("-") == false)
                    {
                        tmp = "+" + tmp;
                    }
                }
            }
            else
            {
                if ((this.Flags & PrintFFLags.Blank) != 0)
                {
                    if (signed)
                    {
                        if (tmp.StartsWith("-") == false)
                        {
                            tmp = " " + tmp;
                        }
                    }
                }
            }

            if ((this.Flags & PrintFFLags.Minus) != 0)
            {
                int w = targetWidth - Str.GetStrWidth(tmp);
                if (w < 0)
                {
                    w = 0;
                }

                tmp += Str.MakeCharArray(' ', w);
            }
            else if ((this.Flags & PrintFFLags.Zero) != 0)
            {
                int w = targetWidth - Str.GetStrWidth(tmp);
                if (w < 0)
                {
                    w = 0;
                }

                tmp = Str.MakeCharArray('0', w) + tmp;
            }
            else
            {
                int w = targetWidth - Str.GetStrWidth(tmp);
                if (w < 0)
                {
                    w = 0;
                }

                tmp = Str.MakeCharArray(' ', w) + tmp;
            }

            if ((this.Flags & PrintFFLags.Sharp) != 0)
            {
                if (Type == "x" || Type == "X")
                {
                    tmp = "0x" + tmp;
                }
            }

            return tmp;
        }

        public PrintFParsedParam(string str)
        {
            Str.NormalizeString(ref str);

            if (str.StartsWith("%") == false)
            {
                return;
            }

            str = str.Substring(1);

            Queue<char> q = new Queue<char>();
            foreach (char c in str)
            {
                q.Enqueue(c);
            }

            while (q.Count >= 1)
            {
                char c = q.Peek();
                PrintFFLags f = CharToFlag(c);

                if (f == 0)
                {
                    break;
                }

                this.Flags |= f;
                q.Dequeue();
            }

            Queue<char> q2 = new Queue<char>();

            while (q.Count >= 1)
            {
                bool bf = false;
                char c = q.Peek();

                switch (c)
                {
                    case 'h':
                    case 'l':
                    case 'I':
                    case 'c':
                    case 'C':
                    case 'd':
                    case 'i':
                    case 'o':
                    case 'u':
                    case 'x':
                    case 'X':
                    case 'e':
                    case 'E':
                    case 'f':
                    case 'g':
                    case 'G':
                    case 'n':
                    case 'p':
                    case 's':
                    case 'S':
                        bf = true;
                        break;

                    default:
                        q2.Enqueue(c);
                        break;
                }

                if (bf)
                {
                    break;
                }

                q.Dequeue();
            }

            string[] widthAndPrec = (new string(q2.ToArray())).Split('.');

            if (widthAndPrec.Length == 1)
            {
                this.Width = Str.StrToInt(widthAndPrec[0]);
            }
            else if (widthAndPrec.Length == 2)
            {
                this.Width = Str.StrToInt(widthAndPrec[0]);
                this.Precision = Str.StrToInt(widthAndPrec[1]);
                this.NoPrecision = false;
            }

            this.Width = Math.Max(this.Width, 0);
            this.Precision = Math.Max(this.Precision, 0);


            while (q.Count >= 1)
            {
                char c = q.Peek();
                bool bf = false;

                switch (c)
                {
                    case 'c':
                    case 'C':
                    case 'd':
                    case 'i':
                    case 'o':
                    case 'u':
                    case 'x':
                    case 'X':
                    case 'e':
                    case 'E':
                    case 'f':
                    case 'g':
                    case 'G':
                    case 'a':
                    case 'A':
                    case 'n':
                    case 'p':
                    case 's':
                    case 'S':
                        bf = true;
                        break;

                    default:
                        break;
                }

                if (bf)
                {
                    break;
                }

                q.Dequeue();
            }

            this.Type = new string(q.ToArray());
            if (this.Type.Length >= 1)
            {
                this.Type = this.Type.Substring(0, 1);
            }

            this.Ok = (Str.IsEmptyStr(this.Type) == false);
        }
    }

    public class DevToolsCsUsingStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if (x.StartsWith("using"))
            {
                Str.GetKeyAndValue(x, out _, out x);
            }

            if (x.EndsWith(";")) x = x.Substring(0, x.Length - 1);

            if (y.StartsWith("using"))
            {
                Str.GetKeyAndValue(y, out _, out y);
            }

            if (y.EndsWith(";")) y = y.Substring(0, y.Length - 1);

            bool b1 = (x.StartsWith("using System") || x.StartsWith("System"));
            bool b2 = (y.StartsWith("using System") || y.StartsWith("System"));

            if (b1 != b2)
            {
                return -b1.CompareTo(b2);
            }

            return x._CmpTrim(y);
        }

        public bool Equals(string? x, string? y)
        {
            return x._IsSameTrim(y);
        }

        public int GetHashCode(string? obj)
        {
            return obj._NonNullTrim().GetHashCode();
        }
    }

    public class ExtendedStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public StringComparison Comparison { get; }

        public ExtendedStrComparer(StringComparison comparison)
        {
            this.Comparison = comparison;
        }

        public int Compare(string? x, string? y)
        {
            return x._CmpTrim(y, this.Comparison);
        }

        public bool Equals(string? x, string? y)
        {
            return x._IsSameTrim(y, this.Comparison);
        }

        public int GetHashCode(string? obj)
        {
            return obj._NonNullTrim().GetHashCode(this.Comparison);
        }
    }

    public class IpAddressStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return 0;

            if (IPAddress.TryParse(x, out IPAddress? ip1))
            {
                if (IPAddress.TryParse(y, out IPAddress? ip2))
                {
                    int r = ip1.AddressFamily.CompareTo(ip2.AddressFamily);
                    if (r != 0) return r;

                    r = Util.MemCompare(ip1.GetAddressBytes(), ip2.GetAddressBytes());
                    if (r != 0) return r;

                    if (ip1.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return 0;

                    return ip1.ScopeId.CompareTo(ip2.ScopeId);
                }
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return true;

            if (IPAddress.TryParse(x, out IPAddress? ip1))
            {
                if (IPAddress.TryParse(y, out IPAddress? ip2))
                {
                    return ip1.Equals(ip2);
                }
            }

            return false;
        }

        public int GetHashCode(string? obj)
        {
            obj = obj._NonNullTrim();

            if (IPAddress.TryParse(obj, out IPAddress? addr) == false)
            {
                return 0;
            }
            else
            {
                return addr.GetHashCode();
            }
        }
    }

    public class HttpFqdnReverseStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return 0;

            if (x._TryParseUrl(out Uri? url_x, out _, null) == false ||
                y._TryParseUrl(out Uri? url_y, out _, null) == false)
            {
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }

            url_x._MarkNotNull();
            url_y._MarkNotNull();

            string fqdn_x = Str.ReverseFqdnStr(url_x.DnsSafeHost);
            string fqdn_y = Str.ReverseFqdnStr(url_y.DnsSafeHost);

            int r = string.Compare(fqdn_x, fqdn_y, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return r;

            r = string.Compare(url_x.PathAndQuery, url_y.PathAndQuery, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return r;

            r = string.Compare(url_x.Scheme, url_y.Scheme, StringComparison.OrdinalIgnoreCase);
            if (r != 0) return r;

            r = url_x.Port.CompareTo(url_y.Port);

            return r;
        }

        public bool Equals(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return true;

            if (x._TryParseUrl(out Uri? url_x, out _, null) == false ||
                y._TryParseUrl(out Uri? url_y, out _, null) == false)
            {
                return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
            }

            url_x._MarkNotNull();
            url_y._MarkNotNull();

            string fqdn_x = Str.ReverseFqdnStr(url_x.DnsSafeHost);
            string fqdn_y = Str.ReverseFqdnStr(url_y.DnsSafeHost);

            if (string.Equals(fqdn_x, fqdn_y, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (string.Equals(url_x.PathAndQuery, url_y.PathAndQuery, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            if (string.Equals(url_x.Scheme, url_y.Scheme, StringComparison.OrdinalIgnoreCase) == false)
                return false;

            return url_x.Port.Equals(url_y.Port);
        }

        public int GetHashCode(string? obj)
        {
            obj = obj._NonNullTrim();

            return obj.GetHashCode();
        }
    }

    public class FqdnReverseStrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public int Compare(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return 0;

            x = Str.ReverseFqdnStr(x);
            y = Str.ReverseFqdnStr(y);

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(string? x, string? y)
        {
            x = x._NonNullTrim();
            y = y._NonNullTrim();

            if ((IgnoreCase)x == y) return true;

            x = Str.ReverseFqdnStr(x);
            y = Str.ReverseFqdnStr(y);

            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(string? obj)
        {
            obj = obj._NonNullTrim();

            return obj.GetHashCode();
        }
    }

    public class StrComparer : IEqualityComparer<string?>, IComparer<string?>
    {
        public static StrComparer IgnoreCaseComparer { get; } = new StrComparer(false);
        public static StrComparer SensitiveCaseComparer { get; } = new StrComparer(true);

        public static DevToolsCsUsingStrComparer DevToolsCsUsingComparer { get; } = new DevToolsCsUsingStrComparer();

        public static ExtendedStrComparer IgnoreCaseTrimComparer { get; } = new ExtendedStrComparer(StringComparison.OrdinalIgnoreCase);
        public static ExtendedStrComparer SensitiveCaseTrimComparer { get; } = new ExtendedStrComparer(StringComparison.Ordinal);

        public static IpAddressStrComparer IpAddressStrComparer { get; } = new IpAddressStrComparer();

        public static FqdnReverseStrComparer FqdnReverseStrComparer { get; } = new FqdnReverseStrComparer();
        public static HttpFqdnReverseStrComparer HttpFqdnReverseStrComparer { get; } = new HttpFqdnReverseStrComparer();

        readonly static Singleton<StringComparison, StrComparer> FromComparisonCache = new Singleton<StringComparison, StrComparer>(x => new StrComparer(x));

        public static StrComparer Get(StringComparison comparison)
        {
            if (comparison == StringComparison.Ordinal)
            {
                return SensitiveCaseComparer;
            }
            else if (comparison == StringComparison.OrdinalIgnoreCase)
            {
                return IgnoreCaseComparer;
            }
            else
            {
                return FromComparisonCache[comparison];
            }
        }

        public static StrComparer Get(bool caseSensitive = false)
        {
            if (caseSensitive == false)
            {
                return IgnoreCaseComparer;
            }
            else
            {
                return SensitiveCaseComparer;
            }
        }

        public StringComparison Comparison { get; }

        private StrComparer(bool caseSensitive = false)
        {
            this.Comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        private StrComparer(StringComparison comparison)
        {
            this.Comparison = comparison;
        }

        public int Compare(string? x, string? y)
            => string.Compare(x, y, this.Comparison);

        public bool Equals(string? x, string? y)
            => string.Equals(x, y, this.Comparison);

        public int GetHashCode(string? obj)
            => obj?.GetHashCode(this.Comparison) ?? 0;

        public static implicit operator StringComparison(StrComparer cmp) => cmp.Comparison;
    }

    public delegate bool RemoveStringFunction(string str);

    [Flags]
    public enum MacAddressStyle
    {
        Windows,
        Linux,
    }

    [Flags]
    public enum FullDateTimeStrFlags : ulong
    {
        None = 0,
        NoSeconds = 1,
        SlashDate = 2,
        CommaTime = 4,
    }

    public class UrlEncodeParam
    {
        public string DoNotEncodeCharList { get; set; }

        public UrlEncodeParam(string dontEncodeCharList = "")
        {
            this.DoNotEncodeCharList = dontEncodeCharList;
        }
    }

    // 文字列操作
    public static class Str
    {
        public static Encoding AsciiEncoding { get; }
        public static Encoding ShiftJisEncoding { get; }
        public static Encoding ISO2022JPEncoding { get; }
        public static Encoding EucJpEncoding { get; }
        public static Encoding ISO88591Encoding { get; }
        public static Encoding GB2312Encoding { get; }
        public static Encoding EucKrEncoding { get; }
        public static Encoding Utf8Encoding { get; }
        public static Encoding UniEncoding { get; }

        public static byte[] BomUtf8 { get; }

        public static readonly ReadOnlyMemory<byte> NewLine_Bytes_Windows = new byte[] { 13, 10 };
        public static readonly ReadOnlyMemory<byte> NewLine_Bytes_Unix = new byte[] { 10 };
        public static readonly ReadOnlyMemory<byte> NewLine_Bytes_Local = (Environment.OSVersion.Platform == PlatformID.Win32NT) ? NewLine_Bytes_Windows : NewLine_Bytes_Unix;

        public static readonly ReadOnlyMemory<byte> CrLf_Bytes = new byte[] { 13, 10 };
        public static readonly ReadOnlyMemory<byte> Lf_Bytes = new byte[] { 10 };

        public static readonly string NewLine_Str_Windows = "\r\n";
        public static readonly string NewLine_Str_Unix = "\n";
        public static readonly string NewLine_Str_Local = (Environment.OSVersion.Platform == PlatformID.Win32NT) ? NewLine_Str_Windows : NewLine_Str_Unix;

        public static readonly string CrLf_Str = "\r\n";
        public static readonly string Lf_Str = "\n";

        public static ReadOnlyMemory<byte> GetCrlfBytes(CrlfStyle style = CrlfStyle.LocalPlatform) => GetNewLineBytes(style);
        public static ReadOnlyMemory<byte> GetNewLineBytes(CrlfStyle style = CrlfStyle.LocalPlatform)
        {
            switch (style)
            {
                case CrlfStyle.LocalPlatform:
                    return NewLine_Bytes_Local;

                case CrlfStyle.CrLf:
                    return NewLine_Bytes_Windows;

                case CrlfStyle.Lf:
                    return NewLine_Bytes_Unix;

                default:
                    throw new ArgumentOutOfRangeException("style");
            }
        }

        public static string GetCrlfStr(CrlfStyle style = CrlfStyle.LocalPlatform) => GetNewLineStr(style);
        public static string GetNewLineStr(CrlfStyle style = CrlfStyle.LocalPlatform)
        {
            switch (style)
            {
                case CrlfStyle.LocalPlatform:
                    return NewLine_Str_Local;

                case CrlfStyle.CrLf:
                    return NewLine_Str_Windows;

                case CrlfStyle.Lf:
                    return NewLine_Str_Unix;

                default:
                    throw new ArgumentOutOfRangeException("style");
            }
        }

        public static IReadOnlyList<Encoding> SuitableEncodingListForJapaneseWin32;

        // Encoding の初期化
        static Str()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            AsciiEncoding = Encoding.ASCII;
            ShiftJisEncoding = Encoding.GetEncoding("shift_jis");
            ISO2022JPEncoding = Encoding.GetEncoding("ISO-2022-JP");
            EucJpEncoding = Encoding.GetEncoding("euc-jp");
            ISO88591Encoding = Encoding.GetEncoding("iso-8859-1");
            GB2312Encoding = Encoding.GetEncoding("gb2312");
            EucKrEncoding = Encoding.GetEncoding("euc-kr");
            Utf8Encoding = Encoding.UTF8;
            UniEncoding = Encoding.Unicode;
            BomUtf8 = Str.GetBOM(Str.Utf8Encoding)!;

            var suitableEncodingListForJapaneseWin32 = new List<Encoding>();
            suitableEncodingListForJapaneseWin32.Add(ShiftJisEncoding);
            suitableEncodingListForJapaneseWin32.Add(AsciiEncoding);
            suitableEncodingListForJapaneseWin32.Add(Utf8Encoding);

            SuitableEncodingListForJapaneseWin32 = suitableEncodingListForJapaneseWin32;
        }

        internal static readonly char[] standardSplitChars =
        {
            ' ', '　', '\t',
        };

        public static char[] StandardSplitChars
        {
            get
            {
                return (char[])standardSplitChars.Clone();
            }
        }


        // 指定されたエンコード一覧を順に試行し、最初に表現可能なエンコードを返す
        public static Encoding GetBestSuitableEncoding(string str, IEnumerable<Encoding?>? canditateList = null)
        {
            if (canditateList == null) canditateList = SuitableEncodingListForJapaneseWin32;

            foreach (var candidate in canditateList)
            {
                if (candidate != null)
                {
                    if (IsSuitableEncodingForString(str, candidate))
                    {
                        return candidate;
                    }
                }
            }

            return Str.Utf8Encoding;
        }

        [Flags]
        public enum SearchableStrFlag
        {
            None = 0,
            IncludeBase64InBinary = 1,
            IncludePrintableAsciiStr = 2,
            PrependFieldName = 4,

            Default = IncludeBase64InBinary | IncludePrintableAsciiStr,
        }

        public static string NormalizeStrForSearch(string s)
        {
            s = s.ToLowerInvariant();

            Str.NormalizeString(ref s, false, true, false, true);

            s = s._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', '\t', '\r', '\n')._Combine(" ");

            return s;
        }

        public static List<string> GetSearchableStrListFromPrimitiveData(object? o, SearchableStrFlag flag, string prefix = "")
        {
            HashSet<string> tmp = new HashSet<string>();

            void AddBytes(ReadOnlySpan<byte> byteArray)
            {
                tmp.Add(byteArray._GetHexString().ToLowerInvariant());

                if (flag.Bit(SearchableStrFlag.IncludeBase64InBinary))
                {
                    tmp.Add(byteArray._Base64Encode().ToLowerInvariant());
                }

                if (flag.Bit(SearchableStrFlag.IncludePrintableAsciiStr))
                {
                    tmp.Add(Str.MakeAsciiOneLinePrintableStr(byteArray).ToLowerInvariant());
                }
            }

            if (o != null && o._IsEmpty() == false)
            {
                if (o is byte[] byteArray)
                {
                    AddBytes(byteArray);
                }
                else if (o is Memory<byte> ms1)
                {
                    AddBytes(ms1.Span);
                }
                else if (o is ReadOnlyMemory<byte> ms2)
                {
                    AddBytes(ms2.Span);
                }
                else if (o is Enum e)
                {
                    tmp.Add(e.ToString().ToLowerInvariant()._ReplaceStr(", ", " ")._ReplaceStr(",", " "));

                    ulong value = Convert.ToUInt64(o);

                    tmp.Add(value.ToString());
                }
                else if (o._IsSignedIntegerObj())
                {
                    long s64 = Convert.ToInt64(o);
                    tmp.Add(s64.ToString());
                    tmp.Add(s64._ToString3());
                }
                else if (o._IsUnsignedIntegerObj())
                {
                    ulong u64 = Convert.ToUInt64(o);
                    tmp.Add(u64.ToString());
                    tmp.Add(u64._ToString3());
                }
                else if (o is decimal d)
                {
                    tmp.Add(d.ToString());
                }
                else if (o is float f1)
                {
                    tmp.Add(f1.ToString("F15"));
                    tmp.Add(f1.ToString("F3"));
                    tmp.Add(f1.ToString("F6"));
                }
                else if (o is double f2)
                {
                    tmp.Add(f2.ToString("F15"));
                    tmp.Add(f2.ToString("F3"));
                    tmp.Add(f2.ToString("F6"));
                }
                else if (o is bool b)
                {
                    tmp.Add(b ? "true" : "false");
                    tmp.Add(b ? "1" : "0");
                    tmp.Add(b ? "yes" : "no");
                }
                else if (o is DateTime dt)
                {
                    tmp.Add(dt._ToDtStr(true));
                    tmp.Add(dt._ToYymmddStr() + dt._ToHhmmssStr());
                    tmp.Add(dt._ToYymmddStr() + " " + dt._ToHhmmssStr());
                }
                else if (o is DateTimeOffset dto)
                {
                    tmp.Add(dto._ToDtStr(true));
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, true)._ToDtStr(true));
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, false)._ToDtStr(true));

                    tmp.Add(dto._ToYymmddStr() + dto._ToHhmmssStr());
                    tmp.Add(dto._ToYymmddStr() + " " + dto._ToHhmmssStr());
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, true)._ToYymmddHhmmssLong().ToString());
                    tmp.Add(dto.UtcDateTime._AsDateTimeOffset(false, false)._ToYymmddHhmmssLong().ToString());
                }
                else if (o is TimeSpan ts)
                {
                    tmp.Add(ts._ToTsStr(true));
                    tmp.Add(ts.TotalMilliseconds.ToString());
                    tmp.Add(ts.TotalSeconds.ToString());
                }
                else
                {
                    string s;

                    if (o is string str)
                    {
                        s = str._NonNullTrim();
                    }
                    else
                    {
                        s = o.ToString()._NonNullTrim();
                    }

                    tmp.Add(NormalizeStrForSearch(s));
                }
            }

            List<string> ret = new List<string>();

            foreach (var s in tmp)
            {
                if (s._IsFilled())
                {
                    ret.Add((prefix + s).Trim());
                }
            }

            return ret;
        }

        public static List<string> GetSearchableStrListFromObject(object? obj, SearchableStrFlag flag = SearchableStrFlag.Default)
        {
            var walkList = Util.WalkObject(obj);

            List<string> tmp = new List<string>();

            foreach (var item in walkList)
            {
                var set = GetSearchableStrListFromPrimitiveData(item.Data, flag, flag.Bit(SearchableStrFlag.PrependFieldName) ? item.Name.ToLower() + "=" : "");

                foreach (var s in set)
                {
                    if (s._IsFilled())
                    {
                        tmp.Add(s.Trim());
                    }
                }
            }

            return tmp;
        }

        public static string GenerateSearchableStrFromObject(object? obj, SearchableStrFlag flag = SearchableStrFlag.Default)
        {
            var list = GetSearchableStrListFromObject(obj, flag);

            if (list.Count >= 1)
            {
                list.Insert(0, "");
                list.Add("");
            }

            return list._Combine(" | ").Trim();
        }

        public static void Test_SearchableStr()
        {
            SSTest1 t1 = new SSTest1
            {
                str1 = "Banana",
                str2 = "Carot",
                u8 = 234,
                s8 = -124,
                u16 = 54321,
                s16 = -12345,
                u32 = 3123456789,
                s32 = -2012345678,
                s64 = -9223372036854775123,
                u64 = 18446744073709550616,
                dt1 = "2022/03/25 01:23:45"._ToDateTime(),
                ts1 = TimeSpan.FromMilliseconds(3600 * 24 * 1000).Add(TimeSpan.FromSeconds(-1)),
                b1 = true,
                decimal1 = 89454141,
                double1 = 3.1415926535897932D,
                float1 = 2.718281828459F,
                flag1 = SSTestFlag1.Microsoft | SSTestFlag1.Oracle,
                ip1 = "2001:af80:1:2:3::8931"._StrToIP()!,
                data1 = "ABCDEFG"._GetBytes_UTF8(),
                data2 = "abcdefg"._GetBytes_UTF8(),
            };

            SSTest1 t2 = new SSTest1
            {
                str1 = "Super",
                str2 = "Nekosan",
                u8 = 134,
                s8 = -104,
                u16 = 53321,
                s16 = -10345,
                u32 = 3113456789,
                s32 = -2019345678,
                s64 = -9223311036854775123,
                u64 = 10446744111119550616,
                dt1 = "2099/03/25 01:23:45"._ToDateTime(),
                ts1 = TimeSpan.FromMilliseconds(3600 * 24 * 1000).Add(TimeSpan.FromSeconds(-1)),
                b1 = true,
                decimal1 = 1689454141,
                double1 = 3.1415926535897932D,
                float1 = 2.718281828459F,
                flag1 = SSTestFlag1.Microsoft | SSTestFlag1.Oracle,
                ip1 = "2001:cafe:1:2:3::8931"._StrToIP()!,
                data1 = "xxxx"._GetBytes_UTF8(),
                data2 = "zzzz"._GetBytes_UTF8(),
            };

            t1.Child = t2;

            string ss = GenerateSearchableStrFromObject(t1, flag: SearchableStrFlag.Default | SearchableStrFlag.PrependFieldName);

            //ss._Print();

            string test = "| str2=carot | str1=banana | u8=234 | s8=-124 | u16=54321 | u16=54,321 | s16=-12345 | s16=-12,345 | u32=3123456789 | u32=3,123,456,789 | s32=-2012345678 | s32=-2,012,345,678 | u64=18446744073709550616 | u64=18,446,744,073,709,550,616 | s64=-9223372036854775123 | s64=-9,223,372,036,854,775,123 | dt1=2022/03/25 01:23:45.000 | dt1=20220325012345 | dt1=20220325 012345 | ts1=23:59:59.000 | ts1=86399000 | ts1=86399 | b1=true | b1=1 | b1=yes | double1=3.141592653589793 | double1=3.142 | double1=3.141593 | float1=2.718281745910645 | float1=2.718 | float1=2.718282 | flag1=microsoft oracle | flag1=3 | ip1=2001:af80:1:2:3::8931 | data1=41424344454647 | data1=qujdrevgrw== | data1=abcdefg | data2=61626364656667 | data2=ywjjzgvmzw== | data2=abcdefg | str2=nekosan | str1=super | u8=134 | s8=-104 | u16=53321 | u16=53,321 | s16=-10345 | s16=-10,345 | u32=3113456789 | u32=3,113,456,789 | s32=-2019345678 | s32=-2,019,345,678 | u64=10446744111119550616 | u64=10,446,744,111,119,550,616 | s64=-9223311036854775123 | s64=-9,223,311,036,854,775,123 | dt1=2099/03/25 01:23:45.000 | dt1=20990325012345 | dt1=20990325 012345 | ts1=23:59:59.000 | ts1=86399000 | ts1=86399 | b1=true | b1=1 | b1=yes | double1=3.141592653589793 | double1=3.142 | double1=3.141593 | float1=2.718281745910645 | float1=2.718 | float1=2.718282 | flag1=microsoft oracle | flag1=3 | ip1=2001:cafe:1:2:3::8931 | data1=78787878 | data1=ehh4ea== | data1=xxxx | data2=7a7a7a7a | data2=enp6eg== | data2=zzzz |";

            Dbg.TestTrue(test == ss);
        }

        [Flags]
        public enum SSTestFlag1
        {
            Apple = 0,
            Microsoft = 1,
            Oracle = 2,
        }

        public class SSTest1
        {
            public string str1 = "";
            public string str2 { get; set; } = "";
            public byte u8;
            public sbyte s8;
            public ushort u16;
            public short s16;
            public uint u32;
            public int s32;
            public ulong u64;
            public long s64;
            public DateTime dt1;
            public TimeSpan ts1;
            public bool b1;
            public decimal decimal1;
            public double double1;
            public float float1;
            public SSTestFlag1 flag1;
            public IPAddress ip1 = null!;
            public byte[] data1 = null!;
            public Memory<byte> data2;

            public SSTest1? Child;
        }


        // Web Linking (RFC 8288) のパース
        public static Tuple<string, KeyValueList<string, string>> ParseWebLinkStr(string src)
        {
            // ; で区切る
            string[] tokens = src._Split(StringSplitOptions.TrimEntries, ';');

            string urlStr = tokens[0];

            if (urlStr.StartsWith("<") == false || urlStr.EndsWith(">") == false)
            {
                throw new CoresLibException("Invalid Web Linking URL String");
            }

            urlStr = urlStr.Substring(1, urlStr.Length - 2);

            if (urlStr._IsEmpty()) throw new CoresLibException("Invalid Web Linking URL String");

            KeyValueList<string, string> x = new KeyValueList<string, string>();

            for (int i = 1; i < tokens.Length; i++)
            {
                string kvstr = tokens[i];

                var tk = kvstr._Split(StringSplitOptions.RemoveEmptyEntries, '=');

                x.Add(tk[0]._RemoveQuotation(), tk[1]._RemoveQuotation());
            }

            return new Tuple<string, KeyValueList<string, string>>(urlStr, x);
        }

        public static string MakeStringUseOnlyChars(string src, string charList = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-", StringComparison comparison = StringComparison.Ordinal)
        {
            src = src._NonNull();

            StringBuilder sb = new StringBuilder(src.Length);

            foreach (char c in src)
            {
                if (charList.IndexOf(c, StringComparison.Ordinal) != -1)
                    sb.Append(c);
            }

            return sb.ToString();
        }

        public static bool CheckUseOnlyChars(string src, Exception? exceptionToThrow = null, string charList = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-", StringComparison comparison = StringComparison.Ordinal)
        {
            src = src._NonNull();

            foreach (char c in src)
            {
                if (charList.IndexOf(c, StringComparison.Ordinal) == -1)
                {
                    if (exceptionToThrow != null)
                    {
                        throw exceptionToThrow;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static string PrependIndent(string src, int indentWidth = 4, char indentChar = ' ', CrlfStyle crlfStyle = CrlfStyle.LocalPlatform)
        {
            var lines = src._GetLines();

            StringWriter w = new StringWriter();

            string padding = indentChar._MakeCharArray(indentWidth);

            foreach (var line in lines)
            {
                w.WriteLine(padding + line);
            }

            return w.ToString()._NormalizeCrlf(crlfStyle, true);
        }

        static readonly CriticalSection LockNewId = new CriticalSection();
        static ulong LastNewIdMSecs = 0;

        public static Tuple<string, int> ParseHostnaneAndPort(string str, int defaultPort)
        {
            str = str._NonNull();

            string hoststr = "";
            string portstr = "";

            // hostname1:hostname2:port のようになっている可能性があるので、最後の ':' の出現場所を取得する
            for (int i = 0; i < str.Length - 1; i++)
            {
                char c = str[i];

                if (c == ':')
                {
                    hoststr = str.Substring(0, i);
                    portstr = str.Substring(i + 1);
                }
            }

            if (portstr._IsNumber())
            {
                return new Tuple<string, int>(hoststr.Trim(), portstr._ToInt());
            }
            else
            {
                return new Tuple<string, int>(str.Trim(), defaultPort);
            }
        }

        public static bool IsValidFqdn(string fqdn)
        {
            fqdn = fqdn._NonNull();

            if (fqdn.EndsWith(".")) fqdn = fqdn.Substring(0, fqdn.Length - 1);

            var tokens = fqdn._Split(StringSplitOptions.None, '.');

            foreach (string token in tokens)
            {
                if (token._IsEmpty()) return false;

                if (token.All(c => ('0' <= c && c <= '9') || ('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z') || c == '-' || c == '_') == false) return false;
            }

            return true;
        }

        [return: NotNullIfNotNull("fqdn")]
        public static string? ReverseFqdnStr(string? fqdn)
        {
            if (fqdn == null) return null;
            return fqdn._Split(StringSplitOptions.None, '.').Reverse()._Combine(".");
        }

        public static Memory<byte> CHexArrayToBinary(string body)
        {
            SpanBuffer<byte> buf = new SpanBuffer<byte>();

            string[] lines = body._GetLines(true, true);

            foreach (string line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t', '　', '{', '}', ',', '\r', '\n');

                foreach (string token in tokens)
                {
                    string tmp = token.Trim();

                    if (tmp.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        string tmp2 = tmp._Slice(2);

                        buf.WriteOne(Convert.ToByte(tmp2, 16));
                    }
                }
            }

            return buf.Span.ToArray();
        }

        // 電話番号かどうか判定
        public static bool IsPhoneNumber(string? str)
        {
            if (str._IsEmpty()) return false;

            return str.All(x => (x >= '0' && x <= '9') || x == ' ' || x == '+' || x == '(' || x == ')');
        }

        // 指定されたディレクトリ名が "YYMMDD_なんとか" または "YYMMDD なんとか" または "YYYYMMDD_なんとか" または "YYYYMMDD なんとか" である場合は日付を返す
        static readonly char[] YymmddSplitChars = new char[] { '_', ' ', '　', '\t' };
        public static bool TryParseYYMMDDDirName(string name, out DateTime date)
        {
            date = default;

            name = name._TrimNonNull();

            int r = name.IndexOfAny(YymmddSplitChars);
            if ((r == 6 || r == 8) && name.Substring(0, r).All(x => x >= '0' && x <= '9'))
            {
                name = name.Substring(0, r);
            }
            else
            {
                if (name.Length >= 7 && name.Substring(0, 6).All(x => x >= '0' && x <= '9') && (!(name[6] >= '0' && name[6] <= '9')))
                {
                    name = name.Substring(0, 6);
                }
                else if (name.Length >= 9 && name.Substring(0, 8).All(x => x >= '0' && x <= '9') && (!(name[8] >= '0' && name[8] <= '9')))
                {
                    name = name.Substring(0, 8);
                }
            }

            return TryParseYYMMDD(name, out date);
        }

        // YYMMDD または YYYYMMDD をパースする
        public static bool TryParseYYMMDD(string str, out DateTime date)
        {
            date = default;

            try
            {
                if (str.Length == 6 && str.All(x => x >= '0' && x <= '9'))
                {
                    if (str == "000000")
                    {
                        date = Util.ZeroDateTimeValue;
                        return true;
                    }

                    if (str == "999999")
                    {
                        date = Util.MaxDateTimeValue;
                        return true;
                    }


                    int year = str.Substring(0, 2)._ToInt();
                    int month = str.Substring(2, 2)._ToInt();
                    int day = str.Substring(4, 2)._ToInt();

                    date = new DateTime(year + 2000, month, day);
                    return true;
                }
                else if (str.Length == 8 && str.All(x => x >= '0' && x <= '9'))
                {
                    if (str == "00000000")
                    {
                        date = Util.ZeroDateTimeValue;
                        return true;
                    }

                    if (str == "99999999")
                    {
                        date = Util.MaxDateTimeValue;
                        return true;
                    }

                    int year = str.Substring(0, 4)._ToInt();
                    int month = str.Substring(4, 2)._ToInt();
                    int day = str.Substring(6, 2)._ToInt();

                    date = new DateTime(year, month, day);
                    return true;
                }
            }
            catch { }

            return false;
        }

        // 複数のワイルドカードパターンにある文字列が一致するかどうか検査
        public static bool MultipleWildcardMatch(string targetStr, string multipleWildcardList, string excludeMultipleWildcardList, bool ignoreCase = false)
        {
            targetStr = targetStr._NonNull();
            multipleWildcardList = multipleWildcardList._NonNull();
            excludeMultipleWildcardList = excludeMultipleWildcardList._NonNull();

            var wildcardList = multipleWildcardList._Split(StringSplitOptions.RemoveEmptyEntries, "|", ",", ";", " ").Where(x => x._IsFilled());

            if (wildcardList.Any() == false) return false;

            var excludeList = excludeMultipleWildcardList._Split(StringSplitOptions.RemoveEmptyEntries, "|", ",", ";", " ").Where(x => x._IsFilled());

            foreach (string exclude in excludeList)
            {
                if (WildcardMatch(targetStr, exclude, ignoreCase))
                {
                    return false;
                }
            }

            foreach (string wildcard in wildcardList)
            {
                if (WildcardMatch(targetStr, wildcard, ignoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        public static bool MultipleWildcardMatch(string targetStr, string multipleWildcard, bool ignoreCase = false)
            => MultipleWildcardMatch(targetStr, multipleWildcard, "", ignoreCase);

        // ワイルドカード一致検査
        public static bool WildcardMatch(string targetStr, string wildcard, bool ignoreCase = false)
        {
            if (wildcard._IsEmpty()) return false;

            if (ignoreCase)
            {
                targetStr = targetStr.ToUpperInvariant();
                wildcard = wildcard.ToUpperInvariant();
            }

            try
            {
                string pattern = WildcardToRegex(wildcard);

                if (new Regex(pattern).IsMatch(targetStr))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // ワイルドカード文字列を正規表現に変換
        public static string WildcardToRegex(string wildcard)
        {
            // https://qiita.com/kazuhirox/items/5e314d5e7732041a3fe7 を参考にいたしました

            var regexPattern = System.Text.RegularExpressions.Regex.Replace(wildcard, ".",
              m =>
              {
                  string s = m.Value;
                  if (s.Equals("?"))
                  {
                      return ".";
                  }
                  else if (s.Equals("*"))
                  {
                      return ".*";
                  }
                  else
                  {
                      return System.Text.RegularExpressions.Regex.Escape(s);
                  }
              }
              );

            return "^" + regexPattern + "$";
        }

        // ランダム MAC アドレスを生成
        public static string GenRandomMacStr(string seedStr, byte firstByte = 0xAE, string paddingStr = "-", bool lowerStr = false)
        {
            return MacToStr(IPUtil.GenRandomMac(seedStr, firstByte).Span, paddingStr, lowerStr);
        }
        public static string GenRandomMacStr(string seedStr, MacAddressStyle style, byte firstByte = 0xAE)
        {
            return MacToStr(IPUtil.GenRandomMac(seedStr, firstByte).Span, style);
        }

        // MAC アドレスをパース
        public static byte[] StrToMac(string src)
        {
            byte[] ret = Str.HexToByte(src);
            if (ret.Length != 6)
            {
                return new byte[6];
            }
            return ret;
        }

        // MAC アドレスを文字列に変換
        public static string MacToStr(ReadOnlySpan<byte> mac, string paddingStr = "-", bool lowerStr = false)
        {
            if (mac.Length > 6)
            {
                mac = mac.Slice(0, 6);
            }
            else if (mac.Length < 6)
            {
                Span<byte> mac2 = new byte[6];
                mac.CopyTo(mac2);
                mac = mac2;
            }

            string str = Str.ByteToHex(mac, paddingStr);

            if (lowerStr) str = str.ToLowerInvariant();

            return str;
        }
        public static string MacToStr(ReadOnlySpan<byte> mac, MacAddressStyle style)
        {
            switch (style)
            {
                case MacAddressStyle.Linux:
                    return MacToStr(mac, ":", true);

                default:
                    return MacToStr(mac, "-", false);
            }
        }

        // MAC アドレスを正規化
        public static string NormalizeMac(string src, string paddingStr = "-", bool lowerStr = false)
        {
            byte[] data = Str.StrToMac(src);
            return Str.MacToStr(data, paddingStr, lowerStr);
        }
        public static string NormalizeMac(string src, MacAddressStyle style)
        {
            byte[] data = Str.StrToMac(src);
            return Str.MacToStr(data, style);
        }

        // 16 進数文字列を正規化
        public static string NormalizeHexString(string? src, bool lowerCase = false, string padding = "")
        {
            src = src._NonNullTrimSe();
            byte[] data = src._GetHexBytes();
            string ret = data._GetHexString(padding);

            if (lowerCase)
                ret = ret.ToLowerInvariant();

            return ret;
        }

        // エラー文字列のシリアライズ
        public static string SerializeErrorStr(string code, string lang, string msg)
        {
            Str.NormalizeString(ref code);
            Str.NormalizeString(ref lang);
            Str.NormalizeString(ref msg);

            code = Str.ReplaceStr(code, ",", ".");
            code = Str.ReplaceStr(code, "{", "[");
            code = Str.ReplaceStr(code, "}", "]");

            lang = Str.ReplaceStr(lang, ",", ".");
            lang = Str.ReplaceStr(lang, "{", "[");
            lang = Str.ReplaceStr(lang, "}", "]");

            msg = Str.ReplaceStr(msg, "{", "[");
            msg = Str.ReplaceStr(msg, "}", "]");

            return "{ErrorCode=" + code + "," + lang + "," + msg + "}";
        }

        // シリアライズされたエラー文字列の展開
        public static SerializedError[] DeserializeErrorStr(string str)
        {
            List<SerializedError> ret = new List<SerializedError>();
            int pos = 0;
            while (true)
            {
                int i = Str.SearchStr(str, "{ErrorCode=", pos);
                if (i == -1)
                {
                    break;
                }

                pos = i + 11;

                // code の取得
                i = Str.SearchStr(str, ",", pos);
                if (i == -1)
                {
                    break;
                }

                string code = str.Substring(pos, (i - pos));
                pos = i + 1;

                // lang の取得
                i = Str.SearchStr(str, ",", pos);
                if (i == -1)
                {
                    break;
                }

                string lang = str.Substring(pos, (i - pos));
                pos = i + 1;

                // msg の取得
                i = Str.SearchStr(str, "}", pos);
                if (i == -1)
                {
                    break;
                }

                string msg = str.Substring(pos, (i - pos));
                pos = i + 1;

                Str.NormalizeString(ref code);
                Str.NormalizeString(ref lang);
                Str.NormalizeString(ref msg);

                SerializedError r = new SerializedError()
                {
                    Code = code,
                    ErrorMsg = msg,
                    Language = lang,
                };

                ret.Add(r);
            }

            return ret.ToArray();
        }

        // 新しい ID を生成する
        public static string NewFullId(string prefix)
        {
            // ID-AAAAAAAAAA-BBB-CCDDDDDEEEEEEE-<ID>-FFFFF-GGGGG

            // AAAAAAAAAA: 10 桁の整数 2000/01/01 からの秒数
            // BBB: 3 桁の整数 2000/01/01 からのミリ秒数
            // DDDDD および EEEEEEE: 乱数
            // FFFFF: 2015/08/10 からの秒数を 9600 で割った値
            // GGGGG: 乱数
            // CC: チェックサム (AAAAAAAAAABBBDDDDDEEEEEFFFFFGGGGG<ID> のハッシュ)
            prefix = prefix._NonNullTrim();

            DateTime start_dt = new DateTime(2000, 1, 1);
            DateTime now = DateTime.Now;

            ulong msecs = (ulong)Util.ConvertTimeSpan(now - start_dt);

            lock (LockNewId)
            {
                if (LastNewIdMSecs >= msecs)
                {
                    msecs = LastNewIdMSecs + 1;
                }

                LastNewIdMSecs = msecs;
            }

            string a = ((msecs / 1000UL) % 10000000000UL).ToString("D10");
            string b = (msecs % 1000UL).ToString("D3");
            string d = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string e = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string f = (((ulong)((now - new DateTime(2015, 8, 10)).TotalSeconds / 9600.0)) % 100000UL).ToString("D5");
            string g = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string hash_str = a + b + d + e + f + g + prefix.ToUpperInvariant();
            byte[] hash = Secure.HashSHA1(Str.AsciiEncoding.GetBytes(hash_str));
            Buf buf = new Buf(hash);
            string c = (buf.ReadInt64() % 100000UL).ToString("D5");

            return "ID-" + a + "-" + b + "-" + c + d + e + "-" + prefix.ToUpperInvariant() + "-" + f + "-" + g;
        }

        // UID を正規化する
        public static string NormalizeUid(string? uid, int maxStrLength = Consts.Numbers.MaxKeyOrLabelStrLength)
        {
            string ret = uid._NonNullTrim().ToUpperInvariant();

            if (maxStrLength != int.MaxValue && maxStrLength < 0)
            {
                ret._CheckSqlMaxSafeStrLength(maxStrLength: maxStrLength);
            }

            ret._CheckAsciiPrintableOneLine();

            return ret;
        }

        // 任意のキーを正規化する
        public static string NormalizeKey(string? key, bool checkAsciiSafe = false, int maxStrLength = Consts.Numbers.MaxKeyOrLabelStrLength)
        {
            string ret = key._NonNullTrim().ToUpperInvariant();

            if (maxStrLength != int.MaxValue && maxStrLength < 0)
            {
                ret._CheckSqlMaxSafeStrLength(maxStrLength: maxStrLength);
            }

            if (checkAsciiSafe)
            {
                ret._CheckAsciiPrintableOneLine();
            }

            return ret;
        }

        // 新しい UID を生成する
        public static string NewUid(string prefix = "UID", char concat = '-', bool prependAtoZHashChar = false)
        {
            // <PREFIX>-AAAAAAAAAA-BBB-CCDDDDDEEEEEEE-FFFFF-GGGGG

            // AAAAAAAAAA: 10 桁の整数 2000/01/01 からの秒数
            // BBB: 3 桁の整数 2000/01/01 からのミリ秒数
            // DDDDD および EEEEEEE: 乱数
            // FFFFF: 2015/08/10 からの秒数を 9600 で割った値
            // GGGGG: 乱数
            // CC: チェックサム (AAAAAAAAAABBBDDDDDEEEEEFFFFFGGGGG<ID> のハッシュ)
            prefix = prefix._NonNullTrim();

            if (prefix._IsNullOrZeroLen()) prefix = "UID";

            DateTime start_dt = new DateTime(2000, 1, 1);
            DateTime now = DateTime.Now;

            ulong msecs = (ulong)Util.ConvertTimeSpan(now - start_dt);

            lock (LockNewId)
            {
                if (LastNewIdMSecs >= msecs)
                {
                    msecs = LastNewIdMSecs + 1;
                }

                LastNewIdMSecs = msecs;
            }

            string a = ((msecs / 1000UL) % 10000000000UL).ToString("D10");
            string b = (msecs % 1000UL).ToString("D3");
            string d = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string e = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string f = (((ulong)((now - new DateTime(2015, 8, 10)).TotalSeconds / 9600.0)) % 100000UL).ToString("D5");
            string g = (Secure.RandUInt64() % 100000UL).ToString("D5");
            string hash_str = a + b + d + e + f + g + prefix.ToUpperInvariant();
            byte[] hash = Secure.HashSHA1(Str.AsciiEncoding.GetBytes(hash_str));
            Buf buf = new Buf(hash);
            string c = (buf.ReadInt64() % 100000UL).ToString("D5");

            if (prependAtoZHashChar)
            {
                prefix = "" + (char)('A' + (Util.RandSInt31() % 26)) + concat + prefix;
            }

            return prefix.ToUpperInvariant() + concat + a + concat + b + concat + c + d + e + concat + f + concat + g;
        }
        // ID 文字列を短縮する
        public static string? GetShortId(string fullId)
        {
            Str.NormalizeString(ref fullId);
            fullId = fullId.ToUpperInvariant();

            if (fullId.StartsWith("ID-") == false)
            {
                return null;
            }

            string[] tokens = fullId.Split('-');
            if (tokens.Length != 7)
            {
                return null;
            }

            return tokens[4] + "-" + tokens[5] + "-" + tokens[6];
        }

        // バイト配列をバイナリ文字列に変換する
        public static string ByteToBinaryString(byte[] data)
        {
            StringBuilder sb = new StringBuilder();

            foreach (byte b in data)
            {
                string str = Convert.ToString((uint)b, 2);

                if (str.Length <= 7)
                {
                    str = Str.MakeCharArray('0', 8 - str.Length) + str;
                }

                sb.Append(str);
            }

            return sb.ToString();
        }

        // バイナリ文字列をバイト配列に変換する
        //public static string BinaryString

        // 文字列をソフトイーサ表記規則に正規化する
        public static string NormalizeStrSoftEther(string? str, bool trim = false)
        {
            bool b = false;
            str = str._NonNull();
            StringReader sr = new StringReader(str);
            StringWriter sw = new StringWriter();
            while (true)
            {
                string? line = sr.ReadLine();
                if (line == null)
                {
                    break;
                }
                if (b)
                {
                    sw.WriteLine();
                }
                b = true;
                line = normalizeStrSoftEtherInternal(line);
                sw.Write(line);
            }

            int len = str.Length;

            try
            {
                if (str[len - 1] == '\n' || str[len - 1] == '\r')
                {
                    sw.WriteLine();
                }
            }
            catch
            {
            }

            str = sw.ToString();

            if (trim)
            {
                str = str.Trim();
            }

            return str;
        }
        static string normalizeStrSoftEtherInternal(string str)
        {
            if (str.Trim().Length == 0)
            {
                return "";
            }

            int i;
            StringBuilder sb1 = new StringBuilder();
            for (i = 0; i < str.Length; i++)
            {
                char c = str[i];

                if (c == ' ' || c == '　' || c == '\t')
                {
                    sb1.Append(c);
                }
                else
                {
                    break;
                }
            }
            string str2 = str.Substring(i).Trim();

            string str1 = sb1.ToString();

            str1 = ReplaceStr(str1, "　", "  ");
            str1 = ReplaceStr(str1, "\t", "    ");

            return str1 + normalizeStrSoftEtherInternal2(str2);
        }
        static string normalizeStrSoftEtherInternal2(string str)
        {
            NormalizeString(ref str, true, true, false, true);
            char[] chars = str.ToCharArray();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool insert_space = false;
                bool insert_space2 = false;

                char c1 = (char)0;
                if (i >= 1)
                {
                    c1 = chars[i - 1];
                }

                char c2 = (char)0;
                if (i < (chars.Length - 1))
                {
                    c2 = chars[i + 1];
                }

                if (c == '\'' || c1 == '\'' || c2 == '\'' || c == '\"' || c1 == '\"' || c2 == '\"' || c == '>' || c1 == '>' || c2 == '>' || c == '<' || c1 == '<' || c2 == '<')
                {
                }
                else if (c == '(' || c == '[' || c == '{' || c == '<')
                {
                    // 括弧開始
                    if (c1 != '「' && c1 != '『' && c1 != '。' && c1 != '、' && c1 != '・')
                    {
                        insert_space = true;
                    }
                }
                else if (c == ')' || c == ']' || c == '}' || c == '>')
                {
                    // 括弧終了
                    if (c2 != '.' && c2 != ',' && c2 != '。' && c2 != '、')
                    {
                        insert_space2 = true;
                    }
                }
                else if (c == '～')
                {
                    if (c1 != '～')
                    {
                        insert_space = true;
                    }

                    if (c2 != '～')
                    {
                        insert_space2 = true;
                    }
                }
                else if (IsZenkaku(c) == false)
                {
                    // 半角
                    if (IsZenkaku(c1))
                    {
                        // 前の文字が全角
                        if (c != '.' && c != ',' && c != ';' && c != ':' && c1 != '※' && c1 != '〒' && c1 != '℡' && c1 != '「' && c1 != '『' && c1 != '。' && c1 != '、' && c1 != '・')
                        {
                            insert_space = true;
                        }
                    }
                }
                else
                {
                    // 全角
                    if (IsZenkaku(c1) == false)
                    {
                        // 前の文字が半角
                        if (c != '。' && c != '、' && c != '」' && c != '』' && c != '・' && c1 != '(' && c1 != '[' && c1 != '{' && c1 != '<' && c1 != ';' && c1 != ':')
                        {
                            insert_space = true;
                        }
                    }
                }

                if (insert_space)
                {
                    sb.Append(' ');
                }

                sb.Append(c);

                if (insert_space2)
                {
                    sb.Append(' ');
                }
            }

            str = sb.ToString();

            NormalizeString(ref str, true, true, false, true);

            return str;
        }

        // 指定した文字列を Git Commit ID として正規化する
        public static string NormalizeGitCommitId(string? src)
        {
            if (src._IsEmpty()) return "";

            if (TryNormalizeGitCommitId(src, out string dst))
            {
                return dst;
            }

            return "";
        }
        public static bool TryNormalizeGitCommitId(string? src, out string dst)
        {
            dst = "";

            if (src._IsEmpty()) return false;

            src = src._NonNullTrimSe();

            byte[] data = src._GetHexBytes();

            if (data.Length != 20)
                return false;

            dst = data._GetHexString().ToLowerInvariant();

            return true;
        }

        // 指定した文字が全角かどうか調べる
        public static bool IsZenkaku(char c)
        {
            return !((c >= (char)0) && (c <= (char)256));
        }

        // 文字列を可能性がある複数のトークンで分割する
        public static string[] DivideStringMulti(string str, bool caseSensitive, params string[] keywords)
        {
            List<string> ret = new List<string>();
            int next = 0;

            while (true)
            {
                int foundIndex;
                string foundKeyword;
                int r = Str.SearchStrMulti(str, next, caseSensitive, out foundIndex, out foundKeyword, keywords);
                if (r == -1)
                {
                    ret.Add(str.Substring(next));
                    break;
                }
                else
                {
                    ret.Add(str.Substring(next, r - next));
                    ret.Add(foundKeyword);
                    next = r + foundKeyword.Length;
                }
            }

            return ret.ToArray();
        }

        // 指定した文字列が指定したエンコードで表現できるかどうか検査
        public static bool IsSuitableEncodingForString(string str, Encoding enc)
        {
            try
            {
                str = Str.NormalizeCrlf(str, CrlfStyle.CrLf);

                byte[] utf1 = Str.Utf8Encoding.GetBytes(str);

                byte[] b = enc.GetBytes(str);
                string str2 = enc.GetString(b);

                byte[] utf2 = Str.Utf8Encoding.GetBytes(str2);

                return Util.MemEquals(utf1, utf2);
            }
            catch
            {
                return false;
            }
        }

        // 文字が数字とアルファベットかどうか
        public static bool IsCharNumOrAlpha(char c)
        {
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            return false;
        }
        public static bool IsStringNumOrAlpha(string s)
        {
            foreach (char c in s)
            {
                if (IsCharNumOrAlpha(c) == false)
                {
                    return false;
                }
            }
            return true;
        }

        // 文字列を項目リストに分割する
        public static string[] StrToStrLineBySplitting(string str)
        {
            StringReader r = new StringReader(str);
            List<string> ret = new List<string>();

            while (true)
            {
                string? line = r.ReadLine();
                if (line == null)
                {
                    break;
                }

                if (IsEmptyStr(line) == false)
                {
                    ret.Add(line.Trim());
                }
            }

            return ret.ToArray();
        }

        // 前方から指定文字だけ取得する
        public static string GetLeft(string str, int len)
        {
            str = str._NonNull();

            if (str.Length > len)
            {
                return str.Substring(0, len);
            }
            else
            {
                return str;
            }
        }

        // 文字列を検索用にパースする
        public static string[] SplitStringForSearch(string str)
        {
            bool b = false;
            int i, len;
            len = str.Length;
            List<string> ret = new List<string>();
            string currentStr = "";

            for (i = 0; i < len; i++)
            {
                char c = str[i];

                if (c == '\"')
                {
                    b = !b;
                    if (b == false)
                    {
                        // " の終了
                        currentStr = currentStr.Trim();
                        if (Str.IsEmptyStr(currentStr) == false)
                        {
                            ret.Add(currentStr);
                            currentStr = "";
                        }
                    }
                }
                else
                {
                    if (b == false && (c == ' ' || c == '　' || c == '\t'))
                    {
                        // 区切り文字
                        currentStr = currentStr.Trim();
                        if (Str.IsEmptyStr(currentStr) == false)
                        {
                            ret.Add(currentStr);
                            currentStr = "";
                        }
                    }
                    else
                    {
                        currentStr += c;
                    }
                }
            }

            currentStr = currentStr.Trim();
            if (Str.IsEmptyStr(currentStr) == false)
            {
                ret.Add(currentStr);
            }

            return ret.ToArray();
        }

        // 数値の桁数が足りない場合にゼロを付ける
        public static string AppendZeroToNumString(string str, int numKeta)
        {
            int n = numKeta - str.Length;

            if (n >= 1)
            {
                return MakeCharArray('0', n) + str;
            }
            else
            {
                return str;
            }
        }

        // 指定したデータに BOM が付いているかどうか判別する
        [MethodImpl(Inline)]
        public static Encoding? CheckBOM(ReadOnlySpan<byte> data)
        {
            return CheckBOM(data, out _);
        }
        [MethodImpl(Inline)]
        public static Encoding? CheckBOM(ReadOnlySpan<byte> data, out int bomNumBytes)
        {
            bomNumBytes = 0;
            try
            {
                if (data.Length >= 3 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xfe && data[3] == 0xff)
                {
                    bomNumBytes = 3;
                    return Encoding.GetEncoding("utf-32BE");
                }
                else if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xfe && data[2] == 0x00 && data[3] == 0x00)
                {
                    bomNumBytes = 4;
                    return Encoding.GetEncoding("utf-32");
                }
                else if (data.Length >= 2 && data[0] == 0xff && data[1] == 0xfe)
                {
                    bomNumBytes = 2;
                    return Encoding.GetEncoding("utf-16");
                }
                else if (data.Length >= 2 && data[0] == 0xfe && data[1] == 0xff)
                {
                    bomNumBytes = 2;
                    return Encoding.GetEncoding("utf-16BE");
                }
                else if (data.Length >= 3 && data[0] == 0xef && data[1] == 0xbb && data[2] == 0xbf)
                {
                    bomNumBytes = 3;
                    return Encoding.GetEncoding("utf-8");
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        // Encoding の種類に応じた適切な BOM を取得する
        public static readonly ReadOnlyMemory<byte> BOM_UTF_32BE = new byte[] { 0x00, 0x00, 0xfe, 0xff };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_32 = new byte[] { 0xff, 0xfe, 0x00, 0x00 };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_16BE = new byte[] { 0xfe, 0xff };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_16 = new byte[] { 0xff, 0xfe };
        public static readonly ReadOnlyMemory<byte> BOM_UTF_8 = new byte[] { 0xef, 0xbb, 0xbf };

        public static ReadOnlySpan<byte> GetBOMSpan(Encoding encoding)
        {
            string name = "";
            try
            {
                name = encoding.BodyName;
            }
            catch
            {
                name = encoding.WebName;
            }

            if (Str.StrCmpi(name, "utf-32BE"))
                return BOM_UTF_32BE.Span;
            else if (Str.StrCmpi(name, "utf-32"))
                return BOM_UTF_32.Span;
            else if (Str.StrCmpi(name, "utf-16"))
                return BOM_UTF_16.Span;
            else if (Str.StrCmpi(name, "utf-16BE") || Str.StrCmpi(name, "unicodeFFFE"))
                return BOM_UTF_16BE.Span;
            else if (Str.StrCmpi(name, "utf-8"))
                return BOM_UTF_8.Span;

            return null;
        }

        public static byte[]? GetBOM(Encoding encoding)
        {
            var span = GetBOMSpan(encoding);

            if (span.IsEmpty)
                return null;

            return span.ToArray();
        }

        // テキストファイルを強制的に指定したエンコーディングにエンコードする
        public static byte[] ConvertEncoding(byte[] srcData, Encoding destEncoding)
        {
            return ConvertEncoding(srcData, destEncoding, false);
        }
        public static byte[] ConvertEncoding(byte[] srcData, Encoding destEncoding, bool appendBom)
        {
            Encoding? srcEncoding = GetEncoding(srcData);
            if (srcEncoding == null)
            {
                srcEncoding = Str.ShiftJisEncoding;
            }

            int nb;
            if (CheckBOM(srcData, out nb) != null)
            {
                srcData = Util.RemoveStartByteArray(srcData, nb);
            }

            string str = srcEncoding.GetString(srcData);

            byte[]? b1 = null;
            if (appendBom)
            {
                b1 = GetBOM(destEncoding);
            }
            byte[] b2 = destEncoding.GetBytes(str);

            return Util.CombineByteArray(b1, b2);
        }

        // テキストファイルを読み込む
        public static string ReadTextFile(string filename)
        {
            byte[] data = IO.ReadFile(filename);
            int bomSize = 0;

            Encoding? enc = GetEncoding(data, out bomSize);
            if (enc == null)
            {
                enc = Str.Utf8Encoding;
            }
            if (bomSize >= 1)
            {
                data = Util.CopyByte(data, bomSize);
            }

            return enc.GetString(data);
        }
        public static string ReadTextFile(string filename, Encoding encoding)
        {
            byte[] data = IO.ReadFile(filename);

            Encoding enc = encoding;

            return enc.GetString(data);
        }


        // テキストファイルに書き込む
        public static void WriteTextFile(string filename, string contents, Encoding encoding, bool writeBom)
        {
            Buf buf = new Buf();
            byte[]? bom = GetBOM(encoding);
            if (writeBom && bom != null && bom.Length >= 1)
            {
                buf.Write(bom);
            }
            buf.Write(encoding.GetBytes(contents));

            buf.SeekToBegin();

            IO.SaveFile(filename, buf.Read());
        }

        // 受信した byte[] 配列を自動的にエンコーディング検出して string に変換する
        public static string DecodeStringAutoDetect(ReadOnlySpan<byte> data, out Encoding detectedEncoding, bool untilNullByte = false)
        {
            int bomSize;

            if (untilNullByte) data = data._UntilNullByte();

            detectedEncoding = Str.GetEncoding(data, out bomSize)!;
            if (detectedEncoding == null)
                detectedEncoding = Encoding.UTF8;

            data = data.Slice(bomSize);

            return detectedEncoding.GetString(data);
        }

        // 文字列をデコードする (BOM があれば BOM に従う)
        [MethodImpl(Inline)]
        public static string DecodeString(ReadOnlySpan<byte> data, Encoding defaultEncoding, out Encoding detectedEncoding, bool untilNullByte = false)
        {
            int bomSize;

            detectedEncoding = CheckBOM(data, out bomSize)!;
            if (detectedEncoding == null)
                detectedEncoding = defaultEncoding;

            data = data.Slice(bomSize);

            if (untilNullByte) data = data._UntilNullByte();

            return detectedEncoding.GetString(data);
        }

        // テキストファイルのエンコーディングを取得する
        public static Encoding? GetEncoding(ReadOnlySpan<byte> data)
        {
            int i;
            return GetEncoding(data, out i);
        }
        public static Encoding? GetEncoding(ReadOnlySpan<byte> data, out int bomSize)
        {
            const byte bESC = 0x1B;
            const byte bAT = 0x40;
            const byte bDollar = 0x24;
            const byte bAnd = 0x26;
            const byte bOP = 0x28;
            const byte bB = 0x42;
            const byte bD = 0x44;
            const byte bJ = 0x4A;
            const byte bI = 0x49;
            bomSize = 0;

            int len = data.Length;
            int binary = 0;
            int ucs2 = 0;
            int sjis = 0;
            int euc = 0;
            int utf8 = 0;
            byte b1, b2;

            Encoding? bomEncoding = CheckBOM(data, out bomSize);
            if (bomEncoding != null)
            {
                return bomEncoding;
            }

            for (int i = 0; i < len; i++)
            {
                if (data[i] <= 0x06 || data[i] == 0x7F || data[i] == 0xFF)
                {
                    //'binary'
                    binary++;
                    if (len - 1 > i && data[i] == 0x00
                        && i > 0 && data[i - 1] <= 0x7F)
                    {
                        //smells like raw unicode
                        ucs2++;
                    }
                }
            }


            if (binary > 0)
            {
                if (ucs2 > 0)
                {
                    //JIS
                    //ucs2(Unicode)

                    int n1 = 0, n2 = 0;
                    for (int i = 0; i < (len / 2); i++)
                    {
                        byte e1 = data[i * 2];
                        byte e2 = data[i * 2 + 1];

                        if (e1 == 0 && e2 != 0)
                        {
                            n1++;
                        }
                        else if (e1 != 0 && e2 == 0)
                        {
                            n2++;
                        }
                    }

                    if (n1 > n2)
                    {
                        return Encoding.GetEncoding("utf-16BE");
                    }
                    else
                    {
                        return System.Text.Encoding.Unicode;
                    }
                }
                else
                {
                    //binary
                    return null;
                }
            }

            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];

                if (b1 == bESC)
                {
                    if (b2 >= 0x80)
                        //not Japanese
                        //ASCII
                        return Str.AsciiEncoding;
                    else if (len - 2 > i &&
                        b2 == bDollar && data[i + 2] == bAT)
                        //JIS_0208 1978
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 2 > i &&
                        b2 == bDollar && data[i + 2] == bB)
                        //JIS_0208 1983
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 5 > i &&
                        b2 == bAnd && data[i + 2] == bAT && data[i + 3] == bESC &&
                        data[i + 4] == bDollar && data[i + 5] == bB)
                        //JIS_0208 1990
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 3 > i &&
                        b2 == bDollar && data[i + 2] == bOP && data[i + 3] == bD)
                        //JIS_0212
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 2 > i &&
                        b2 == bOP && (data[i + 2] == bB || data[i + 2] == bJ))
                        //JIS_ASC
                        //JIS
                        return Str.ISO2022JPEncoding;
                    else if (len - 2 > i &&
                        b2 == bOP && data[i + 2] == bI)
                        //JIS_KANA
                        //JIS
                        return Str.ISO2022JPEncoding;
                }
            }

            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if (((b1 >= 0x81 && b1 <= 0x9F) || (b1 >= 0xE0 && b1 <= 0xFC)) &&
                    ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC)))
                {
                    sjis += 2;
                    i++;
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if (((b1 >= 0xA1 && b1 <= 0xFE) && (b2 >= 0xA1 && b2 <= 0xFE)) ||
                    (b1 == 0x8E && (b2 >= 0xA1 && b2 <= 0xDF)))
                {
                    euc += 2;
                    i++;
                }
                else if (len - 2 > i &&
                    b1 == 0x8F && (b2 >= 0xA1 && b2 <= 0xFE) &&
                    (data[i + 2] >= 0xA1 && data[i + 2] <= 0xFE))
                {
                    euc += 3;
                    i += 2;
                }
            }
            for (int i = 0; i < len - 1; i++)
            {
                b1 = data[i];
                b2 = data[i + 1];
                if ((b1 >= 0xC0 && b1 <= 0xDF) && (b2 >= 0x80 && b2 <= 0xBF))
                {
                    utf8 += 2;
                    i++;
                }
                else if (len - 2 > i &&
                    (b1 >= 0xE0 && b1 <= 0xEF) && (b2 >= 0x80 && b2 <= 0xBF) &&
                    (data[i + 2] >= 0x80 && data[i + 2] <= 0xBF))
                {
                    utf8 += 3;
                    i += 2;
                }
            }

            if (euc > sjis && euc > utf8)
                //EUC
                return Str.EucJpEncoding;
            else if (sjis > euc && sjis > utf8)
                //SJIS
                return Str.ShiftJisEncoding;
            else if (utf8 > euc && utf8 > sjis)
                //UTF8
                return Str.Utf8Encoding;

            return null;
        }

        // いずれかの文字が最初に一致するかどうか
        public static bool StartsWithMulti(string str, StringComparison comparison, params string[] keys)
        {
            NormalizeString(ref str);

            foreach (string key in keys)
            {
                if (str.StartsWith(key, comparison))
                {
                    return true;
                }
            }

            return false;
        }

        // メールアドレスに使用可能な文字かどうか取得
        public static bool IsCharForMail(char c)
        {
            switch (c)
            {
                case '<':
                case '>':
                case ' ':
                case ';':
                case ':':
                case '/':
                case '(':
                case ')':
                case '&':
                case ',':
                case '%':
                case '$':
                case '#':
                case '\"':
                case '\'':
                case '!':
                case '=':
                case '\\':
                    return false;
            }

            if (c >= 0x80)
            {
                return false;
            }

            if (IsAscii(c) == false)
            {
                return false;
            }

            return true;
        }

        // メールのリンクになっているような部分をリンクする
        public static string LinkMailtoOnText(string text)
        {
            NormalizeString(ref text);

            StringBuilder sb = new StringBuilder();

            string tmp = "";

            int i;
            for (i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (IsCharForMail(c) == false)
                {
                    if (Str.CheckMailAddress(tmp) == false)
                    {
                        tmp += c;
                        sb.Append(tmp);
                        tmp = "";
                    }
                    else
                    {
                        sb.AppendFormat("<a href=\"mailto:{0}\">{0}</a>", tmp);
                        sb.Append(c);
                        tmp = "";
                    }
                }
                else
                {
                    tmp += c;
                }
            }
            if (Str.CheckMailAddress(tmp) == false)
            {
                sb.Append(tmp);
                tmp = "";
            }
            else
            {
                sb.AppendFormat("<a href=\"mailto:{0}\">{0}</a>", tmp);
                tmp = "";
            }

            return sb.ToString();
        }

        // URL のリンクになっているような部分をリンクする
        public static string LinkUrlOnText(string text, string target = "")
        {
            int findStart = 0;

            NormalizeString(ref text);
            NormalizeString(ref target);

            StringBuilder sb = new StringBuilder();

            while (true)
            {
                int foundStrIndex;
                int foundIndex = FindStrings(text, findStart, StringComparison.OrdinalIgnoreCase, out foundStrIndex,
                    "http://", "https://", "ftp://", "telnet://", "mailto://", "news://");

                // URL の末尾まで検索
                if (foundIndex != -1)
                {
                    int i;
                    int endOfUrl = -1;
                    for (i = foundIndex; i < text.Length; i++)
                    {
                        char c = text[i];

                        if (IsValidForUrl(c) == false)
                        {
                            endOfUrl = i;
                            break;
                        }

                        if (c == '<' || c == '&')
                        {
                            if (StartsWithMulti(text.Substring(i), StringComparison.OrdinalIgnoreCase,
                                HtmlSpacing, HtmlCrlf, HtmlBr, HtmlLt, HtmlGt))
                            {
                                endOfUrl = i;
                                break;
                            }
                        }
                    }

                    if (endOfUrl == -1)
                    {
                        endOfUrl = text.Length;
                    }

                    // URL を抽出
                    string url = text.Substring(foundIndex, endOfUrl - foundIndex);
                    string beforeUrl = text.Substring(findStart, foundIndex - findStart);

                    sb.Append(beforeUrl);

                    if (Str.IsEmptyStr(target) == false)
                    {
                        sb.AppendFormat("<a href=\"{0}\" target=\"{2}\">{1}</a>", url, url, target);
                    }
                    else
                    {
                        sb.AppendFormat("<a href=\"{0}\">{1}</a>", url, url);
                    }

                    findStart = endOfUrl;
                }
                else
                {
                    sb.Append(text.Substring(findStart));

                    break;
                }
            }

            return LinkMailtoOnText(sb.ToString());
        }

        // いずれかの最初に発見される文字列を検索
        public static int FindStrings(string str, int findStartIndex, StringComparison comparison, out int foundKeyIndex, params string[] keys)
        {
            int ret = -1;
            foundKeyIndex = -1;
            int n = 0;

            foreach (string key in keys)
            {
                int i = str.IndexOf(key, findStartIndex, comparison);

                if (i != -1)
                {
                    if (ret == -1)
                    {
                        ret = i;
                        foundKeyIndex = n;
                    }
                    else
                    {
                        if (ret > i)
                        {
                            ret = i;
                            foundKeyIndex = n;
                        }
                    }
                }

                n++;
            }

            return ret;
        }

        public static int FindStrings(string str, int findStartIndex, StringComparison comparison, out string foundString, params string[] keys)
        {
            int r = FindStrings(str, findStartIndex, comparison, out int foundIndex, keys: keys);
            if (r == -1)
            {
                foundString = "";
                return -1;
            }

            foundString = keys[foundIndex];

            return r;
        }

        // URL として使用可能な文字かどうか
        public static bool IsValidForUrl(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            switch (c)
            {
                case '_':
                case '-':
                case '?':
                case '!':
                case '\"':
                case ',':
                case '\'':
                case '/':
                case '\\':
                case '&':
                case ';':
                case '%':
                case '#':
                case '@':
                case '~':
                case ':':
                case '=':
                case '+':
                case '*':
                case '$':
                case '.':
                    return true;
            }

            return false;
        }

        // 文字列リストから指定した文字列を削除する
        public static List<string> RemoteStringFromList(List<string> str, RemoveStringFunction func)
        {
            List<string> ret = new List<string>();

            foreach (string s in str)
            {
                if (func(s) == false)
                {
                    ret.Add(s);
                }
            }

            return ret;
        }

        public const string ConstZenkaku = "｀｛｝０１２３４５６７８９／＊－＋！”＃＄％＆’（）＝￣｜￥［］＠；：＜＞？＿＾　ａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺ‘";
        public const string ConstHankaku = "`{}0123456789/*-+!\"#$%&'()=~|\\[]@;:<>?_^ abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ'";
        public const string ConstKanaZenkaku = "ー「」アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲンァゥェォャュョッィ゛゜";
        public const string ConstKanaHankaku = "ｰ｢｣ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜｦﾝｧｩｪｫｬｭｮｯｨﾞﾟ";
        public const string ConstKanaZenkakuDakuon = "ガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポ";

        // 空白の除去
        public static void RemoveSpace(ref string str)
        {
            NormalizeString(ref str);

            str = str.Replace(" ", "").Replace("　", "").Replace("\t", "");
        }

        // 先頭文字列の除去
        public static bool TrimStartWith(ref string str, string key, StringComparison comparison)
        {
            if (str.StartsWith(key, comparison))
            {
                str = str.Substring(key.Length);
                return true;
            }
            return false;
        }

        // 末尾文字列の除去
        public static bool TrimEndsWith(ref string str, string key, StringComparison comparison)
        {
            if (str.EndsWith(key, comparison))
            {
                str = str.Substring(0, str.Length - key.Length);
                return true;
            }
            return false;
        }

        // 空白の除去
        public static void RemoveSpaceChar([AllowNull] ref string str)
        {
            if (Str.IsEmptyStr(str))
            {
                return;
            }

            StringBuilder sb = new StringBuilder();

            foreach (char c in str)
            {
                if (c == ' ' || c == '\t' || c == '　')
                {
                }
                else
                {
                    sb.Append(c);
                }
            }

            str = sb.ToString();
        }

        // 文字列を正規化する
        public static void NormalizeStringStandard([AllowNull] ref string str)
        {
            NormalizeString(ref str, true, true, false, true);
        }
        public static void NormalizeString([AllowNull] ref string str, bool space, bool toHankaku, bool toZenkaku, bool toZenkakuKana)
        {
            NormalizeString(ref str);

            if (space)
            {
                str = NormalizeSpace(str);
            }

            if (toHankaku)
            {
                str = ZenkakuToHankaku(str);
            }

            if (toZenkaku)
            {
                str = HankakuToZenkaku(str);
            }

            if (toZenkakuKana)
            {
                str = KanaHankakuToZenkaku(str);
            }
        }
        public static string NormalizeString(string src, bool space, bool toHankaku, bool toZenkaku, bool toZenkakuKana)
        {
            NormalizeString(ref src, space, toHankaku, toZenkaku, toZenkakuKana);
            return src;
        }

        // 指定した文字幅に満ちるまでスペースを追加する
        public static string AddSpacePadding(string? srcStr, int totalWidth, bool addOnLeft = false, char spaceChar = ' ')
        {
            srcStr = srcStr._NonNull();

            int spaceCount = 0;

            int strWidth = Str.GetStrWidth(srcStr);
            if (strWidth < totalWidth)
            {
                spaceCount = totalWidth - strWidth;
            }

            if (addOnLeft == false)
            {
                return srcStr + Str.MakeCharArray(spaceChar, spaceCount);
            }
            else
            {
                return Str.MakeCharArray(spaceChar, spaceCount) + srcStr;
            }
        }

        // スペースを正規化する
        public static string NormalizeSpace(string? str)
        {
            NormalizeString(ref str);
            char[] sps =
            {
                ' ', '　', '\t',
            };

            string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);

            return Str.CombineStringArray(tokens, " ");
        }

        // 指定された文字がカタカナかどうか判別する
        public static bool IsKatakanaChar(char c)
        {
            if (ConstKanaHankaku.IndexOf(c) != -1)
            {
                return true;
            }
            if (ConstKanaZenkaku.IndexOf(c) != -1)
            {
                return true;
            }
            if (ConstKanaZenkakuDakuon.IndexOf(c) != -1)
            {
                return true;
            }

            return false;
        }
        public static bool IsKatakanaStr(string str)
        {
            str = str.Trim();

            if (Str.IsEmptyStr(str))
            {
                return false;
            }

            foreach (char c in str)
            {
                if (IsKatakanaChar(c) == false)
                {
                    return false;
                }
            }
            return true;
        }

        // 半角カナを全角カナに変換する
        public static string KanaHankakuToZenkaku(string str)
        {
            NormalizeString(ref str);

            str = str.Replace("ｶﾞ", "ガ");
            str = str.Replace("ｷﾞ", "ギ");
            str = str.Replace("ｸﾞ", "グ");
            str = str.Replace("ｹﾞ", "ゲ");
            str = str.Replace("ｺﾞ", "ゴ");
            str = str.Replace("ｻﾞ", "ザ");
            str = str.Replace("ｼﾞ", "ジ");
            str = str.Replace("ｽﾞ", "ズ");
            str = str.Replace("ｾﾞ", "ゼ");
            str = str.Replace("ｿﾞ", "ゾ");
            str = str.Replace("ﾀﾞ", "ダ");
            str = str.Replace("ﾁﾞ", "ヂ");
            str = str.Replace("ﾂﾞ", "ヅ");
            str = str.Replace("ﾃﾞ", "デ");
            str = str.Replace("ﾄﾞ", "ド");
            str = str.Replace("ﾊﾞ", "バ");
            str = str.Replace("ﾋﾞ", "ビ");
            str = str.Replace("ﾌﾞ", "ブ");
            str = str.Replace("ﾍﾞ", "ベ");
            str = str.Replace("ﾎﾞ", "ボ");
            str = str.Replace("ﾊﾟ", "パ");
            str = str.Replace("ﾋﾟ", "ピ");
            str = str.Replace("ﾌﾟ", "プ");
            str = str.Replace("ﾍﾟ", "ペ");
            str = str.Replace("ﾎﾟ", "ポ");

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstKanaHankaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstKanaZenkaku[j];
                }
            }

            return new string(a);
        }

        // 全角カナを半角カナに変換する
        public static string KanaZenkakuToHankaku(string str)
        {
            NormalizeString(ref str);

            str = str.Replace("ガ", "ｶﾞ");
            str = str.Replace("ギ", "ｷﾞ");
            str = str.Replace("グ", "ｸﾞ");
            str = str.Replace("ゲ", "ｹﾞ");
            str = str.Replace("ゴ", "ｺﾞ");
            str = str.Replace("ザ", "ｻﾞ");
            str = str.Replace("ジ", "ｼﾞ");
            str = str.Replace("ズ", "ｽﾞ");
            str = str.Replace("ゼ", "ｾﾞ");
            str = str.Replace("ゾ", "ｿﾞ");
            str = str.Replace("ダ", "ﾀﾞ");
            str = str.Replace("ヂ", "ﾁﾞ");
            str = str.Replace("ヅ", "ﾂﾞ");
            str = str.Replace("デ", "ﾃﾞ");
            str = str.Replace("ド", "ﾄﾞ");
            str = str.Replace("バ", "ﾊﾞ");
            str = str.Replace("ビ", "ﾋﾞ");
            str = str.Replace("ブ", "ﾌﾞ");
            str = str.Replace("ベ", "ﾍﾞ");
            str = str.Replace("ボ", "ﾎﾞ");
            str = str.Replace("パ", "ﾊﾟ");
            str = str.Replace("ピ", "ﾋﾟ");
            str = str.Replace("プ", "ﾌﾟ");
            str = str.Replace("ペ", "ﾍﾟ");
            str = str.Replace("ポ", "ﾎﾟ");

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstKanaZenkaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstKanaHankaku[j];
                }
            }

            return new string(a);
        }

        // 全角を半角に変換する
        public static string ZenkakuToHankaku(string str)
        {
            NormalizeString(ref str);

            str = ReplaceStr(str, "“", " \"");
            str = ReplaceStr(str, "”", "\" ");
            str = ReplaceStr(str, "‘", "'");
            str = ReplaceStr(str, "’", "'");

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstZenkaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstHankaku[j];
                }
            }

            return new string(a);
        }

        // 半角を全角に変換する
        public static string HankakuToZenkaku(string str)
        {
            NormalizeString(ref str);

            str = KanaHankakuToZenkaku(str);

            char[] a = str.ToCharArray();
            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j = ConstHankaku.IndexOf(a[i]);

                if (j != -1)
                {
                    a[i] = ConstZenkaku[j];
                }
            }

            return new string(a);
        }

        public const string HtmlSpacing = "&nbsp;";
        public const string HtmlCrlf = "<BR>";
        public const string HtmlBr = HtmlCrlf;
        public const string HtmlLt = "&lt;";
        public const string HtmlGt = "&gt;";
        public const string HtmlAmp = "&amp;";
        public const int HtmlNumTabChar = 8;

        public static string HtmlTab
        {
            get
            {
                int i;
                StringBuilder sb = new StringBuilder();
                for (i = 0; i < HtmlNumTabChar; i++)
                {
                    sb.Append(HtmlSpacing);
                }
                return sb.ToString();
            }
        }

        public static string GetHostNameFromFqdn(string fqdn)
        {
            if (fqdn._IsEmpty()) return "";
            int[] dots = fqdn._FindStringIndexes(".", true);
            if (dots.Length == 0)
                return fqdn;

            int i = dots.First();
            return fqdn.Substring(0, i);
        }

        public static string GetDomainFromFqdn(string fqdn)
        {
            if (fqdn._IsEmpty()) return "";
            int[] dots = fqdn._FindStringIndexes(".", true);
            if (dots.Length == 0)
                return "";

            int i = dots.First();
            return fqdn.Substring(i);
        }

        // URL パスエンコード
        public static string EncodeUrlPath(string? str, Encoding? encoding = null)
        {
            if (str == null) str = "";

            List<string> tokenList = new List<string>();

            StringBuilder currentToken = new StringBuilder();

            foreach (char c in str)
            {
                if (PathParser.Windows.PossibleDirectorySeparators.Where(x => x == c).Any())
                {
                    tokenList.Add(EncodeUrl(currentToken.ToString(), encoding));
                    currentToken.Clear();

                    tokenList.Add("/");
                }
                else
                {
                    currentToken.Append(c);
                }
            }

            tokenList.Add(EncodeUrl(currentToken.ToString(), encoding));
            currentToken.Clear();

            return tokenList._Combine();
        }

        // URL パスデコード
        public static string DecodeUrlPath(string? str, Encoding? encoding = null)
        {
            if (str == null) str = "";

            List<string> tokenList = new List<string>();

            StringBuilder currentToken = new StringBuilder();

            foreach (char c in str)
            {
                if (PathParser.Windows.PossibleDirectorySeparators.Where(x => x == c).Any())
                {
                    tokenList.Add(DecodeUrl(currentToken.ToString(), encoding));
                    currentToken.Clear();

                    tokenList.Add("/");
                }
                else
                {
                    currentToken.Append(c);
                }
            }

            tokenList.Add(DecodeUrl(currentToken.ToString(), encoding));
            currentToken.Clear();

            return tokenList._Combine();
        }

        // 任意の文字列を安全にエンコード (JavaScript 用)
        public static string JavaScriptSafeStrEncode(string? str)
        {
            str = str._NonNull();

            return str._EncodeUrl()._GetBytes_Ascii()._Base64Encode();
        }

        // 任意の文字列を安全にデコード (JavaScript 用)
        public static string JavaScriptSafeStrDecode(string? str)
        {
            str = str._NonNull();

            return str._Base64Decode()._GetString_Ascii()._DecodeUrl();
        }

        // URL エンコード
        public static string EncodeUrl(string? str, Encoding? encoding = null, UrlEncodeParam? param = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (str == null) str = "";

            string ignoreCharList = param?.DoNotEncodeCharList._NonNull() ?? "";

            if (ignoreCharList._IsEmpty())
            {
                return Uri.EscapeDataString(str);
            }
            else
            {
                StringBuilder b = new StringBuilder();
                foreach (char c in str)
                {
                    if (ignoreCharList.IndexOf(c) == -1)
                    {
                        b.Append(Uri.EscapeDataString("" + c));
                    }
                    else
                    {
                        b.Append(c);
                    }
                }
                return b.ToString();
            }
        }

        // URL デコード
        public static string DecodeUrl(string? str, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (str == null) str = "";
            return Uri.UnescapeDataString(str);
        }

        // URL デコード (バイト配列に)
        public static byte[] DecodeUrlToBytes(string? str)
        {
            str = str._NonNull();
            return HttpUtility.UrlDecodeToBytes(str);
        }

        // URL デコードが可能であると考えられるエンコーディングの一覧を取得
        public static List<Encoding> GetSuitableUrlDecodeEncodings(string? str)
        {
            byte[] data = DecodeUrlToBytes(str);

            return GetSuitableEncodings(data);
        }

        // URL に含まれる任意のキーワードの抽出
        public static List<string> ExtractKeywordsFromUrl(string url, bool recursive = true)
        {
            if (url.StartsWith("/") == false && url.StartsWith("http://") == false && url.StartsWith("https://") == false && url.StartsWith("ftp://") == false)
            {
                url = "/" + url;
            }

            var encodingCandidates = GetSuitableUrlDecodeEncodings(url);

            HashSet<string> keywords = new HashSet<string>(StrComparer.IgnoreCaseComparer);

            foreach (var enc in encodingCandidates)
            {
                if (url._TryParseUrl(out Uri? uri, out QueryStringList qs, enc))
                {
                    uri._MarkNotNull();

                    // URL パスセグメント
                    uri.Segments._DoForEach(x => keywords.Add(x._DecodeUrl(enc)._NonNullTrim()));

                    // クエリ
                    foreach (var kv in qs)
                    {
                        keywords.Add(kv.Key._NonNullTrim());
                        keywords.Add(kv.Value._NonNullTrim());
                    }
                }
            }

            if (recursive)
            {
                foreach (var keyword in keywords._ToArrayList())
                {
                    // キーワードをもう一重デコードしてみる
                    var encodingCandidates2 = GetSuitableUrlDecodeEncodings(keyword);

                    string url2 = keyword;

                    if (url2.StartsWith("http://") || url2.StartsWith("https://"))
                    {
                        StringBuilder sb = new StringBuilder();
                        bool f = false;
                        for (int i = 0; i < url2.Length; i++)
                        {
                            char c = url2[i];

                            if (c == '?')
                            {
                                f = true;
                                c = '&';
                            }

                            if (f == false)
                            {
                                if (c == '/') c = '&';
                            }

                            sb.Append(c);
                        }

                        url2 = sb.ToString();
                    }

                    foreach (var enc in encodingCandidates2)
                    {
                        QueryStringList qs2 = new QueryStringList(url2);

                        foreach (var kv in qs2)
                        {
                            keywords.Add(kv.Key._NonNullTrim());
                            keywords.Add(kv.Value._NonNullTrim());
                        }
                    }
                }
            }

            return keywords
                .Where(x => x._IsFilled())
                .Where(x => x.Where(c => (c >= 128 || c == '\r' || c == '\n' || c == ' ' || c == '　' || c == '\t')).Any())
                .Where(x => IsPossiblyBase64(x) == false)
                .ToList();
        }

        // 文字列が Base64 っぽいかどうか検出
        public static bool IsPossiblyBase64(string str)
        {
            if (str.Where(c => c >= 0x80).Any()) return false;

            string[] tokens = str._Split(StringSplitOptions.RemoveEmptyEntries, ' ');

            foreach (string token in tokens)
            {
                if (token.Length >= 8)
                {
                    if (token._Split(StringSplitOptions.RemoveEmptyEntries, '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '/').Length >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // HTML のダウンロード用 JavaScript の作成
        public static string GenerateHtmlDownloadJavaScript(ReadOnlySpan<byte> data, string mimeType, string filename, string buttonId = "download", string functionName = "handleDownload")
        {
            // 参考元: https://qiita.com/wadahiro/items/eb50ac6bbe2e18cf8813

            string src = @"
        <script type='text/javascript'>
            function ___FUNCTION___() {
                var data = new Uint8Array([__HEX__]);
                var blob = new Blob([data], { 'type' : '__MIME__' });

                if (window.navigator.msSaveBlob) { 
                    window.navigator.msSaveBlob(blob, '__FILENAME__'); 

                    window.navigator.msSaveOrOpenBlob(blob, '__FILENAME__'); 
                } else {
                    document.getElementById('___ID___').href = window.URL.createObjectURL(blob);
                }
            }
        </script>";


            return src._ReplaceStrWithReplaceClass(new
            {
                __MIME__ = mimeType,
                __FILENAME__ = filename,
                ___ID___ = buttonId,
                ___FUNCTION___ = functionName,
                __HEX__ = Str.ByteToJavaScriptHexString(data),
            });
        }

        // HTML デコード
        public static string DecodeHtml(string? str, bool normalizeMultiSpaces = false)
        {
            str = str._NonNull();

            if (normalizeMultiSpaces)
            {
                string[] strs = str.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                str = strs._Combine(" ").Trim();
            }

            str = Str.ReplaceStr(str, HtmlCrlf, "\r\n", false);

            str = str.Replace(HtmlSpacing, " ");

            str = str.Replace(HtmlLt, "<").Replace(HtmlGt, ">").Replace(HtmlAmp, "&");

            str = NormalizeCrlf(str, CrlfStyle.CrLf);

            return str;
        }

        // HTML エンコード
        public static string EncodeHtml(string? str, bool forceAllSpaceToTag = false, bool spaceIfEmpty = false)
        {
            str = str._NonNull();

            // 改行を正規化
            str = NormalizeCrlf(str, CrlfStyle.CrLf);

            // & を変換
            str = str.Replace("&", HtmlAmp);

            // タグを変換
            str = str.Replace("<", HtmlLt).Replace(">", HtmlGt);

            // スペースを変換
            if (str.IndexOf(' ') != -1)
            {
                if (forceAllSpaceToTag)
                {
                    str = str.Replace(" ", HtmlSpacing);
                }
                else
                {
                    // 連続するスペースのみ変換
                    int i;
                    StringBuilder sb = new StringBuilder();
                    bool flag = false;

                    for (i = 0; i < str.Length; i++)
                    {
                        char c = str[i];

                        if (c == ' ')
                        {
                            if (flag == false)
                            {
                                flag = true;
                                sb.Append(' ');
                            }
                            else
                            {
                                sb.Append(HtmlSpacing);
                            }
                        }
                        else
                        {
                            flag = false;
                            sb.Append(c);
                        }
                    }

                    str = sb.ToString();
                }
            }

            // tab を変換
            str = str.Replace("\t", HtmlTab);

            // 改行コード
            str = str.Replace("\r\n", HtmlCrlf);

            if (spaceIfEmpty)
            {
                if (str._IsEmpty())
                {
                    str = Str.HtmlSpacing;
                }
            }

            return str;
        }

        // HTML Code ブロックエンコード
        public static string EncodeHtmlCodeBlock(string? str)
        {
            str = str._NonNull();

            // 改行を正規化
            str = NormalizeCrlf(str, CrlfStyle.CrLf);

            // & を変換
            str = str.Replace("&", HtmlAmp);

            // タグを変換
            str = str.Replace("<", HtmlLt).Replace(">", HtmlGt);

            return str;
        }

        // 指定した文字が表示可能で安全かどうか
        public static bool IsSafeAndPrintable(char c, bool crlfIsOk = false, bool htmlTagAreNotGood = false)
        {
            try
            {
                if (c == '\t' || c == '　')
                    return true;
                if (crlfIsOk)
                    if (c == '\r' || c == '\n')
                        return true;
                if (htmlTagAreNotGood)
                {
                    if (c == '<' || c == '>')
                        return false;
                    if (c == '＜' || c == '＞')
                        return false;
                }
                if (Char.IsControl(c))
                    return false;
                if (c >= 32 && c <= 126)
                    return true;
                if (Char.IsDigit(c) || Char.IsLetter(c) || Char.IsLetterOrDigit(c) || Char.IsNumber(c) ||
                    Char.IsPunctuation(c) || Char.IsSeparator(c) || Char.IsSymbol(c))
                {
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public static bool IsSafeAndPrintable(string s, bool crlfIsOk = false, bool htmlTagAreNotGood = false)
        {
            try
            {
                foreach (char c in s)
                {
                    if (IsSafeAndPrintable(c, crlfIsOk, htmlTagAreNotGood) == false)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // 指定した文字が表示可能かどうか
        public static bool IsPrintable(char c)
        {
            if (c >= 256)
            {
                return true;
            }

            if (c >= 32 && c <= 126)
            {
                return true;
            }

            return false;
        }
        public static bool IsPrintable(string str)
        {
            foreach (char c in str)
            {
                if (IsPrintable(c) == false)
                {
                    return false;
                }
            }

            return true;
        }

        // 指定した文字が Ascii 文字として 1 行で表示可能か
        public static bool IsAsciiOneLinePrintable(char c)
        {
            if (c >= 32 && c <= 126)
            {
                return true;
            }

            return false;
        }
        public static bool IsAsciiOneLinePrintable(string str)
        {
            foreach (char c in str)
            {
                if (!(c >= 32 && c <= 126))
                {
                    return false;
                }
            }

            return true;
        }

        // Ascii 文字として 1 行で表示可能な文字列に変換する
        public static string MakeAsciiOneLinePrintableStr(string? src, char alternativeChar = ' ')
        {
            src = src._NonNull();
            StringBuilder sb = new StringBuilder();
            foreach (char c in src)
            {
                if (IsAsciiOneLinePrintable(c) == false)
                {
                    sb.Append(alternativeChar);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // Ascii 文字として 1 行で表示可能な文字列に変換する
        public static string MakeAsciiOneLinePrintableStr(ReadOnlySpan<byte> src, char alternativeChar = ' ')
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte c2 in src)
            {
                char c = (char)c2;
                if (IsAsciiOneLinePrintable(c) == false)
                {
                    sb.Append(alternativeChar);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        public static int[] ParsePortsList(string str)
        {
            string[] tokens = str.Split(new char[] { ',', ' ', '　', '\t', '/', ';' }, StringSplitOptions.RemoveEmptyEntries);

            SortedSet<int> ret = new SortedSet<int>();

            foreach (string token in tokens)
            {
                int i = token._ToInt();

                if (i >= 1 && i <= 65535)
                {
                    ret.Add(i);
                }
            }

            return ret.ToArray();
        }

        public static string PortsListToStr(IEnumerable<int> ports)
        {
            SortedSet<int> tmp = new SortedSet<int>();
            ports.Where(x => x >= 1 && x <= 65535)._DoForEach(x => tmp.Add(x));
            return tmp.Select(x => x.ToString())._Combine(",");
        }

        // エスケープエンコード (本来のデータ => C 風の安全なデータ)
        public static string EncodeCEscape(string str)
        {
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < str.Length; i++)
            {
                char c = str[i];

                if (IsPrintable(c) && c != '\\')
                {
                    sb.Append(c);
                }
                else
                {
                    string s = "" + c;
                    switch (c)
                    {
                        case '\r':
                            s = "\\r";
                            break;

                        case '\n':
                            s = "\\n";
                            break;

                        case '\"':
                            s = "\\\"";
                            break;

                        case '\0':
                            s = "\\0";
                            break;

                        case '\t':
                            s = "\\t";
                            break;

                        case '\\':
                            s = "\\\\";
                            break;

                        default:
                            s = "0x" + Convert.ToString((int)c, 16);
                            break;
                    }
                    sb.Append(s);
                }
            }

            return sb.ToString();
        }

        // エスケープデコード (C 風の安全なデータ => 本来のデータ)
        public static string DecodeCEscape(string str)
        {
            StringBuilder sb = new StringBuilder();

            int i, j, hex;
            string padding = "00000000";
            str = str + padding;
            StringBuilder sb2;

            for (i = 0; i < str.Length - padding.Length; i++)
            {
                char c = str[i];
                char d = c;

                if (c == '\\')
                {
                    char c1 = str[i + 1];

                    switch (c1)
                    {
                        case '\'':
                            d = '\'';
                            i++;
                            break;

                        case '?':
                            d = '?';
                            i++;
                            break;

                        case '\\':
                            d = '\\';
                            i++;
                            break;

                        case 't':
                            d = '\t';
                            i++;
                            break;

                        case 'r':
                            d = '\r';
                            i++;
                            break;

                        case 'n':
                            d = '\n';
                            i++;
                            break;

                        case ' ':
                            d = ' ';
                            i++;
                            break;

                        case '　':
                            d = '　';
                            i++;
                            break;

                        case '\t':
                            d = '\t';
                            i++;
                            break;

                        case '0':
                            d = '\0';
                            i++;
                            break;

                        case '\"':
                            d = '\"';
                            break;

                        case 'x':
                            i++;
                            sb2 = new StringBuilder();
                            for (j = 0; j < 4; j++)
                            {
                                char c2 = str[++i];

                                if ((c2 >= '0' && c2 <= '9') || (c2 >= 'a' && c2 <= 'f') || (c2 >= 'A' && c2 <= 'F'))
                                {
                                    sb2.Append(c2);
                                }
                                else
                                {
                                    i--;
                                    break;
                                }
                            }
                            hex = Convert.ToInt32(sb2.ToString(), 16);
                            d = (char)hex;
                            break;

                        default:
                            if (c1 >= '0' && c1 <= '7')
                            {
                                sb2 = new StringBuilder();
                                for (j = 0; j < 3; j++)
                                {
                                    char c2 = str[++i];

                                    if (c2 >= '0' && c2 <= '7')
                                    {
                                        sb2.Append(c2);
                                    }
                                    else
                                    {
                                        i--;
                                        break;
                                    }
                                }
                                hex = Convert.ToInt32(sb2.ToString(), 8);
                                d = (char)hex;
                            }
                            else
                            {
                                d = '\\';
                                i++;
                            }
                            break;
                    }
                }

                if (d != '\0')
                {
                    sb.Append(d);
                }
                else
                {
                    break;
                }
            }

            return sb.ToString();
        }

        // 横幅を取得
        public static int GetStrWidth(string? str)
        {
            if (str._IsNullOrZeroLen()) return 0;

            int ret = 0;
            foreach (char c in str!)
            {
                if (c <= 255)
                {
                    ret++;
                }
                else
                {
                    ret += 2;
                }
            }
            return ret;
        }

        // 末尾の \r \n を削除
        public static string TrimCrlf(string? str)
        {
            return str._NonNull().TrimEnd('\r', '\n');
        }

        // 指定した文字列がすべて大文字かどうかチェックする
        public static bool IsAllUpperStr(string? str)
        {
            int i, len;
            // 引数チェック
            if (str == null)
            {
                return false;
            }

            len = str.Length;

            for (i = 0; i < len; i++)
            {
                char c = str[i];

                if ((c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'Z'))
                {
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        // 文字列配列を文字列リストに変換
        public static List<string> StrArrayToList(IEnumerable<string> strArray, bool removeEmpty = false, bool distinct = false, bool distinctCaseSensitive = false)
        {
            List<string> ret = new List<string>();

            foreach (string s in strArray)
            {
                if (removeEmpty == false || s._IsEmpty() == false)
                {
                    ret.Add(s);
                }
            }

            if (distinct)
            {
                List<string> ret2 = new List<string>();
                HashSet<string> tmp = new HashSet<string>();
                foreach (string s in ret)
                {
                    string t = s;
                    if (distinctCaseSensitive == false) t = s.ToUpperInvariant();
                    if (tmp.Add(t))
                    {
                        ret2.Add(t);
                    }
                }
                ret = ret2;
            }

            return ret;
        }

        // コマンドライン文字列をパースする
        private static string[] __new_ParseCmdLine(string str)
        {
            List<string> o;
            int i, len, mode;
            char c;
            StringBuilder tmp;
            bool ignore_space = false;
            // 引数チェック
            if (str == null)
            {
                // トークン無し
                return new string[0];
            }

            o = new List<string>();
            tmp = new StringBuilder();

            mode = 0;
            len = str.Length;
            for (i = 0; i < len; i++)
            {
                c = str[i];

                switch (mode)
                {
                    case 0:
                        // 次のトークンを発見するモード
                        if (c == ' ' || c == '\t')
                        {
                            // 次の文字へ進める
                        }
                        else
                        {
                            // トークンの開始
                            if (c == '\"')
                            {
                                if (str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp.Append("\"");
                                    i++;
                                }
                                else
                                {
                                    // 1 個の " はスペース無視フラグを有効にする
                                    ignore_space = true;
                                }
                            }
                            else
                            {
                                tmp.Append(c);
                            }
                        }

                        mode = 1;
                        break;

                    case 1:
                        if (ignore_space == false && (c == ' ' || c == '\t'))
                        {
                            // トークンの終了
                            o.Add(tmp.ToString());

                            tmp = new StringBuilder();
                            mode = 0;
                        }
                        else
                        {
                            if (c == '\"')
                            {
                                if (str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp.Append("\"");
                                    i++;
                                }
                                else
                                {
                                    if (ignore_space == false)
                                    {
                                        // 1 個の " はスペース無視フラグを有効にする
                                        ignore_space = true;
                                    }
                                    else
                                    {
                                        // スペース無視フラグを無効にする
                                        ignore_space = false;
                                    }
                                }
                            }
                            else
                            {
                                tmp.Append(c);
                            }
                        }
                        break;

                }
            }

            if (tmp.Length >= 1)
            {
                o.Add(tmp.ToString());
            }

            List<string> ret = new List<string>();
            foreach (string s in o)
            {
                ret.Add(s);
            }

            return ret.ToArray();
        }

        // 文字列の比較関数
        public static int CompareString(string s1, string s2)
        {
            try
            {
                return string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return 0;
            }
        }
        public static int CompareStringCaseSensitive(string s1, string s2)
        {
            try
            {
                return string.Compare(s1, s2, StringComparison.Ordinal);
            }
            catch
            {
                return 0;
            }
        }

        // 文字列の置換 (置換クラスを利用)
        public static string ReplaceStrWithReplaceClass(string str, object replaceClass, bool caseSensitive = false)
        {
            Type t = replaceClass.GetType();

            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();

            // 要素を抽出

            MemberInfo[] members = t.GetMembers(BindingFlags.Instance | BindingFlags.Public);
            foreach (MemberInfo member in members)
            {
                FieldInfo? fi = member as FieldInfo;
                PropertyInfo? pi = member as PropertyInfo;

                string? from = null;
                string? to = null;

                if (fi != null)
                {
                    to = (fi.GetValue(replaceClass))?.ToString();
                    from = fi.Name;
                }
                else if (pi != null)
                {
                    to = (pi.GetValue(replaceClass))?.ToString();
                    from = pi.Name;
                }

                if (from._IsFilled())
                {
                    to = to._NonNull();

                    list.Add(new KeyValuePair<string, string>(from, to));
                }
            }

            // 要素の文字列長順 (長い順) にソート (同じ場合は、文字列でソート)
            list.Sort((x, y) =>
            {
                int r = -x.Key.Length.CompareTo(y.Key.Length);
                if (r != 0) return r;
                return string.Compare(x.Key, y.Key, StringComparison.Ordinal);
            });

            foreach (var kv in list)
            {
                str = str._ReplaceStr(kv.Key, kv.Value, caseSensitive);
            }

            return str;
        }

        // 指定した文字列が出現する位置のリストを取得
        public static int[] FindStringIndexes(string str, string keyword, bool caseSensitive = false)
        {
            List<int> ret = new List<int>();

            str = str._NonNull();

            if (keyword._IsNullOrZeroLen()) return new int[0];

            int len_string, len_keyword;

            int i, j, num;

            len_string = str.Length;
            len_keyword = keyword.Length;

            i = j = num = 0;

            while (true)
            {
                i = SearchStr(str, keyword, i, caseSensitive);
                if (i == -1)
                {
                    break;
                }

                ret.Add(i);

                num++;

                i += len_keyword;
                j = i;
            }

            return ret.ToArray();
        }

        // 指定した文字列が出現する回数のカウント
        public static int GetCountSearchKeywordInStr(string str, string keyword, bool caseSensitive = false)
        {
            var r = FindStringIndexes(str, keyword, caseSensitive);
            if (r == null) return 0;
            return r.Length;
        }

        // 文字列の置換
        public static string ReplaceStr(string str, string oldKeyword, string newKeyword, bool caseSensitive = false)
        {
            if (str._IsNullOrZeroLen())
            {
                return "";
            }

            if (str.Length == 0)
            {
                return str;
            }

            if (oldKeyword._IsNullOrZeroLen())
            {
                return str;
            }

            newKeyword = newKeyword._NonNull();

            return str.Replace(oldKeyword, newKeyword, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        // 複数の文字列を検索する
        public static int SearchStrMulti(string str, int start, bool caseSensitive, out int foundIndex, out string foundKeyword, params string[] keywords)
        {
            int i;
            foundIndex = -1;
            foundKeyword = "";
            int ret = -1;
            int min = int.MaxValue;
            for (i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                int r = Str.SearchStr(str, keyword, start, caseSensitive);
                if (r != -1)
                {
                    if (min > r)
                    {
                        min = r;
                        foundKeyword = str.Substring(r, keyword.Length);
                        foundIndex = i;
                    }
                }
            }

            if (foundIndex != -1)
            {
                ret = min;
            }

            return ret;
        }

        // 文字列 string から文字列 keyword を検索して最初に見つかった文字の場所を返す
        // (1文字目に見つかったら 0, 見つからなかったら -1)
        public static int SearchStr(string str, string keyword, int start)
        {
            return SearchStr(str, keyword, start, false);
        }
        public static int SearchStr(string str, string keyword, int start, bool caseSensitive)
        {
            if (str == null || keyword == null)
            {
                return -1;
            }

            try
            {
                return str.IndexOf(keyword, start, (caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return -1;
            }
        }

        // Printf
        public static void Printf(string fmt, params object[] args)
        {
            if (args.Length == 0)
            {
                Con.WriteLine(fmt);
            }
            else
            {
                Con.WriteLine(FormatC(fmt, args));
            }
        }

        // 文字列のフォーマット (内部関数)
        public static string FormatC(string fmt)
        {
            return FormatC(fmt, new object[0]);
        }
        public static string FormatC(string fmt, params object[] args)
        {
            int i, len;
            StringBuilder tmp;
            List<string> o;
            int mode = 0;
            int pos = 0;
            // 引数チェック
            if (fmt == null)
            {
                return "";
            }

            len = fmt.Length;
            tmp = new StringBuilder();
            o = new List<string>();

            mode = 0;

            for (i = 0; i < len; i++)
            {
                char c = fmt[i];

                if (mode == 0)
                {
                    // 通常の文字モード
                    switch (c)
                    {
                        case '%':
                            // 書式指定の開始
                            if (fmt[i + 1] == '%')
                            {
                                // 次の文字も % の場合は % を一文字出力するだけ
                                i++;
                                tmp.Append("%");
                            }
                            else
                            {
                                // 次の文字が % でない場合は状態遷移を行う
                                mode = 1;
                                o.Add(tmp.ToString());
                                tmp = new StringBuilder();

                                tmp.Append(c);
                            }
                            break;

                        default:
                            // 通常の文字
                            tmp.Append(c);
                            break;
                    }
                }
                else
                {
                    // 書式指定モード
                    switch (c)
                    {
                        case 'c':
                        case 'C':
                        case 'd':
                        case 'i':
                        case 'o':
                        case 'u':
                        case 'x':
                        case 'X':
                        case 'e':
                        case 'E':
                        case 'f':
                        case 'g':
                        case 'G':
                        case 'a':
                        case 'A':
                        case 'n':
                        case 'p':
                        case 's':
                        case 'S':
                            tmp.Append(c);

                            PrintFParsedParam pp = new PrintFParsedParam(tmp.ToString());
                            string s;
                            if (pp.Ok)
                            {
                                s = pp.GetString(args[pos++]);
                            }
                            else
                            {
                                s = "(parse_error)";
                            }

                            o.Add(s);

                            tmp = new StringBuilder();
                            mode = 0;
                            break;

                        default:
                            tmp.Append(c);
                            break;
                    }
                }
            }

            if (tmp.Length >= 1)
            {
                o.Add(tmp.ToString());
            }

            StringBuilder retstr = new StringBuilder();
            foreach (string stmp in o)
            {
                retstr.Append(stmp);
            }

            return retstr.ToString();
        }

        // 文字列を正規化する
        public static void NormalizeString([AllowNull] ref string str)
        {
            if (str == null)
            {
                str = "";
            }

            str = str.Trim();
        }

        public static string NormalizeString(string? str)
        {
            if (str == null)
            {
                return "";
            }

            return str.Trim();
        }

        // パスワードプロンプト
        public static string? PasswordPrompt()
        {
            Queue<char> ret = new Queue<char>();
            bool escape = false;

            while (true)
            {
                ConsoleKeyInfo ki = Console.ReadKey(true);
                char c = ki.KeyChar;

                if (c >= 0x20 && c <= 0x7e)
                {
                    // 1 文字
                    ret.Enqueue(c);
                    Console.Write("*");
                }
                else if (c == 0x04 || c == 0x1a || c == 0x0d || c == 0x0a)
                {
                    // 終了
                    if (c == 0x04 || c == 0x1a)
                    {
                        escape = true;
                    }
                    break;
                }
                else if (c == 0x08)
                {
                    // BS
                    Console.Write(c);
                    Console.Write(" ");
                    Console.Write(c);

                    if (ret.Count >= 1)
                    {
                        ret.Dequeue();
                    }
                }
            }

            lock (Con.ConsoleWriteLock)
            {
                Console.WriteLine();
            }

            if (escape)
            {
                return null;
            }

            return new string(ret.ToArray());
        }

        // 文字列の長さをチェックする
        public static bool CheckStrLen(string? str, int maxLen)
        {
            if (str == null)
            {
                return false;
            }

            if (str.Length > maxLen)
            {
                return false;
            }

            return true;
        }

        public static bool CheckStrSize(string? str, int maxSize, Encoding encoding)
        {
            if (str == null)
            {
                return false;
            }

            if (encoding.GetByteCount(str) > maxSize)
            {
                return false;
            }

            return true;
        }

        // 文字列が安全かどうか検査する
        public static bool IsSafe(string s, bool pathCharsAreNotGood = false)
        {
            foreach (char c in s)
            {
                if (IsSafe(c, pathCharsAreNotGood) == false)
                {
                    return false;
                }
            }

            return true;
        }

        // 文字が安全かどうか検査する
        public static bool IsSafe(char c, bool pathCharsAreNotGood = false)
        {
            foreach (char bb in InvalidFileNameChars)
            {
                if (bb == c)
                {
                    return false;
                }
            }

            if (pathCharsAreNotGood)
            {
                if (c == '\\' || c == '/')
                {
                    return false;
                }
            }

            return true;
        }

        // パスを安全にする (ShiftJIS の観点から)
        public static string MakeSafePathNameShiftJis(string name)
        {
            byte[] sjis = Str.ShiftJisEncoding.GetBytes(name);

            name = Str.ShiftJisEncoding.GetString(sjis);

            char[] a = name.ToCharArray();
            char[] b = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j;
                bool ok = true;

                for (j = 0; j < b.Length; j++)
                {
                    if (b[j] == a[i])
                    {
                        ok = false;
                        break;
                    }
                }

                if (a[i] == '\\' || a[i] == '/')
                {
                    ok = true;
                    a[i] = Env.PathSeparatorChar;
                }

                if (i == 1 && a[i] == ':')
                {
                    ok = true;
                }

                string s;

                if (ok == false)
                {
                    s = "_" + ((int)a[i]).ToString() + "_";
                }
                else
                {
                    s = "" + a[i];
                }

                sb.Append(s);
            }

            return sb.ToString();
        }

        // パスを安全にする
        static readonly char[] InvalidPathChars = Win32PathInternal.GetInvalidPathChars();
        public static string MakeSafePathName(string name, PathParser? pathParser = null)
        {
            char[] a = name.ToCharArray();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j;
                bool ok = true;

                for (j = 0; j < InvalidPathChars.Length; j++)
                {
                    if (InvalidPathChars[j] == a[i])
                    {
                        ok = false;
                        break;
                    }
                }

                if (a[i] == '\\' || a[i] == '/')
                {
                    ok = true;
                    if (pathParser == null)
                    {
                        a[i] = Env.PathSeparatorChar;
                    }
                    else
                    {
                        a[i] = pathParser.DirectorySeparator;
                    }
                }

                if (i == 1 && a[i] == ':')
                {
                    ok = true;
                }

                string s;

                if (ok == false)
                {
                    s = "_" + ((int)a[i]).ToString() + "_";
                }
                else
                {
                    s = "" + a[i];
                }

                sb.Append(s);
            }

            return sb.ToString();
        }

        // 任意文字列を安全な Ascii 文字のみを含む、かつ、空白文字を含まないファイル名に変換する
        public static string MakeVerySafeAsciiOnlyNonSpaceString(string? src, bool allowDot = false)
        {
            if (src._IsEmpty()) return "";
            src = src._NonNullTrim();

            Str.NormalizeString(ref src, true, true, false, false);

            string okChars = "0123456789-_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            if (allowDot)
            {
                okChars += ".";
            }

            StringBuilder sb = new StringBuilder();

            foreach (char c in src)
            {
                if (okChars.IndexOf(c) != -1)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("_");
                }
            }

            string ret = sb.ToString();
            ret = ret.Trim();

            ret = ret._Split(StringSplitOptions.RemoveEmptyEntries, '_')._Combine("_", true);
            ret = ret._Split(StringSplitOptions.RemoveEmptyEntries, '.')._Combine(".", true);

            ret = ret.Trim();

            if (ret.All(x => x == '.'))
            {
                ret = "_";
            }

            return ret;
        }

        // 任意のファイルパスを安全な Ascii 文字のみを含む、かつ、空白文字を含まないファイル名に変換する
        public static string MakeVerySafeAsciiOnlyNonSpaceFileName(string? fullPath)
        {
            if (fullPath._IsEmpty()) return "";
            fullPath = fullPath._NonNullTrim();

            string fn = PathParser.Windows.GetFileName(fullPath);

            Str.NormalizeString(ref fn, true, true, false, false);

            string okChars = "0123456789-_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            StringBuilder sb = new StringBuilder();

            foreach (char c in fn)
            {
                if (okChars.IndexOf(c) != -1)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("_");
                }
            }

            string ret = sb.ToString();
            ret = ret.Trim();

            ret = ret._Split(StringSplitOptions.RemoveEmptyEntries, '_')._Combine("_", true);

            ret = ret.Trim();

            return ret;
        }

        // ファイル名を安全にする
        static readonly char[] InvalidFileNameChars = Win32PathInternal.GetInvalidFileNameChars();
        public static string MakeSafeFileName(string name)
        {
            char[] a = name.ToCharArray();
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < a.Length; i++)
            {
                int j;
                bool ok = true;

                for (j = 0; j < InvalidFileNameChars.Length; j++)
                {
                    if (InvalidFileNameChars[j] == a[i])
                    {
                        ok = false;
                        break;
                    }
                }

                string s;

                if (ok == false)
                {
                    s = "_" + ((int)a[i]).ToString() + "_";
                }
                else
                {
                    s = "" + a[i];
                }

                sb.Append(s);
            }

            return sb.ToString();
        }

        // オブジェクト配列を CSV に変換する
        public static string ObjectArrayToCsv<T>(IEnumerable<T> array, bool withHeader = false)
            where T : notnull
        {
            StringWriter w = new StringWriter();

            if (withHeader)
            {
                string header = ObjectHeaderToCsv<T>();

                w.WriteLine(header);
            }

            foreach (T item in array)
            {
                w.WriteLine(ObjectDataToCsv(item));
            }

            return w.ToString();
        }

        // オブジェクトのヘッダを CSV に変換する
        public static string ObjectHeaderToCsv<T>()
            => ObjectHeaderToCsv(typeof(T));
        public static string ObjectHeaderToCsv(Type objType)
        {
            FieldReaderWriter rw = objType._GetFieldReaderWriter(false);

            List<string> o = new List<string>();

            foreach (string name in rw.FieldOrPropertyNamesList)
            {
                o.Add(name);
            }

            return CombineStringArrayForCsv(o, "#");
        }

        // オブジェクトのデータを CSV に変換する
        public static string ObjectDataToCsv<T>(T obj, FieldReaderWriter? rw = null) where T : notnull
        {
            if (rw == null) rw = obj._GetFieldReaderWriter(false);

            List<string> o = new List<string>();

            foreach (string name in rw.FieldOrPropertyNamesList)
            {
                object? value = rw.GetValue(obj, name);

                string str = "";

                if (value != null)
                {
                    str = value.ToString()._NonNull();
                }

                o.Add(str);
            }

            return CombineStringArrayForCsv(o);
        }

        // 1 行の CSV をオブジェクトデータに変換する
        public static T CsvToObjectData<T>(string csvLine, bool trimStr = false, FieldReaderWriter? rw = null) where T : notnull, new()
        {
            T obj = new T();

            if (rw == null) rw = obj._GetFieldReaderWriter(false);

            string[] tokens = SplitCsvToStringArray(csvLine);

            int i = 0;
            foreach (string name in rw.FieldOrPropertyNamesList)
            {
                string str;

                if (i < tokens.Length)
                    str = tokens[i];
                else
                    str = "";

                str = str._NonNull();

                if (trimStr) str = str.Trim();

                rw.SetValue(obj, name, str);

                i++;
            }

            return obj;
        }

        // 1 行の CSV を複数の文字列に分割する
        public static string[] SplitCsvToStringArray(string csvLine)
        {
            csvLine = csvLine._NonNull();

            // TODO: 後で CombineStringArrayForCsv と同じルールをちゃんと適用すること

            string[] tokens = csvLine._Split(StringSplitOptions.None, ",");

            return tokens;
        }

        // 複数の文字列を CSV 結合する
        public static string CombineStringArrayForCsv(params string?[]? strs)
        {
            return CombineStringArrayForCsv((IEnumerable<string?>?)strs);
        }
        public static string CombineStringArrayForCsv(IEnumerable<string?>? strs, string prefix = "")
        {
            if (strs == null) strs = new string[0];

            List<string> tmp = new List<string>();

            foreach (string? str in strs)
            {
                string str2 = str._NonNull();

                str2 = str2._GetLines()._Combine(" ");

                if (str2._InStr(",") || str2._InStr("\""))
                {
                    str2 = "\"" + str2._ReplaceStr("\"", "\"\"") + "\"";
                }

                tmp.Add(prefix + str2);
            }

            return tmp._Combine(",");
        }

        // 複数の文字列を結合する
        public static string CombineStringArray(string sepstr, params string?[]? strs)
        {
            if (strs == null) return "";

            List<string> tmp = new List<string>();

            foreach (string? str in strs)
            {
                if (str._IsFilled())
                {
                    tmp.Add(str);
                }
            }

            return CombineStringArray(tmp.ToArray(), sepstr);
        }

        public static string CombineStringArray(string sepstr, params object[] objs)
        {
            List<string> tmp = new List<string>();

            foreach (object obj in objs)
            {
                string? str = (obj == null ? "" : obj.ToString());
                if (Str.IsEmptyStr(str) == false)
                {
                    tmp.Add(str!);
                }
            }

            return CombineStringArray(tmp.ToArray(), sepstr);
        }

        public static string CombineStringArray(IEnumerable<string?> strList, string? sepstr = "", bool removeEmpty = false, int maxItems = int.MaxValue, string? ommitStr = "...")
        {
            sepstr = sepstr._NonNull();

            StringBuilder b = new StringBuilder();

            int num = 0;

            foreach (string? s in strList)
            {
                if (removeEmpty == false || s._IsFilled())
                {
                    if (num >= maxItems)
                    {
                        b.Append(ommitStr._NonNull());
                        break;
                    }

                    if (num >= 1)
                    {
                        b.Append(sepstr);
                    }

                    if (s != null) b.Append(s);

                    num++;
                }
            }

            return b.ToString();
        }

        public static string CombineStringSpan(ReadOnlySpan<string> strList, string? sepstr = "", bool removeEmpty = false, int maxItems = int.MaxValue, string? ommitStr = "...")
        {
            sepstr = sepstr._NonNull();

            StringBuilder b = new StringBuilder();

            int num = 0;

            foreach (string? s in strList)
            {
                if (removeEmpty == false || s._IsFilled())
                {
                    if (num >= maxItems)
                    {
                        b.Append(ommitStr._NonNull());
                        break;
                    }

                    if (num >= 1)
                    {
                        b.Append(sepstr);
                    }

                    if (s != null) b.Append(s);

                    num++;
                }
            }

            return b.ToString();
        }

        // 文字列の最大長を指定してそこまで切り取る
        public static string TruncStr(string? str, int len)
        {
            if (str == null)
            {
                return "";
            }
            if (str.Length <= len)
            {
                return str;
            }
            else
            {
                return str.Substring(0, len);
            }
        }

        // 文字列の最大長を指定してそこまで切り取る
        public static string TruncStrEx(string? str, int len, string? appendCode = "...")
        {
            if (str == null)
            {
                return "";
            }
            if (str.Length <= len)
            {
                return str;
            }
            else
            {
                if (appendCode != null)
                {
                    return str.Substring(0, len) + appendCode;
                }
                else
                {
                    return str.Substring(0, len);
                }
            }
        }

        // 文字列が最大長を超える時は中間を省略する
        public static string TruncStrMiddle(string? str, int maxLen, string appendCode = "..")
        {
            str = str._NonNullTrim();
            if (str._IsEmpty()) return str;

            appendCode._NotEmptyCheck();

            appendCode = appendCode.Trim();

            int appendCodeLen = appendCode.Length;
            maxLen = Math.Max(maxLen, appendCodeLen + 2);

            int strLen = str.Length;

            if (strLen <= maxLen) return str;

            int leftLen = maxLen / 2;
            int rightLen = maxLen - leftLen;

            return str.Substring(0, leftLen) + appendCode + str.Substring(strLen - rightLen, rightLen);
        }

        // おもしろ黒塗り
        public static string Kuronuri(string src, char replaceChar = '■')
        {
            StringBuilder sb = new StringBuilder();

            foreach (char c in src)
            {
                if (c == '\r' || c == '\n' || c == ' ' || c == '\t' || c == '　' || c == '.' || c == '(' || c == ')')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(replaceChar);
                }
            }

            return sb.ToString();
        }

        // 新しい GUID を生成
        public static string NewGuid() => System.Guid.NewGuid().ToString("N");

        // 新しい乱数文字列を生成
        public static string GenRandStr()
        {
            return ByteToStr(Secure.HashSHA1(Guid.NewGuid().ToByteArray()));
        }

        // 新しい数字だけのパスワードを生成
        public static string GenRandNumericPassword(int count = 16)
        {
            count._SetMax(1);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                int r = Secure.RandSInt31();
                char c = (char)('0' + (r % 10));
                sb.Append(c);
            }

            return sb.ToString();
        }
        public static string GenRandNumericPasswordWithBlocks(int lenPerBlock = 4, int numBlocks = 4, char sepstr = '_')
        {
            lenPerBlock._SetMax(1);
            numBlocks._SetMax(1);

            List<string> o = new List<string>();

            for (int i = 0; i < numBlocks; i++)
            {
                o.Add(GenRandNumericPassword(lenPerBlock));
            }

            return o._Combine(sepstr);
        }

        // 新しいパスワードを生成
        public static string GenRandPassword(int count = 16, bool mustHaveOneUnderBar = true)
        {
            count._SetMax(4);

            while (true)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    char c;
                    int type = Secure.RandSInt31() % 5;
                    int r = Secure.RandSInt31();
                    if (type == 0)
                    {
                        c = (char)('0' + (r % 10));
                    }
                    else if (type == 1 || type == 2)
                    {
                        c = (char)('a' + (r % 26));
                    }
                    else
                    {
                        c = (char)('A' + (r % 26));
                    }
                    sb.Append(c);
                }

                if (mustHaveOneUnderBar)
                {
                    sb[Secure.RandSInt31() % (sb.Length - 2) + 1] = '_';
                }

                string ret = sb.ToString();

                bool b1 = false;
                bool b2 = false;
                bool b3 = false;

                foreach (char c in ret)
                {
                    if ('0' <= c && c <= '9') b1 = true;
                    if ('a' <= c && c <= 'z') b2 = true;
                    if ('A' <= c && c <= 'Z') b3 = true;
                }

                if (b1 && b2 && b3) return ret;
            }
        }

        // 文字列をハッシュ
        public static byte[] HashStrSHA1(string? str)
            => HashStr(str);
        public static byte[] HashStr(string? str)
        {
            return Secure.HashSHA1(Encoding.UTF8.GetBytes(str._NonNull()));
        }
        public static ulong HashStrToLong(string str)
        {
            Buf b = new Buf();
            b.Write(HashStr(str));
            b.SeekToBegin();
            return b.ReadInt64();
        }
        public static byte[] HashStrSHA256(string? str)
        {
            str = str._NonNull();
            return Secure.HashSHA256(Encoding.UTF8.GetBytes(str));
        }

        // SHA-256 パスワードハッシュ
        public static string HashPasswordSHA256(string password)
        {
            return Str.ByteToHex(Secure.HashSHA256(Str.Utf8Encoding.GetBytes(password))).ToUpperInvariant();
        }

        // バイト列を文字列に変換
        public static string ByteToStr(byte[] data)
        {
            return ByteToStr(data, "");
        }
        public static string ByteToStr(byte[] data, string paddingStr)
        {
            StringBuilder sb = new StringBuilder();

            int i;
            for (i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                sb.Append(b.ToString("X2"));

                if (i != (data.Length - 1))
                {
                    sb.Append(paddingStr);
                }
            }

            return sb.ToString();
        }

        // 乱数文字を 16 進数に変換する
        public static string RandToStr6(string rand)
        {
            byte[] hash = HashStr(rand + "coreutil");
            return ByteToStr(hash).Substring(0, 6);
        }

        // 指定された文字が ASCII 文字かどうかチェックする
        public static bool IsAscii(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return true;
            }
            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }
            if (c == '!' || c == '\"' || c == '#' || c == '$' || c == '%' || c == '&' || c == '\'' ||
                c == '(' || c == ')' || c == '-' || c == ' ' || c == '=' || c == '~' || c == '^' || c == '_' ||
                c == '\\' || c == '|' || c == '{' || c == '}' || c == '[' || c == ']' || c == '@' ||
                c == '*' || c == '+' || c == '.' || c == '<' || c == '>' ||
                c == ',' || c == '?' || c == '/' || c == ' ' || c == '^' || c == '\'')
            {
                return true;
            }
            return false;
        }
        public static bool IsAscii(string str)
        {
            foreach (char c in str)
            {
                if (IsAscii(c) == false)
                {
                    return false;
                }
            }
            return true;
        }

        // 通信速度文字列
        public static string GetBpsStr(long size)
        {
            if (size >= 1000000000000L)
            {
                return ((double)(size) / 1000.0f / 1000.0f / 1000.0f / 1000.0f).ToString(".00") + " Tbps";
            }
            if (size >= 1000 * 1000 * 1000)
            {
                return ((double)(size) / 1000.0f / 1000.0f / 1000.0f).ToString(".00") + " Gbps";
            }
            if (size >= 1000 * 1000)
            {
                return ((double)(size) / 1000.0f / 1000.0f).ToString(".00") + " Mbps";
            }
            if (size >= 1000)
            {
                return ((double)(size) / 1000.0f).ToString(".00") + " Kbps";
            }
            return ((double)(size)).ToString() + " bps";
        }

        // ファイルサイズ文字列
        public static string GetFileSizeStr(long size)
        {
            size = Math.Min(size, long.MaxValue);
            if (size >= 1099511627776L)
            {
                return ((double)(size) / 1024.0f / 1024.0f / 1024.0f / 1024.0f).ToString(".00") + " TB";
            }
            if (size >= 1024 * 1024 * 1024)
            {
                return ((double)(size) / 1024.0f / 1024.0f / 1024.0f).ToString(".00") + " GB";
            }
            if (size >= 1024 * 1024)
            {
                return ((double)(size) / 1024.0f / 1024.0f).ToString(".00") + " MB";
            }
            if (size >= 1024)
            {
                return ((double)(size) / 1024.0f).ToString(".00") + " KB";
            }
            return ((double)(size)).ToString() + " Bytes";
        }

        // 2 つの hex 文字列を比較する
        public static int CompareHex(string? hex1, string? hex2)
        {
            return Util.MemCompare(hex1._GetHexBytes(), hex2._GetHexBytes());
        }
        public static bool IsSameHex(string? hex1, string? hex2)
        {
            return Util.MemEquals(hex1._GetHexBytes(), hex2._GetHexBytes());
        }

        // int 型を文字列に変換する
        public static string IntToStr(int i)
        {
            return i.ToString();
        }
        public static string IntToStr(uint i)
        {
            return i.ToString();
        }

        // long 型を文字列に変換する
        public static string LongToStr(long i)
        {
            return i.ToString();
        }
        public static string LongToStr(ulong i)
        {
            return i.ToString();
        }

        // 文字列を Enum に変換する
        public static T ParseEnum<T>(string? str, T defaultValue, bool exactOnly = false, bool noMatchError = false) where T : unmanaged, Enum
        {
            return (T)StrToEnum(str, defaultValue, exactOnly, noMatchError);
        }
        public static object ParseEnum(object value, object defaultValue, bool exactOnly = false, bool noMatchError = false)
        {
            return ParseEnum(value.ToString()!, defaultValue, exactOnly, noMatchError);
        }
        public static object ParseEnum(string str, object defaultValue, bool exactOnly = false, bool noMatchError = false)
        {
            return StrToEnum(str, defaultValue, exactOnly, noMatchError);
        }

        static class EnumCacheRawValue<T> where T : unmanaged, Enum
        {
            public static readonly Dictionary<string, ulong> DictCastSensitive = new Dictionary<string, ulong>();
            public static readonly Dictionary<string, ulong> DictCastIgnore = new Dictionary<string, ulong>(StrComparer.IgnoreCaseComparer);
            public static readonly Dictionary<ulong, string> DictULongToStr = new Dictionary<ulong, string>();
            public static readonly IEnumerable<string> ElementNamesOrderByValue;
            public static readonly IEnumerable<string> ElementNamesOrderByName;

            static EnumCacheRawValue()
            {
                Type t = typeof(T);
                string[] names = Enum.GetNames(t);

                foreach (string name in names)
                {
                    T value = (T)Enum.Parse(t, name);
                    ulong u64 = value._RawReadValueUInt64();

                    DictCastSensitive.Add(name, u64);
                    DictCastIgnore.Add(name, u64);
                    DictULongToStr.Add(u64, name);
                }

                ElementNamesOrderByValue = DictCastSensitive.OrderBy(x => x.Value).Select(x => x.Key).Distinct();
                ElementNamesOrderByName = DictCastSensitive.OrderBy(x => x.Key, StrComparer.IgnoreCaseComparer).Select(x => x.Key).Distinct();
            }
        }

        public static IEnumerable<KeyValuePair<string, object>> GetEnumValuesList(Type type)
        {
            return EnumCacheCaseSensitive[type];
        }

        static Singleton<Type, Dictionary<string, object>> EnumCacheCaseSensitive = new Singleton<Type, Dictionary<string, object>>(t =>
        {
            string[] names = Enum.GetNames(t);
            Dictionary<string, object> d = new Dictionary<string, object>();
            foreach (string name in names)
            {
                d.Add(name, Enum.Parse(t, name));
            }
            return d;
        });

        static Singleton<Type, Dictionary<string, object>> EnumCacheCaseIgnore = new Singleton<Type, Dictionary<string, object>>(t =>
        {
            string[] names = Enum.GetNames(t);
            Dictionary<string, object> d = new Dictionary<string, object>(StrComparer.IgnoreCaseComparer);
            foreach (string name in names)
            {
                d.Add(name, Enum.Parse(t, name));
            }
            return d;
        });

        public static IEnumerable<string> GetDefinedEnumElementsStrList<T>(T anyOfEnumSampleElement)
             where T : unmanaged, Enum
        {
            return EnumCacheRawValue<T>.ElementNamesOrderByValue;
        }

        public static T StrToEnumBits<T>(string? str, T defaultValue, params char[] separators)
             where T : unmanaged, Enum
        {
            if (str._IsNullOrZeroLen()) return defaultValue;

            string[] strs = str._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, separators);

            var dict1 = EnumCacheRawValue<T>.DictCastSensitive;
            var dict2 = EnumCacheRawValue<T>.DictCastIgnore;

            ulong ret = 0;

            foreach (string token in strs)
            {
                if (token[0] >= '0' && token[0] <= '9')
                {
                    ret |= token._ToULong();
                }
                else
                {
                    if (dict1.TryGetValue(token, out ulong value))
                    {
                        ret |= value;
                    }
                    else if (dict2.TryGetValue(token, out ulong value2))
                    {
                        ret |= value2;
                    }
                }
            }

            T ret2 = default;
            ret2._RawWriteValueUInt64(ret);

            return ret2;
        }

        public static object StrToEnum(string? str, object defaultValue, bool exactOnly = false, bool noMatchError = false)
        {
            if (str._IsNullOrZeroLen()) return defaultValue;

            Type type = defaultValue.GetType();
            if (EnumCacheCaseSensitive[type].TryGetValue(str, out object? ret))
            {
                return ret;
            }
            if (EnumCacheCaseIgnore[type].TryGetValue(str, out object? ret2))
            {
                return ret2;
            }
            if (exactOnly == false)
            {
                if (Enum.TryParse(type, str, out object? ret3))
                {
                    if (ret3 == null) return defaultValue;

                    return ret3;
                }
            }
            if (noMatchError)
                throw new ArgumentException($"The string \"{str}\' doesn't match to any items of the type \"{type.Name}\".");
            return defaultValue;
        }

        public static string EnumToStrExact<T>(T value, string? defaultStr = null) where T : unmanaged, Enum
        {
            ulong u64 = value._RawReadValueUInt64();

            if (EnumCacheRawValue<T>.DictULongToStr.TryGetValue(u64, out string? name))
            {
                return name;
            }

            if (defaultStr == null)
            {
                return u64.ToString();
            }
            else
            {
                return defaultStr;
            }
        }

        // 文字列を bool に変換する
        public static bool StrToBool(string? s, bool defaultValue = false)
        {
            if (s._IsNullOrZeroLen())
            {
                return defaultValue;
            }

            Str.NormalizeString(ref s, true, true, false, false);

            if (s.StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("t", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (s.StartsWith("enable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Str.StrToInt(s) != 0)
            {
                return true;
            }

            return false;
        }

        // 文字列を double に変換する
        public static double StrToDouble(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                if (double.TryParse(str, out double ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0.0;
            }
        }

        // 文字列を decimal に変換する
        public static decimal StrToDecimal(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                if (decimal.TryParse(str, out decimal ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0.0m;
            }
        }

        // 文字列を int 型に変換する
        public static int StrToInt(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (int.TryParse(str, out int ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
        public static uint StrToUInt(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (uint.TryParse(str, out uint ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // 文字列を long 型に変換する
        public static long StrToLong(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (long.TryParse(str, out long ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
        public static ulong StrToULong(string? str)
        {
            try
            {
                Str.RemoveSpaceChar(ref str);
                Str.NormalizeString(ref str, true, true, false, false);
                str = str.Replace(",", "");
                if (ulong.TryParse(str, out ulong ret))
                {
                    return ret;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // 文字列を日時に変換する
        public static bool IsStrDateTime(string str)
        {
            try
            {
                Str.NormalizeString(ref str, true, true, false, false);
                StrToDateTime(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static DateTime StrToDateTime(string? str, bool toUtc = false, bool emptyToZeroDateTime = false)
        {
            if (str._IsEmpty())
            {
                if (emptyToZeroDateTime) return Util.ZeroDateTimeValue;
                return new DateTime(0);
            }
            DateTime ret = new DateTime(0);

            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Trim();
            string[] sps =
                {
                    " ",
                    "_",
                    "　",
                    "\t",
                    "T",
                };

            string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length != 2)
            {
                int r1 = str.IndexOf("年", StringComparison.OrdinalIgnoreCase);
                int r2 = str.IndexOf("月", StringComparison.OrdinalIgnoreCase);
                int r3 = str.IndexOf("日", StringComparison.OrdinalIgnoreCase);

                if (r1 != -1 && r2 != -1 && r3 != -1)
                {
                    tokens = new string[2];

                    tokens[0] = str.Substring(0, r3 + 1);
                    tokens[1] = str.Substring(r3 + 1);
                }
            }

            if (tokens.Length == 2)
            {
                DateTime dt1 = StrToDate(tokens[0]);
                DateTime dt2 = StrToTime(tokens[1]);

                ret = dt1.Date + dt2.TimeOfDay;
            }
            else if (tokens.Length == 1)
            {
                if (tokens[0].Length == 14)
                {
                    // yyyymmddhhmmss
                    DateTime dt1 = StrToDate(tokens[0].Substring(0, 8));
                    DateTime dt2 = StrToTime(tokens[0].Substring(8));

                    ret = dt1.Date + dt2.TimeOfDay;
                }
                else if (tokens[0].Length == 12)
                {
                    // yymmddhhmmss
                    DateTime dt1 = StrToDate(tokens[0].Substring(0, 6));
                    DateTime dt2 = StrToTime(tokens[0].Substring(6));

                    ret = dt1.Date + dt2.TimeOfDay;
                }
                else
                {
                    // 日付のみ
                    DateTime dt1 = StrToDate(tokens[0]);

                    ret = dt1.Date;
                }
            }
            else
            {
                throw new ArgumentException(str);
            }

            if (toUtc) ret = ret.ToUniversalTime();

            return ret;
        }

        // 文字列を時刻に変換する
        public static bool IsStrTime(string str)
        {
            try
            {
                Str.NormalizeString(ref str, true, true, false, false);
                StrToTime(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static DateTime StrToTime(string? str, bool toUtc = false, bool emptyToZeroDateTime = false)
        {
            if (emptyToZeroDateTime && str._IsEmpty()) return Util.ZeroDateTimeValue;

            DateTime ret = new DateTime(0);

            string[] sps =
                {
                    "/",
                    "-",
                    ":",
                    "時",
                    "分",
                    "秒",
                };
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Trim();

            string[] tokens;

            tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 3)
            {
                // hh:mm:ss
                string hourStr = tokens[0];
                string minuteStr = tokens[1];
                string secondStr = tokens[2];
                string msecStr = "";
                int hour = -1;
                int minute = -1;
                int second = -1;
                int msecond = 0;
                long add_ticks = 0;

                int msec_index = secondStr._Search(".");
                if (msec_index != -1)
                {
                    msecStr = secondStr.Substring(msec_index + 1);
                    secondStr = secondStr.Substring(0, msec_index);

                    msecStr = "0." + msecStr;

                    decimal tmp = msecStr._ToDecimal();
                    msecond = (int)((tmp % 1.0m) * 1000.0m);
                    add_ticks = (int)((tmp % 0.001m) * 10000000.0m);
                }

                if ((hourStr.Length == 1 || hourStr.Length == 2) && IsNumber(hourStr))
                {
                    hour = StrToInt(hourStr);
                }
                if ((minuteStr.Length == 1 || minuteStr.Length == 2) && IsNumber(minuteStr))
                {
                    minute = StrToInt(minuteStr);
                }
                if ((secondStr.Length == 1 || secondStr.Length == 2) && IsNumber(secondStr))
                {
                    second = StrToInt(secondStr);
                }

                if (hour < 0 || hour >= 25 || minute < 0 || minute >= 60 || second < 0 || second >= 60 || msecond < 0 || msecond >= 1000)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(2000, 1, 1, hour, minute, second, msecond).AddTicks(add_ticks);
            }
            else if (tokens.Length == 2)
            {
                // hh:mm
                string hourStr = tokens[0];
                string minuteStr = tokens[1];
                int hour = -1;
                int minute = -1;
                int second = 0;

                if ((hourStr.Length == 1 || hourStr.Length == 2) && IsNumber(hourStr))
                {
                    hour = StrToInt(hourStr);
                }
                if ((minuteStr.Length == 1 || minuteStr.Length == 2) && IsNumber(minuteStr))
                {
                    minute = StrToInt(minuteStr);
                }

                if (hour < 0 || hour >= 25 || minute < 0 || minute >= 60 || second < 0 || second >= 60)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(2000, 1, 1, hour, minute, second);
            }
            else if (tokens.Length == 1)
            {
                string hourStr = tokens[0];
                int hour = -1;
                int minute = 0;
                int second = 0;
                int msec = 0;

                if ((hourStr.Length == 1 || hourStr.Length == 2) && IsNumber(hourStr))
                {
                    // hh
                    hour = StrToInt(hourStr);
                }
                else
                {
                    if ((hourStr.Length == 4) && IsNumber(hourStr))
                    {
                        // hhmm
                        int i = StrToInt(hourStr);
                        hour = i / 100;
                        minute = i % 100;
                    }
                    else if ((hourStr.Length == 6) && IsNumber(hourStr))
                    {
                        // hhmmss
                        int i = StrToInt(hourStr);
                        hour = i / 10000;
                        minute = ((i % 10000) / 100);
                        second = i % 100;
                    }
                    else if ((hourStr.Length == 10 && hourStr[6] == '.'))
                    {
                        // hhmmss.abc
                        int i = StrToInt(hourStr.Substring(0, 6));
                        hour = i / 10000;
                        minute = ((i % 10000) / 100);
                        second = i % 100;

                        msec = StrToInt(hourStr.Substring(7));
                    }
                }

                if (hour < 0 || hour >= 25 || minute < 0 || minute >= 60 || second < 0 || second >= 60 || msec < 0 || msec >= 1000)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(2000, 1, 1, hour, minute, second, msec);
            }
            else
            {
                throw new ArgumentException(str);
            }

            if (toUtc)
            {
                ret = ret.ToUniversalTime();
            }

            return ret;
        }

        // 文字列を日付に変換する
        public static bool IsStrDate(string str)
        {
            try
            {
                Str.NormalizeString(ref str, true, true, false, false);
                StrToDate(str);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public static DateTime StrToDate(string? str, bool toUtc = false, bool emptyToZeroDateTime = false)
        {
            if (emptyToZeroDateTime && str._IsEmpty()) return Util.ZeroDateTimeValue;

            string[] sps =
                {
                    "/",
                    "/",
                    "-",
                    ":",
                    "年",
                    "月",
                    "日",
                };
            str = str._NonNullTrim();
            Str.NormalizeString(ref str, true, true, false, false);

            string[] youbi =
            {
                "月", "火", "水", "木", "金", "土", "日",
            };

            foreach (string ys in youbi)
            {
                string ys2 = string.Format("({0})", ys);

                str = str.Replace(ys2, "");
            }

            string[] tokens;

            DateTime ret = new DateTime(0);

            tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 3)
            {
                // yyyy/mm/dd
                string yearStr = tokens[0];
                string monthStr = tokens[1];
                string dayStr = tokens[2];
                int year = 0;
                int month = 0;
                int day = 0;

                if ((yearStr.Length == 1 || yearStr.Length == 2) && IsNumber(yearStr))
                {
                    year = 2000 + StrToInt(yearStr);
                }
                else if (yearStr.Length == 4 && IsNumber(yearStr))
                {
                    year = StrToInt(yearStr);
                }

                if ((monthStr.Length == 1 || monthStr.Length == 2) && IsNumber(monthStr))
                {
                    month = StrToInt(monthStr);
                }
                if ((dayStr.Length == 1 || dayStr.Length == 2) && IsNumber(dayStr))
                {
                    day = StrToInt(dayStr);
                }

                if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear || month <= 0 || month >= 13 || day <= 0 || day >= 32)
                {
                    throw new ArgumentException(str);
                }

                ret = new DateTime(year, month, day);
            }
            else if (tokens.Length == 1)
            {
                if (str.Length == 8)
                {
                    // yyyymmdd
                    string yearStr = str.Substring(0, 4);
                    string monthStr = str.Substring(4, 2);
                    string dayStr = str.Substring(6, 2);
                    int year = int.Parse(yearStr);
                    int month = int.Parse(monthStr);
                    int day = int.Parse(dayStr);

                    if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear || month <= 0 || month >= 13 || day <= 0 || day >= 32)
                    {
                        throw new ArgumentException(str);
                    }

                    ret = new DateTime(year, month, day);
                }
                else if (str.Length == 6)
                {
                    // yymmdd
                    string yearStr = str.Substring(0, 2);
                    string monthStr = str.Substring(2, 2);
                    string dayStr = str.Substring(4, 2);
                    int year = int.Parse(yearStr) + 2000;
                    int month = int.Parse(monthStr);
                    int day = int.Parse(dayStr);

                    if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear || month <= 0 || month >= 13 || day <= 0 || day >= 32)
                    {
                        throw new ArgumentException(str);
                    }

                    ret = new DateTime(year, month, day);
                }
            }
            else
            {
                throw new ArgumentException(str);
            }

            if (toUtc)
            {
                ret = ret.ToUniversalTime();
            }

            return ret;
        }

        // 時刻を文字列に変換する
        public static string TimeToStr(DateTime dt)
        {
            return TimeToStr(dt, false);
        }
        public static string TimeToStr(DateTime dt, CoreLanguage lang)
        {
            return TimeToStr(dt, false, lang);
        }
        public static string TimeToStr(DateTime dt, bool toLocalTime)
        {
            return TimeToStr(dt, toLocalTime, CoreLanguageClass.CurrentThreadLanguage);
        }
        public static string TimeToStr(DateTime dt, bool toLocalTime, CoreLanguage lang)
        {
            string s = DateTimeToStr(dt, toLocalTime, lang);

            string[] tokens = s.Split(' ');

            return tokens[1];
        }
        public static string TimeToStrShort(DateTime dt)
        {
            return TimeToStrShort(dt, false);
        }
        public static string TimeToStrShort(DateTime dt, bool toLocalTime)
        {
            string s = DateTimeToStrShort(dt, toLocalTime);

            string[] tokens = s.Split('_');

            return tokens[1];
        }

        // 日付を文字列に変換する
        public static string DateToStr(DateTime dt)
        {
            return DateToStr(dt, false);
        }
        public static string DateToStr(DateTime dt, CoreLanguage lang)
        {
            return DateToStr(dt, false, lang);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime)
        {
            return DateToStr(dt, toLocalTime, false);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime, CoreLanguage lang)
        {
            return DateToStr(dt, toLocalTime, false, lang);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime, bool noDayOfWeek)
        {
            return DateToStr(dt, toLocalTime, noDayOfWeek, CoreLanguageClass.CurrentThreadLanguage);
        }
        public static string DateToStr(DateTime dt, bool toLocalTime, bool noDayOfWeek, CoreLanguage lang)
        {
            string s = DateTimeToStr(dt, toLocalTime, lang);

            string[] tokens = s.Split(' ');

            string ret = tokens[0];

            if (noDayOfWeek)
            {
                string[] tokens2 = s.Split('(');

                ret = tokens2[0];
            }

            return ret;
        }
        public static string DateToStrShort(DateTime dt)
        {
            return DateToStrShort(dt, false);
        }
        public static string DateToStrShort(DateTime dt, bool toLocalTime)
        {
            string s = DateTimeToStrShort(dt, toLocalTime);

            string[] tokens = s.Split('_');

            return tokens[0];
        }

        // 曜日を日付に変換する
        public static string DayOfWeekToStr(CoreLanguage lang, int d)
        {
            if (lang == CoreLanguage.Japanese)
            {
                string[] youbi =
                {
                    "日", "月", "火", "水", "木", "金", "土",
                };

                return youbi[d];
            }
            else
            {
                string[] youbi =
                {
                    "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
                };

                return youbi[d];
            }
        }

        public static string TimeSpanToTsStr(TimeSpan ts, bool withMSecs = false, bool withNanoSecs = false)
        {
            string tag = "";

            if (withNanoSecs)
                tag = @"\.fffffff";
            else if (withMSecs)
                tag = @"\.fff";

            if (ts.TotalDays >= 1)
                return ts.ToString(@"d\.hh\:mm\:ss" + tag);
            else
                return ts.ToString(@"hh\:mm\:ss" + tag);
        }

        // 日時を文字列に変換する
        public static string DateTimeToStr(DateTime dt, bool toLocalTime = false, CoreLanguage lang = CoreLanguage.Japanese, FullDateTimeStrFlags flags = FullDateTimeStrFlags.None)
        {
            if (toLocalTime)
            {
                dt = dt.ToLocalTime();
            }

            if (lang == CoreLanguage.Japanese)
            {
                string dateTag = "yyyy年M月d日";
                string timeTag = flags.Bit(FullDateTimeStrFlags.CommaTime) ? " HH:mm:ss" : " H時m分s秒";

                if (flags.Bit(FullDateTimeStrFlags.SlashDate))
                {
                    dateTag = "yyyy/MM/dd";
                }

                if (flags.Bit(FullDateTimeStrFlags.NoSeconds))
                {
                    timeTag = flags.Bit(FullDateTimeStrFlags.CommaTime) ? " HH:mm" : " H時m分";
                }

                return dt.ToString(dateTag) + "(" + DayOfWeekToStr(lang, (int)dt.DayOfWeek) + ")" + dt.ToString(timeTag);
            }
            else
            {
                string timeTag = ") H:mm:ss";

                if (flags.Bit(FullDateTimeStrFlags.NoSeconds))
                {
                    timeTag = ") H:mm";
                }

                return dt.ToString("yyyy-MM-dd(") + DayOfWeekToStr(lang, (int)dt.DayOfWeek) + dt.ToString(timeTag);
            }
        }
        public static string DateTimeToStrShort(DateTime dt, bool toLocalTime = false)
        {
            if (toLocalTime)
            {
                dt = dt.ToLocalTime();
            }

            return dt.ToString("yyyyMMdd_HHmmss");
        }
        public static string DateTimeToStrShortWithMilliSecs(DateTime dt, bool toLocalTime = false)
        {
            if (toLocalTime)
            {
                dt = dt.ToLocalTime();
            }

            long ticks = dt.Ticks % 10000000;
            if (ticks >= 9990000)
            {
                ticks = 9990000;
            }

            string msecStr = ((decimal)ticks / (decimal)10000000).ToString(".000");

            return dt.ToString("yyyyMMdd_HHmmss") + "." + msecStr.Split('.')[1];
        }

        public static int DateTimeToYymmddInt(DateTime dt, int zeroValue = 0, bool yearTwoDigits = false)
        {
            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("yyyyMMdd");

            if (yearTwoDigits)
            {
                ret = ret.Substring(2);
            }

            return Str.StrToInt(ret);
        }

        public static string DateTimeToYymmddStr(DateTime dt, string zeroValue = "", bool yearTwoDigits = false)
        {
            zeroValue = zeroValue._NonNull();

            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("yyyyMMdd");

            if (yearTwoDigits)
            {
                ret = ret.Substring(2);
            }

            return ret;
        }

        public static int DateTimeToHhmmssInt(DateTime dt, int zeroValue = 0)
        {
            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("HHmmss");

            return Str.StrToInt(ret);
        }

        public static string DateTimeToHhmmssStr(DateTime dt, string zeroValue = "")
        {
            zeroValue = zeroValue._NonNull();

            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("HHmmss");

            return ret;
        }

        public static long DateTimeToYymmddHHmmssLong(DateTime dt, long zeroValue = 0, bool yearTwoDigits = false)
        {
            if (dt._IsZeroDateTime())
            {
                return zeroValue;
            }

            string ret = dt.ToString("yyyyMMddHHmmss");

            if (yearTwoDigits)
            {
                ret = ret.Substring(2);
            }

            return Str.StrToLong(ret);
        }

        public static string GenerateRandomDigit(int numDigits)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < numDigits; i++)
            {
                char c = (char)('0' + Secure.RandSInt31() % 10);
                sb.Append(c);
            }

            return sb.ToString();
        }

        public static string DateTimeToDtstr(DateTime dt, bool withMSecs = false, DtStrOption option = DtStrOption.All, bool withNanoSecs = false, string zeroDateTimeStr = "")
        {
            long ticks = dt.Ticks % 10000000;
            if (ticks >= 9999999) ticks = 9999999;

            if (dt._IsZeroDateTime())
            {
                return zeroDateTimeStr;
            }

            string msecStr = "";
            if (withNanoSecs)
            {
                msecStr = dt.ToString("fffffff");
            }
            else if (withMSecs)
            {
                msecStr = dt.ToString("fff");
            }

            string ret = dt.ToString("yyyy/MM/dd HH:mm:ss") + ((withMSecs || withNanoSecs) ? "." + msecStr : "");

            if (option == DtStrOption.DateOnly)
            {
                ret = ret._ToToken(" ")[0];
            }
            else if (option == DtStrOption.TimeOnly)
            {
                ret = ret._ToToken(" ")[1];
            }

            return ret;
        }

        public static string DateTimeToDtstr(DateTimeOffset dt, bool withMSecs = false, DtStrOption option = DtStrOption.All, bool withNanoSecs = false, string zeroDateTimeStr = "")
        {
            long ticks = dt.Ticks % 10000000;
            if (ticks >= 9999999) ticks = 9999999;

            if (dt._IsZeroDateTime())
            {
                return zeroDateTimeStr;
            }

            string msecStr = "";
            if (withNanoSecs)
            {
                msecStr = dt.ToString("fffffff");
            }
            else if (withMSecs)
            {
                msecStr = dt.ToString("ffff").Substring(0, 3);
            }

            string ret = dt.ToString("yyyy/MM/dd HH:mm:ss") + ((withMSecs || withNanoSecs) ? "." + msecStr : "");

            if (option == DtStrOption.DateOnly)
            {
                ret = ret._ToToken(" ")[0];
            }
            else if (option == DtStrOption.TimeOnly)
            {
                ret = ret._ToToken(" ")[1];
            }

            ret += " " + dt.ToString("%K");

            return ret;
        }

        public static DateTimeOffset DtstrToDateTimeOffset(string str)
        {
            if (str._IsEmpty()) return ZeroDateTimeOffsetValue;

            string[] tokens = str._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, " ");

            DateTime date = Str.StrToDate(tokens[0], false);
            DateTime time = Str.StrToTime(tokens[1], false);

            TimeSpan offset = default;
            string? offsetStr = tokens.ElementAtOrDefault(2);
            if (offsetStr._IsEmpty())
            {
            }
            else
            {
                if (offsetStr[0] == '+')
                {
                    offset = TimeSpan.Parse(offsetStr.Substring(1), CultureInfo.InvariantCulture);
                }
                else
                {
                    offset = -TimeSpan.Parse(offsetStr.Substring(1), CultureInfo.InvariantCulture);
                }
            }

            DateTime dt = date.Add(time.TimeOfDay);

            return new DateTimeOffset(dt._OverwriteKind(DateTimeKind.Unspecified), offset);
        }

        // "20190924-123456+0900" という形式のファイル名をパースする
        public static ResultOrError<DateTimeOffset> FileNameStrToDateTimeOffset(string str)
        {
            str = str._NonNullTrim();

            ReadOnlySpan<char> stra = str;

            if (stra.Length < 20) return false;

            if (stra.Length > 20)
                stra = stra.Slice(0, 20);

            if (stra[8] != '-') return false;

            bool positive;
            if (stra[15] == '+')
                positive = true;
            else if (stra[15] == '-')
                positive = false;
            else
                return false;

            if (int.TryParse(stra.Slice(0, 4), out int year) == false ||
                int.TryParse(stra.Slice(4, 2), out int month) == false ||
                int.TryParse(stra.Slice(6, 2), out int day) == false ||
                int.TryParse(stra.Slice(9, 2), out int hour) == false ||
                int.TryParse(stra.Slice(11, 2), out int minute) == false ||
                int.TryParse(stra.Slice(13, 2), out int second) == false ||
                int.TryParse(stra.Slice(16, 2), out int offsetHour) == false ||
                int.TryParse(stra.Slice(18, 2), out int offsetMinute) == false)
            {
                return false;
            }

            if (year < Consts.Numbers.MinYear || year > Consts.Numbers.MaxYear) return false;
            if (month < 1 || month > 12) return false;
            if (day < 1 || day > 31) return false;
            if (hour < 0 || hour >= 24) return false;
            if (minute < 0 || minute >= 60) return false;
            if (second < 0 || second >= 60) return false;
            if (offsetHour < 0 || offsetHour >= 24) return false;
            if (offsetMinute < 0 || offsetMinute >= 60) return false;

            TimeSpan offset = new TimeSpan(offsetHour, offsetMinute, 0);
            if (positive == false) offset = -offset;

            return new DateTimeOffset(year, month, day, hour, minute, second, 0, offset);
        }

        // "20190924-123456+0900" という形式のファイル名を出力する
        public static string DateTimeOffsetToFileNameStr(DateTimeOffset dt)
        {
            StringBuilder b = new StringBuilder();
            b.Append(dt.ToString("yyyyMMdd-HHmmss"));

            TimeSpan ts = dt.Offset;
            bool positive = true;

            if (ts.Ticks < 0)
            {
                ts = -ts;
                positive = false;
            }

            if (ts >= new TimeSpan(24, 0, 0)) throw new ArgumentOutOfRangeException(nameof(dt.Offset));
            if (ts.Seconds != 0 || ts.Milliseconds != 0) throw new ArgumentOutOfRangeException(nameof(dt.Offset));

            if (positive)
                b.Append("+");
            else
                b.Append("-");

            b.Append(ts.Hours.ToString("D2"));
            b.Append(ts.Minutes.ToString("D2"));

            return b.ToString();
        }

        // 2ch 方式のレスタグ生成
        public static string Make2chThreadTypeResHyperLink(string srcText, string baseThreadUrl)
        {
            baseThreadUrl = baseThreadUrl.TrimEnd('/');

            string[] lines = srcText._Split(StringSplitOptions.None, Str.HtmlCrlf);

            StringBuilder w = new StringBuilder();

            string resChars = "0123456789,-";

            string restag = ">>"._EncodeHtml();

            for (int j = 0; j < lines.Length; j++)
            {
                string line = lines[j];

                int startResTag = line.IndexOf(restag, StringComparison.OrdinalIgnoreCase);

                if (startResTag != -1)
                {
                    string beforeString = line.Substring(0, startResTag);
                    string tmp = line.Substring(startResTag + restag.Length);

                    StringBuilder resStringBuilder = new StringBuilder();
                    StringBuilder afterStringBuilder = new StringBuilder();
                    bool flag = false;

                    for (int i = 0; i < tmp.Length; i++)
                    {
                        char c = tmp[i];

                        if (flag == false)
                        {
                            if (resChars.IndexOf(c) != -1 || c == ' ' || c == '　')
                            {
                                resStringBuilder.Append(c);
                            }
                            else if (c == '&' && tmp.Substring(i).StartsWith(Str.HtmlSpacing, StringComparison.OrdinalIgnoreCase))
                            {
                                resStringBuilder.Append(Str.HtmlSpacing);
                                i += Str.HtmlSpacing.Length - 1;
                            }
                            else
                            {
                                afterStringBuilder.Append(c);
                                flag = true;
                            }
                        }
                        else
                        {
                            afterStringBuilder.Append(c);
                        }
                    }

                    string resString = resStringBuilder.ToString().Trim();
                    string resStringRemoveSpace = resString._ReplaceStr(Str.HtmlSpacing, "", true);
                    string afterString = afterStringBuilder.ToString();

                    string linkStart = $"<a href=\"{baseThreadUrl}/{resStringRemoveSpace}/\">";
                    string linkEnd = "</a>";

                    w.Append($"{linkStart}{restag}{resString}{linkEnd}{afterString}");
                }
                else
                {
                    w.Append($"{line}");
                }

                if (j != (lines.Length - 1))
                {
                    w.Append(Str.HtmlBr);
                }
            }

            return w.ToString();
        }

        // 2ch 方式のハッシュ文字列の生成
        public static string Easy2chTypeHashStr(string src, int len = 8)
        {
            len = Math.Max(len, 1);

            var rand = new SeedBasedRandomGenerator(src);

            StringBuilder b = new StringBuilder();

            for (int i = 0; i < len; i++)
            {
                char c = IntToChar(rand.GetSInt31());

                b.Append(c);
            }

            return b.ToString();

            char IntToChar(int i)
            {
                i = i % (10 + 26 * 2);

                if (i < 10)
                {
                    return (char)('0' + i);
                }
                else if (i < (10 + 26))
                {
                    return (char)('a' + i - 10);
                }
                else
                {
                    return (char)('A' + i - (10 + 26));
                }
            }
        }

        // 文字列置換
        public static string Base64ToSafe64(string str)
        {
            return str.Replace('=', '(').Replace('+', ')').Replace('/', '_');
        }
        public static string Safe64ToBase64(string str)
        {
            return str.Replace('(', '=').Replace(')', '+').Replace('_', '/');
        }

        // Base 64 エンコード
        public static string Base64Encode(byte[] data)
        {
            try
            {
                return Convert.ToBase64String(data);
            }
            catch
            {
                return "";
            }
        }
        public static string Base64Encode(ReadOnlySpan<byte> data)
        {
            try
            {
                return Convert.ToBase64String(data);
            }
            catch
            {
                return "";
            }
        }
        public static string Base64Encode(ReadOnlyMemory<byte> data)
            => Base64Encode(data.Span);

        // Base 64 デコード
        public static byte[] Base64Decode(string str)
        {
            try
            {
                return Convert.FromBase64String(str);
            }
            catch
            {
                return new byte[0];
            }
        }

        // Base 64 URL エンコード
        public static string Base64UrlEncode(byte[] data)
        {
            string tmp = Base64Encode(data);

            tmp = tmp.TrimEnd('=').Replace('+', '-').Replace('/', '_');

            return tmp;
        }

        // Base 64 URL デコード
        public static byte[] Base64UrlDecode(string str)
        {
            str = str._NonNull();

            string tmp = str.Replace('_', '/').Replace('-', '+');

            switch (str.Length % 4)
            {
                case 2: tmp += "=="; break;
                case 3: tmp += "="; break;
            }

            return Base64Decode(tmp);
        }

        // 文字列をバイトに変換
        public static byte[] StrToByte(string? str)
        {
            Str.NormalizeString(ref str, true, true, false, false);
            return Base64Decode(Safe64ToBase64(str));
        }

        // 文字列を Base 64 エンコードする
        public static string Encode64(string str)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(str)).Replace("/", "(").Replace("+", ")");
        }

        // 文字列を Base 64 デコードする
        public static string Decode64(string str)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(str.Replace(")", "+").Replace("(", "/")));
        }


        // メールアドレスをチェックする
        public static bool CheckMailAddress(string? str)
        {
            try
            {
                str = str._NonNullTrim();
                if (str.Length == 0)
                {
                    return false;
                }

                string[] tokens = str.Split('@');

                if (tokens.Length != 2)
                {
                    return false;
                }

                string a = tokens[0];
                string b = tokens[1];

                if (a.Length == 0 || b.Length == 0)
                {
                    return false;
                }

                if (b.IndexOf(".") == -1)
                {
                    return false;
                }

                return IsAscii(str);
            }
            catch
            {
                return false;
            }
        }

        // 文字列を大文字・小文字を区別せずに比較
        public static bool StrCmpi(string? s1, string? s2)
        {
            if (s1 == null && s2 == null) return true;
            if (s1 == null || s2 == null) return false;

            return string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);
        }

        // 文字列を大文字・小文字を区別して比較
        public static bool StrCmp(string? s1, string? s2)
        {
            if (s1 == null && s2 == null) return true;
            if (s1 == null || s2 == null) return false;

            return string.Equals(s1, s2, StringComparison.Ordinal);
        }

        // 簡易エンコードを実施
        public static string EncodeEasy(string? str)
        {
            try
            {
                str = str._NonNull();
                return Consts.Strings.EncodeEasyPrefix + str._GetBytes_UTF8()._GetHexString();
            }
            catch
            {
                return "";
            }
        }

        // 簡易デコードを実施
        public static string DecodeEasy(string? str)
        {
            try
            {
                str = str._NonNull();

                if (str._TryTrimStartWith(out string str2, StringComparison.OrdinalIgnoreCase, Consts.Strings.EncodeEasyPrefix) == false)
                {
                    return "";
                }

                return str2._GetHexBytes()._GetString_UTF8();
            }
            catch
            {
                return "";
            }
        }

        // バイト列を JavaScript 16 進数データ列に変換
        public static string ByteToJavaScriptHexString(ReadOnlySpan<byte> data)
        {
            StringBuilder ret = new StringBuilder();

            int i;
            for (i = 0; i < data.Length; i++)
            {
                byte b = data[i];

                string s = b.ToString("X");
                if (s.Length == 1)
                {
                    s = "0" + s;
                }

                ret.Append("0x" + s);

                if (i != (data.Length - 1))
                {
                    ret.Append(", ");
                }
            }

            return ret.ToString();
        }

        // バイト列を 16 進数文字列に変換
        public static string ByteToHex(ReadOnlySpan<byte> data)
        {
            return ByteToHex(data, "");
        }
        public static string ByteToHex(ReadOnlySpan<byte> data, string paddingStr)
        {
            StringBuilder ret = new StringBuilder();

            int i;
            for (i = 0; i < data.Length; i++)
            {
                byte b = data[i];

                string s = b.ToString("X");
                if (s.Length == 1)
                {
                    s = "0" + s;
                }

                ret.Append(s);

                if (paddingStr != null)
                {
                    if (i != (data.Length - 1))
                    {
                        ret.Append(paddingStr);
                    }
                }
            }

            return ret.ToString().Trim();
        }

        // 16 進数文字列をバイト列に変換
        public static byte[] HexToByte(string? str)
        {
            try
            {
                if (str._IsNullOrZeroLen()) return new byte[0];

                List<byte> o = new List<byte>();
                string tmp = "";
                int i, len;

                str = str!.ToUpperInvariant().Trim();
                len = str.Length;

                for (i = 0; i < len; i++)
                {
                    char c = str[i];
                    if (('0' <= c && c <= '9') || ('A' <= c && c <= 'F'))
                    {
                        tmp += c;
                        if (tmp.Length == 2)
                        {
                            byte b = Convert.ToByte(tmp, 16);
                            o.Add(b);
                            tmp = "";
                        }
                    }
                    else if (c == ' ' || c == ',' || c == '-' || c == ';' || c == ':')
                    {
                        // 何もしない
                    }
                    else
                    {
                        break;
                    }
                }

                return o.ToArray();
            }
            catch
            {
                return new byte[0];
            }
        }

        // テキストから複数行を取り出す
        public static string[] GetLines(string str, bool removeEmpty = false, bool stripCommentsFromLine = false, IEnumerable<string>? commentStartStrList = null, bool singleLineAtLeast = false, bool trim = false)
        {
            List<string> a = new List<string>();
            StringReader sr = new StringReader(str);
            while (true)
            {
                string? s = sr.ReadLine();
                if (s == null)
                {
                    break;
                }
                if (trim)
                {
                    s = s.Trim();
                }
                if (stripCommentsFromLine)
                {
                    s = s._StripCommentFromLine(commentStartStrList);
                }
                if (trim)
                {
                    s = s.Trim();
                }
                if (removeEmpty == false || s._IsFilled())
                {
                    a.Add(s);
                }
            }
            if (singleLineAtLeast)
            {
                if (a.Count == 0)
                {
                    a.Add("");
                }
            }
            return a.ToArray();
        }

        // 複数行をテキストに変換する
        public static string LinesToStr(IEnumerable<string> lines, string? newLineStr = null)
        {
            StringWriter sw = new StringWriter();
            if (newLineStr._IsNullOrZeroLen() == false)
            {
                sw.NewLine = newLineStr;
            }
            foreach (string s in lines)
            {
                sw.WriteLine(s);
            }
            return sw.ToString();
        }

        // 最初のトークンを取得
        public static string GetFirstToken(string str)
        {
            return GetFirstToken(str, new char[] { ' ', '\t', '　', });
        }
        public static string GetFirstToken(string str, params char[] sps)
        {
            try
            {
                string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 1)
                {
                    return tokens[0];
                }
            }
            catch
            {
            }
            return "";
        }

        // 最後のトークンを取得
        public static string GetLastToken(string str)
        {
            return GetLastToken(str, new char[] { ' ', '\t', '　', });
        }
        public static string GetLastToken(string str, params char[] sps)
        {
            try
            {
                string[] tokens = str.Split(sps, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 1)
                {
                    return tokens[tokens.Length - 1];
                }
            }
            catch
            {
            }
            return "";
        }
        // 空かどうか調べる
        [MethodImpl(Inline)]
        public static bool IsEmptyStr([NotNullWhen(false)] string? s)
        {
            return string.IsNullOrWhiteSpace(s);
        }
        [MethodImpl(Inline)]
        public static bool IsFilledStr([NotNullWhen(true)] string? str)
        {
            return !IsEmptyStr(str);
        }

        // 指定された文字が区切り文字に該当するかどうかチェックする
        public static bool IsSplitChar(char c, string splitStr = Consts.Strings.DefaultSplitStr)
        {
            splitStr = splitStr._FilledOrDefault(Consts.Strings.DefaultSplitStr);

            return (splitStr.IndexOf(c, StringComparison.OrdinalIgnoreCase) != -1);
        }

        // QueryString をパースする
        public static QueryStringList ParseQueryString(string src, Encoding? encoding = null)
        {
            return new QueryStringList(src, encoding);
        }

        // 文字列から URL と QueryString を分離する
        public static void SplitUrlAndQueryString(string src, out string url, out string queryString)
        {
            src = src._NonNull();

            int i = src.IndexOf('?');
            if (i == -1)
            {
                url = src;
                queryString = "";
            }
            else
            {
                url = src.Substring(0, i);
                queryString = src.Substring(i + 1);
            }
        }

        // 文字列からキーと値を取得する
        public static bool GetKeyAndValue(string str, out string key, out string value, string splitStr = Consts.Strings.DefaultKeyAndValueSplitStr)
        {
            uint mode = 0;
            string keystr = "", valuestr = "";

            splitStr = splitStr._FilledOrDefault(Consts.Strings.DefaultKeyAndValueSplitStr);

            foreach (char c in str)
            {
                switch (mode)
                {
                    case 0:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 1;
                            keystr += c;
                        }
                        break;

                    case 1:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            keystr += c;
                        }
                        else
                        {
                            mode = 2;
                        }
                        break;

                    case 2:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 3;
                            valuestr += c;
                        }
                        break;

                    case 3:
                        valuestr += c;
                        break;
                }
            }

            if (mode == 0)
            {
                value = "";
                key = "";
                return false;
            }
            else
            {
                value = valuestr;
                key = keystr;
                return true;
            }
        }

        // 文字列からキー (複数) と値を取得する
        public static bool GetKeysListAndValue(string str, int numKeys, out List<string> keys, out string value, string splitStr = Consts.Strings.DefaultKeyAndValueSplitStr)
        {
            uint mode = 0;
            string valuestr = "";
            List<string> keysList = new List<string>();

            string currentKeyStr = "";

            if (numKeys <= 0) throw new ArgumentOutOfRangeException(nameof(numKeys));

            splitStr = splitStr._FilledOrDefault(Consts.Strings.DefaultKeyAndValueSplitStr);

            foreach (char c in str)
            {
                switch (mode)
                {
                    case 0:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 1;
                            currentKeyStr += c;
                        }
                        break;

                    case 1:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            currentKeyStr += c;
                        }
                        else
                        {
                            if (keysList.Count < (numKeys - 1))
                            {
                                keysList.Add(currentKeyStr);
                                currentKeyStr = "";
                                mode = 0;
                            }
                            else
                            {
                                mode = 2;
                            }
                        }
                        break;

                    case 2:
                        if (IsSplitChar(c, splitStr) == false)
                        {
                            mode = 3;
                            valuestr += c;
                        }
                        break;

                    case 3:
                        valuestr += c;
                        break;
                }
            }

            if (currentKeyStr.Length >= 1)
            {
                keysList.Add(currentKeyStr);
            }

            if (keysList.Count < numKeys)
            {
                value = "";
                keys = new List<string>();
                return false;
            }
            else
            {
                value = valuestr;
                keys = keysList;
                return true;
            }
        }
        // 文字列比較
        public static int StrCmpRetInt(string s1, string s2)
        {
            return string.Compare(s1, s2, StringComparison.Ordinal);
        }
        public static int StrCmpiRetInt(string s1, string s2)
        {
            return string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase);
        }

        // 文字列がリストに含まれるかどうか比較
        public static bool IsStrInList(string str, params string[] args)
        {
            return IsStrInList(str, true, args);
        }
        public static bool IsStrInList(string str, bool ignoreCase, params string[] args)
        {
            foreach (string s in args)
            {
                if (ignoreCase)
                {
                    if (StrCmpi(str, s))
                    {
                        return true;
                    }
                }
                else
                {
                    if (StrCmp(str, s))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // double かどうか取得
        public static bool IsDouble(string? str)
        {
            double v;
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");
            return double.TryParse(str, out v);
        }

        // long かどうか取得
        public static bool IsLong(string? str)
        {
            long v;
            Str.RemoveSpaceChar(ref str);
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");
            return long.TryParse(str, out v);
        }

        // int かどうか取得
        public static bool IsInt(string? str)
        {
            int v;
            Str.RemoveSpaceChar(ref str);
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");
            return int.TryParse(str, out v);
        }

        // 数値かどうか取得
        public static bool IsNumber(string? str)
        {
            str = str._NonNullTrim();
            Str.RemoveSpaceChar(ref str);
            Str.NormalizeString(ref str, true, true, false, false);
            str = str.Replace(",", "");

            if (str._IsEmpty()) return false;

            foreach (char c in str)
            {
                if (IsNumber(c) == false)
                {
                    return false;
                }
            }

            return true;
        }
        public static bool IsNumber(char c)
        {
            if (c >= '0' && c <= '9')
            {
            }
            else if (c == '-')
            {
            }
            else
            {
                return false;
            }

            return true;
        }

        // 文字列の両端が指定された文字の場合は削除
        public static string TrimBothSideChar(string? src, char c1, char c2)
        {
            src = src._NonNull();

            int len = src.Length;

            if (len >= 2)
            {
                if (src[0] == c1 && src[len - 1] == c2)
                {
                    src = src.Substring(1, len - 2);
                }
            }

            return src;
        }

        // 指定した文字列が含まれているかどうかチェック
        public static bool InStr(string str, string keyword, bool caseSensitive = false)
        {
            if (str.IndexOf(keyword, (caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) == -1)
            {
                return false;
            }

            return true;
        }

        public static string MakeStrArray(string str, int count, string sepstr = "")
        {
            sepstr = sepstr._NonNull();
            StringWriter w = new StringWriter();

            for (int i = 0; i < count; i++)
            {
                w.Write(str);

                if (i != (count - 1))
                {
                    w.Write(sepstr);
                }
            }

            return w.ToString();
        }

        // 指定した文字の列を生成する
        public static string MakeCharArray(char c, int len)
        {
            return new string(c, len);
        }

        // 最後の改行を削除する
        public static string RemoveLastCrlf(string? str)
        {
            if (str == null) return "";
            if (str.Length >= 1 && str[str.Length - 1] == 10)
                str = str.Substring(0, str.Length - 1);
            if (str.Length >= 1 && str[str.Length - 1] == 13)
                str = str.Substring(0, str.Length - 1);
            return str;
        }

        // 改行コードを正規化する
        [return: NotNullIfNotNull("str")]
        public static string? NormalizeCrlf(string str, CrlfStyle style = CrlfStyle.LocalPlatform, bool ensureLastLineCrlf = false)
        {
            if (str == null) return null;
            if (style == CrlfStyle.NoChange) return str;
            return NormalizeCrlf(str, Str.GetNewLineBytes(style), ensureLastLineCrlf);
        }
        [return: NotNullIfNotNull("str")]
        public static string NormalizeCrlf(string str, ReadOnlyMemory<byte> crlfData, bool ensureLastLineCrlf = false)
        {
            if (str == null) return "";
            byte[] srcData = Str.Utf8Encoding.GetBytes(str);
            Memory<byte> destData = NormalizeCrlf(srcData, crlfData, ensureLastLineCrlf);
            return Str.Utf8Encoding.GetString(destData.Span);
        }
        public static Memory<byte> NormalizeCrlf(ReadOnlySpan<byte> srcData, CrlfStyle style = CrlfStyle.LocalPlatform, bool ensureLastLineCrlf = false)
        {
            if (style == CrlfStyle.NoChange) return srcData.ToArray();
            return NormalizeCrlf(srcData, Str.GetNewLineBytes(style), ensureLastLineCrlf);
        }
        public static Memory<byte> NormalizeCrlf(ReadOnlySpan<byte> srcData, ReadOnlyMemory<byte> crlfData, bool ensureLastLineCrlf = false)
        {
            FastMemoryBuffer<byte> ret = new FastMemoryBuffer<byte>();
            FastMemoryBuffer<byte> tmp = new FastMemoryBuffer<byte>();

            int i;
            for (i = 0; i < srcData.Length; i++)
            {
                bool isNewLine = false;
                if (srcData[i] == 13)
                {
                    if (i < (srcData.Length - 1) && srcData[i + 1] == 10)
                    {
                        i++;
                    }
                    isNewLine = true;
                }
                else if (srcData[i] == 10)
                {
                    isNewLine = true;
                }

                if (isNewLine)
                {
                    ret.Write(tmp.Span);
                    ret.Write(crlfData);

                    tmp.Clear();
                }
                else
                {
                    tmp.WriteUInt8(srcData[i]);
                }
            }

            if (tmp.Span.Length >= 1)
            {
                ret.Write(tmp.Span);

                if (ensureLastLineCrlf)
                {
                    ret.Write(crlfData);
                }
            }

            return ret.Memory;
        }

        // トークンリストを重複の無いものに変換する
        public static string[] UniqueToken(IEnumerable<string> t)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            List<string> ret = new List<string>();

            foreach (string s in t)
            {
                string key = s.ToUpperInvariant();

                if (o.ContainsKey(key) == false)
                {
                    o.Add(key, new Object());

                    ret.Add(s);
                }
            }

            return ret.ToArray();
        }

        // 数字を文字列に変換して 3 桁ずつカンマで区切る
        public static string ToString3(long v)
        {
            bool neg = false;

            if (v < 0)
            {
                neg = true;
                v = v * (long)-1;
            }

            string tmp, tmp2;
            int i;

            tmp = Str.LongToStr(v);

            tmp2 = "";
            for (i = tmp.Length - 1; i >= 0; i--)
            {
                tmp2 += tmp[i];
            }

            int len = tmp.Length;

            tmp = "";
            for (i = 0; i < len; i++)
            {
                if (i != 0 && (i % 3) == 0)
                {
                    tmp += ",";
                }

                tmp += tmp2[i];
            }

            char[] array = tmp.ToCharArray();
            Array.Reverse(array);

            string str = new string(array);

            if (neg)
            {
                str = "-" + str;
            }

            return str;
        }
        public static string ToString3(BigNumber v)
        {
            bool neg = false;

            if (v < 0)
            {
                neg = true;
                v = v * (long)-1;
            }

            string tmp, tmp2;
            int i;

            tmp = v.ToString();

            tmp2 = "";
            for (i = tmp.Length - 1; i >= 0; i--)
            {
                tmp2 += tmp[i];
            }

            int len = tmp.Length;

            tmp = "";
            for (i = 0; i < len; i++)
            {
                if (i != 0 && (i % 3) == 0)
                {
                    tmp += ",";
                }

                tmp += tmp2[i];
            }

            char[] array = tmp.ToCharArray();
            Array.Reverse(array);

            string str = new string(array);

            if (neg)
            {
                str = "-" + str;
            }

            return str;
        }

        // コマンドラインをビルドする
        public static string BuildCmdLine(IEnumerable<string> args)
        {
            List<string> o = new List<string>();

            foreach (string arg in args)
            {
                if (arg != null)
                {
                    // 1 つの " を 2 つの "" に置換する
                    string tmp = arg._ReplaceStr("\"", "\"\"");

                    if (tmp._InStr(" "))
                    {
                        // 文字列にスペースが含まれる場合は前後を "" で囲む
                        tmp = " " + tmp + " ";
                    }

                    o.Add(tmp);
                }
            }

            return o._Combine(" ");
        }

        // コマンドラインをパースする
        public static string[] ParseCmdLine(string str)
        {
            List<string> o;
            int i, len, mode;
            string tmp;
            bool ignoreSpace = false;
            bool ignoreSpace2 = false;

            o = new List<string>();
            mode = 0;
            len = str.Length;

            tmp = "";

            for (i = 0; i < len; i++)
            {
                char c = str[i];

                switch (mode)
                {
                    case 0:
                        // 次のトークンを発見するモード
                        if (c == ' ' || c == '\t')
                        {
                            // 次の文字へ進める
                        }
                        else
                        {
                            // トークンの開始
                            if (c == '\"')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp += '\"';
                                    i++;
                                }
                                else
                                {
                                    // 1 個の " はスペース無視フラグを有効にする
                                    ignoreSpace = true;
                                }
                            }
                            else if (c == '\'')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\'')
                                {
                                    // 2 重の ' は 1 個の ' 文字として見なす
                                    tmp += '\'';
                                    i++;
                                }
                                else
                                {
                                    // 1 個の ' はスペース無視フラグを有効にする
                                    ignoreSpace2 = true;
                                }
                            }
                            else
                            {
                                tmp += c;
                            }

                            mode = 1;
                        }
                        break;

                    case 1:
                        if (ignoreSpace == false && ignoreSpace2 == false && (c == ' ' || c == '\t'))
                        {
                            // トークンの終了
                            o.Add(tmp);
                            tmp = "";
                            mode = 0;
                        }
                        else
                        {
                            if (c == '\"')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\"')
                                {
                                    // 2 重の " は 1 個の " 文字として見なす
                                    tmp += '\"';
                                    i++;
                                }
                                else
                                {
                                    if (ignoreSpace == false)
                                    {
                                        // 1 個の " はスペース無視フラグを有効にする
                                        ignoreSpace = true;
                                    }
                                    else
                                    {
                                        // スペース無視フラグを無効にする
                                        ignoreSpace = false;
                                    }
                                }
                            }
                            else if (c == '\'')
                            {
                                if ((i != (len - 1)) && str[i + 1] == '\'')
                                {
                                    // 2 重の ' は 1 個の ' 文字として見なす
                                    tmp += '\'';
                                    i++;
                                }
                                else
                                {
                                    if (ignoreSpace2 == false)
                                    {
                                        // 1 個の ' はスペース無視フラグを有効にする
                                        ignoreSpace2 = true;
                                    }
                                    else
                                    {
                                        // スペース無視フラグを無効にする
                                        ignoreSpace2 = false;
                                    }
                                }
                            }
                            else
                            {
                                tmp += c;
                            }
                        }
                        break;
                }
            }

            if (tmp.Length != 0)
            {
                o.Add(tmp);
                tmp = "";
            }

            return o.ToArray();
        }

        public static string GetFirstFilledLineFromLines(string src)
        {
            string[] lines = src._GetLines(true);
            return (lines.FirstOrDefault())._NonNullTrim();
        }

        public static string OneLine(string? s, string splitter = " / ")
        {
            if (s._IsNullOrZeroLen()) return "";

            StringWriter w = new StringWriter();
            string[] lines = Str.GetLines(s);
            int num = 0;
            foreach (string line in lines)
            {
                string ss = line.Trim();

                if (Str.IsEmptyStr(ss) == false)
                {
                    if (num != 0)
                    {
                        if (splitter._IsNullOrZeroLen() == false)
                        {
                            w.Write(splitter);
                        }
                    }
                    w.Write(ss);
                    num++;
                }
            }
            return w.ToString();
        }

        public static string ObjectToXMLSimple_PublicLegacy(object o)
        {
            return ObjectToXMLSimple_PublicLegacy(o, o.GetType());
        }
        public static string ObjectToXMLSimple_PublicLegacy(object o, Type t)
        {
            XmlSerializer xs = new XmlSerializer(t);

            MemoryStream ms = new MemoryStream();
            xs.Serialize(ms, o);

            return Str.Utf8Encoding.GetString(ms.ToArray());
        }

        public static object? XMLToObjectSimple_PublicLegacy(string str, Type t)
        {
            XmlSerializer xs = new XmlSerializer(t);

            MemoryStream ms = new MemoryStream();
            byte[] data = Str.Utf8Encoding.GetBytes(str);
            ms.Write(data, 0, data.Length);
            ms.Position = 0;

            return xs.Deserialize(ms);
        }

        public static string ObjectToXmlStr(object obj, DataContractSerializerSettings? settings = null) => Util.ObjectToXml(obj, settings)._GetString_UTF8();
        public static object? XmlStrToObject(string src, Type type, DataContractSerializerSettings? settings = null) => Util.XmlToObject(src._GetBytes_UTF8(), type, settings);
        public static T? XmlStrToObject<T>(string src, DataContractSerializerSettings? settings = null) => Util.XmlToObject<T>(src._GetBytes_UTF8(), settings);

        public static string ObjectToRuntimeJsonStr(object obj, DataContractJsonSerializerSettings? settings = null) => Util.ObjectToRuntimeJson(obj, settings)._GetString_UTF8();
        public static object? RuntimeJsonStrToObject(string src, Type type, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject(src._GetBytes_UTF8(), type, settings);
        public static T? RuntimeJsonStrToObject<T>(string src, DataContractJsonSerializerSettings? settings = null) => Util.RuntimeJsonToObject<T>(src._GetBytes_UTF8(), settings);

        public static string BuildHttpUrl(string protocol, string host, int port, string localPath)
        {
            protocol = Util.NormalizeHttpProtocolString(protocol);
            int defaultPort = Util.GetHttpProtocolDefaultPort(protocol);

            localPath = localPath._NonNull();

            if (host._IsEmpty()) throw new ArgumentNullException(nameof(host));

            if (port == 0) port = defaultPort;

            StringBuilder sb = new StringBuilder();
            sb.Append(protocol);
            sb.Append("://");
            sb.Append(host);

            if (defaultPort != port)
            {
                sb.Append(":");
                sb.Append(port.ToString());
            }

            sb.Append("/");

            if (localPath.StartsWith("/")) localPath = localPath.Substring(1);

            sb.Append(localPath);

            return sb.ToString();
        }

        public static void ParseUrl(string urlString, out Uri uri, out QueryStringList queryString, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (urlString._IsEmpty()) throw new ApplicationException("url_string is empty.");
            if (urlString.StartsWith("/")) urlString = "http://null" + urlString;
            uri = new Uri(urlString);
            queryString = uri.Query._ParseQueryString(encoding);
        }

        public static bool TryParseUrl(string urlString, out Uri uri, out QueryStringList queryString, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;
            if (urlString._IsEmpty()) throw new ApplicationException("url_string is empty.");
            if (urlString.StartsWith("/")) urlString = "http://null" + urlString;
            if (Uri.TryCreate(urlString, UriKind.Absolute, out uri!))
            {
                queryString = uri.Query._ParseQueryString(encoding);
                return true;
            }
            else
            {
                uri = null!;
                queryString = null!;
                return false;
            }
        }

        public static List<string> ParseUrlQueryKeywords(string urlString)
        {
            return null!;
        }

        public static string NormalizeFqdn(string fqdn)
        {
            fqdn = fqdn._NonNullTrim().ToLowerInvariant();
            return fqdn.Split(".", StringSplitOptions.RemoveEmptyEntries)._Combine(".");
        }

        public static bool CheckFqdn(string fqdn)
        {
            try
            {
                if (fqdn.Length > 255) return false;
                string[] tokens = fqdn.Split(".", StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length <= 1) return false;
                foreach (string token in tokens)
                {
                    string token2 = token.ToLowerInvariant();
                    if (token2.Length > 63) return false;
                    foreach (char c in token2)
                    {
                        if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c == '-')) { }
                        else
                        {
                            return false;
                        }
                    }
                }

                if (IPAddress.TryParse(fqdn, out _))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetSimpleHostnameFromFqdn(string fqdn)
        {
            fqdn = fqdn._NonNullTrim();
            if (fqdn._IsEmpty()) return "";
            return fqdn.Split(".", StringSplitOptions.RemoveEmptyEntries)[0];
        }

        public static bool TryTrimStartWith(string srcStr, out string outStr, StringComparison comparison, params string[] keys)
        {
            outStr = srcStr;
            if (keys.Length == 0 || keys == null) return false;

            foreach (string key in keys)
            {
                if (key._IsEmpty() == false)
                {
                    if (srcStr.StartsWith(key, comparison))
                    {
                        outStr = srcStr.Substring(key.Length);
                        return true;
                    }
                }
            }

            return false;
        }

        public static long CalcKeywordMatchPoint(string targetStr, string keyword, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            targetStr = targetStr._NonNullTrim();
            keyword = keyword._NonNullTrim();

            if (targetStr._IsSame(keyword, comparison))
            {
                return Consts.Numbers.MaxMatchPoint;
            }

            if (keyword._IsEmpty())
            {
                return 0;
            }

            int originalLen = targetStr.Length;

            if (originalLen >= 1)
            {
                if (targetStr.IndexOf(keyword, comparison) != -1)
                {
                    int replacedLen = targetStr.Replace(keyword, "", comparison).Length;
                    int matchLen = originalLen - replacedLen;

                    matchLen = Math.Min(matchLen, originalLen);

                    long v = (long)matchLen * Consts.Numbers.MaxMatchPoint / (long)originalLen;

                    return v;
                }
                else if (keyword.IndexOf(targetStr, comparison) != -1)
                {
                    int replacedLen = keyword.Replace(targetStr, "", comparison).Length;
                    int matchLen = keyword.Length - replacedLen;

                    matchLen = Math.Min(matchLen, keyword.Length);

                    long v = (long)matchLen * Consts.Numbers.MaxMatchPoint2 / (long)keyword.Length;

                    return v;
                }
            }

            return 0;
        }

        public static string StripCommentFromLine(string srcLine, IEnumerable<string>? commentStartStrList = null)
        {
            if (srcLine == null) return "";
            if (commentStartStrList == null) commentStartStrList = Consts.Strings.CommentStartString;

            int minStart = int.MaxValue;

            foreach (string keyword in commentStartStrList)
            {
                int i = srcLine.IndexOf(keyword);
                if (i != -1)
                {
                    minStart = Math.Min(i, minStart);
                }
            }

            if (minStart != int.MaxValue)
            {
                string ret = srcLine.Substring(0, minStart);

                ret = ret.TrimEnd();

                return ret;
            }
            else
            {
                return srcLine;
            }
        }

        public static string GetTimeSpanStr(TimeSpan ts)
        {
            if (ts.Ticks < 0) ts = new TimeSpan(0);

            if (ts.TotalDays >= 1)
                return $"{ts.Days}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
            else if (ts.TotalHours >= 1)
                return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
            else if (ts.Minutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            else
                return $"{ts.Seconds}s";
        }

        public static string GetTimeSpanStr(long tick)
            => GetTimeSpanStr(new TimeSpan(tick));

        // 文字列を特殊文字で区切る ただし特殊文字が 2 つ続いた場合は元の 1 つの特殊文字であるとみなす
        // 例: abc;def => [abc], [def]
        // 例: abc;;def;ghi => [abc;def], [ghi]
        public static IReadOnlyList<string> SplitBySpecialChar(string str, char sp = ';')
        {
            str = str._NonNullTrim();

            int mode = 0;

            StringBuilder sb = new StringBuilder();

            List<string> ret = new List<string>();

            foreach (char c in str)
            {
                if (mode == 0)
                {
                    if (c != sp)
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        mode = 1;
                    }
                }
                else if (mode == 1)
                {
                    if (c == sp)
                    {
                        // 連続 2 文字 = 1 文字
                        sb.Append(c);
                    }
                    else
                    {
                        // 区切り検出
                        string s = sb.ToString();
                        if (s._IsFilled())
                        {
                            ret.Add(sb.ToString());
                        }
                        sb.Clear();
                        sb.Append(c);
                    }

                    mode = 0;
                }
            }

            string t = sb.ToString();
            if (t._IsFilled())
            {
                ret.Add(t);
            }

            return ret;
        }

        // 文字コード自動判別機
        private static class StrEncodingAutoDetectorInternal
        {
            public readonly static string UnknownStr = "{" + Str.NewGuid().ToLowerInvariant() + "}";

            public readonly static List<Encoding> EncodingsWithUnknownStrReplacement = new List<Encoding>();
            public readonly static List<Encoding> EncodingsNormal = new List<Encoding>();

            static StrEncodingAutoDetectorInternal()
            {
                string[] names = Consts.StrEncodingAutoDetector.Candidates._Split(StringSplitOptions.RemoveEmptyEntries, " ", "　", ",", "/", "\t", "\r", "\n");

                foreach (string name in names)
                {
                    Encoding encWithError = Encoding.GetEncoding(name, new EncoderReplacementFallback(UnknownStr), new DecoderReplacementFallback(UnknownStr));
                    Encoding enc = Encoding.GetEncoding(name);

                    EncodingsWithUnknownStrReplacement.Add(encWithError);
                    EncodingsNormal.Add(enc);
                }
            }
        }

        public static List<Encoding> GetSuitableEncodings(ReadOnlySpan<byte> data)
        {
            List<Encoding> ret = new List<Encoding>();

            for (int i = 0; i < StrEncodingAutoDetectorInternal.EncodingsWithUnknownStrReplacement.Count; i++)
            {
                try
                {
                    var encWithError = StrEncodingAutoDetectorInternal.EncodingsWithUnknownStrReplacement[i];
                    var encNormal = StrEncodingAutoDetectorInternal.EncodingsNormal[i];

                    bool isShiftJis = encWithError.WebName._IsSamei("shift_jis");

                    string tmp = encWithError.GetString(data);

                    if (tmp.IndexOf(StrEncodingAutoDetectorInternal.UnknownStr) == -1)
                    {
                        char[] charArray = tmp.ToCharArray();
                        bool ok = true;

                        foreach (var c in charArray)
                        {
                            if (c <= 127)
                            {
                                continue;
                            }

                            if (Char.IsControl(c))
                            {
                                ok = false;
                            }

                            var category = Char.GetUnicodeCategory(c);
                            if (category == UnicodeCategory.Control || category == UnicodeCategory.OtherNotAssigned || category == UnicodeCategory.Surrogate)
                            {
                                ok = false;
                            }

                            if (isShiftJis)
                            {
                                if (c > 0x9FCF && c < 0xff00)
                                {
                                    ok = false;
                                }
                            }

                            if (ok == false) break;
                        }

                        var data2 = NormalizeCrlf(data, CrlfStyle.Lf);

                        string test = encWithError.GetString(data2.Span);
                        byte[] data3 = encNormal.GetBytes(test);

                        if (data2._MemCompare(data3) != 0)
                        {
                            ok = false;
                        }

                        if (ok)
                        {
                            ret.Add(StrEncodingAutoDetectorInternal.EncodingsNormal[i]);
                        }
                    }
                }
                catch { }
            }

            return ret;
        }
    }

    public class AmbiguousSearchResult<T>
    {
        public AmbiguousSearchResult(string key, T value, long matchPoint, int index)
        {
            this.Key = key;
            this.Value = value;
            this.MatchPoint = matchPoint;
            this.Index = index;
        }

        public string Key { get; }
        public T Value { get; }
        public long MatchPoint { get; }
        public int Index { get; }
    }

    public class AmbiguousSearch<T> where T : class
    {
        readonly List<KeyValuePair<string, T>> List = new List<KeyValuePair<string, T>>();
        readonly Singleton<string, T> SearchTopWithCacheSingleton;

        public bool AllowWildcard { get; }

        public AmbiguousSearch(bool allowWildcard = false)
        {
            this.AllowWildcard = allowWildcard;

            this.SearchTopWithCacheSingleton = new Singleton<string, T>(
                x => this.SearchTop(x)!,
                StrComparer.IgnoreCaseComparer);
        }

        public void Add(string key, T value)
        {
            key = key._NonNullTrim();

            this.List.Add(new KeyValuePair<string, T>(key, value));
        }

        public IEnumerable<AmbiguousSearchResult<T>> Search(string key, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            List<AmbiguousSearchResult<T>> ret = new List<AmbiguousSearchResult<T>>();

            for (int i = 0; i < List.Count; i++)
            {
                KeyValuePair<string, T> t = List[i];

                long point = t.Key._CalcKeywordMatchPoint(key, comparison);
                if (point >= 1)
                {
                    AmbiguousSearchResult<T> r = new AmbiguousSearchResult<T>(t.Key, t.Value, point, i);

                    ret.Add(r);
                }
                else
                {
                    if (this.AllowWildcard && t.Key == "*")
                    {
                        AmbiguousSearchResult<T> r = new AmbiguousSearchResult<T>(t.Key, t.Value, 0, i);

                        ret.Add(r);
                    }
                }
            }

            return ret.OrderByDescending(x => x.MatchPoint).ThenBy(x => x.Key, StrComparer.Get(comparison)).ThenBy(x => x.Index);
        }

        public T? SearchTop(string key, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            AmbiguousSearchResult<T>? x = this.Search(key, comparison).FirstOrDefault();

            if (x == null)
            {
                return default!;
            }

            return x.Value;
        }

        public T? SearchTopWithCache(string key)
        {
            return SearchTopWithCacheSingleton[key];
        }
    }

    namespace Legacy
    {
        public class XmlCheckObjectInternal
        {
            public string? Str = null;
        }

        // 文字列トークン操作
        public class StrToken
        {
            string[] tokens;

            public string[] Tokens
            {
                get { return tokens; }
            }

            public string this[uint index]
            {
                get { return tokens[index]; }
            }

            public uint NumTokens
            {
                get
                {
                    return (uint)Tokens.Length;
                }
            }

            public StrToken(string[] tokens)
            {
                List<string> a = new List<string>();
                foreach (string s in tokens)
                {
                    a.Add(s);
                }

                this.tokens = a.ToArray();
            }

            public StrToken(string str, string splitStr = Consts.Strings.DefaultSplitStr)
            {
                // トークンの切り出し
                splitStr = splitStr._FilledOrDefault(Consts.Strings.DefaultSplitStr);

                int i, len;
                len = splitStr.Length;
                char[] chars = new char[len];
                for (i = 0; i < len; i++)
                {
                    chars[i] = splitStr[i];
                }
                tokens = str.Split(chars, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        // 文字列を各種のデータ型に変換
        public class StrData
        {
            public string StrValue { get; }

            public uint IntValue => Str.StrToUInt(StrValue);

            public ulong Int64Value => Str.StrToULong(StrValue);

            public bool BoolValue
            {
                get
                {
                    string s = StrValue.Trim();

                    if (Str.IsEmptyStr(s))
                    {
                        return false;
                    }
                    if (s.StartsWith("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if ("true".StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (s.StartsWith("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if ("yes".StartsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (Str.StrToUInt(s) != 0)
                    {
                        return true;
                    }

                    return false;
                }
            }

            public StrData(string? str)
            {
                str = str._NonNull();

                StrValue = str;
            }
        }
    }

    public class StrClass
    {
        public string Value { get; } = "";

        public StrClass() { }

        public StrClass(string str)
        {
            this.Value = str;
        }

        public override string ToString()
        {
            return this.Value;
        }

        public static implicit operator string(StrClass s)
        {
            return s.Value;
        }

        public static implicit operator StrClass(string s)
        {
            return new StrClass(s);
        }
    }

    public class OneLineParams : KeyValueList<string, string>
    {
        public char Delimiter { get; }

        public OneLineParams(string oneLine = "", char delimiter = ';', bool treatDoubleDelimiterAsEscape = true)
        {
            oneLine = oneLine._NonNullTrim();

            // Parse
            this.Delimiter = delimiter;

            oneLine = Str.DecodeCEscape(oneLine);

            IReadOnlyList<string> list;

            if (treatDoubleDelimiterAsEscape)
                list = Str.SplitBySpecialChar(oneLine, this.Delimiter);
            else
                list = oneLine._Split(StringSplitOptions.RemoveEmptyEntries, delimiter);

            foreach (string token in list)
            {
                Str.GetKeyAndValue(token, out string key, out string value, "=:");

                if (key._IsFilled())
                {
                    key = key._NonNullTrim();
                    value = value._NonNullTrim();

                    this.Add(key, value);
                }
            }
        }

        public override string ToString()
        {
            // Combine
            List<string> o = new List<string>();

            foreach (var kv in this)
            {
                if (kv.Key._IsFilled())
                {
                    string tmp;

                    if (kv.Value._IsEmpty())
                    {
                        tmp = kv.Key;
                    }
                    else
                    {
                        tmp = kv.Key + "=" + kv.Value;
                    }

                    tmp = tmp._ReplaceStr("" + this.Delimiter, "" + this.Delimiter + this.Delimiter, true);

                    o.Add(tmp);
                }
            }

            string oneLine = o._Combine("" + this.Delimiter + " ");

            oneLine = Str.EncodeCEscape(oneLine);

            return oneLine;
        }
    }

    public readonly struct IgnoreCase
    {
        // Thanks to the great idea: https://stackoverflow.com/questions/631233/is-there-a-c-sharp-case-insensitive-equals-operator
        readonly string? Value;

        public IgnoreCase(string? value)
        {
            this.Value = value;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null && this.Value == null) return true;
            if (obj == null) return false;
            if (ReferenceEquals(this.Value, obj)) return true;
            if (obj is IgnoreCase target)
            {
                return this == target;
            }
            else if (obj is string s)
            {
                return this == (IgnoreCase)s;
            }
            else
            {
                string? s2 = obj.ToString();
                return this == s2;
            }
        }

        public override int GetHashCode()
        {
            return Value?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
        }

        public static bool operator ==(IgnoreCase a, IgnoreCase b)
        {
            if ((object)a == null && (object)b == null) return true;
            if ((object)a == null || (object)b == null) return false;

            return a.Value._IsSamei(b.Value);
        }

        public static bool operator !=(IgnoreCase a, IgnoreCase b)
        {
            return !(a == b);
        }

        public static implicit operator string?(IgnoreCase s)
        {
            return s.Value;
        }

        public static implicit operator IgnoreCase(string? s)
        {
            return new IgnoreCase(s);
        }

        public override string? ToString() => this.Value;
    }

    public readonly struct Trim
    {
        // Thanks to the great idea: https://stackoverflow.com/questions/631233/is-there-a-c-sharp-case-insensitive-equals-operator
        readonly string? Value;

        public Trim(string? value)
        {
            this.Value = value;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null && this.Value == null) return true;
            if (obj == null) return false;
            if (ReferenceEquals(this.Value, obj)) return true;
            if (obj is Trim target)
            {
                return this == target;
            }
            else if (obj is string s)
            {
                return this == (Trim)s;
            }
            else
            {
                string? s2 = obj.ToString();
                return this == s2;
            }
        }

        public override int GetHashCode()
        {
            return Value?._NonNullTrim().GetHashCode() ?? 0;
        }

        public static bool operator ==(Trim a, Trim b)
        {
            if ((object)a == null && (object)b == null) return true;
            if ((object)a == null || (object)b == null) return false;

            return a.Value._IsSameTrim(b.Value);
        }

        public static bool operator !=(Trim a, Trim b)
        {
            return !(a == b);
        }

        public static implicit operator string?(Trim s)
        {
            return s.Value;
        }

        public static implicit operator Trim(string? s)
        {
            return new Trim(s);
        }

        public override string? ToString() => this.Value;
    }

    public readonly struct IgnoreCaseTrim
    {
        // Thanks to the great idea: https://stackoverflow.com/questions/631233/is-there-a-c-sharp-case-insensitive-equals-operator
        readonly string? Value;

        public IgnoreCaseTrim(string? value)
        {
            this.Value = value;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null && this.Value == null) return true;
            if (obj == null) return false;
            if (ReferenceEquals(this.Value, obj)) return true;
            if (obj is IgnoreCaseTrim target)
            {
                return this == target;
            }
            else if (obj is string s)
            {
                return this == (IgnoreCaseTrim)s;
            }
            else
            {
                string? s2 = obj.ToString();
                return this == s2;
            }
        }

        public override int GetHashCode()
        {
            return Value?._NonNullTrim().GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
        }

        public static bool operator ==(IgnoreCaseTrim a, IgnoreCaseTrim b)
        {
            if ((object)a == null && (object)b == null) return true;
            if ((object)a == null || (object)b == null) return false;

            return a.Value._IsSameTrimi(b.Value);
        }

        public static bool operator !=(IgnoreCaseTrim a, IgnoreCaseTrim b)
        {
            return !(a == b);
        }

        public static implicit operator string?(IgnoreCaseTrim s)
        {
            return s.Value;
        }

        public static implicit operator IgnoreCaseTrim(string? s)
        {
            return new IgnoreCaseTrim(s);
        }

        public override string? ToString() => this.Value;
    }

    public readonly struct IsEmpty
    {
        readonly string? Target;

        public IsEmpty(string? target)
        {
            this.Target = target;
        }

        public static implicit operator bool(IsEmpty t)
        {
            return t.Target._IsEmpty();
        }

        public static implicit operator IsEmpty(string? target)
        {
            return new IsEmpty(target);
        }

        public override string ToString() => ((bool)this).ToString();
    }

    public readonly struct IsFilled
    {
        readonly string? Target;

        public IsFilled(string? target)
        {
            this.Target = target;
        }

        public static implicit operator bool(IsFilled t)
        {
            return t.Target._IsFilled();
        }

        public static implicit operator IsFilled(string? target)
        {
            return new IsFilled(target);
        }

        public override string ToString() => ((bool)this).ToString();
    }


    public class QueryStringList : KeyValueList<string, string>
    {
        public QueryStringList() { }

        public QueryStringList(string queryString, Encoding? encoding = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;

            queryString = queryString._NonNull();

            // 先頭に ? があれば無視する
            if (queryString.StartsWith("?")) queryString = queryString.Substring(1);

            // ハッシュ文字 # があればそれ以降は無視する
            int i = queryString.IndexOf('#');
            if (i != -1) queryString = queryString.Substring(0, i);

            // & で分離する
            string[] tokens = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                // key と value を取得する
                string key, value;

                i = token.IndexOf('=');

                if (i == -1)
                {
                    key = token;
                    value = "";
                }
                else
                {
                    key = token.Substring(0, i);
                    value = token.Substring(i + 1);
                }

                // key と value を URL デコードする
                key = Str.DecodeUrl(key, encoding);
                value = Str.DecodeUrl(value, encoding);

                this.Add(key, value);
            }
        }

        public override string ToString()
            => ToString(null);

        public string ToString(Encoding? encoding, UrlEncodeParam? urlEncodeParam = null)
        {
            if (encoding == null) encoding = Str.Utf8Encoding;

            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < this.Count; i++)
            {
                var kv = this[i];
                bool isLast = (i == (this.Count - 1));

                if (kv.Key._IsFilled() || kv.Value._IsFilled())
                {
                    string key = kv.Key._NonNull();
                    string value = kv.Value._NonNull();

                    // key と value を URL エンコードする
                    key = key._EncodeUrl(encoding, urlEncodeParam);
                    value = value._EncodeUrl(encoding, urlEncodeParam);

                    if (value._IsEmpty())
                    {
                        sb.Append(key);
                    }
                    else
                    {
                        sb.Append(key);
                        sb.Append('=');
                        sb.Append(value);
                    }

                    if (isLast == false)
                    {
                        sb.Append('&');
                    }
                }
            }

            return sb.ToString();
        }

        public void RemoveItem(string key)
        {
            this.RemoveWhenKey(key, StrComparer.IgnoreCaseTrimComparer);
        }

        public void AddOrUpdateKeyItemSingle(string key, string value)
        {
            this.AddOrUpdateKeyValueSingle(key, value, StrComparer.IgnoreCaseTrimComparer);
        }
    }


    // ユニークな文字列のリスト。StartWith による包含関係なしが保証されている
    public class LongestDistinctStrList
    {
        readonly StringComparison Comparison;

        readonly List<string> List = new List<string>();

        public LongestDistinctStrList(StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            Comparison = comparison;
        }

        public void Add(string str)
        {
            str = str._NonNullTrim();

            // 全く同一の文字列がリストに含まれるか?
            if (List.Where(t => string.Equals(t, str, Comparison)).Any()) return;

            // 追加しようとする文字列よりも長く、追加しようとする文字列を包含する文字列がリストに含まれるか?
            foreach (var s in List)
            {
                if (s.Length > str.Length)
                {
                    if (s.StartsWith(str, Comparison)) return;
                }
            }

            // 追加しようとする文字列よりも短く、追加しようとする文字列によって包含される文字列がリストに含まれるか?
            for (int i = 0; i < List.Count; i++)
            {
                if (List[i].Length < str.Length)
                {
                    if (str.StartsWith(List[i], Comparison))
                    {
                        // 置換
                        List[i] = str;
                        return;
                    }
                }
            }

            // いずれにも該当しない
            List.Add(str);
        }

        public IReadOnlyList<string> GetList() => this.List;

        public static IReadOnlyList<string> Normalize(IEnumerable<string> srcList, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            LongestDistinctStrList o = new LongestDistinctStrList(comparison);

            srcList._DoForEach(x => o.Add(x));

            return o.GetList();
        }
    }

    // StrTable 言語
    public class StrTableLanguage
    {
        public string Key { get; }
        public string Name_English { get; }
        public string Name_Local { get; }
        public int WindowsLocaleId { get; }
        public HashSet<string> UnixLocalesList { get; } = new HashSet<string>();
        public HashSet<string> HttpAcceptLanguageList { get; } = new HashSet<string>();
        public StrTable Table { get; }

        public StrTableLanguage(string key, string name_English, string name_Local, int windowsLocaleId, string unixLocalesStrList, string httpAcceptLanguageStartWithList, StrTable table)
        {
            Key = key._NonNullTrim().ToLowerInvariant();
            Name_English = name_English;
            Name_Local = name_Local;
            WindowsLocaleId = windowsLocaleId;

            unixLocalesStrList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",")._DoForEach(x => this.UnixLocalesList.Add(x.ToLowerInvariant()));
            httpAcceptLanguageStartWithList._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ",")._DoForEach(x => this.HttpAcceptLanguageList.Add(x.ToLowerInvariant()));

            this.Table = table;
        }
    }

    // StrTable 言語リスト
    public class StrTableLanguageList
    {
        readonly CriticalSection<StrTableLanguageList> Lock = new CriticalSection<StrTableLanguageList>();
        readonly List<StrTableLanguage> LaugnageList = new List<StrTableLanguage>();
        readonly Singleton<string, StrTableLanguage> ByKeyCache;
        readonly Singleton<string, StrTableLanguage> ByHttpAcceptLanguageCache;

        public static StrTableLanguageList DefaultEmptyStrTableLanguageList { get; }

        static StrTableLanguageList()
        {
            DefaultEmptyStrTableLanguageList = new StrTableLanguageList();
            DefaultEmptyStrTableLanguageList.Add(new StrTableLanguage("zz", "Neutral", "Neutral", 0, "", "", new StrTable()));
        }

        public StrTableLanguageList()
        {
            ByKeyCache = new Singleton<string, StrTableLanguage>(key =>
            {
                key = key._NonNullTrim().ToLowerInvariant()._TruncStr(2);

                lock (this.Lock)
                {
                    if (key._IsEmpty()) return this.LaugnageList[0];

                    foreach (var item in this.LaugnageList)
                    {
                        if (item.Key == key)
                        {
                            return item;
                        }
                    }
                }

                return this.LaugnageList[0];
            });

            ByHttpAcceptLanguageCache = new Singleton<string, StrTableLanguage>(language =>
            {
                language = language._NonNullTrim().ToLowerInvariant()._TruncStr(2);

                lock (this.Lock)
                {
                    if (language._IsEmpty()) return this.LaugnageList[0];

                    foreach (var item in this.LaugnageList)
                    {
                        if (item.HttpAcceptLanguageList.Contains(language))
                        {
                            return item;
                        }
                    }
                }

                return this.LaugnageList[0];
            });
        }

        public IEnumerable<StrTableLanguage> GetLaugnageList()
        {
            lock (this.Lock)
            {
                return this.LaugnageList.ToArray();
            }
        }

        public void Add(StrTableLanguage language)
        {
            lock (this.Lock)
            {
                this.LaugnageList.Add(language);
            }
        }

        public StrTableLanguage DefaultLangage => FindDefaultLanguage();

        public StrTableLanguage FindDefaultLanguage()
            => FindLanguageByKey("");

        public StrTableLanguage FindLanguageByKey(string key)
        {
            key = key._NonNullTrim().ToLowerInvariant()._TruncStr(2);
            return this.ByKeyCache[key];
        }

        public StrTableLanguage FindLanguageByHttpAcceptLanguage(string language)
        {
            language = language._NonNullTrim().ToLowerInvariant()._TruncStr(2);
            return this.ByHttpAcceptLanguageCache[language];
        }
    }

    // StrTable
    public class StrTable
    {
        readonly Dictionary<string, string> EntryList = new Dictionary<string, string>();
        readonly KeyValueList<string, string> ReplaceList = new KeyValueList<string, string>();

        string CurrentPrefix = "";

        public StrTable(params string[] bodyList)
        {
            if (bodyList != null)
            {
                foreach (string body in bodyList)
                {
                    this.ImportLines(body._GetLines());
                }
            }
        }

        public string this[string key]
        {
            get => GetStr(key);
        }

        public KeyValueList<string, string> ToList()
        {
            KeyValueList<string, string> ret = new KeyValueList<string, string>();

            foreach (var item in this.EntryList)
            {
                ret.Add(item);
            }

            ret.Sort((x, y) => x.Key._Cmpi(y.Key));

            return ret;
        }

        public string GetStr(string key, string notFoundValue = "")
        {
            key = key._NonNullTrim().ToUpperInvariant();

            return this.EntryList._GetOrDefault(key, notFoundValue)._NonNull();
        }

        public async Task ImportFileAsync(FilePath file, CancellationToken cancel = default)
        {
            string body = await file.ReadStringFromFileAsync(cancel: cancel);

            ImportLines(body._GetLines());
        }
        public void ImportFile(FilePath file, CancellationToken cancel = default)
            => ImportFileAsync(file, cancel)._GetResult();

        public void ImportLines(IEnumerable<string> lines)
        {
            this.CurrentPrefix = "";

            lines._DoForEach(line => ImportLine(line));
        }

        public void ImportLine(string line)
        {
            line = line._NonNull().TrimStart(' ', '\t');
            if (line._IsEmpty()) return;
            if (line[0] == '#' || (line[0] == '/' && line[1] == '/')) return;

            Str.GetKeyAndValue(line, out string key, out string value);
            if (key._IsEmpty()) return;

            key = key.ToUpperInvariant();

            value = UnescapeStr(value);

            if (key == "PREFIX")
            {
                value = value._NonNullTrim();

                if (value == "$" || value._IsSamei("NULL"))
                {
                    value = "";
                }

                this.CurrentPrefix = value;
                return;
            }

            if (this.CurrentPrefix._IsFilled())
            {
                key = this.CurrentPrefix + "@" + key;
            }

            if (key.StartsWith("$") && key.EndsWith("$") && key.Length >= 3)
            {
                if (this.ReplaceList.Where(x => x.Key._IsSamei(key)).Any() == false)
                {
                    this.ReplaceList.Add(key, value);
                }
            }
            else
            {
                if (value._InStr("$"))
                {
                    foreach (var replaceItem in this.ReplaceList)
                    {
                        value = value._ReplaceStr(replaceItem.Key, replaceItem.Value, false);
                    }
                }
            }

            this.EntryList._GetOrNew(key, value);
        }

        public static string UnescapeStr(string str)
        {
            int i, len;
            StringBuilder tmp = new StringBuilder();

            len = str.Length;

            for (i = 0; i < len; i++)
            {
                char c = str[i];
                if (c == '\\')
                {
                    i++;
                    char c2 = ' ';
                    try
                    {
                        c2 = str[i];
                    }
                    catch { }
                    switch (c2)
                    {
                        case '\\':
                            tmp.Append('\\');
                            break;

                        case ' ':
                            tmp.Append(' ');
                            break;

                        case 'n':
                        case 'N':
                            tmp.Append('\n');
                            break;

                        case 'r':
                        case 'R':
                            tmp.Append('\r');
                            break;

                        case 't':
                        case 'T':
                            tmp.Append('\t');
                            break;
                    }
                }
                else
                {
                    tmp.Append(c);
                }
            }

            return tmp.ToString();
        }
    }
}
