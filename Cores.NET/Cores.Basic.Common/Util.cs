// CoreUtil
// 
// Copyright (C) 1997-2010 Daiyuu Nobori. All Rights Reserved.
// Copyright (C) 2004-2010 SoftEther Corporation. All Rights Reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Drawing;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

using IPA.DN.CoreUtil.Helper.Basic;

namespace IPA.Cores.Basic
{
    // 言語一覧
    public enum CoreLanguage
    {
        Japanese = 0,
        English = 1,
    }

    // 言語クラス
    public class CoreLanguageClass
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
            ThreadData.CurrentThreadData["current_thread_language"] = lang;
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
                lang = (CoreLanguageClass)ThreadData.CurrentThreadData["current_thread_language"];
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
    public static class CoreLanguageList
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

    public class RefInt
    {
        public RefInt() : this(0) { }
        public RefInt(int value)
        {
            this.Value = value;
        }
        public int Value;
        public void Set(int value) => this.Value = value;
        public int Get() => this.Value;
        public override string ToString() => this.Value.ToString();
        public int Increment() => Interlocked.Increment(ref this.Value);
        public int Decrement() => Interlocked.Decrement(ref this.Value);
    }

    public class RefBool
    {
        public RefBool() : this(false) { }
        public RefBool(bool value)
        {
            this.Value = value;
        }
        public bool Value;
        public void Set(bool value) => this.Value = value;
        public bool Get() => this.Value;
        public override string ToString() => this.Value.ToString();
    }

