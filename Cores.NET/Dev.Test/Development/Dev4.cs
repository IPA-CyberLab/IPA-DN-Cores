// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace IPA.Cores.Basic;

[Flags]
public enum SearchableStrFlag
{
    None = 0,
    IncludeBase64InBinary = 1,
    IncludePrintableAsciiStr = 2,

    Default = IncludeBase64InBinary | IncludePrintableAsciiStr,
}

public static class SSTest
{
    public static string NormalizeStrForSearch(string s)
    {
        s = s.ToLowerInvariant();

        Str.NormalizeString(ref s, false, true, false, true);

        s = s._Split(StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, ' ', '\t', '\r', '\n')._Combine(" ");

        return s;
    }

    public static HashSet<string> GetSearchableStrListFromPrimitiveData(object? o, SearchableStrFlag flag)
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

        return tmp;
    }

    public static List<string> GetSearchableStrListFromObject(object? obj, SearchableStrFlag flag = SearchableStrFlag.Default)
    {
        var walkList = Util.WalkObject(obj);

        List<string> tmp = new List<string>();

        foreach (var item in walkList)
        {
            var set = GetSearchableStrListFromPrimitiveData(item.Data, flag);

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
            dto1 = "2049/12/31 19:18:37"._ToDateTime()._AsDateTimeOffset(true, true),
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

        string ss = GenerateSearchableStrFromObject(t1);

        ss._Print();
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
        public DateTimeOffset dto1;
        public TimeSpan ts1;
        public bool b1;
        public decimal decimal1;
        public double double1;
        public float float1;
        public SSTestFlag1 flag1;
        public IPAddress ip1 = null!;
        public byte[] data1 = null!;
        public Memory<byte> data2;
    }
}

#endif

