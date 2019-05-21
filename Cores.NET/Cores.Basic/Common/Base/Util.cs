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
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Collections;
using System.Reflection.Emit;
using System.Runtime.Serialization.Json;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Immutable;

namespace IPA.Cores.Basic
{
    namespace Legacy
    {
        // 言語一覧
        enum CoreLanguage
        {
            Japanese = 0,
            English = 1,
        }

        // 言語クラス
        class CoreLanguageClass
        {
            public readonly CoreLanguage Language;
            public readonly int Id;
            readonly string name;
            public string Name
            {
                get
                {
                    if (name == "ja")
                    {
                        if (CoreLanguageList.RegardsJapanAsJP)
                        {
                            return "jp";
                        }
                    }

                    return name;
                }
            }
            public readonly string TitleInEnglish;
            public readonly string TitleInNative;

            public CoreLanguageClass(CoreLanguage lang, int id, string name,
                string titleInEnglish, string titleInNative)
            {
                this.Language = lang;
                this.Id = id;
                this.name = name;
                this.TitleInEnglish = titleInEnglish;
                this.TitleInNative = titleInNative;
            }

            public static void SetCurrentThreadLanguageClass(CoreLanguageClass lang)
            {
                ThreadLocalStorage.CurrentThreadData["current_thread_language"] = lang;
            }

            public static CoreLanguageClass CurrentThreadLanguageClass
            {
                get
                {
                    return GetCurrentThreadLanguageClass();
                }

                set
                {
                    SetCurrentThreadLanguageClass(value);
                }
            }

            public static CoreLanguage CurrentThreadLanguage
            {
                get
                {
                    return CurrentThreadLanguageClass.Language;
                }
            }

            public static CoreLanguageClass GetCurrentThreadLanguageClass()
            {
                CoreLanguageClass lang = null;

                try
                {
                    lang = (CoreLanguageClass)ThreadLocalStorage.CurrentThreadData["current_thread_language"];
                }
                catch
                {
                }

                if (lang == null)
                {
                    lang = CoreLanguageList.DefaultLanguage;

                    SetCurrentThreadLanguageClass(lang);
                }

                return lang;
            }
        }

        // 言語リスト
        static class CoreLanguageList
        {
            public static readonly CoreLanguageClass DefaultLanguage;
            public static readonly CoreLanguageClass Japanese;
            public static readonly CoreLanguageClass English;
            public static bool RegardsJapanAsJP = false;

            public static readonly List<CoreLanguageClass> LanguageList = new List<CoreLanguageClass>();

            static CoreLanguageList()
            {
                CoreLanguageList.LanguageList = new List<CoreLanguageClass>();

                CoreLanguageList.Japanese = new CoreLanguageClass(CoreLanguage.Japanese,
                    0, "ja", "Japanese", "日本語");
                CoreLanguageList.English = new CoreLanguageClass(CoreLanguage.English,
                    1, "en", "English", "English");

                CoreLanguageList.DefaultLanguage = CoreLanguageList.Japanese;

                CoreLanguageList.LanguageList.Add(CoreLanguageList.Japanese);
                CoreLanguageList.LanguageList.Add(CoreLanguageList.English);
            }

            public static CoreLanguageClass GetLanguageClassByName(string name)
            {
                Str.NormalizeStringStandard(ref name);

                foreach (CoreLanguageClass c in LanguageList)
                {
                    if (Str.StrCmpi(c.Name, name))
                    {
                        return c;
                    }
                }

                return DefaultLanguage;
            }

            public static CoreLanguageClass GetLangugageClassByEnum(CoreLanguage lang)
            {
                foreach (CoreLanguageClass c in LanguageList)
                {
                    if (c.Language == lang)
                    {
                        return c;
                    }
                }

                return DefaultLanguage;
            }
        }
    }

    class RefInt : IEquatable<RefInt>, IComparable<RefInt>
    {
        public RefInt() : this(0) { }
        public RefInt(int value)
        {
            this.Value = value;
        }
        public volatile int Value;
        public void Set(int value) => this.Value = value;
        public int Get() => this.Value;
        public override string ToString() => this.Value.ToString();
        public int Increment() => Interlocked.Increment(ref this.Value);
        public int Decrement() => Interlocked.Decrement(ref this.Value);

        public override bool Equals(object obj) => obj is RefInt x && this.Value == x.Value;
        public override int GetHashCode() => Value.GetHashCode();

        public bool Equals(RefInt other) => this.Value.Equals(other.Value);
        public int CompareTo(RefInt other) => this.Value.CompareTo(other.Value);

        public static bool operator ==(RefInt left, int right) => left.Value == right;
        public static bool operator !=(RefInt left, int right) => left.Value != right;
        public static implicit operator int(RefInt r) => r.Value;
        public static implicit operator RefInt(int value) => new RefInt(value);
    }

    class RefLong : IEquatable<RefLong>, IComparable<RefLong>
    {
        public RefLong() : this(0) { }
        public RefLong(long value)
        {
            this.Value = value;
        }
        long _value;
        public long Value { get => Get(); set => Set(value); }
        public void Set(long value) => Interlocked.Exchange(ref this._value, value);
        public long Get() => Interlocked.Read(ref this._value);
        public override string ToString() => this.Value.ToString();
        public long Increment() => Interlocked.Increment(ref this._value);
        public long Decrement() => Interlocked.Decrement(ref this._value);

        public override bool Equals(object obj) => obj is RefLong x && this.Value == x.Value;
        public override int GetHashCode() => Value.GetHashCode();

        public bool Equals(RefLong other) => this.Value.Equals(other.Value);
        public int CompareTo(RefLong other) => this.Value.CompareTo(other.Value);

        public static bool operator ==(RefLong left, long right) => left.Value == right;
        public static bool operator !=(RefLong left, long right) => left.Value != right;
        public static implicit operator long(RefLong r) => r.Value;
        public static implicit operator RefLong(long value) => new RefLong(value);
    }

    class RefBool : IEquatable<RefBool>, IComparable<RefBool>
    {
        public RefBool() : this(false) { }
        public RefBool(bool value)
        {
            this.Value = value;
        }
        public volatile bool Value;
        public void Set(bool value) => this.Value = value;
        public bool Get() => this.Value;
        public override string ToString() => this.Value.ToString();

        public override bool Equals(object obj) => obj is RefBool x && this.Value == x.Value;
        public override int GetHashCode() => Value.GetHashCode();

        public bool Equals(RefBool other) => this.Value.Equals(other.Value);
        public int CompareTo(RefBool other) => this.Value.CompareTo(other.Value);

        public static bool operator ==(RefBool left, bool right) => left.Value == right;
        public static bool operator !=(RefBool left, bool right) => left.Value != right;
        public static implicit operator bool(RefBool r) => r.Value;
        public static implicit operator RefBool(bool value) => new RefBool(value);
    }

    class ValueRef<T> where T : struct
    {
        public T Value;
        public readonly int Size = Marshal.SizeOf<T>();

        public ValueRef() : this(default) { }
        public ValueRef(T value)
        {
            Value = value;
        }
    }

    class Ref<T>
    {
        public Ref() : this(default) { }
        public Ref(T value)
        {
            Value = value;
        }

        public T Value { get; set; }
        public void Set(T value) => this.Value = value;
        public T Get() => this.Value;
        public bool IsTrue()
        {
            switch (this.Value)
            {
                case bool b:
                    return b;
                case int i:
                    return (i != 0);
                case string s:
                    return bool.TryParse(s, out bool ret2) ? ret2 : false;
            }
            return bool.TryParse(this.Value.ToString(), out bool ret) ? ret : false;
        }
        public override string ToString() => Value?.ToString() ?? null;

        public override bool Equals(object obj)
        {
            var refObj = obj as Ref<T>;
            return refObj != null &&
                   EqualityComparer<T>.Default.Equals(Value, refObj.Value);
        }

        public override int GetHashCode()
        {
            return -1937169414 + EqualityComparer<T>.Default.GetHashCode(Value);
        }

        public static bool operator true(Ref<T> r) { return r.IsTrue(); }
        public static bool operator false(Ref<T> r) { return !r.IsTrue(); }
        public static bool operator ==(Ref<T> r, bool b) { return r.IsTrue() == b; }
        public static bool operator !=(Ref<T> r, bool b) { return r.IsTrue() != b; }
        public static bool operator !(Ref<T> r) { return !r.IsTrue(); }
        public static implicit operator T(Ref<T> r) => r.Value;
        public static implicit operator Ref<T>(T value) => new Ref<T>(value);
    }

    abstract class MemberwiseClonable : ICloneable
    {
        public virtual object Clone() => this.MemberwiseClone();
    }

    // Readonly values holder
    // See https://en.wikipedia.org/wiki/Copenhagen_interpretation
    class Copenhagen<T>
    {
        T _Value;
        T _InitialValue;
        CriticalSection LockObj;
        volatile bool Determined;
        bool IsValueType;

        public Copenhagen(T initialValue)
        {
            this._Value = initialValue;
            this._InitialValue = initialValue;
            this.LockObj = new CriticalSection();
            this.Determined = false;
            IsValueType = !(typeof(T).IsClass);
        }

        public T Value { get => GetValue(); set => SetValue(value); }

        public void RestoreToDefault() => this.Set(this._InitialValue);

        public T Get() => GetValue();
        public T GetValue()
        {
            if (Determined)
            {
                if (IsValueType)
                    return this._Value;
                return this._Value._CloneIfClonable();
            }
            lock (LockObj)
            {
                Determined = true;
                if (IsValueType)
                    return this._Value;
                return this._Value._CloneIfClonable();
            }
        }

        public void Set(T value) => SetValue(value);
        public void SetValue(T value)
        {
            lock (LockObj)
            {
                if (Determined == false)
                {
                    this._Value = value._CloneIfClonable();
                }
                else
                {
                    throw new ApplicationException($"The value '{this.GetType()}' is readonly becasue it is already determined.");
                }
            }
        }

        public override bool Equals(object obj)
        {
            var refObj = obj as Ref<T>;
            return refObj != null &&
                   EqualityComparer<T>.Default.Equals(Value, refObj.Value);
        }

        public override int GetHashCode() => -1937169414 + EqualityComparer<T>.Default.GetHashCode(Value);

        public override string ToString() => Value?.ToString() ?? null;

        public static implicit operator T(Copenhagen<T> r) => r.Value;
        public static implicit operator Copenhagen<T>(T value) => new Copenhagen<T>(value);
    }

    interface IEmptyChecker
    {
        bool IsThisEmpty();
    }

    [Flags]
    enum MultipleActionsFlag
    {
        IgnoreError,
        AllOk,
        AnyOk,
        AnyOkContinueAll,
        AnyOkContinueAllRetLast,
    }

    // ユーティリティクラス
    static partial class Util
    {

        public static readonly DateTime ZeroDateTimeValue = new DateTime(1800, 1, 1);
        public static readonly DateTimeOffset ZeroDateTimeOffsetValue = new DateTimeOffset(1800, 1, 1, 0, 0, 0, new TimeSpan(9, 0, 0));

        static readonly Random RandomShared = new Random();

        static Util()
        {
            using (SHA1 sha = new SHA1Managed())
            {
                Span<byte> rand = new byte[20];
                sha.TryComputeHash(System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString()), rand, out _);

                int seed = BitConverter.ToInt32(rand.Slice(0, 4));
                RandomShared = new Random(seed);
            }
        }

        // サイズ定数
        public const int SizeOfInt32 = 4;
        public const int SizeOfInt16 = 2;
        public const int SizeOfInt64 = 8;
        public const int SizeOfInt8 = 1;

