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
    PrependFieldName = 4,

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
}

#endif