    public class Ref<T>
    {
        public Ref() : this(default(T)) { }
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
                    return Str.StrToBool(s);
            }
            return Str.StrToBool(this.Value.ToString());
        }
        public override string ToString() => Value?.ToString() ?? null;

        public override bool Equals(object obj)
        {
            var @ref = obj as Ref<T>;
            return @ref != null &&
                   EqualityComparer<T>.Default.Equals(Value, @ref.Value);
        }

        public override int GetHashCode()
        {
            return -1937169414 + EqualityComparer<T>.Default.GetHashCode(Value);
        }

        public static bool operator true(Ref<T> r) { return r.IsTrue();  }
        public static bool operator false(Ref<T> r) { return !r.IsTrue(); }
        public static bool operator ==(Ref<T> r, bool b) { return r.IsTrue() == b; }
        public static bool operator !=(Ref<T> r, bool b) { return r.IsTrue() != b; }
        public static bool operator !(Ref<T> r) { return !r.IsTrue();  }
    }

    // ユーティリティクラス
    public static class Util
    {
        public static readonly DateTime ZeroDateTimeValue = new DateTime(1800, 1, 1);
        public static readonly DateTimeOffset ZeroDateTimeOffsetValue = new DateTimeOffset(1800, 1, 1, 0, 0, 0, new TimeSpan(9, 0, 0));

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
        public static T[] IEnumerableToArrayList<T>(IEnumerable<T> i)
        {
            List<T> ret = new List<T>();
            foreach (var o in i)
            {
                ret.Add(o);
            }
            return ret.ToArray();
        }

        //// IEnumerable の項目を List に追加
        //public static void AddArrayItemsToList<T>(IEnumerable<T> items, List<T> list)
        //{
        //    foreach (var o in items)
        //        list.Add(o);
        //}
        //public static void AddArrayItemsToList(IEnumerable items, IList list)
        //{
        //    foreach (var o in items)
        //        list.Add(o);
        //}

        // オブジェクトのハッシュ値を計算
        public static ulong GetObjectHash(object o)
        {
            if (o == null) return 0;
            try
            {
                return Str.HashStrToLong(Json.Serialize(o, true, false, null));
            }
            catch
            {
                return 0;
            }
        }

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
            return IsZero(data, 0, data.Length);
        }
        public static bool IsZero(byte[] data, int offset, int size)
        {
            int i;
            for (i = offset; i < offset + size; i++)
            {
                if (data[i] != 0)
                {
                    return false;
                }
            }
            return true;
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

        // ulong を DateTime に変換
        public static DateTime ConvertDateTime(ulong time64)
        {
            if (time64 == 0)
            {
                return new DateTime(0);
            }
            return new DateTime(((long)time64 + 62135629200000) * 10000);
        }

        // DateTime を ulong に変換
        public static ulong ConvertDateTime(DateTime dt)
        {
            if (dt.Ticks == 0)
            {
                return 0;
            }
            return (ulong)dt.Ticks / 10000 - 62135629200000;
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
        public static byte[] GetXmlSchemaFromType(Type type)
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
        public static string GetXmlSchemaFromTypeString(Type type)
        {
            byte[] data = GetXmlSchemaFromType(type);

            return Str.Utf8Encoding.GetString(data);
        }

        // オブジェクトを XML に変換
        public static string ObjectToXmlString(object o)
        {
            byte[] data = ObjectToXml(o);

            return Str.Utf8Encoding.GetString(data);
        }
        public static byte[] ObjectToXml(object o)
        {
            if (o == null)
            {
                return null;
            }
            Type t = o.GetType();

            return ObjectToXml(o, t);
        }
        public static string ObjectToXmlString(object o, Type t)
        {
            byte[] data = ObjectToXml(o, t);

            return Str.Utf8Encoding.GetString(data);
        }
        public static byte[] ObjectToXml(object o, Type t)
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

        // オブジェクトをクローンする
        public static object CloneObject_UsingBinary(object o)
        {
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
        public static object BinaryToObject(byte[] data)
        {
            BinaryFormatter f = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Position = 0;

            return f.Deserialize(ms);
        }

        // オブジェクトの内容をクローンする
        public static object CloneObject_UsingXml(object o)
        {
            byte[] data = Util.ObjectToXml(o);

            return Util.XmlToObject(data, o.GetType());
        }

        // XML をオブジェクトに変換
        public static object XmlToObject(string str, Type t)
        {
            if (Str.IsEmptyStr(str))
            {
                return null;
            }

            byte[] data = Str.Utf8Encoding.GetBytes(str);

            return XmlToObject(data, t);
        }
        public static object XmlToObject(byte[] data, Type t)
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
        public static bool False
        {
            get
            {
                return false;
            }
        }

        // true
        public static bool True
        {
            get
            {
                return true;
            }
        }

        // Zero
        public static int Zero
        {
            get
            {
                return 0;
            }
        }

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
        public static int SizeOfStruct(object obj)
        {
            return Marshal.SizeOf(obj);
        }
        public static int SizeOfStruct(Type type)
        {
            return Marshal.SizeOf(type);
        }

        // オブジェクトから XML とスキーマを生成
        public static XmlAndXsd GenerateXmlAndXsd(object obj)
        {
            XmlAndXsd ret = new XmlAndXsd();
            Type type = obj.GetType();

            ret.XsdFileName = Str.MakeSafeFileName(type.Name + ".xsd");
            ret.XsdData = GetXmlSchemaFromType(type);

            ret.XmlFileName = Str.MakeSafeFileName(type.Name + ".xml");
            string str = Util.ObjectToXmlString(obj);
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
        public static T[] GetEnumValuesList<T>()
        {
            List<T> ret = new List<T>();
            return (T[])Enum.GetValues(typeof(T));
/*            foreach (T v in Enum.GetValues(typeof(T)))
            {
                ret.Add(v);
            }
            return ret.ToArray();*/
        }
    }

    public class XmlAndXsd
    {
        public byte[] XmlData;
        public byte[] XsdData;
        public string XmlFileName;
        public string XsdFileName;
    }

    // 1 度しか実行しない処理を実行しやすくするための構造体
    public struct Once
    {
        volatile private int flag;
        public bool IsFirstCall() => (Interlocked.CompareExchange(ref this.flag, 1, 0) == 0);
        public bool IsSet => (this.flag != 0);
    }

    // 再試行ヘルパー
    public class RetryHelper<T>
    {
        public int DefaultRetryInterval { get; set; }
        public int DefaultTryCount { get; set; }

        public RetryHelper(int default_retry_interval, int default_try_count)
        {
            this.DefaultRetryInterval = default_retry_interval;
            this.DefaultTryCount = default_try_count;
        }

        public async Task<T> RunAsync(Func<Task<T>> proc, int? retry_interval = null, int? try_count = null)
        {
            if (retry_interval == null) retry_interval = DefaultRetryInterval;
            if (try_count == null) try_count = DefaultTryCount;
            try_count = Math.Max((int)try_count, 1);
            retry_interval = Math.Max((int)retry_interval, 0);

            Exception first_exception = null;

            for (int i = 0; i < try_count; i++)
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

                    Dbg.WriteLine($"RetryHelper: round {i} error. Retrying in {retry_interval} msecs...");

                    await Task.Delay((int)retry_interval);
                }
            }

            throw first_exception;
        }
    }

    // シングルトン
    public struct Singleton<T> where T: class
    {
        static object lockobj = new object();
        T obj;

        public T CreateOrGet(Func<T> create_proc)
        {
            lock (lockobj)
            {
                if (obj == null)
                    obj = create_proc();
                return obj;
            }
        }
    }

    public static class GlobalObjectExchange
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

    public class IntervalManager
    {
        long last_tick = 0;
        public int Interval { get; private set; }
        int last_interval;

        public IntervalManager(int interval)
        {
            last_tick = Time.Tick64;
            this.last_interval = this.Interval = interval;
        }

        public int GetNextInterval(int? next_interval = null)
            => GetNextInterval(out _, next_interval);

        int count = 0;

        public int GetNextInterval(out int last_timediff, int? next_interval = null)
        {
            long now = Time.Tick64;
            last_timediff = (int)(now - last_tick);
            int over = last_timediff - this.last_interval;
            if (next_interval != null) this.Interval = (int)next_interval;
            this.last_interval = this.Interval;
            int ret = this.Interval;
            if (over > 0)
            {
                ret = this.Interval - over;
            }
            last_tick = now;
            if (ret <= 0) ret = 1;
            if (this.Interval == Timeout.Infinite) ret = Timeout.Infinite;
            if (last_timediff <= 0) last_timediff = 1;
            if (count == 0) last_timediff = this.last_interval;
            count++;
            return ret;
        }
    }

    public class Distinct<T>
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

        public T[] Values { get => d.Keys.ToArrayList(); }
    }

    public class DelayLoader<T> : IDisposable
    {
        Func<long, (T, long)> load_proc;
        int retry_interval;
        int update_interval;
        ThreadObj thread;
        ManualResetEventSlim halt_event = new ManualResetEventSlim();
        public readonly ManualResetEventSlim LoadCompleteEvent = new ManualResetEventSlim();
        bool halt_flag = false;

        public T Data { get; private set; }

        public DelayLoader(Func<long, (T data, long data_timestamp)> load_proc, int retry_interval = 1000, int update_interval = 1000)
        {
            this.load_proc = load_proc;
            this.retry_interval = retry_interval;
            this.update_interval = update_interval;

            this.thread = new ThreadObj(thread_proc, is_background: true);
        }

        void thread_proc(object param)
        {
            long last_timestamp = 0;

            (T data, long data_timestamp) ret;

            LABEL_RETRY:
            try
            {
                // データの読み込み
                ret = load_proc(last_timestamp);
            }
            catch (Exception ex)
            {
                Dbg.WriteLine(ex.ToString());

                halt_event.Wait(retry_interval);

                if (halt_flag)
                {
                    return;
                }

                goto LABEL_RETRY;
            }

            if (ret.data != null)
            {
                // 読み込んだデータをグローバルにセット
                last_timestamp = ret.data_timestamp;
                this.Data = ret.data;
                LoadCompleteEvent.Set();
            }

            // 次回まで待機
            halt_event.Wait(update_interval);
            if (halt_flag)
            {
                return;
            }

            goto LABEL_RETRY;
        }

        Once dispose_flag;
        public void Dispose()
        {
            if (dispose_flag.IsFirstCall())
            {
                halt_flag = true;
                halt_event.Set();
                thread.WaitForEnd();
            }
        }
    }
}