        // 数値処理
        public static byte[] ToByte(ushort i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(short i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(uint i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(int i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(ulong i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static byte[] ToByte(long i)
        {
            byte[] ret = BitConverter.GetBytes(i);
            Endian(ret);
            return ret;
        }
        public static ushort ByteToUShort(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToUInt16(c, 0);
        }
        public static short ByteToShort(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToInt16(c, 0);
        }
        public static uint ByteToUInt(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToUInt32(c, 0);
        }
        public static int ByteToInt(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToInt32(c, 0);
        }
        public static ulong ByteToULong(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToUInt64(c, 0);
        }
        public static long ByteToLong(byte[] b)
        {
            byte[] c = CloneByteArray(b);
            Endian(c);
            return BitConverter.ToInt64(c, 0);
        }

        public unsafe static long GetObjectAddress(object o)
        {
            return (long)(*(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref o));
        }

        // IEnumerable から Array List を作成
        public static T[] IEnumerableToArrayList<T>(IEnumerable<T> i) => i.ToArray();

        // ストリームからすべて読み出す
        public static byte[] ReadAllFromStream(Stream st)
        {
            byte[] tmp = new byte[32 * 1024];
            Buf b = new Buf();

            while (true)
            {
                int r = st.Read(tmp, 0, tmp.Length);

                if (r == 0)
                {
                    break;
                }

                b.Write(tmp, 0, r);
            }

            return b.ByteData;
        }

        // リストのクローン
        public static List<T> CloneList<T>(List<T> src)
        {
            List<T> ret = new List<T>();
            foreach (T t in src)
            {
                ret.Add(t);
            }
            return ret;
        }

        // byte 配列から一部分だけ抜き出す
        public static byte[] ExtractByteArray(byte[] data, int pos, int len)
        {
            byte[] ret = new byte[len];

            Util.CopyByte(ret, 0, data, pos, len);

            return ret;
        }

        // 配列を結合する
        public static T[] CombineArray<T>(params T[][] arrays)
        {
            List<T> o = new List<T>();
            foreach (T[] array in arrays)
            {
                foreach (T element in array)
                {
                    o.Add(element);
                }
            }
            return o.ToArray();
        }

        // byte 配列を結合する
        public static byte[] CombineByteArray(byte[] b1, byte[] b2)
        {
            if (b1 == null || b1.Length == 0) return b2;
            if (b2 == null || b2.Length == 0) return b1;
            byte[] ret = new byte[b1.Length + b2.Length];
            Array.Copy(b1, 0, ret, 0, b1.Length);
            Array.Copy(b2, 0, ret, b1.Length, b2.Length);
            return ret;
        }

        // byte 配列の先頭を削除する
        public static byte[] RemoveStartByteArray(byte[] src, int numBytes)
        {
            if (numBytes == 0)
            {
                return src;
            }
            int num = src.Length - numBytes;
            byte[] ret = new byte[num];
            Util.CopyByte(ret, 0, src, numBytes, num);
            return ret;
        }

        // 2 つの値を引き算する return (a - b)
        public static object Subtract(object a, object b)
        {
            if (a == null) return null;
            if (b == null) return a;
            if (a.GetType() != b.GetType()) throw new ArgumentException("a.GetType() != b.GetType()");

            switch (a)
            {
                case byte aa: return aa - (byte)b;
                case sbyte aa: return aa - (sbyte)b;
                case short aa: return aa - (short)b;
                case ushort aa: return aa - (ushort)b;
                case int aa: return aa - (int)b;
                case uint aa: return aa - (uint)b;
                case long aa: return aa - (long)b;
                case ulong aa: return aa - (ulong)b;
                case char aa: return aa - (char)b;
                case float aa: return aa - (float)b;
                case double aa: return aa - (double)b;
                case decimal aa: return aa - (decimal)b;
                case bool aa: return aa != (bool)b;
                case BigNumber aa: return aa - (BigNumber)b;
                case BigInteger aa: return aa - (BigInteger)b;
                default: return a;
            }
        }

        // 割り算する (return a / b)
        public static object Divide(object a, double b)
        {
            if (a == null) return null;

            switch (a)
            {
                case byte aa: return (byte)((double)aa / b);
                case sbyte aa: return (sbyte)((double)aa / b);
                case short aa: return (short)((double)aa / b);
                case ushort aa: return (ushort)((double)aa / b);
                case int aa: return (int)((double)aa / b);
                case uint aa: return (uint)((double)aa / b);
                case long aa: return (long)((double)aa / b);
                case ulong aa: return (ulong)((double)aa / b);
                case char aa: return (char)((double)aa / b);
                case float aa: return (float)((double)aa / b);
                case double aa: return (double)((double)aa / b);
                case decimal aa: return (decimal)((double)aa / b);
                case bool aa: return aa;
                case BigNumber aa: return aa / (long)b;
                case BigInteger aa: return aa / (long)b;
                default: return a;
            }
        }

        // 2 つの年をはさむ年度のリストを取得する
        public static DateTime[] GetYearNendoList(DateTime startYear, DateTime endYear)
        {
            startYear = GetStartOfNendo(startYear);
            endYear = GetEndOfNendo(endYear);

            if (startYear > endYear)
            {
                throw new ArgumentException();
            }

            List<DateTime> ret = new List<DateTime>();

            DateTime dt;
            for (dt = startYear; dt <= endYear; dt = GetStartOfNendo(dt.AddYears(1)))
            {
                ret.Add(dt);
            }

            return ret.ToArray();
        }

        // 2 つの年をさはむ年のリストを取得する
        public static DateTime[] GetYearList(DateTime startYear, DateTime endYear)
        {
            startYear = GetStartOfYear(startYear);
            endYear = GetEndOfYear(endYear);

            if (startYear > endYear)
            {
                throw new ArgumentException();
            }

            List<DateTime> ret = new List<DateTime>();

            DateTime dt;
            for (dt = startYear; dt <= endYear; dt = GetStartOfYear(dt.AddYears(1)))
            {
                ret.Add(dt);
            }

            return ret.ToArray();
        }

        // 2 つの月をはさむ月のリストを取得する
        public static DateTime[] GetMonthList(DateTime startMonth, DateTime endMonth)
        {
            startMonth = GetStartOfMonth(startMonth);
            endMonth = GetEndOfMonth(endMonth);

            if (startMonth > endMonth)
            {
                throw new ArgumentException();
            }

            List<DateTime> ret = new List<DateTime>();

            DateTime dt;
            for (dt = startMonth; dt <= endMonth; dt = GetStartOfMonth(dt.AddMonths(1)))
            {
                ret.Add(dt);
            }

            return ret.ToArray();
        }

        // ある日における年齢を取得する
        public static int GetAge(DateTime birthDay, DateTime now)
        {
            birthDay = birthDay.Date;
            now = now.Date;

            DateTime dayBirthDay = new DateTime(2000, birthDay.Month, birthDay.Day);
            DateTime dayNow = new DateTime(2000, now.Month, now.Day);

            int ret = now.Year - birthDay.Year;

            if (dayBirthDay > dayNow)
            {
                ret -= 1;
            }

            return Math.Max(ret, 0);
        }

        // 特定の月の日数を取得する
        public static int GetNumOfDaysInMonth(DateTime dt)
        {
            DateTime dt1 = new DateTime(dt.Year, dt.Month, dt.Day);
            DateTime dt2 = dt1.AddMonths(1);
            TimeSpan span = dt2 - dt1;

            return span.Days;
        }

        // 2 個の日付の間に存在する月数を取得する
        public static int GetNumMonthSpan(DateTime dt1, DateTime dt2, bool kiriage)
        {
            if (dt1 > dt2)
            {
                DateTime dtt = dt2;
                dt2 = dt1;
                dt1 = dtt;
            }

            int i;
            DateTime dt = dt1;
            for (i = 0; ; i++)
            {
                if (kiriage)
                {
                    if (dt >= dt2)
                    {
                        return i;
                    }
                }
                else
                {
                    if (dt >= dt2.AddMonths(1).AddTicks(-1))
                    {
                        return i;
                    }
                }

                dt = dt.AddMonths(1);
            }
        }

        // 特定の月の初日を取得する
        public static DateTime GetStartOfMonth(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, 1);
        }

        // 特定の月の末日を取得する
        public static DateTime GetEndOfMonth(DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, 1).AddMonths(1).AddSeconds(-1).Date;
        }

        // 特定の年の初日を取得する
        public static DateTime GetStartOfYear(DateTime dt)
        {
            return new DateTime(dt.Year, 1, 1, 0, 0, 0);
        }

        // 特定の年の年末を取得する
        public static DateTime GetEndOfYear(DateTime dt)
        {
            return GetStartOfYear(dt).AddYears(1).AddSeconds(-1).Date;
        }

        // 特定の月の末日 (月末が休業日の場合は翌営業日) を取得する
        public static DateTime GetEndOfMonthForSettle(DateTime dt)
        {
            dt = new DateTime(dt.Year, dt.Month, 1).AddMonths(1).AddSeconds(-1).Date;
            if (dt.Month == 4 && (new DateTime(dt.Year, 4, 29).DayOfWeek == DayOfWeek.Sunday))
            {
                dt = dt.AddDays(1);
            }
            while ((dt.DayOfWeek == DayOfWeek.Sunday || dt.DayOfWeek == DayOfWeek.Saturday) ||
                (dt.Month == 12 && dt.Day >= 29) ||
                (dt.Month == 1 && dt.Day <= 3))
            {
                dt = dt.AddDays(1);
            }
            return dt;
        }

        // 特定の日の開始時刻を取得する
        public static DateTime GetStartOfDay(DateTime dt)
        {
            return dt.Date;
        }

        // 特定の日の終了時刻を取得する
        public static DateTime GetEndOfDate(DateTime dt)
        {
            return GetStartOfDay(dt).AddDays(1).AddTicks(-1);
        }

        // 年度を取得する
        public static int GetNendo(DateTime dt)
        {
            if (dt.Month >= 4)
            {
                return dt.Year;
            }
            else
            {
                return dt.Year - 1;
            }
        }

        // 年度の開始日を指定する
        public static DateTime GetStartOfNendo(DateTime dt)
        {
            return GetStartOfNendo(GetNendo(dt));
        }
        public static DateTime GetStartOfNendo(int nendo)
        {
            return new DateTime(nendo, 4, 1, 0, 0, 0).Date;
        }

        // 年度の終了日を指定する
        public static DateTime GetEndOfNendo(DateTime dt)
        {
            return GetEndOfNendo(GetNendo(dt));
        }
        public static DateTime GetEndOfNendo(int nendo)
        {
            return new DateTime(nendo + 1, 3, 31, 0, 0, 0).Date;
        }

        // エンディアン変換処理
        public static void Endian(byte[] b)
        {
            if (Env.IsLittleEndian)
            {
                Array.Reverse(b);
            }
        }
        public static byte[] EndianRetByte(byte[] b)
        {
            b = Util.CloneByteArray(b);

            Endian(b);

            return b;
        }
        public static UInt16 Endian(UInt16 v)
        {
            return Util.ByteToUShort(Util.EndianRetByte(Util.ToByte(v)));
        }
        public static UInt32 Endian(UInt32 v)
        {
            return Util.ByteToUInt(Util.EndianRetByte(Util.ToByte(v)));
        }
        public static UInt64 Endian(UInt64 v)
        {
            return Util.ByteToULong(Util.EndianRetByte(Util.ToByte(v)));
        }

        // ドメイン名に使用できる文字列に変換する
        public static string SafeDomainStr(string str)
        {
            string ret = str.Replace("(", "").Replace(")", "").Replace(" ", "").Replace("-", "").Replace("#", "")
                .Replace("%", "").Replace("%", "").Replace("&", "").Replace(".", "");
            if (ret == "")
            {
                ret = "host";
            }

            return ret;
        }

        // byte[] 配列をコピーする
        public static byte[] CopyByte(byte[] src)
        {
            return (byte[])src.Clone();
        }
        public static byte[] CopyByte(byte[] src, int srcOffset)
        {
            return CopyByte(src, srcOffset, src.Length - srcOffset);
        }
        public static byte[] CopyByte(byte[] src, int srcOffset, int size)
        {
            byte[] ret = new byte[size];
            CopyByte(ret, 0, src, srcOffset, size);
            return ret;
        }
        public static void CopyByte(byte[] dst, byte[] src, int srcOffset, int size)
        {
            CopyByte(dst, 0, src, srcOffset, size);
        }
        public static void CopyByte(byte[] dst, int dstOffset, byte[] src)
        {
            CopyByte(dst, dstOffset, src, 0, src.Length);
        }
        public static void CopyByte(byte[] dst, int dstOffset, byte[] src, int srcOffset, int size)
        {
            Array.Copy(src, srcOffset, dst, dstOffset, size);
        }

        public static DateTime NormalizeDateTime(DateTime dt)
        {
            if (IsZero(dt)) return Util.ZeroDateTimeValue;
            return dt;
        }

        public static DateTimeOffset NormalizeDateTime(DateTimeOffset dt)
        {
            if (IsZero(dt)) return Util.ZeroDateTimeOffsetValue;
            return dt;
        }

        // 指定されたオブジェクトが Null、0 または空データであるかどうか判別する
        public static bool IsEmpty<T>(T data, bool zeroValueIsEmpty = false)
        {
            if (data == default) return true;

            try
            {
                if (data is IEmptyChecker emptyChecker) return emptyChecker.IsThisEmpty();

                if (zeroValueIsEmpty)
                {
                    switch (data)
                    {
                        case char c: return c == 0;
                        case byte b: return b == 0;
                        case sbyte sb: return sb == 0;
                        case ushort us: return us == 0;
                        case short s: return s == 0;
                        case uint ui: return ui == 0;
                        case int i: return i == 0;
                        case ulong ul: return ul == 0;
                        case long l: return l == 0;
                        case float f: return f == 0;
                        case double d: return d == 0;
                        case decimal v: return v == 0;
                        case bool b: return b == false;
                        case BigNumber bn: return bn == 0;
                        case BigInteger bi: return bi == 0;
                        case Memory<byte> m: return m._IsZero();
                        case byte[] x: return Util.IsZero(x);
                    }
                }
                switch (data)
                {
                    case Array a: return a.Length == 0;
                    case IntPtr p: return p == IntPtr.Zero;
                    case UIntPtr up: return up == UIntPtr.Zero;
                    case DateTime dt: return Util.IsZero(dt);
                    case DateTimeOffset dt: return Util.IsZero(dt);
                    case ICollection c: return c.Count == 0;
                    case string s:
                        if (s.Length == 0) return true;
                        if (Char.IsWhiteSpace(s[0]) == false) return false;
                        return s.Trim().Length == 0;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
        public static bool IsFilled<T>(T data, bool zeroValueIsEmpty = false) => !IsEmpty(data, zeroValueIsEmpty);

        // DateTime がゼロかどうか検査する
        public static bool IsZero(DateTime dt)
        {
            if (dt.Year < 1850)
            {
                return true;
            }
            return false;
        }
        public static bool IsZero(DateTimeOffset dt)
        {
            if (dt.Year < 1850)
            {
                return true;
            }
            return false;
        }

        // byte[] 配列がオールゼロかどうか検査する
        public static bool IsZero(byte[] data)
        {
            if (data == null) return true;
            return IsZero(data, 0, data.Length);
        }
        public static bool IsZero(byte[] data, int offset, int size)
        {
            if (data == null) return true;
            return IsZero(new ReadOnlySpan<byte>(data, offset, size));
        }

        public static bool IsZero(ReadOnlySpan<byte> data)
        {
            int i;
            for (i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                {
                    return false;
                }
            }
            return true;
        }
        public static bool IsZero(ReadOnlyMemory<byte> data) => IsZero(data.Span);

        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static unsafe bool IsZeroFast(ReadOnlySpan<byte> srcMemory)
        {
            fixed (byte* srcPointer = srcMemory)
            {
                long startLong = (long)srcPointer;
                long srcLen = srcMemory.Length;
                long endLong = startLong + srcLen;

                startLong = ((startLong + 7) / 8) * 8;
                endLong = (endLong / 8) * 8;

                for (byte* p = srcPointer; p < (byte*)startLong; p++)
                    if (*p != 0) return false;

                byte* endP = srcPointer + srcLen;

                for (byte* p = (byte*)endLong; p < endP; p++)
                    if (*p != 0) return false;

                for (long i = startLong; i < endLong; i += 8)
                {
                    if (*((long*)i) != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool IsZeroFast(ReadOnlyMemory<byte> data) => IsZeroFast(data.Span);
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool IsZeroFast(byte[] data)
        {
            if (data == null) return true;
            return IsZeroFast(data, 0, data.Length);
        }
        /// <summary>Recommended to byte array more than 16 bytes.</summary>
        public static bool IsZeroFast(byte[] data, int offset, int size)
        {
            if (data == null) return true;
            return IsZeroFast(new ReadOnlySpan<byte>(data, offset, size));
        }

        // byte[] 配列同士を比較する
        public static bool CompareByte(byte[] b1, byte[] b2)
        {
            if (b1.Length != b2.Length)
            {
                return false;
            }
            return System.Linq.Enumerable.SequenceEqual<byte>(b1, b2);
        }

        // byte[] 配列同士を比較する
        public static int CompareByteRetInt(byte[] b1, byte[] b2)
        {
            int i;
            for (i = 0; ; i++)
            {
                int a1 = -1, a2 = -1;
                if (i < b1.Length)
                {
                    a1 = (int)b1[i];
                }
                if (i < b2.Length)
                {
                    a2 = (int)b2[i];
                }

                if (a1 > a2)
                {
                    return 1;
                }
                else if (a1 < a2)
                {
                    return -1;
                }
                if (a1 == -1 && a2 == -1)
                {
                    return 0;
                }
            }
        }

        // byte[] 配列のコピー
        public static byte[] CloneByteArray(byte[] src)
        {
            byte[] ret = new byte[src.Length];

            Util.CopyByte(ret, src, 0, src.Length);

            return ret;
        }

        // UNIX 時間を DateTime に変換
        public static DateTime UnixTimeToDateTime(uint t)
        {
            return new DateTime(1970, 1, 1).AddSeconds(t);
        }

        // DateTime を UNIX 時間に変換
        public static uint DateTimeToUnixTime(DateTime dt)
        {
            TimeSpan ts = dt - new DateTime(1970, 1, 1);
            if (ts.Ticks < 0)
            {
                throw new InvalidDataException("dt");
            }

            return (uint)ts.TotalSeconds;
        }

        // Convert to a time to be used safely in the current POSIX implementation
        public static long SafeTime64(long time64) => (long)SafeTime64((ulong)time64);
        public static ulong SafeTime64(ulong time64)
        {
            time64 = Math.Max(time64, 0);
            time64 = Math.Min(time64, 4102243323123UL);
            return time64;
        }

        // ulong を DateTime に変換
        public static DateTime ConvertDateTime(long time64) => ConvertDateTime((ulong)time64);
        public static DateTime ConvertDateTime(ulong time64)
        {
            time64 = SafeTime64(time64);
            if (time64 == 0)
            {
                return new DateTime(0);
            }
            return new DateTime(((long)time64 + 62135629200000L) * 10000L);
        }

        // DateTime を ulong に変換
        public static ulong ConvertDateTime(DateTime dt)
        {
            if (dt.Ticks == 0)
            {
                return 0;
            }
            return (ulong)SafeTime64(dt.Ticks / 10000L - 62135629200000L);
        }

        // ulong を TimeSpan に変換
        public static TimeSpan ConvertTimeSpan(ulong tick)
        {
            return new TimeSpan((long)tick * 10000);
        }

        // TimeSpan を ulong に変換
        public static ulong ConvertTimeSpan(TimeSpan span)
        {
            return (ulong)span.Ticks / 10000;
        }

        // DateTime を DOS の日付に変換
        public static ushort DateTimeToDosDate(DateTime dt)
        {
            return (ushort)(
                ((uint)(dt.Year - 1980) << 9) |
                ((uint)dt.Month << 5) |
                (uint)dt.Day);
        }

        // DateTime を DOS の時刻に変換
        public static ushort DateTimeToDosTime(DateTime dt)
        {
            return (ushort)(
                ((uint)dt.Hour << 11) |
                ((uint)dt.Minute << 5) |
                ((uint)dt.Second >> 1));
        }

        // Array が Null か Empty かどうか
        public static bool IsNullOrEmpty(object o)
        {
            if (o == null)
            {
                return true;
            }

            if (o is string)
            {
                string s = (string)o;

                return Str.IsEmptyStr(s);
            }

            if (o is Array)
            {
                Array a = (Array)o;
                if (a.Length == 0)
                {
                    return true;
                }
            }

            return false;
        }

        // 型から XML スキーマを生成
        public static byte[] GetXmlSchemaFromType_PublicLegacy(Type type)
        {
            XmlSchemas sms = new XmlSchemas();
            XmlSchemaExporter ex = new XmlSchemaExporter(sms);
            XmlReflectionImporter im = new XmlReflectionImporter();
            XmlTypeMapping map = im.ImportTypeMapping(type);
            ex.ExportTypeMapping(map);
            sms.Compile(null, false);

            MemoryStream ms = new MemoryStream();
            StreamWriter sw = new StreamWriter(ms);
            foreach (System.Xml.Schema.XmlSchema sm in sms)
            {
                sm.Write(sw);
            }
            sw.Close();
            ms.Flush();

            byte[] data = ms.ToArray();
            return data;
        }
        public static string GetXmlSchemaFromTypeString_PublicLegacy(Type type)
        {
            byte[] data = GetXmlSchemaFromType_PublicLegacy(type);

            return Str.Utf8Encoding.GetString(data);
        }

        // オブジェクトを XML に変換
        public static string ObjectToXmlString_PublicLegacy(object o)
        {
            byte[] data = ObjectToXml_PublicLegacy(o);

            return Str.Utf8Encoding.GetString(data);
        }
        public static byte[] ObjectToXml_PublicLegacy(object o)
        {
            if (o == null)
            {
                return null;
            }
            Type t = o.GetType();

            return ObjectToXml_PublicLegacy(o, t);
        }
        public static string ObjectToXmlString_PublicLegacy(object o, Type t)
        {
            byte[] data = ObjectToXml_PublicLegacy(o, t);

            return Str.Utf8Encoding.GetString(data);
        }
        public static byte[] ObjectToXml_PublicLegacy(object o, Type t)
        {
            if (o == null)
            {
                return null;
            }

            MemoryStream ms = new MemoryStream();
            XmlSerializer x = new XmlSerializer(t);

            x.Serialize(ms, o);

            return ms.ToArray();
        }

        public static DataContractJsonSerializerSettings NewDefaultRuntimeJsonSerializerSettings()
        {
            return new DataContractJsonSerializerSettings()
            {
                DateTimeFormat = new DateTimeFormat("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK"),
                UseSimpleDictionaryFormat = true,
            };
        }

        public static void ObjectToRuntimeJson(object obj, MemoryBuffer<byte> dst, DataContractJsonSerializerSettings settings = null)
        {
            if (settings == null) settings = NewDefaultRuntimeJsonSerializerSettings();
            DataContractJsonSerializer d = new DataContractJsonSerializer(obj.GetType(), settings);
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(dst._AsDirectStream(), Str.Utf8Encoding, false, true, "  "))
            {
                d.WriteObject(writer, obj);
            }
        }
        public static byte[] ObjectToRuntimeJson(object obj, DataContractJsonSerializerSettings settings = null)
        {
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
            ObjectToRuntimeJson(obj, buf, settings);
            return buf.Span.ToArray();
        }

        public static object RuntimeJsonToObject(MemoryBuffer<byte> src, Type type, DataContractJsonSerializerSettings settings = null)
        {
            if (settings == null) settings = NewDefaultRuntimeJsonSerializerSettings();
            DataContractJsonSerializer d = new DataContractJsonSerializer(type, settings);
            return d.ReadObject(src._AsDirectStream());
        }
        public static T RuntimeJsonToObject<T>(MemoryBuffer<byte> src, DataContractJsonSerializerSettings settings = null) => (T)RuntimeJsonToObject(src, typeof(T), settings);

        public static object RuntimeJsonToObject(byte[] src, Type type, DataContractJsonSerializerSettings settings = null) => RuntimeJsonToObject(src._AsMemoryBuffer(), type, settings);
        public static T RuntimeJsonToObject<T>(byte[] src, DataContractJsonSerializerSettings settings = null) => RuntimeJsonToObject<T>(src._AsMemoryBuffer(), settings);



        public static void ObjectToXml(object obj, MemoryBuffer<byte> dst, DataContractSerializerSettings settings = null)
        {
            if (settings == null)
            {
                settings = new DataContractSerializerSettings();
                settings.PreserveObjectReferences = true;
            }
            DataContractSerializer d = new DataContractSerializer(obj.GetType(), settings);
            d.WriteObject(dst._AsDirectStream(), obj);
        }
        public static byte[] ObjectToXml(object obj, DataContractSerializerSettings settings = null)
        {
            MemoryBuffer<byte> buf = new MemoryBuffer<byte>();
            ObjectToXml(obj, buf, settings);
            return buf.Span.ToArray();
        }

        public static object XmlToObject(MemoryBuffer<byte> src, Type type, DataContractSerializerSettings settings = null)
        {
            if (settings == null)
            {
                settings = new DataContractSerializerSettings();
                settings.PreserveObjectReferences = true;
            }
            DataContractSerializer d = new DataContractSerializer(type, settings);
            return d.ReadObject(src._AsDirectStream());
        }
        public static T XmlToObject<T>(MemoryBuffer<byte> src, DataContractSerializerSettings settings = null) => (T)XmlToObject(src, typeof(T), settings);

        public static object XmlToObject(byte[] src, Type type, DataContractSerializerSettings settings = null) => XmlToObject(src._AsMemoryBuffer(), type, settings);
        public static T XmlToObject<T>(byte[] src, DataContractSerializerSettings settings = null) => XmlToObject<T>(src._AsMemoryBuffer(), settings);

        // オブジェクトをクローンする
        public static object CloneObject_UsingBinary(object o)
        {
            if (o == null) return null;
            return BinaryToObject(ObjectToBinary(o));
        }

        // オブジェクトをバイナリに変換する
        public static byte[] ObjectToBinary(object o)
        {
            BinaryFormatter f = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
            f.Serialize(ms, o);

            return ms.ToArray();
        }

        // バイナリをオブジェクトに変換する
        public static object BinaryToObject(ReadOnlySpan<byte> data)
        {
            BinaryFormatter f = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
            ms.Write(data);
            ms.Position = 0;

            return f.Deserialize(ms);
        }

        // オブジェクトの内容をクローンする
        public static object CloneObject_UsingXml_PublicLegacy(object o)
        {
            byte[] data = Util.ObjectToXml_PublicLegacy(o);

            return Util.XmlToObject_PublicLegacy(data, o.GetType());
        }

        // XML をオブジェクトに変換
        public static object XmlToObject_PublicLegacy(string str, Type t)
        {
            if (Str.IsEmptyStr(str))
            {
                return null;
            }

            byte[] data = Str.Utf8Encoding.GetBytes(str);

            return XmlToObject_PublicLegacy(data, t);
        }
        public static object XmlToObject_PublicLegacy(byte[] data, Type t)
        {
            if (data == null || data.Length == 0)
            {
                return null;
            }

            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Position = 0;

            XmlSerializer x = new XmlSerializer(t);

            return x.Deserialize(ms);
        }

        // 何もしない
        public static void NoOP(object o)
        {
        }
        public static void NoOP()
        {
        }
        public static void DoNothing()
        {
        }
        public static void DoNothing(object o)
        {
        }
        public static System.Threading.Tasks.Task DoNothingAsync()
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
        public static int RetZero()
        {
            return 0;
        }

        // false
        public static bool False => false;

        // true
        public static bool True => true;

        // Zero
        public static int Zero => 0;

        // バイト配列から構造体にコピー
        public static object ByteToStruct(byte[] src, Type type)
        {
            int size = src.Length;
            if (size != SizeOfStruct(type))
            {
                throw new SystemException("size error");
            }

            IntPtr p = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.Copy(src, 0, p, size);

                return Marshal.PtrToStructure(p, type);
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        // 構造体をバイト配列にコピー
        public static byte[] StructToByte(object obj)
        {
            int size = SizeOfStruct(obj);
            IntPtr p = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(obj, p, false);

                byte[] ret = new byte[size];

                Marshal.Copy(p, ret, 0, size);

                return ret;
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        // 構造体のサイズの取得
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOfStruct(object obj)
            => Marshal.SizeOf(obj);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOfStruct(Type type)
            => Marshal.SizeOf(type);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOfStruct<T>()
            => Unsafe.SizeOf<T>();

        // オブジェクトから XML とスキーマを生成
        public static XmlAndXsd GenerateXmlAndXsd(object obj)
        {
            XmlAndXsd ret = new XmlAndXsd();
            Type type = obj.GetType();

            ret.XsdFileName = Str.MakeSafeFileName(type.Name + ".xsd");
            ret.XsdData = GetXmlSchemaFromType_PublicLegacy(type);

            ret.XmlFileName = Str.MakeSafeFileName(type.Name + ".xml");
            string str = Util.ObjectToXmlString_PublicLegacy(obj);
            str = str.Replace(
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"",
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xsi:noNamespaceSchemaLocation=\""
                + ret.XsdFileName
                + "\"");
            ret.XmlData = Str.Utf8Encoding.GetBytes(str);

            return ret;
        }

        // 何でも登録するリスト
        static LinkedList<object> blackhole = new LinkedList<object>();
        public static void AddToBlackhole(object obj)
        {
            lock (blackhole)
            {
                blackhole.AddLast(obj);
            }
        }

        // Enum の値一覧を取得する
        public static T[] GetEnumValuesList<T>() where T : Enum
        {
            List<T> ret = new List<T>();
            return (T[])Enum.GetValues(typeof(T));
        }

        public static T GetMaxEnumValue<T>() where T : Enum
        {
            return GetEnumValuesList<T>().OrderBy(x => x).LastOrDefault();
        }


        public static byte[] Rand(int size) { byte[] r = new byte[size]; Rand(r); return r; }

        public static void Rand(Span<byte> dest)
        {
            if (Env.IsWindows)
            {
                // In the Windows system, Secure.Rand() is faster.
                Secure.Rand(dest);
                return;
            }

            // In UNIX systems, Util.Rand() is faster.
            lock (RandomShared)
            {
                RandomShared.NextBytes(dest);
            }
        }

        public static byte RandUInt8()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            return mem._GetUInt8();
        }

        public static ushort RandUInt16()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            return mem._GetUInt16();
        }

        public static uint RandUInt32()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            return mem._GetUInt32();
        }

        public static ulong RandUInt64()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            return mem._GetUInt64();
        }

        public static byte RandUInt7()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt8();
        }

        public static ushort RandUInt15()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt16();
        }

        public static uint RandUInt31()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt32();
        }

        public static ulong RandUInt63()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetUInt64();
        }

        public static sbyte RandSInt8_Caution()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            return mem._GetSInt8();
        }

        public static short RandSInt16_Caution()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            return mem._GetSInt16();
        }

        public static int RandSInt32_Caution()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            return mem._GetSInt32();
        }

        public static long RandSInt64_Caution()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            return mem._GetSInt64();
        }

        public static sbyte RandSInt7()
        {
            Span<byte> mem = stackalloc byte[1];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt8();
        }

        public static short RandSInt15()
        {
            Span<byte> mem = stackalloc byte[2];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt16();
        }

        public static int RandSInt31()
        {
            Span<byte> mem = stackalloc byte[4];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt32();
        }

        public static long RandSInt63()
        {
            Span<byte> mem = stackalloc byte[8];
            Rand(mem);
            mem[0] &= 0x7F;
            return mem._GetSInt64();
        }

        public static bool RandBool()
        {
            return (RandUInt32() % 2) == 0;
        }

        public static int GenRandIntervalWithRetry(int standard, int numRetry, int max, double plusMinusPercentage = 30.0)
        {
            numRetry = Math.Max(numRetry, 1);

            int v = standard * numRetry;
            v = Math.Min(v, max);

            return GenRandInterval(v, plusMinusPercentage);
        }

        public static int GenRandInterval(int standard, double plusMinusPercentage = 30.0)
        {
            double rate = plusMinusPercentage / 100.0;
            int v = (int)((double)standard * rate);
            if (v == 0) return standard;
            bool b = Util.RandBool();
            v = Util.RandSInt31() % v;
            int ret;
            if (b)
                ret = standard + v;
            else
                ret = standard - v;
            return Math.Max(ret, 1);
        }

        public static int GenRandInterval(int min, int max)
        {
            int a = Math.Min(min, max);
            int b = Math.Max(min, max);

            if (a == b)
            {
                return a;
            }

            return (RandSInt31() % (b - 1)) + a;
        }

        public static T NewWithoutConstructor<T>()
            => (T)NewWithoutConstructor(typeof(T));

        public static object NewWithoutConstructor(Type t)
            => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t);

        // baseData に overwriteData の値 (NULL 以外の場合) を上書きしたオブジェクトを返す
        public static T DbOverwriteValues<T>(T baseData, T overwriteData) where T : new()
        {
            T ret = new T();
            Type t = typeof(T);
            var propertyList = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in propertyList)
            {
                var propertyType = p.PropertyType;
                object value = p.GetValue(overwriteData);
                if (value._IsEmpty())
                {
                    value = p.GetValue(baseData);
                }
                p.SetValue(ret, value);
            }

            DbEnforceNonNull(ret);

            return ret;
        }

        // データベースのテーブルのクラスで Non NULL を強制する
        public static void DbEnforceNonNull(object obj)
        {
            Type t = obj.GetType();

            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                var ptype = p.PropertyType;
                if (ptype._IsNullable() == false)
                {
                    if (ptype == typeof(string))
                    {
                        string s = (string)p.GetValue(obj);
                        if (s == null) p.SetValue(obj, "");
                    }
                    else if (ptype == typeof(DateTime))
                    {
                        DateTime d = (DateTime)p.GetValue(obj);
                        if (d._IsZeroDateTime()) p.SetValue(obj, Util.ZeroDateTimeValue);
                    }
                    else if (ptype == typeof(DateTimeOffset))
                    {
                        DateTimeOffset d = (DateTimeOffset)p.GetValue(obj);
                        if (d._IsZeroDateTime()) p.SetValue(obj, Util.ZeroDateTimeOffsetValue);
                    }
                    else if (ptype == typeof(byte[]))
                    {
                        byte[] b = (byte[])p.GetValue(obj);
                        if (b == null) p.SetValue(obj, new byte[0]);
                    }
                }
            }
        }

        public static T CloneIfClonable<T>(T obj)
        {
            if (obj == default) return default;
            if (typeof(T).IsClass == false) return obj;
            if (obj is ICloneable clonable) return (T)clonable.Clone();
            return obj;
        }

        public static TResult DoMultipleFuncs<TResult>(MultipleActionsFlag flag, CancellationToken cancel, params Func<TResult>[] funcs)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception firstException = null;

            TResult firstRet = default;
            bool isFirstRetSet = false;
            TResult lastRet = default;

            if (funcs != null)
            {
                foreach (Func<TResult> func in funcs)
                {
                    if (func != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            lastRet = func();

                            if (isFirstRetSet == false)
                            {
                                firstRet = lastRet;
                                isFirstRetSet = true;
                            }

                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return firstRet;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        throw firstException;
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                    if (anyOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return lastRet;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        public static async Task<TResult> DoMultipleFuncsAsync<TResult>(MultipleActionsFlag flag, CancellationToken cancel, params Func<Task<TResult>>[] funcs)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception firstException = null;

            TResult firstRet = default;
            bool isFirstRetSet = false;
            TResult lastRet = default;

            if (funcs != null)
            {
                foreach (Func<Task<TResult>> func in funcs)
                {
                    if (func != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            lastRet = await func();

                            if (isFirstRetSet == false)
                            {
                                firstRet = lastRet;
                                isFirstRetSet = true;
                            }

                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return firstRet;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        throw firstException;
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                    if (anyOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return firstRet;

                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return lastRet;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        public static bool DoMultipleActions(MultipleActionsFlag flag, CancellationToken cancel, params Action[] actions)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception firstException = null;

            if (actions != null)
            {
                foreach (Action action in actions)
                {
                    if (action != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            action();
                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return anyOk;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return true;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return allOk;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }


        public static async Task<bool> DoMultipleActionsAsync(MultipleActionsFlag flag, CancellationToken cancel, params Func<Task>[] actions)
        {
            bool allOk = true;
            bool anyOk = false;
            Exception firstException = null;

            if (actions != null)
            {
                foreach (Func<Task> action in actions)
                {
                    if (action != null)
                    {
                        try
                        {
                            cancel.ThrowIfCancellationRequested();
                            await action();
                            anyOk = true;

                            if (flag == MultipleActionsFlag.AnyOk)
                                break;
                        }
                        catch (Exception ex)
                        {
                            allOk = false;
                            if (firstException == null)
                                firstException = ex;
                        }
                    }
                }
            }

            if (allOk)
                anyOk = true;

            switch (flag)
            {
                case MultipleActionsFlag.IgnoreError:
                    return anyOk;

                case MultipleActionsFlag.AllOk:
                    if (allOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return true;

                case MultipleActionsFlag.AnyOk:
                case MultipleActionsFlag.AnyOkContinueAll:
                case MultipleActionsFlag.AnyOkContinueAllRetLast:
                    if (anyOk == false)
                    {
                        firstException._ReThrow();
                    }
                    return allOk;

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        [Flags]
        public enum SpanSparseInfo
        {
            NotSparse,
            ContainsSparse,
            AllZero,
        }

        public static unsafe SpanSparseInfo GetSpanSparseInfo(ReadOnlySpan<byte> srcMemory, int minZeroBlockSize)
        {
            if (srcMemory.Length == 0) return SpanSparseInfo.AllZero;
            if (minZeroBlockSize <= 7 || minZeroBlockSize == int.MaxValue) return SpanSparseInfo.NotSparse;

            fixed (byte* srcPointer = srcMemory)
            {
                long srcLen = srcMemory.Length;
                long startLong = (long)srcPointer;
                long endLong = startLong + srcLen;

                startLong = ((startLong + 7) / 8) * 8;
                endLong = (endLong / 8) * 8;

                int blockSizeCount = (minZeroBlockSize / 8);

                int count = 0;

                bool hasSparse = false;
                bool hasNonZero = false;

                for (long i = startLong; i < endLong; i += 8)
                {
                    if (*((long*)i) == 0)
                    {
                        count++;
                        if (count >= blockSizeCount)
                        {
                            hasSparse = true;
                        }
                    }
                    else
                    {
                        count = 0;
                        hasNonZero = true;
                    }
                }

                if (hasSparse)
                {
                    if (hasNonZero)
                    {
                        return SpanSparseInfo.ContainsSparse;
                    }
                    else
                    {
                        for (byte* p = srcPointer; p < (byte*)startLong; p++)
                            if (*p != 0) return SpanSparseInfo.ContainsSparse;

                        byte* endp = srcPointer + srcLen;
                        for (byte* p = (byte*)endLong; p < endp; p++)
                            if (*p != 0) return SpanSparseInfo.ContainsSparse;

                        return SpanSparseInfo.AllZero;
                    }
                }
                else
                {
                    return SpanSparseInfo.NotSparse;
                }
            }
        }

        public static unsafe IReadOnlyList<SparseChunk<byte>> GetSparseChunks(ReadOnlyMemory<byte> srcMemory, int minZeroBlockSize)
        {
            checked
            {
                List<SparseChunk<byte>> ret = new List<SparseChunk<byte>>();

                if (srcMemory.Length == 0)
                {
                    return ret;
                }

                SpanSparseInfo sparseInfo = GetSpanSparseInfo(srcMemory.Span, minZeroBlockSize);

                if (sparseInfo == SpanSparseInfo.NotSparse)
                {
                    ret.Add(new SparseChunk<byte>(0, srcMemory));
                    return ret;
                }
                else if (sparseInfo == SpanSparseInfo.AllZero)
                {
                    ret.Add(new SparseChunk<byte>(0, srcMemory.Length));
                    return ret;
                }

                int mode = -1;
                int lastMode = -1;
                int pos1 = 0, pos2 = 0;
                long offset = 0;

                minZeroBlockSize = Math.Max(minZeroBlockSize, 1);

                ReadOnlySpan<byte> srcSpan = srcMemory.Span;
                int length = srcSpan.Length;

                int currentPos;

                for (currentPos = 0; currentPos < length; currentPos++)
                {
                    mode = (srcSpan[currentPos] == 0) ? 0 : 1;

                    if (lastMode != mode)
                    {
                        lastMode = mode;
                        if (mode == 1)
                        {
                            if ((currentPos - pos2) >= minZeroBlockSize)
                            {
                                int size1 = pos2 - pos1;
                                if (size1 >= 1)
                                {
                                    var chunk1 = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size1));
                                    offset += size1;
                                    ret.Add(chunk1);
                                }

                                int size2 = currentPos - pos2;
                                if (size2 >= 1)
                                {
                                    var chunk2 = new SparseChunk<byte>(offset, size2);
                                    offset += size2;
                                    ret.Add(chunk2);
                                }

                                pos1 = currentPos;
                                pos2 = currentPos;
                            }
                        }
                        else if (mode == 0)
                        {
                            pos2 = currentPos;
                        }
                    }
                }

                currentPos = length;

                if (mode == 0)
                {
                    if ((currentPos - pos2) >= minZeroBlockSize)
                    {
                        int size1 = pos2 - pos1;
                        if (size1 >= 1)
                        {
                            var chunk1 = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size1));
                            offset += size1;
                            ret.Add(chunk1);
                        }

                        int size2 = currentPos - pos2;
                        if (size2 >= 1)
                        {
                            var chunk2 = new SparseChunk<byte>(offset, size2);
                            offset += size2;
                            ret.Add(chunk2);
                        }
                    }
                    else
                    {
                        int size = currentPos - pos1;
                        if (size >= 1)
                        {
                            var chunk = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size));
                            offset += size;
                            ret.Add(chunk);
                        }
                    }
                }
                else if (mode == 1)
                {
                    int size = currentPos - pos1;
                    if (size >= 1)
                    {
                        var chunk = new SparseChunk<byte>(offset, srcMemory.Slice(pos1, size));
                        offset += size;
                        ret.Add(chunk);
                    }
                }

                return ret;
            }
        }

        public static int CopySparseChunkListToSpan<T>(IReadOnlyList<SparseChunk<T>> chunkList, Span<T> dest)
        {
            Span<T> pos = dest;

            int size = 0;

            foreach (var chunk in chunkList)
            {
                if (chunk.IsSparse == false)
                {
                    pos._WalkWrite(chunk.Memory.Span);
                    size += chunk.Memory.Span.Length;
                }
                else
                {
                    pos._Walk(chunk.Size).Fill(default);
                    size += chunk.Size;
                }
            }

            return size;
        }

        public static ReadOnlySpan<ReadOnlyMemory<T>> GetTailOfReadOnlyMemoryArray<T>(ReadOnlySpan<ReadOnlyMemory<T>> itemList, long maxSize, out long returnTotalSize)
        {
            checked
            {
                returnTotalSize = 0;

                if (maxSize <= 0)
                {
                    return new ReadOnlySpan<ReadOnlyMemory<T>>();
                }

                Span<ReadOnlyMemory<T>> ret = new ReadOnlyMemory<T>[itemList.Length];

                int index = 0;

                for (int i = itemList.Length - 1; i >= 0; i--)
                {
                    ref readonly ReadOnlyMemory<T> item = ref itemList[i];
                    if (item.Length >= 1)
                    {
                        long sizeToRead = Math.Min(item.Length, maxSize);
                        ret[index++] = item.Slice(item.Length - (int)sizeToRead);
                        maxSize -= sizeToRead;
                        returnTotalSize += sizeToRead;
                        if (maxSize <= 0)
                        {
                            break;
                        }
                    }
                }

                ret = ret.Slice(0, index);

                ret.Reverse();

                return ret;
            }
        }

        public static ReadOnlySpan<ReadOnlyMemory<T>> GetHeadOfReadOnlyMemoryArray<T>(ReadOnlySpan<ReadOnlyMemory<T>> itemList, long maxSize, out long returnTotalSize)
        {
            checked
            {
                returnTotalSize = 0;

                if (maxSize <= 0)
                {
                    return new ReadOnlySpan<ReadOnlyMemory<T>>();
                }

                Span<ReadOnlyMemory<T>> ret = new ReadOnlyMemory<T>[itemList.Length];

                int index = 0;

                for (int i = 0; i < itemList.Length; i++)
                {
                    ref readonly ReadOnlyMemory<T> item = ref itemList[i];
                    if (item.Length >= 1)
                    {
                        long sizeToRead = Math.Min(item.Length, maxSize);
                        ret[index++] = item.Slice(0, (int)sizeToRead);
                        maxSize -= sizeToRead;
                        returnTotalSize += sizeToRead;
                        if (maxSize <= 0)
                        {
                            break;
                        }
                    }
                }

                return ret.Slice(0, index);
            }
        }

        // Stream を最後まで読む
        public static byte[] ReadStreamToEnd(Stream s, int maxSize = 0)
        {
            if (maxSize <= 0) maxSize = int.MaxValue;
            MemoryStream ms = new MemoryStream();

            byte[] tmp = new byte[200000];
            while (true)
            {
                int r = s.Read(tmp, 0, tmp.Length);
                if (r == 0)
                {
                    break;
                }
                ms.Write(tmp, 0, r);
                if (ms.Length > maxSize) throw new OverflowException();
            }

            return ms.ToArray();
        }

        public static async Task<byte[]> ReadStreamToEndAsync(Stream s, int maxSize = 0, CancellationToken cancel = default(CancellationToken))
        {
            if (maxSize <= 0) maxSize = int.MaxValue;
            MemoryStream ms = new MemoryStream();

            byte[] tmp = MemoryHelper.FastAllocMoreThan<byte>(200000);
            try
            {
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    int r = await s.ReadAsync(tmp, 0, tmp.Length, cancel);
                    if (r == 0)
                    {
                        break;
                    }
                    ms.Write(tmp, 0, r);
                    if (ms.Length > maxSize) throw new OverflowException("ReadStreamToEndAsync: too large data");
                }
            }
            finally
            {
                MemoryHelper.FastFree(tmp);
            }

            return ms.ToArray();
        }

        public static IReadOnlyList<ReadOnlyMemory<T>> DefragmentMemoryArrays<T>(IEnumerable<ReadOnlyMemory<T>> srcDataList, int minBlockSize)
        {
            minBlockSize = Math.Max(minBlockSize, 1);

            List<ReadOnlyMemory<T>> ret = new List<ReadOnlyMemory<T>>();

            MemoryBuffer<T> current = null;

            foreach (ReadOnlyMemory<T> src in srcDataList)
            {
                if (src.Length >= 1)
                {
                    if (src.Length >= minBlockSize)
                    {
                        if (current != null)
                        {
                            if (current.Length >= 1)
                            {
                                ret.Add(current);
                            }
                            current = null;
                        }
                        ret.Add(src);
                    }
                    else
                    {
                        if (current == null)
                        {
                            current = new MemoryBuffer<T>();
                        }

                        current.Write(src);

                        if (current.Length >= minBlockSize)
                        {
                            ret.Add(current);
                            current = null;
                        }
                    }
                }
            }

            if (current != null)
            {
                if (current.Length >= 1)
                {
                    ret.Add(current);
                }
            }

            return ret.ToArray();
        }

        public static void PutGitIgnoreFileOnDirectory(DirectoryPath dir, FileOperationFlags flags = FileOperationFlags.None)
        {
            try
            {
                var srcFilePath = new FilePath(Res.Cores, "190521_LocalGitIgnore.txt");
                var destFilePath = dir.Combine(".gitignore");

                if (destFilePath.IsFileExists() == false)
                {
                    FileUtil.CopyFile(srcFilePath, destFilePath, new CopyFileParams(overwrite: false, flags: flags));
                }
            }
            catch { }
        }
    }


    static class Marvin
    {
        // From: https://github.com/dotnet/corefx/blob/master/src/Common/src/System/Marvin.cs
        /* The MIT License (MIT)
         * Copyright (c) .NET Foundation and Contributors
         * All rights reserved.
         * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal
         * in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
         * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
         * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
         * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
         * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
         * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
         * SOFTWARE. */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeHash32(ReadOnlySpan<byte> data, ulong seed)
        {
            long hash64 = ComputeHash(data, seed);
            return ((int)(hash64 >> 32)) ^ (int)hash64;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ComputeHash32(ReadOnlySpan<byte> data) => ComputeHash32(data, DefaultSeed);

        public static long ComputeHash(ReadOnlySpan<byte> data, ulong seed)
        {
            uint p0 = (uint)seed;
            uint p1 = (uint)(seed >> 32);

            if (data.Length >= sizeof(uint))
            {
                ReadOnlySpan<uint> uData = MemoryMarshal.Cast<byte, uint>(data);

                for (int i = 0; i < uData.Length; i++)
                {
                    p0 += uData[i];
                    Block(ref p0, ref p1);
                }

                int byteOffset = data.Length & (~3);
                data = data.Slice(byteOffset);
            }

            switch (data.Length)
            {
                case 0:
                    p0 += 0x80u;
                    break;

                case 1:
                    p0 += 0x8000u | data[0];
                    break;

                case 2:
                    p0 += 0x800000u | MemoryMarshal.Cast<byte, ushort>(data)[0];
                    break;

                case 3:
                    p0 += 0x80000000u | (((uint)data[2]) << 16) | (uint)(MemoryMarshal.Cast<byte, ushort>(data)[0]);
                    break;
            }

            Block(ref p0, ref p1);
            Block(ref p0, ref p1);

            return (((long)p1) << 32) | p0;
        }

        public static long ComputeHash(ReadOnlySpan<byte> data) => ComputeHash(data, DefaultSeed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Block(ref uint rp0, ref uint rp1)
        {
            uint p0 = rp0;
            uint p1 = rp1;

            p1 ^= p0;
            p0 = _rotl(p0, 20);

            p0 += p1;
            p1 = _rotl(p1, 9);

            p1 ^= p0;
            p0 = _rotl(p0, 27);

            p0 += p1;
            p1 = _rotl(p1, 19);

            rp0 = p0;
            rp1 = p1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint _rotl(uint value, int shift)
        {
            return (value << shift) | (value >> (32 - shift));
        }

        public static ulong DefaultSeed { get; } = 0;

        private static ulong GenerateSeed()
        {
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[sizeof(ulong)];
                rng.GetBytes(bytes);
                return BitConverter.ToUInt64(bytes, 0);
            }
        }

        #region AutoGenerated

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a) => (a);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b) => (a ^ b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c) => (a ^ b ^ c);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d) => (a ^ b ^ c ^ d);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e) => (a ^ b ^ c ^ d ^ e);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f) => (a ^ b ^ c ^ d ^ e ^ f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g) => (a ^ b ^ c ^ d ^ e ^ f ^ g);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w, int x) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w ^ x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w, int x, int y) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w ^ x ^ y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Xor(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r, int s, int t, int u, int v, int w, int x, int y, int z) => (a ^ b ^ c ^ d ^ e ^ f ^ g ^ h ^ i ^ j ^ k ^ l ^ m ^ n ^ o ^ p ^ q ^ r ^ s ^ t ^ u ^ v ^ w ^ x ^ y ^ z);

        #endregion

        public static int Xor(params int[] hashList)
        {
            int ret = 0;
            foreach (var i in hashList)
                ret ^= i;
            return ret;
        }

    }

    class SparseChunk
    {
        public readonly ReadOnlyMemory<byte> Memory;
        public readonly bool IsSparse;
        public readonly long Offset;
        public readonly int Size;

        public SparseChunk(long offset, int size)
        {
            this.IsSparse = true;
            this.Size = size;
            this.Memory = default;
            this.Offset = offset;
        }

        public SparseChunk(long offset, ReadOnlyMemory<byte> memory)
        {
            this.IsSparse = false;
            this.Memory = memory;
            this.Size = memory.Length;
            this.Offset = offset;
        }

        public ReadOnlyMemory<byte> GetMemoryOrGenerateSparse()
        {
            if (this.IsSparse == false)
                return this.Memory;
            else
                return ZeroedSharedMemory<byte>.GetZeroFilledMemory(this.Size);
        }
    }

    class SparseChunk<T>
    {
        public readonly ReadOnlyMemory<T> Memory;
        public readonly bool IsSparse;
        public readonly long Offset;
        public readonly int Size;

        public SparseChunk(long offset, int size)
        {
            this.IsSparse = true;
            this.Size = size;
            this.Memory = default;
            this.Offset = offset;
        }

        public SparseChunk(long offset, ReadOnlyMemory<T> memory)
        {
            this.IsSparse = false;
            this.Memory = memory;
            this.Size = memory.Length;
            this.Offset = offset;
        }

        public ReadOnlyMemory<T> GetMemoryOrGenerateSparse()
        {
            if (this.IsSparse == false)
                return this.Memory;
            else
                return ZeroedSharedMemory<T>.GetZeroFilledMemory(this.Size);
        }
    }

    readonly struct DividedSegment
    {
        public readonly long AbsolutePosition;
        public readonly long RelativePosition;
        public readonly long InSegmentOffset;
        public readonly long Size;
        public readonly long SegmentIndex;

        public DividedSegment(long absolutePosition, long relativePosition, long inSegmentOffset, long size, long segmentIndex)
        {
            this.AbsolutePosition = absolutePosition;
            this.RelativePosition = relativePosition;
            this.InSegmentOffset = inSegmentOffset;
            this.Size = size;
            this.SegmentIndex = segmentIndex;
        }
    }

    class DividedSegmentList
    {
        public readonly long SegmentSize;
        public readonly long Start;
        public readonly long Size;

        public readonly DividedSegment[] SegmentList;

        public DividedSegmentList(long start, long size, long segmentSize)
        {
            this.Start = start;
            this.Size = size;
            this.SegmentSize = segmentSize;
            this.SegmentList = CalcSegmentInfo(start, size, segmentSize);
        }

        DividedSegment[] CalcSegmentInfo(long start, long size, long segmentSize)
        {
            checked
            {
                if (start < 0) throw new ArgumentOutOfRangeException("start");
                if (size < 0) throw new ArgumentOutOfRangeException("size");
                if (segmentSize <= 0) throw new ArgumentOutOfRangeException("size");

                long end = start + size;

                if (end == start)
                    return new DividedSegment[0];

                long startIndex = start / segmentSize;
                long endIndex = (end - 1) / segmentSize;
                long numIndex = endIndex - startIndex + 1;
                Debug.Assert(numIndex >= 1);

                DividedSegment[] ret = new DividedSegment[numIndex];

                for (long i = startIndex; i <= endIndex; i++)
                {
                    long startOfThisSegment = i * segmentSize;
                    long endOfThisSegment = (i + 1) * segmentSize;

                    long absoluteStart = Math.Max(startOfThisSegment, start);
                    long absoluteEnd = Math.Min(endOfThisSegment, end);
                    long sizeInThisSegment = absoluteEnd - absoluteStart;

                    Debug.Assert(sizeInThisSegment >= 1);
                    Debug.Assert(sizeInThisSegment <= segmentSize);

                    ret[i - startIndex] = new DividedSegment(absoluteStart, absoluteStart - start, absoluteStart - startOfThisSegment, sizeInThisSegment, i);
                }

                return ret;
            }
        }
    }

    namespace Legacy
    {
        class XmlAndXsd
        {
            public byte[] XmlData;
            public byte[] XsdData;
            public string XmlFileName;
            public string XsdFileName;
        }
    }

    // 1 度しか実行しない処理を実行しやすくするための構造体
    struct Once
    {
        volatile private int flag;
        public void Set() => IsFirstCall();
        public bool IsFirstCall() => (Interlocked.CompareExchange(ref this.flag, 1, 0) == 0);
        public bool IsSet => (this.flag != 0);
        public static implicit operator bool(Once once) => once.flag != 0;
        public void Reset() => this.flag = 0;

        public override string ToString() => IsSet.ToString();
    }

    // 再試行ヘルパー
    class RetryHelper<T>
    {
        public int DefaultRetryInterval { get; set; }
        public int DefaultTryCount { get; set; }

        public RetryHelper(int defaultRetry_interval, int defaultTryCount)
        {
            this.DefaultRetryInterval = defaultRetry_interval;
            this.DefaultTryCount = defaultTryCount;
        }

        public async Task<T> RunAsync(Func<Task<T>> proc, int? retryInterval = null, int? tryCount = null)
        {
            if (retryInterval == null) retryInterval = DefaultRetryInterval;
            if (tryCount == null) tryCount = DefaultTryCount;
            tryCount = Math.Max((int)tryCount, 1);
            retryInterval = Math.Max((int)retryInterval, 0);

            Exception first_exception = null;

            for (int i = 0; i < tryCount; i++)
            {
                try
                {
                    if (i >= 1) Dbg.WriteLine("Retrying...");
                    T ret = await proc();
                    return ret;
                }
                catch (Exception ex)
                {
                    if (first_exception == null)
                    {
                        first_exception = ex;
                    }

                    Dbg.WriteLine($"RetryHelper: round {i} error. Retrying in {retryInterval} msecs...");

                    await Task.Delay((int)retryInterval);
                }
            }

            throw first_exception;
        }
    }

    class StaticModule : StaticModule<int, int>
    {
        public StaticModule(Action initProc, Action freeProc)
            : base(_ => initProc(), () => { freeProc(); return default; })
        {
        }

        public void Init() => base.Init(0);
    }

    class StaticModule<TResult> : StaticModule<int, TResult>
    {
        public StaticModule(Action initProc, Func<TResult> freeProc)
            : base(_ => initProc(), freeProc)
        {
        }

        public void Init() => base.Init(0);
    }

    class StaticModule<TOptions, TResult>
    {
        readonly Action<TOptions> InitProc;
        readonly Func<TResult> FreeProc;
        readonly CriticalSection LockObj = new CriticalSection();

        readonly List<Action> ActionListAfterInit = new List<Action>();

        public bool Initialized { get; private set; } = false;
        public bool Freeing { get; private set; } = false;

        public StaticModule(Action<TOptions> initProc, Func<TResult> freeProc)
        {
            this.InitProc = initProc;
            this.FreeProc = freeProc;
        }

        public void Init(TOptions options)
        {
            lock (LockObj)
            {
                if (Initialized)
                    throw new ApplicationException("The StaticModule object is already initialized.");

                this.InitProc(options);

                Initialized = true;

                foreach (Action action in ActionListAfterInit)
                {
                    try
                    {
                        action();
                    }
                    catch { }
                }

                ActionListAfterInit.Clear();
            }
        }

        public void AddAfterInitAction(Action action)
        {
            if (action != null)
                ActionListAfterInit.Add(action);
        }

        public TResult Free()
        {
            lock (LockObj)
            {
                Freeing = true;

                ActionListAfterInit.Clear();

                Interlocked.MemoryBarrierProcessWide();

                try
                {
                    Interlocked.MemoryBarrierProcessWide();
                    if (Initialized == false)
                        throw new ApplicationException("The StaticModule object is not initialized.");

                    TResult ret = this.FreeProc();

                    Interlocked.MemoryBarrierProcessWide();

                    Initialized = false;

                    Interlocked.MemoryBarrierProcessWide();

                    return ret;
                }
                finally
                {
                    Freeing = false;
                }
            }
        }

        public void CheckInitalized()
        {
            if (this.Initialized == false || Freeing)
                throw new ApplicationException("The StaticModule object is not initialized.");
        }
    }

    class SingletonFastArraySlim<TKey, TObject>
    where TObject : class
    where TKey : Enum
    {
        public const int MaxElements = 8_000_000; // Max 64Mbytes

        readonly Func<TKey, TObject> CreateProc;
        TObject[] ObjectList;
        public int Count { get; private set; }

        int MaxValue;

        public SingletonFastArraySlim(Func<TKey, TObject> createProc)
            : this(createProc, Util.GetMaxEnumValue<TKey>()) { }

        public SingletonFastArraySlim(Func<TKey, TObject> createProc, TKey maxValue)
        {
            this.CreateProc = createProc;

            ulong tmp = maxValue._RawReadValueUInt64();

            if (tmp >= MaxElements)
                throw new ArgumentException($"The maxValue is {tmp} while the allowed max elements is {MaxElements}.");

            this.MaxValue = (int)tmp;

            this.ObjectList = new TObject[this.MaxValue + 1];

            this.Count = 0;
        }

        public TObject this[TKey key] => CreateOrGet(key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TObject CreateOrGet(TKey key)
        {
            ulong value = key._RawReadValueUInt64();
            if (value > (ulong)MaxValue)
                throw new ArgumentException($"The key value is {value} while the allowed max value is {MaxValue}.");
            int valueInt = (int)value;

            if (this.ObjectList[valueInt] != null)
            {
                return this.ObjectList[valueInt];
            }
            else
            {
                this.ObjectList[valueInt] = this.CreateProc(key);
                this.Count++;
            }
            return this.ObjectList[valueInt];
        }

        public void Clear()
        {
            Array.Clear(this.ObjectList, 0, this.ObjectList.Length);

            this.Count = 0;
        }
    }


    class SingletonFastArray<TKey, TObject> : IDisposable
        where TObject : class
        where TKey : Enum
    {
        public const int MaxElements = 8_000_000; // Max 64Mbytes

        readonly CriticalSection LockObj = new CriticalSection();
        readonly Func<TKey, TObject> CreateProc;
        TObject[] ObjectList;
        public int Count { get; private set; }

        int MaxValue;

        public SingletonFastArray(Func<TKey, TObject> createProc)
            : this(createProc, Util.GetMaxEnumValue<TKey>()) { }

        public SingletonFastArray(Func<TKey, TObject> createProc, TKey maxValue)
        {
            this.CreateProc = createProc;

            ulong tmp = maxValue._RawReadValueUInt64();

            if (tmp >= MaxElements)
                throw new ArgumentException($"The maxValue is {tmp} while the allowed max elements is {MaxElements}.");

            this.MaxValue = (int)tmp;

            this.ObjectList = new TObject[this.MaxValue + 1];

            this.Count = 0;
        }

        public TObject this[TKey key] => CreateOrGet(key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TObject CreateOrGet(TKey key)
        {
            ulong value = key._RawReadValueUInt64();
            if (value > (ulong)MaxValue)
                throw new ArgumentException($"The key value is {value} while the allowed max value is {MaxValue}.");
            int valueInt = (int)value;

            if (DisposeFlag) throw new ObjectDisposedException("SingletonFastArray");

            if (this.ObjectList[valueInt] != null) return this.ObjectList[valueInt];

            lock (LockObj)
            {
                if (DisposeFlag) throw new ObjectDisposedException("SingletonFastArray");

                if (this.ObjectList[valueInt] == null)
                {
                    this.ObjectList[valueInt] = this.CreateProc(key);
                    this.Count++;
                }
                return this.ObjectList[valueInt];
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            Clear();
        }

        public void Clear()
        {
            List<IDisposable> disposalList = new List<IDisposable>();

            lock (LockObj)
            {
                for (int i = 0; i < this.ObjectList.Length; i++)
                    if (this.ObjectList[i] != null)
                        if (this.ObjectList[i] is IDisposable disposable)
                            disposalList.Add(disposable);

                Array.Clear(this.ObjectList, 0, this.ObjectList.Length);

                this.Count = 0;
            }

            foreach (var disposable in disposalList)
                disposable._DisposeSafe();
        }
    }

    class SingletonSlim<TObject> where TObject : class
    {
        readonly Func<TObject> CreateProc;
        TObject Object = null;
        public bool IsCreated { get; private set; }

        public SingletonSlim(Func<TObject> createProc)
        {
            this.CreateProc = createProc;
        }

        public static implicit operator TObject(SingletonSlim<TObject> singleton) => singleton.CreateOrGet();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TObject CreateOrGet()
        {
            if (this.Object != null)
            {
                return this.Object;
            }
            else
            {
                this.Object = this.CreateProc();
                this.IsCreated = true;
                return this.Object;
            }
        }

        public void Clear()
        {
            this.Object = null;
            IsCreated = false;
        }
    }

    class Singleton<TObject> : IDisposable where TObject : class
    {
        readonly CriticalSection LockObj = new CriticalSection();
        readonly Func<TObject> CreateProc;
        TObject Object = null;
        public bool IsCreated { get; private set; }

        public Singleton(Func<TObject> createProc)
        {
            this.CreateProc = createProc;
        }

        public static implicit operator TObject(Singleton<TObject> singleton) => singleton.CreateOrGet();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TObject CreateOrGet()
        {
            if (DisposeFlag) throw new ObjectDisposedException("Singleton");

            if (this.Object != null) return this.Object;

            lock (LockObj)
            {
                if (DisposeFlag) throw new ObjectDisposedException("Singleton");

                if (this.Object == null)
                {
                    this.Object = this.CreateProc();
                    this.IsCreated = true;
                }
                return this.Object;
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            Clear();
        }

        public void Clear()
        {
            TObject obj = null;
            lock (LockObj)
            {
                obj = this.Object;
                this.Object = null;
            }

            if (obj is IDisposable disposeTarget)
                disposeTarget._DisposeSafe();

            IsCreated = false;
        }
    }

    class Singleton<TKey, TObject> : IDisposable where TObject : class
    {
        readonly CriticalSection LockObj = new CriticalSection();
        readonly Func<TKey, TObject> CreateProc;
        readonly Dictionary<TKey, TObject> Table;
        public IEnumerable<TKey> Keys => this.Table.Keys;

        public Singleton(Func<TKey, TObject> createProc, IEqualityComparer<TKey> keyComparer = null)
        {
            this.CreateProc = createProc;

            if (keyComparer == null)
                this.Table = new Dictionary<TKey, TObject>();
            else
                this.Table = new Dictionary<TKey, TObject>(keyComparer);
        }

        public TObject this[TKey key] => CreateOrGet(key);

        public TObject CreateOrGet(TKey key)
        {
            if (DisposeFlag) throw new ObjectDisposedException("Singleton");

            lock (LockObj)
            {
                if (DisposeFlag) throw new ObjectDisposedException("Singleton");

                if (this.Table.TryGetValue(key, out TObject obj) == false)
                {
                    obj = this.CreateProc(key);
                    this.Table.Add(key, obj);
                }
                return obj;
            }
        }

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            Clear();
        }

        public void Clear()
        {
            TObject[] list;

            lock (LockObj)
            {
                list = this.Table.Values.ToArray();
                this.Table.Clear();
            }

            foreach (var obj in list)
            {
                if (obj is IDisposable disposeTarget)
                    disposeTarget._DisposeSafe();
            }
        }
    }

    class SingletonSlim<TKey, TObject> where TObject : class
    {
        readonly Func<TKey, TObject> CreateProc;
        readonly Dictionary<TKey, TObject> Table;
        public IEnumerable<TKey> Keys => this.Table.Keys;

        public SingletonSlim(Func<TKey, TObject> createProc, IEqualityComparer<TKey> keyComparer = null)
        {
            this.CreateProc = createProc;

            if (keyComparer == null)
                this.Table = new Dictionary<TKey, TObject>();
            else
                this.Table = new Dictionary<TKey, TObject>(keyComparer);
        }

        public TObject this[TKey key] => CreateOrGet(key);

        public TObject CreateOrGet(TKey key)
        {
            if (this.Table.TryGetValue(key, out TObject obj) == false)
            {
                obj = this.CreateProc(key);
                this.Table.Add(key, obj);
            }
            return obj;
        }

        public void Clear()
        {
            TObject[] list;

            list = this.Table.Values.ToArray();
            this.Table.Clear();

            foreach (var obj in list)
            {
                if (obj is IDisposable disposeTarget)
                    disposeTarget._DisposeSafe();
            }
        }
    }

    namespace Legacy
    {
        // シングルトン
        struct OldSingleton<T> where T : class
        {
            static object lockobj = new object();
            T obj;

            public T CreateOrGet(Func<T> createProc)
            {
                lock (lockobj)
                {
                    if (obj == null)
                        obj = createProc();
                    return obj;
                }
            }
        }
    }

    static class GlobalObjectExchange
    {
        static Dictionary<string, object> table = new Dictionary<string, object>();

        public static object TryWithdraw(string token)
        {
            lock (table)
            {
                if (table.ContainsKey(token))
                {
                    object ret = table[token];
                    table.Remove(token);
                    return ret;
                }
            }
            return null;
        }

        public static object Withdraw(string token)
        {
            object ret = TryWithdraw(token);
            if (ret == null) throw new ApplicationException("invalid token");
            return ret;
        }

        public static string Deposit(object o)
        {
            string id = Str.NewGuid();
            lock (table) table.Add(id, o);
            return id;
        }
    }

    class IntervalManager
    {
        long LastTick = 0;
        public int Interval { get; private set; }
        int LastInterval;

        public IntervalManager(int interval)
        {
            LastTick = Time.Tick64;
            this.LastInterval = this.Interval = interval;
        }

        public int GetNextInterval(int? nextInterval = null)
            => GetNextInterval(out _, nextInterval);

        int Count = 0;

        public int GetNextInterval(out int lastTimediff, int? nextInterval = null)
        {
            long now = Time.Tick64;
            lastTimediff = (int)(now - LastTick);
            int over = lastTimediff - this.LastInterval;
            if (nextInterval != null) this.Interval = (int)nextInterval;
            this.LastInterval = this.Interval;
            int ret = this.Interval;
            if (over > 0)
            {
                ret = this.Interval - over;
            }
            LastTick = now;
            if (ret <= 0) ret = 1;
            if (this.Interval == Timeout.Infinite) ret = Timeout.Infinite;
            if (lastTimediff <= 0) lastTimediff = 1;
            if (Count == 0) lastTimediff = this.LastInterval;
            Count++;
            return ret;
        }
    }

    class Distinct<T>
    {
        Dictionary<T, T> d = new Dictionary<T, T>();

        public T AddOrGet(T obj)
        {
            if (d.ContainsKey(obj))
            {
                return d[obj];
            }
            else
            {
                d.Add(obj, obj);
                return obj;
            }
        }

        public T[] Values { get => d.Keys._ToArrayList(); }
    }

    namespace Legacy
    {
        class DelayLoader<T> : IDisposable
        {
            Func<long, (T, long)> LoadProc;
            int RetryInterval;
            int UpdateInterval;
            ThreadObj Thread;
            ManualResetEventSlim HaltEvent = new ManualResetEventSlim();
            public readonly ManualResetEventSlim LoadCompleteEvent = new ManualResetEventSlim();
            bool HaltFlag = false;

            public T Data { get; private set; }

            public DelayLoader(Func<long, (T data, long dataTimeStamp)> loadProc, int retryInterval = 1000, int updateInterval = 1000)
            {
                this.LoadProc = loadProc;
                this.RetryInterval = retryInterval;
                this.UpdateInterval = updateInterval;

                this.Thread = new ThreadObj(ThreadProc, isBackground: true);
            }

            void ThreadProc(object param)
            {
                long lastTimeStamp = 0;

                (T data, long dataTimeStamp) ret;

                LABEL_RETRY:
                try
                {
                    // データの読み込み
                    ret = LoadProc(lastTimeStamp);
                }
                catch (Exception ex)
                {
                    Dbg.WriteLine(ex.ToString());

                    HaltEvent.Wait(RetryInterval);

                    if (HaltFlag)
                    {
                        return;
                    }

                    goto LABEL_RETRY;
                }

                if (ret.data != null)
                {
                    // 読み込んだデータをグローバルにセット
                    lastTimeStamp = ret.dataTimeStamp;
                    this.Data = ret.data;
                    LoadCompleteEvent.Set();
                }

                // 次回まで待機
                HaltEvent.Wait(UpdateInterval);
                if (HaltFlag)
                {
                    return;
                }

                goto LABEL_RETRY;
            }

            Once DisposeFlag;
            public void Dispose()
            {
                if (DisposeFlag.IsFirstCall())
                {
                    HaltFlag = true;
                    HaltEvent.Set();
                    Thread.WaitForEnd();
                }
            }
        }
    }

    class CachedProperty<T>
    {
        object LockObj = new object();
        bool IsCached = false;
        T CachedValue;

        Func<T> Getter;
        Func<T, T> Setter;
        Func<T, T> Normalizer;

        public CachedProperty(Func<T, T> setter = null, Func<T> getter = null, Func<T, T> normalizer = null)
        {
            Setter = setter;
            Getter = getter;
            Normalizer = normalizer;
        }

        public void Set(T value)
        {
            if (Setter == null) throw new NotImplementedException();
            if (Normalizer != null)
                value = Normalizer(value);

            lock (LockObj)
            {
                value = Setter(value);
                CachedValue = value;
                IsCached = true;
            }
        }

        public T Get()
        {
            if (IsCached)
                return CachedValue;

            lock (LockObj)
            {
                if (IsCached)
                    return CachedValue;

                if (Getter == null) throw new NotImplementedException("The value is undefined yet.");

                T value = Getter();

                if (Normalizer != null)
                    value = Normalizer(value);

                CachedValue = value;
                IsCached = true;

                return value;
            }
        }

        public void Flush()
        {
            lock (LockObj)
            {
                IsCached = false;
                CachedValue = default;
            }
        }

        public T Value { get => Get(); set => Set(value); }

        public static implicit operator T(CachedProperty<T> r) => r.Value;
    }

    static class Limbo
    {
        public static bool Bool = false;
        public static long SInt64 = 0;
        public static ulong UInt64 = 0;
        public static int SInt32 = 0;
        public static uint UInt32 = 0;

        public volatile static bool BoolVolatile = false;
        public volatile static int SInt32Volatile = 0;
        public volatile static uint UInt32Volatile = 0;
        public volatile static object ObjectVolatileSlow = null;
    }

    static class GlobalMicroBenchmark
    {
        const int DefaultOfDefaultDurationMSecs = 250;
        const LogSwitchType SwitchType = LogSwitchType.Month;

        public static readonly StaticModule Module = new StaticModule(InitModule, FreeModule);

        static void InitModule()
        {
            DefaultDurationMSecs = DefaultOfDefaultDurationMSecs;
        }

        static void FreeModule()
        {
            if (LogRouter != null)
            {
                LogRouter._DisposeSafe();
                LogRouter = null;

                OnceFlag.Reset();
            }
        }

        public static int DefaultDurationMSecs { get; private set; }
        static LogRouter LogRouter = null;
        static Once OnceFlag;

        public static void SetParameters(bool recordLog, int? defaultDurationMsecs = null)
        {
            if (OnceFlag.IsFirstCall() == false) return;

            if (defaultDurationMsecs.HasValue)
                DefaultDurationMSecs = defaultDurationMsecs.Value;

            if (recordLog)
            {
                var logRouter = new LogRouter();

                logRouter.InstallLogRoute(new LoggerLogRoute("data", LogPriority.Trace, "benchmark", Path.Combine(Env.AppRootDir, "BenchmarkRecords/Json"),
                    SwitchType,
                    new LogInfoOptions()
                    {
                        WithTypeName = false,
                        WriteAsJsonFormat = true,
                        WithTag = true
                    },
                    long.MaxValue));

                logRouter.InstallLogRoute(new LoggerLogRoute("text", LogPriority.Trace, "benchmark", Path.Combine(Env.AppRootDir, "BenchmarkRecords/Text"),
                    SwitchType,
                    new LogInfoOptions()
                    {
                        WithTypeName = false,
                        WriteAsJsonFormat = false,
                        WithTag = false
                    },
                    long.MaxValue));

                LogRouter = logRouter;
            }
        }

        public static void RecordStart()
        {
            WriteTextLog($"----- Start new benchmark -----");
            WriteTextLog($"Commit: {Dbg.GetCurrentGitCommitInfo()}");
            WriteTextLog($"ExeFileName: {Env.ExeFileName}");
            WriteTextLog($"OS: {Env.OsInfoString}");
            WriteTextLog($"Framework: {Env.FrameworkInfoString}");
            WriteTextLog($"Host: {Env.MachineName}");
        }

        public static void WriteTextLog(string text)
        {
            LogRouter?.PostLog(new LogRecord(text, tag: "text"), "data");
            LogRouter?.PostLog(new LogRecord(text, tag: "text"), "text");
        }

        public static void WriteDataLog(object data, string text)
        {
            LogRouter?.PostLog(new LogRecord(data, tag: "result"), "data");
            LogRouter?.PostLog(new LogRecord(text, tag: "result"), "text");
        }
    }

    class MicroBenchmarkQueue
    {
        List<(IMicroBenchmark bm, int priority, int index)> List = new List<(IMicroBenchmark bm, int priority, int index)>();

        public MicroBenchmarkQueue Add(IMicroBenchmark benchmark, bool enabled = true, int priority = 0)
        {
            if (enabled)
                List.Add((benchmark, priority, List.Count));
            return this;
        }

        public void Run()
        {
            foreach (var a in List.OrderBy(x => x.index).OrderBy(x => -x.priority).Select(x => x.bm))
                a.StartAndPrint();
        }
    }

    interface IMicroBenchmark
    {
        double Start(int duration = 0);
        double StartAndPrint(int duration = 0);
    }

    class MicroBenchmark : MicroBenchmark<int>
    {
        public MicroBenchmark(string name, int iterations, Action<int> proc, Func<int> init = null)
            : base(name, iterations, (_state, _iterations) => proc(_iterations), init) { }
    }

    class MicroBenchmark<TUserVariable> : IMicroBenchmark
    {
        public readonly string Name;
        volatile bool StopFlag;
        object LockObj = new object();
        public readonly int Iterations;

        public readonly Func<TUserVariable> Init;
        public readonly Action<TUserVariable, int> Proc;

        readonly Action<TUserVariable, int> DummyLoopProc = (state, count) =>
        {
            for (int i = 0; i < count; i++) Limbo.SInt64++;
        };

        public MicroBenchmark(string name, int iterations, Action<TUserVariable, int> proc, Func<TUserVariable> init = null)
        {
            Name = name;
            Init = init;
            Proc = proc;
            Iterations = Math.Max(iterations, 1);
        }

        public double StartAndPrint(int duration = 0)
        {
            double ret = Start(duration);

            double perSecond = 1000000000.0 / ret;
            if (ret == 0.0)
            {
                perSecond = 9_9999_9999_9999.0;
            }

            string str = $"{Name}: {ret.ToString("#,0.00")} ns, {((long)perSecond)._ToString3()} / sec";

            Con.WriteLine(str);

            GlobalMicroBenchmark.WriteDataLog(new { TestName = Name, Ret = ret, PerSecond = (long)perSecond }, str);

            return ret;
        }

        double MeasureInternal(int duration, TUserVariable state, Action<TUserVariable, int> proc, int interationsPassValue)
        {
            StopFlag = false;

            ManualResetEventSlim ev = new ManualResetEventSlim();

            Thread thread = new Thread(() =>
            {
                ev.Wait();
                Thread.Sleep(duration);
                StopFlag = true;
            });

            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.Start();

            long count = 0;
            Stopwatch sw = new Stopwatch();
            ev.Set();
            sw.Start();
            TimeSpan ts1 = sw.Elapsed;
            while (StopFlag == false)
            {
                if (Init == null && interationsPassValue == 0)
                {
                    DummyLoopProc(state, Iterations);
                }
                else
                {
                    proc(state, interationsPassValue);
                    if (interationsPassValue == 0)
                    {
                        for (int i = 0; i < Iterations; i++) Limbo.SInt64++;
                    }
                }
                count += Iterations;
            }
            TimeSpan ts2 = sw.Elapsed;
            TimeSpan ts = ts2 - ts1;
            thread.Join();

            double nano = (double)ts.Ticks * 100.0;
            double nanoPerCall = nano / (double)count;

            if (nanoPerCall < 0.001)
                nanoPerCall = 0.0;

            return nanoPerCall;
        }

        public double Start(int duration = 0)
        {
            lock (LockObj)
            {
                if (duration <= 0) duration = GlobalMicroBenchmark.DefaultDurationMSecs;

                TUserVariable state = default(TUserVariable);

                double v1 = 0;
                double v2 = 0;

                if (Init != null) state = Init();
                v2 = MeasureInternal(duration, state, Proc, 0);

                if (Init != null) state = Init();
                v1 = MeasureInternal(duration, state, Proc, Iterations);

                double v = Math.Max(v1 - v2, 0);
                //v -= EmptyBaseLine;
                v = Math.Max(v, 0);
                return v;
            }
        }
    }

    struct EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    class EtaCalculator
    {
        class Data
        {
            public long Tick { get; }
            public double Percentage { get; }

            public Data(long tick, double percentage)
            {
                this.Tick = tick;
                this.Percentage = Math.Min(Math.Max(percentage, 0.0), 100.0);
            }
        }

        readonly long ShorterSample, LongerSample;

        public EtaCalculator(long shorterSample = 500, long longerSample = 5000)
        {
            shorterSample = Math.Max(shorterSample, 1);
            longerSample = Math.Max(longerSample, 1);

            longerSample = Math.Max(longerSample, shorterSample * 2);

            ShorterSample = shorterSample;
            LongerSample = longerSample;
        }

        readonly CriticalSection LockObj = new CriticalSection();

        Data ShorterData1 = null;
        Data ShorterData2 = null;

        Data LongerData1 = null;
        Data LongerData2 = null;

        public long? Calculate(long tick, double? percentage)
        {
            try
            {
                if (percentage == null)
                    return null;

                if (percentage >= 100.0)
                    return 0;

                lock (LockObj)
                {
                    long? etaFromShorterSample = null;
                    long? etaFromLongerSample = null;
                    double remainPercentage = 100 - percentage ?? 0.0;

                    Data d = new Data(tick, percentage ?? 0.0);
                    long tickDelta;
                    double percentageDelta;

                    if (ShorterData1 == null)
                    {
                        ShorterData1 = d;
                        ShorterData2 = d;
                    }
                    else
                    {
                        if ((ShorterData1.Tick + ShorterSample) <= d.Tick)
                        {
                            //                        ShorterData2 = ShorterData1;
                            ShorterData1 = d;
                        }
                    }

                    tickDelta = ShorterData1.Tick - ShorterData2.Tick;
                    if (tickDelta > 0)
                    {
                        percentageDelta = ShorterData1.Percentage - ShorterData2.Percentage;
                        if (percentageDelta > 0)
                        {
                            etaFromShorterSample = (long)((double)tickDelta * remainPercentage / (double)percentageDelta);
                        }
                    }

                    if (LongerData1 == null)
                    {
                        LongerData1 = d;
                        LongerData2 = d;
                    }
                    else
                    {
                        if ((LongerData1.Tick + LongerSample) <= d.Tick)
                        {
                            LongerData2 = LongerData1;
                            LongerData1 = d;
                        }
                    }

                    tickDelta = LongerData1.Tick - LongerData2.Tick;
                    if (tickDelta > 0)
                    {
                        percentageDelta = LongerData1.Percentage - LongerData2.Percentage;
                        if (percentageDelta > 0)
                        {
                            etaFromLongerSample = (long)((double)tickDelta * remainPercentage / (double)percentageDelta);
                        }
                    }

                    if (etaFromLongerSample != null) return (long)etaFromLongerSample;
                    if (etaFromShorterSample != null) return (long)etaFromShorterSample;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    [Flags]
    enum ProgressReportType
    {
        Start,
        InProgress,
        Abort,
        Finish,
    }

    class ProgressData
    {
        public long CurrentCount { get; }
        public long? TotalCount { get; }
        public bool IsFinish { get; }

        public ProgressData(long currentCount, long? totalCount, bool isFinish = false)
        {
            this.CurrentCount = currentCount;
            this.TotalCount = totalCount;
            this.IsFinish = isFinish;
            if (this.IsFinish && this.TotalCount != null)
            {
                this.CurrentCount = Math.Max((long)this.TotalCount, this.CurrentCount);
            }
            if (this.TotalCount != null)
            {
                if (this.CurrentCount >= this.TotalCount)
                {
                    this.IsFinish = true;
                }
            }
        }

        public static ProgressData Empty { get; } = new ProgressData(0, null, false);

        string LongValueToString(long? value, bool tostr3, string unitString = "", bool fileStr = false)
        {
            if (fileStr)
            {
                return value == null ? "??" : Str.GetFileSizeStr((long)value);
            }

            string s = value == null ? "??" : (tostr3 ? ((long)value)._ToString3() : value.ToString());
            if (unitString._IsFilled()) s += " " + unitString;
            return s;
        }

        public virtual string GetPercentageStr(ProgressReporterSettingBase setting, double? value)
        {
            if (value == null) return "?? %";

            return $"{value:F1}%";
        }

        public virtual string GenerateStatusStr(ProgressReporterSettingBase setting, ProgressReport report)
        {
            string statusStr = "...";
            if (report.Type == ProgressReportType.Start) statusStr = " Started:";
            if (report.Type == ProgressReportType.Finish) statusStr = " Finished:";
            if (report.Type == ProgressReportType.Abort) statusStr = " Aborted:";
            string etaStr = "";

            if (setting.ShowEta)
            {
                if (report.Type == ProgressReportType.InProgress && report.Eta != null)
                {
                    TimeSpan timeSpan = Util.ConvertTimeSpan((ulong)(report.Eta ?? 0));

                    etaStr = $" ETA {timeSpan._ToTsStr()}";
                }
            }

            return $"{setting.Title}{statusStr} {GetPercentageStr(setting, report.Percentage)} " +
                $"({LongValueToString(report.Data.CurrentCount, setting.ToStr3, "", setting.FileSizeStr)} / {LongValueToString(report.Data.TotalCount, setting.ToStr3, setting.Unit, setting.FileSizeStr)}).{etaStr}";
        }
    }

    sealed class ProgressReport
    {
        public ProgressReportType Type { get; }

        public ProgressData Data { get; }
        public double? Percentage { get; }
        public long? Eta { get; }

        public long Tick { get; }

        public ProgressReport(ProgressReportType type, ProgressData data, double? percentage, long? eta)
        {
            this.Data = data;
            this.Percentage = percentage;
            this.Tick = Tick64.Now;
            this.Type = type;
            this.Eta = eta;
        }
    }

    class ProgressReportTimingSetting
    {
        public bool ReportEveryTiming { get; }
        public int ReportTimingIntervalMsecs { get; }
        public double ReportTimingPercentageSpace { get; }

        public ProgressReportTimingSetting(bool reportEveryTiming = false, int reportTimingMsecs = 1000, double reportTimingPercentageSpace = 10.0)
        {
            this.ReportEveryTiming = reportEveryTiming;
            this.ReportTimingIntervalMsecs = Math.Max(reportTimingMsecs, 0);
            this.ReportTimingPercentageSpace = Math.Max(0.0, reportTimingPercentageSpace);
        }
    }

    class ProgressReporterSettingBase
    {
        public string Title { get; set; }

        public bool FileSizeStr { get; set; }
        public string Unit { get; set; }
        public bool ToStr3 { get; set; }

        public bool ShowEta { get; set; }

        public ProgressReportTimingSetting ReportTimingSetting { get; set; }

        public ProgressReporterSettingBase(string title = "Processing", string unit = "", bool toStr3 = false, bool showEta = true, bool fileSizeStr = false, ProgressReportTimingSetting reportTimingSetting = null)
        {
            if (reportTimingSetting == null)
            {
                reportTimingSetting = new ProgressReportTimingSetting();
            }
            this.Title = title._NonNullTrim();
            this.Unit = unit._NonNullTrim();
            this.ToStr3 = toStr3;
            this.ShowEta = showEta;
            this.FileSizeStr = fileSizeStr;

            this.ReportTimingSetting = reportTimingSetting;
        }
    }

    class NullProgressReporter : ProgressReporterBase
    {
        public NullProgressReporter(object state) : base(new ProgressReporterSettingBase(), state) { }

        public override void ReportProgress(ProgressData data) { }

        protected override void ReceiveProgressInternal(ProgressData data, ProgressReportType type) { }

        protected override void ReportedImpl(ProgressReport report) { }
    }

    abstract class ProgressReporterBase : IDisposable
    {
        readonly CriticalSection LockObj = new CriticalSection();

        readonly EtaCalculator EtaCalc = new EtaCalculator();

        ProgressReport ReportOnStart = null;
        ProgressReport LastReport = null;
        ProgressReport ReportOnFinishOrAbort = null;

        protected abstract void ReportedImpl(ProgressReport report);

        public ProgressReporterSettingBase Setting { get; }

        public object State { get; }

        public ProgressReporterBase(ProgressReporterSettingBase setting, object state)
        {
            this.Setting = setting;
            this.State = state;
        }

        public ProgressReport LastStatus
        {
            get
            {
                lock (LockObj)
                {
                    if (LastReport != null)
                    {
                        return LastReport;
                    }
                    else
                    {
                        return new ProgressReport(ProgressReportType.Start, ProgressData.Empty, null, null);
                    }
                }
            }
        }

        public virtual void ReportProgress(ProgressData data)
        {
            try
            {
                if (data == null) return;

                lock (LockObj)
                {
                    if (ReportOnStart == null)
                    {
                        ReceiveProgressInternal(data, ProgressReportType.Start);
                    }
                    else
                    {
                        ReceiveProgressInternal(data, data.IsFinish ? ProgressReportType.Finish : ProgressReportType.InProgress);
                    }
                }
            }
            catch
            {
            }
        }

        protected virtual void ReceiveProgressInternal(ProgressData data, ProgressReportType type)
        {
            double? percentage = null;

            long initialPosition = this.ReportOnStart?.Data.CurrentCount ?? 0;

            if (data.TotalCount != null)
            {
                long totalCount = Math.Max(data.TotalCount ?? 0, 1);
                long currentCount = Math.Min(Math.Max(data.CurrentCount, 0), totalCount);

                if (type == ProgressReportType.Finish)
                {
                    percentage = 100.0;
                }
                else
                {
                    percentage = (double)currentCount * 100.0 / (double)totalCount;
                    if (currentCount >= totalCount)
                    {
                        percentage = 100.0;
                    }
                }
            }
            else
            {
                if (type == ProgressReportType.Finish)
                {
                    percentage = 100.0;
                }
                else if (type == ProgressReportType.Start)
                {
                    percentage = 0.0;
                }
                else if (type == ProgressReportType.Abort)
                {
                }
                else
                {
                }
            }

            if (ReportOnFinishOrAbort != null)
            {
                return;
            }

            long? eta = EtaCalc.Calculate(Time.Tick64, percentage);

            ProgressReport report = new ProgressReport(type, data, percentage, eta);

            if (type == ProgressReportType.Start)
            {
                ReportOnStart = report;
            }
            else if (type == ProgressReportType.Finish || type == ProgressReportType.Abort)
            {
                ReportOnFinishOrAbort = report;
            }

            LastReport = report;

            if (DetermineToReportOrNot(report))
            {
                ReportedImpl(report);
            }
        }

        ProgressReport LastReportedReport = null;

        protected virtual bool DetermineToReportOrNot(ProgressReport report)
        {
            bool ret = false;
            if (LastReportedReport == null)
            {
                ret = true;
            }
            else
            {
                if (report.Type == ProgressReportType.Abort || report.Type == ProgressReportType.Finish || report.Type == ProgressReportType.Start)
                {
                    ret = true;
                }
                else
                {
                    if (Setting.ReportTimingSetting.ReportEveryTiming)
                        ret = true;

                    if (Setting.ReportTimingSetting.ReportTimingIntervalMsecs > 0)
                    {
                        if ((LastReportedReport.Tick + Setting.ReportTimingSetting.ReportTimingIntervalMsecs) <= report.Tick)
                            ret = true;
                    }

                    if (LastReportedReport.Percentage == null && report.Percentage != null)
                        ret = true;

                    if (LastReportedReport.Percentage != null && report.Percentage == null)
                        ret = true;

                    if (Setting.ReportTimingSetting.ReportTimingPercentageSpace != 0.0)
                        if (LastReportedReport.Percentage != null && report.Percentage != null)
                            if ((LastReportedReport.Percentage + Setting.ReportTimingSetting.ReportTimingPercentageSpace) <= report.Percentage)
                                ret = true;
                }
            }

            if (ret)
            {
                LastReportedReport = report;
            }

            return ret;
        }

        public void Abort() => Dispose();

        public void Dispose() => Dispose(true);
        Once DisposeFlag;
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || DisposeFlag.IsFirstCall() == false) return;

            try
            {
                lock (LockObj)
                {
                    if (this.LastReport != null)
                    {
                        ReceiveProgressInternal(this.LastReport.Data, ProgressReportType.Abort);
                    }
                    else
                    {
                        ReceiveProgressInternal(ProgressData.Empty, ProgressReportType.Abort);
                    }
                }
            }
            catch
            {
            }
        }
    }

    [Flags]
    enum ProgressReporterOutputs
    {
        None = 0,
        Console = 1,
        Debug = 2,
    }

    delegate void ProgressReportListener(ProgressReport report, string str);

    class ProgressReporterSetting : ProgressReporterSettingBase
    {
        public ProgressReportListener Listener { get; set; }
        public ProgressReporterOutputs AdditionalOutputs { get; }

        public ProgressReporterSetting(ProgressReporterOutputs outputs, ProgressReportListener listener = null, string title = "Processing", string unit = "", bool toStr3 = false, bool showEta = true,
            bool fileSizeStr = false, ProgressReportTimingSetting reportTimingSetting = null)
            : base(title, unit, toStr3, showEta, fileSizeStr, reportTimingSetting)
        {
            this.AdditionalOutputs = outputs;
        }
    }

    class ProgressReporter : ProgressReporterBase
    {
        public ProgressReporterSetting MySetting => (ProgressReporterSetting)this.Setting;

        public ProgressReporter(ProgressReporterSetting setting, object state) : base(setting, state)
        {
        }

        protected override void ReportedImpl(ProgressReport report)
        {
            string str = report.Data.GenerateStatusStr(MySetting, report);

            if (str._IsFilled())
            {
                var outputs = MySetting.AdditionalOutputs;

                try
                {
                    if (outputs.Bit(ProgressReporterOutputs.Console))
                        Con.WriteLine(str);
                }
                catch { }

                try
                {
                    if (outputs.Bit(ProgressReporterOutputs.Debug))
                        Con.WriteDebug(str);
                }
                catch { }

                try
                {
                    MySetting.Listener?.Invoke(report, str);
                }
                catch { }
            }
        }
    }

    class ProgressFileProcessingReporter : ProgressReporter
    {
        public ProgressFileProcessingReporter(object state, ProgressReporterOutputs outputs, ProgressReportListener listener = null,
            string title = "Processing a file", ProgressReportTimingSetting reportTimingSetting = null)
            : base(new ProgressReporterSetting(outputs, listener, title, "", false, true, true, reportTimingSetting), state) { }
    }

    abstract class ProgressReporterFactoryBase
    {
        public ProgressReporterOutputs Outputs { get; set; }
        public ProgressReportListener Listener { get; set; }
        public ProgressReportTimingSetting ReportTimingSetting { get; set; }

        public ProgressReporterFactoryBase(ProgressReporterOutputs outputs, ProgressReportListener listener = null, ProgressReportTimingSetting reportTimingSetting = null)
        {
            this.Outputs = outputs;
            this.Listener = listener;
            this.ReportTimingSetting = reportTimingSetting;
        }

        public abstract ProgressReporterBase CreateNewReporter(string title, object state);
    }

    class ProgressFileProcessingReporterFactory : ProgressReporterFactoryBase
    {
        public ProgressFileProcessingReporterFactory(ProgressReporterOutputs outputs, ProgressReportListener listener = null, ProgressReportTimingSetting reportTimingSetting = null)
            : base(outputs, listener, reportTimingSetting) { }

        public override ProgressReporterBase CreateNewReporter(string title, object state)
        {
            return new ProgressFileProcessingReporter(state, this.Outputs, this.Listener, title, this.ReportTimingSetting);
        }
    }

    class NullReporterFactory : ProgressReporterFactoryBase
    {
        public NullReporterFactory()
            : base(ProgressReporterOutputs.None, null, null) { }

        public override ProgressReporterBase CreateNewReporter(string title, object state)
        {
            return new NullProgressReporter(state);
        }
    }

    static class ZeroedSharedMemory<T>
    {
        const int Size = 65536;
        public static readonly ReadOnlyMemory<T> Memory = new T[Size];
        public static ReadOnlyMemory<T> GetZeroFilledMemory(int size)
        {
            if (size < Size)
                return Memory.Slice(0, size);
            else
                return new T[size];
        }
    }

    class WeightedExceptionList
    {
        static long IdSeed = 0;

        class Entry
        {
            public long Id;
            public int Weight;
            public Exception Exception;
        }

        readonly List<Entry> List = new List<Entry>();

        CriticalSection LockObj = new CriticalSection();

        public void Add(Exception exception, int weight)
        {
            lock (LockObj)
            {
                List.Add(new Entry() { Exception = exception, Weight = weight, Id = Interlocked.Increment(ref IdSeed) });
            }
        }

        public Exception GetException()
        {
            lock (LockObj)
            {
                return this.List.OrderByDescending(x => x.Weight).ThenBy(x => IdSeed).FirstOrDefault()?.Exception ?? null;
            }
        }
    }

    class FieldReaderWriter
    {
        public readonly Dictionary<string, MemberInfo> MetadataTable = new Dictionary<string, MemberInfo>();

        public Type TargetType { get; }

        public IReadOnlyList<string> FieldOrPropertyNamesList { get; }
        public IReadOnlyList<string> MethodNamesList { get; }

        static readonly Singleton<Type, FieldReaderWriter> _PublicSingleton = new Singleton<Type, FieldReaderWriter>(t => new FieldReaderWriter(t, false));
        static readonly Singleton<Type, FieldReaderWriter> _PrivateSingleton = new Singleton<Type, FieldReaderWriter>(t => new FieldReaderWriter(t, true));

        public FieldReaderWriter(Type targetType, bool includePrivate = false)
        {
            this.TargetType = targetType;

            var fields = TargetType.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Concat(includePrivate ? TargetType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic) : new FieldInfo[0]);

            var properties = TargetType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty | BindingFlags.SetProperty)
                .Concat(includePrivate ? TargetType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.SetProperty) : new PropertyInfo[0]);

            var methods = TargetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod)
                .Concat(includePrivate ? TargetType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod) : new MethodInfo[0]);

            var m1 = TargetType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod);
            var m2 = TargetType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod);

            foreach (MemberInfo info in fields.Cast<MemberInfo>().Concat(properties.Cast<MemberInfo>().Concat(methods.Cast<MemberInfo>())))
            {
                MetadataTable.TryAdd(info.Name, info);
            }

            this.FieldOrPropertyNamesList = new List<string>(MetadataTable.Values.Where(x => x is FieldInfo || x is PropertyInfo).Select(x => x.Name));
            this.MethodNamesList = new List<string>(MetadataTable.Values.Where(x => x is MethodInfo).Select(x => x.Name));
        }

        public static FieldReaderWriter GetCached(Type type) => _PublicSingleton.CreateOrGet(type);
        public static FieldReaderWriter GetCached<T>() => GetCached(typeof(T));

        public static FieldReaderWriter GetCachedPrivate(Type type) => _PrivateSingleton.CreateOrGet(type);
        public static FieldReaderWriter GetCachedPrivate<T>() => GetCachedPrivate(typeof(T));

        public object Invoke(object targetObject, string name, params object[] parameters)
        {
            if (parameters == null) parameters = new object[1] { null };
            if (targetObject.GetType()._IsSubClassOfOrSame(this.TargetType) == false) throw new ArgumentException("Type of targetObject is different from TargetType.");

            if (this.MetadataTable.TryGetValue(name, out MemberInfo info) == false)
                throw new ArgumentException($"The member \"{name}\" not found.");

            switch (info)
            {
                case MethodInfo method:
                    try
                    {
                        return method.Invoke(targetObject, parameters);
                    }
                    catch (Exception ex)
                    {
                        throw ex._GetSingleException();
                    }

                default:
                    throw new ApplicationException($"The member \"{name}\" is not a method.");
            }
        }

        public object GetValue(object targetObject, string name)
        {
            if (targetObject.GetType()._IsSubClassOfOrSame(this.TargetType) == false) throw new ArgumentException("Type of targetObject is different from TargetType.");

            if (this.MetadataTable.TryGetValue(name, out MemberInfo info) == false)
                throw new ArgumentException($"The member \"{name}\" not found.");

            switch (info)
            {
                case FieldInfo field:
                    return field.GetValue(targetObject);

                case PropertyInfo property:
                    return property.GetValue(targetObject);

                default:
                    throw new ApplicationException($"The member \"{name}\" is not a field or a property.");
            }
        }

        public void SetValue(object targetObject, string name, object value)
        {
            if (targetObject.GetType()._IsSubClassOfOrSame(this.TargetType) == false) throw new ArgumentException("Type of targetObject is different from TargetType.");

            if (this.MetadataTable.TryGetValue(name, out MemberInfo info) == false || ((info as PropertyInfo)?.CanWrite ?? true) == false)
            {
                if (this.MetadataTable.TryGetValue($"<{name}>k__BackingField", out info) == false)
                {
                    throw new ArgumentException($"The member \"{name}\" not found.");
                }
            }

            switch (info)
            {
                case FieldInfo field:
                    field.SetValue(targetObject, value);
                    return;

                case PropertyInfo property:
                    property.SetValue(targetObject, value);
                    return;

                default:
                    throw new ApplicationException($"The member \"{name}\" is not a field or a property.");
            }
        }

        public T CalcDiff<T>(T current, T prev)
        {
            T ret = (T)Util.NewWithoutConstructor(this.TargetType);

            CalcDiff(ret, current, prev);

            return ret;
        }

        public void CalcDiff(object dest, object current, object prev)
        {
            foreach (string name in this.FieldOrPropertyNamesList)
            {
                object prevValue = this.GetValue(prev, name);
                object currentValue = this.GetValue(current, name);
                object destValue = Util.Subtract(currentValue, prevValue);
                this.SetValue(dest, name, destValue);
            }
        }

        public T DivideBy<T>(T target, double by)
        {
            T ret = (T)Util.NewWithoutConstructor(this.TargetType);

            DivideBy(ret, target, by);

            return ret;
        }

        public void DivideBy(object dest, object target, double by)
        {
            foreach (string name in this.FieldOrPropertyNamesList)
            {
                object currentValue = this.GetValue(target, name);
                object destValue = Util.Divide(currentValue, by);
                this.SetValue(dest, name, destValue);
            }
        }

        public void CopyValues(object dest, object src)
        {
            foreach (string name in this.FieldOrPropertyNamesList)
            {
                object srcValue = this.GetValue(src, name);
                this.SetValue(dest, name, srcValue);
            }
        }

        public object CreateClone(object targetObject)
        {
            if (targetObject.GetType() != this.TargetType) throw new ArgumentException("Type of targetObject is different from TargetType.");

            object ret = Util.NewWithoutConstructor(this.TargetType);

            CopyValues(ret, targetObject);

            return ret;
        }

        public T CreateClone<T>(T targetObject) => (T)CreateClone((object)targetObject);
    }

    [Flags]
    enum StatisticsReporterLogTypes
    {
        None = 0,
        Snapshot = 1,
        Diffs = 2,
        Velocity = 4,
        All = 0x7fffffff,
    }

    class StatisticsReporter<T> : AsyncServiceWithMainLoop
        where T : class
    {
        public readonly T CurrentValues;

        public int Interval { get; }

        readonly CriticalSection LockObj = new CriticalSection();

        public readonly AsyncEventListenerList<T, NonsenseEventType> ListenerList = new AsyncEventListenerList<T, NonsenseEventType>();

        readonly FieldReaderWriter ReaderWriter;

        readonly Func<T, T, T, Task> ReceiverAsync; // async (snapshot, diff, velocity)

        readonly StatisticsReporterLogTypes LogTypes;

        public StatisticsReporter(int interval, StatisticsReporterLogTypes logTypes, Func<T, T, T, Task> receiverProc = null, params AsyncEventCallback<T, NonsenseEventType>[] initialListenerProcs)
            : base(default)
        {
            this.Interval = interval;
            this.LogTypes = logTypes;
            this.ReaderWriter = new FieldReaderWriter(typeof(T));
            this.CurrentValues = Util.NewWithoutConstructor<T>();
            this.ReceiverAsync = receiverProc;

            foreach (var proc in initialListenerProcs)
            {
                ListenerList.RegisterCallback(proc);
            }

            StartMainLoop(MainLoopAsync, true);
        }

        async Task FireEventAsync()
        {
            await ListenerList.FireAsync(this.CurrentValues, NonsenseEventType.Nonsense);
        }

        async Task MainLoopAsync(CancellationToken cancel)
        {
            T prevValues = Util.NewWithoutConstructor<T>();
            double prevTime = Time.NowHighResDouble;
            int num = 0;

            while (true)
            {
                this.GrandCancel.ThrowIfCancellationRequested();

                await FireEventAsync();

                double nowTime = Time.NowHighResDouble;
                double timeDiff = nowTime - prevTime;
                prevTime = nowTime;

                try
                {
                    T snapshot = this.ReaderWriter.CreateClone(this.CurrentValues);
                    T diffs = this.ReaderWriter.CalcDiff(snapshot, prevValues);
                    T velocity = (num == 0 ? Util.NewWithoutConstructor<T>() : this.ReaderWriter.DivideBy(diffs, timeDiff));

                    prevValues = snapshot;

                    if (LogTypes.Bit(StatisticsReporterLogTypes.Snapshot))
                        LocalLogRouter.PostStat(snapshot, tag: "Snapshot");

                    if (LogTypes.Bit(StatisticsReporterLogTypes.Diffs))
                        LocalLogRouter.PostStat(diffs, tag: "Diffs");

                    if (LogTypes.Bit(StatisticsReporterLogTypes.Velocity))
                        LocalLogRouter.PostStat(velocity, tag: "Velocity");

                    if (this.ReceiverAsync != null)
                        await this.ReceiverAsync(snapshot, diffs, velocity);
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                await TaskUtil.WaitObjectsAsync(cancels: cancel._SingleArray(), timeout: this.Interval);

                num++;
            }
        }
    }


    class InternalOverrideClassTypeBuilder
    {
        static readonly MethodInfo MethodInfoForMethodInvoke = typeof(MethodInfo).GetMethods().Where(x => x.Name == "Invoke").Where(x => x.GetParameters().Length == 2).Single();
        static readonly MethodInfo MethodInfoForMethodInvoke2 = typeof(MethodBase).GetMethods().Where(x => x.Name == "Invoke").Where(x => x.GetParameters().Length == 2).Single();

        readonly TypeBuilder TypeBuilder;

        const string AppStateFieldName = "__AppState__";

        Type BuiltType = null;

        public InternalOverrideClassTypeBuilder(Type originalType, string typeNameSuffix = "Ex")
        {
            Assembly originalAsm = originalType.Assembly;
            string friendAssemblyName = originalAsm.GetCustomAttributes<InternalsVisibleToAttribute>().Where(x => x.AllInternalsVisible).First().AssemblyName;

            AssemblyName asmName = new AssemblyName(friendAssemblyName);
            AssemblyBuilder asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = asmBuilder.DefineDynamicModule(Guid.NewGuid().ToString());

            TypeBuilder = moduleBuilder.DefineType(originalType.Name + typeNameSuffix, TypeAttributes.Public | TypeAttributes.Class, originalType);

            ConstructorInfo[] baseConstructorsList = originalType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Concat(originalType.GetConstructors(BindingFlags.Instance | BindingFlags.Public)).ToArray();

            // App state field
            this.TypeBuilder.DefineField(AppStateFieldName, typeof(object), FieldAttributes.Public);

            // Create all new constructors for each of base constructors
            foreach (ConstructorInfo baseCtor in baseConstructorsList)
            {
                Type[] parameterTypes = baseCtor.GetParameters().Select(x => x.ParameterType).ToArray();

                ConstructorBuilder newCtor = TypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, parameterTypes);
                ILGenerator il = newCtor.GetILGenerator();

                for (int i = 0; i < parameterTypes.Length + 1; i++)
                {
                    il.Emit(OpCodes.Ldarg, i);
                }

                il.Emit(OpCodes.Call, baseCtor);
                il.Emit(OpCodes.Ret);
            }
        }

        public static int FieldIdSeed = 0;

        class FieldEntry
        {
            public FieldInfo Field;
            public object Value;
        }

        List<FieldEntry> DynamicFieldList = new List<FieldEntry>();

        public void AddOverloadMethod(string name, Delegate m, Type retType, params Type[] argsType)
            => AddOverloadMethod(name, m.GetMethodInfo(), retType, argsType);

        public void AddOverloadMethod(string name, MethodInfo methodInfoToCall, Type retType, params Type[] argsType)
        {
            if (BuiltType != null) throw new ApplicationException("BuildType() has been already called.");

            if (argsType == null) argsType = new Type[0];

            FieldBuilder fieldForMethodToCall = this.TypeBuilder.DefineField($"__method_to_call_{Interlocked.Increment(ref FieldIdSeed)}_{name}", typeof(MethodInfo), FieldAttributes.Public);

            DynamicFieldList.Add(new FieldEntry() { Field = fieldForMethodToCall, Value = methodInfoToCall });

            FieldBuilder fieldAsThisPointer = null;

            if (methodInfoToCall.IsStatic == false)
            {
                fieldAsThisPointer = this.TypeBuilder.DefineField($"__method_as_this_for_{Interlocked.Increment(ref FieldIdSeed)}_{name}", methodInfoToCall.DeclaringType, FieldAttributes.Public);

                DynamicFieldList.Add(new FieldEntry() { Field = fieldAsThisPointer, Value = Util.NewWithoutConstructor(methodInfoToCall.DeclaringType) });
            }

            MethodBuilder newMethod = this.TypeBuilder.DefineMethod(name,
                MethodAttributes.Virtual | MethodAttributes.Public,
                retType,
                argsType);

            ILGenerator il = newMethod.GetILGenerator();

            // Generate IL for:
            // ----
            // public virtual retType NAME(argsType[0] arg0, argsType[1] arg1, argsType[2] arg2, ...)
            // {
            //    return methodInfoToCall.Invoke(this, new object[] { arg0, arg1, arg2, ... });
            // }
            // ----

            // The target object for MethodInfo.Invoke()
            il.Emit(OpCodes.Ldarg, 0); // to execute Ldfld, the target container object of the field must be loaded onto the stack
            il.Emit(OpCodes.Ldfld, fieldForMethodToCall);

            // this (The first argument for MethodInfo.Invoke())
            if (methodInfoToCall.IsStatic)
            {
                // Set null since the method to call is a static method
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                // Set the dummy instance as the this pointer of the dynamic method to call
                il.Emit(OpCodes.Ldarg, 0); // to execute Ldfld, the target container object of the field must be loaded onto the stack
                il.Emit(OpCodes.Ldfld, fieldAsThisPointer);
            }

            // create new object[argsType.Length] array
            il.Emit(OpCodes.Ldc_I4, argsType.Length + 1);
            il.Emit(OpCodes.Newarr, typeof(object));

            // array_copy = array
            il.Emit(OpCodes.Dup);

            // array_copy[0] = arg[0]
            il.Emit(OpCodes.Ldc_I4, 0);
            il.Emit(OpCodes.Ldarg, 0);
            il.Emit(OpCodes.Stelem_Ref);

            for (int i = 0; i < argsType.Length; i++)
            {
                // array_copy = array
                il.Emit(OpCodes.Dup);

                // array_copy[i + 1] = arg[i + 1]
                il.Emit(OpCodes.Ldc_I4, i + 1);
                il.Emit(OpCodes.Ldarg, i + 1);
                il.Emit(OpCodes.Stelem_Ref);
            }

            // Call MethodInfo.Invoke()
            il.Emit(OpCodes.Call, MethodInfoForMethodInvoke);

            // return with cast
            il.Emit(OpCodes.Castclass, retType);
            il.Emit(OpCodes.Ret);
        }

        public Type BuildType()
        {
            if (BuiltType == null)
                BuiltType = this.TypeBuilder.CreateType();

            return BuiltType;
        }

        public object CreateInstance(object appState, params object[] parameters)
        {
            if (parameters == null)
                parameters = new object[] { null };

            object ret;

            try
            {
                if (parameters.Length == 0)
                    ret = Activator.CreateInstance(this.BuildType());
                else
                    ret = Activator.CreateInstance(this.BuildType(), parameters);
            }
            catch (Exception ex)
            {
                throw ex._GetSingleException();
            }

            foreach (var fieldEntry in this.DynamicFieldList)
            {
                ret._PrivateSet(fieldEntry.Field.Name, fieldEntry.Value);
            }

            SetAppState(ret, appState);

            return ret;
        }

        public static object GetAppState(object targetObject) => targetObject._PrivateGet(AppStateFieldName);

        public static void SetAppState(object targetObject, object state) => targetObject._PrivateSet(AppStateFieldName, state);
    }

    [Flags]
    enum DaemonStatus
    {
        Stopped = 0,
        Starting,
        Running,
        Stopping,
    }

    class DaemonOptions
    {
        public string Name { get; }
        public string FriendlyName { get; }
        public int StopTimeout { get; }
        public bool SingleInstance { get; }
        public int TelnetLogWatcherPort { get; }

        public DaemonOptions(string name, string friendlyName, bool singleInstance, int stopTimeout = 0, int telnetLogWatcherPort = 0)
        {
            if (stopTimeout == 0)
                stopTimeout = CoresConfig.DaemonSettings.DefaultStopTimeout;

            if (stopTimeout < 0) stopTimeout = Timeout.Infinite;

            this.Name = name;
            this.FriendlyName = friendlyName;
            this.StopTimeout = stopTimeout;

            this.SingleInstance = singleInstance;

            this.TelnetLogWatcherPort = telnetLogWatcherPort;
        }
    }

    class SizedDataQueue<T>
    {
        readonly List<T> InternalList;

        public long CurrentTotalSize { get; private set; }

        public IReadOnlyList<T> List => this.InternalList;

        public SizedDataQueue()
        {
            this.InternalList = new List<T>();
        }

        public void Add(T t, long size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException("size < 0");
            this.InternalList.Add(t);
            this.CurrentTotalSize += size;
        }

        public void Clear()
        {
            this.InternalList.Clear();
            this.CurrentTotalSize = 0;
        }
    }
}

