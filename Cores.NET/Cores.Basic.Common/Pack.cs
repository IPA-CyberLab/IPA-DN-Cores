using System;
using System.Threading;
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

namespace IPA.Cores.Basic
{
    // 値
    class Value : IComparable
    {
        uint index;             // インデックス
        public uint Index
        {
            get { return index; }
        }

        object data;            // データ

        // 比較
        int IComparable.CompareTo(object obj)
        {
            Value v = (Value)obj;

            return this.index.CompareTo(v.index);
        }

        // コンストラクタ
        public Value(uint index, uint intValue)
        {
            this.index = index;
            this.data = intValue;
        }
        public Value(uint index, ulong int64Value)
        {
            this.index = index;
            this.data = int64Value;
        }
        public Value(uint index, string str)
        {
            this.index = index;
            this.data = str;
        }
        public Value(uint index, byte[] data)
        {
            this.index = index;
            this.data = data;
        }

        // デフォルトの値
        public static Value GetDefaultValue(uint index, ValueType type)
        {
            switch (type)
            {
                case ValueType.Int:
                    return new Value(index, 0);

                case ValueType.Int64:
                    return new Value(index, 0);

                case ValueType.Str:
                    return new Value(index, "");

                case ValueType.UniStr:
                    return new Value(index, "");

                case ValueType.Data:
                    return new Value(index, new byte[0]);
            }

            return null;
        }

        // データの取得
        public uint IntValue
        {
            get { return (uint)data; }
        }
        public ulong Int64Value
        {
            get { return (ulong)data; }
        }
        public string StrValue
        {
            get { return (string)data; }
        }
        public byte[] Data
        {
            get { return (byte[])data; }
        }
        public bool BoolValue
        {
            get { return (IntValue == 0 ? false : true); }
        }

        // 読み込む
        public static Value CreateFromBuf(Buf b, uint index, ValueType type)
        {
            switch (type)
            {
                case ValueType.Int:
                    return new Value(index, b.ReadInt());

                case ValueType.Int64:
                    return new Value(index, b.ReadInt64());

                case ValueType.Str:
                    return new Value(index, b.ReadStr().Trim());

                case ValueType.UniStr:
                    uint len = b.ReadInt();
                    if (len == 0)
                    {
                        return new Value(index, "");
                    }
                    else
                    {
                        byte[] data = b.Read(len - 1);
                        b.Read(1);
                        return new Value(index, Str.Utf8Encoding.GetString(data).Trim());

                    }

                case ValueType.Data:
                    uint size = b.ReadInt();
                    return new Value(index, b.Read(size));
            }

            return null;
        }

        // 書き出す
        public void WriteToBuf(Buf b, ValueType type)
        {
            switch (type)
            {
                case ValueType.Int:
                    b.WriteInt(IntValue);
                    break;

                case ValueType.Int64:
                    b.WriteInt64(Int64Value);
                    break;

                case ValueType.Data:
                    b.WriteInt((uint)Data.Length);
                    b.Write(Data);
                    break;

                case ValueType.Str:
                    b.WriteStr(StrValue.Trim());
                    break;

                case ValueType.UniStr:
                    byte[] data = Str.Utf8Encoding.GetBytes(StrValue.Trim());
                    b.WriteInt((uint)data.Length + 1);
                    b.Write(data);
                    b.WriteByte(0);
                    break;
            }
        }
    }

    // 要素の型
    enum ValueType
    {
        Int = 0,                // 整数型
        Data = 1,               // データ型
        Str = 2,                // ANSI 文字列型
        UniStr = 3,             // Unicode 文字列型
        Int64 = 4,              // 64 bit 整数型
    }

    // 要素
    class Element : IComparable
    {
        string name;            // 要素名
        ValueType type;         // 型
                                //List<Value> values;		// 値リスト
        Dictionary<uint, Value> values;
        uint maxIndex;

        // コンストラクタ
        public Element(string name, ValueType type)
        {
            this.name = name;
            this.type = type;
            maxIndex = 0;
            this.values = new Dictionary<uint, Value>();
        }

        // 値の取得
        public Value GetValue(uint index)
        {
            bool tmp;
            return GetValue(index, out tmp);
        }
        public Value GetValue(uint index, out bool exists)
        {
            if (values.ContainsKey(index) == false)
            {
                exists = false;
                return Value.GetDefaultValue(index, this.type);
            }
            else
            {
                exists = true;
                return values[index];
            }
        }

        // 値の追加
        public void AddValue(Value value)
        {
            bool exists;
            Value existValue = GetValue(value.Index, out exists);

            if (exists)
            {
                values.Remove(value.Index);
            }

            values.Add(value.Index, value);

            if (maxIndex < value.Index)
            {
                maxIndex = value.Index;
            }
        }

        // 型のチェック
        public bool CheckType(ValueType type)
        {
            return this.type == type;
        }

        // 値の個数の取得
        public uint Count
        {
            get
            {
                if (values.Count >= 1)
                {

                    return maxIndex + 1;
                }
                else
                {
                    return 0;
                }
            }
        }

        int IComparable.CompareTo(object obj)
        {
            Element e = (Element)obj;

            return this.name.ToUpper().CompareTo(e.name.ToUpper());
        }

        // 読み込む
        public static Element CreateFromBuf(Buf b)
        {
            // 名前
            string name = b.ReadStr(true);

            // 種類
            ValueType type = (ValueType)b.ReadInt();

            // 項目数
            uint num = b.ReadInt();

            Element e = new Element(name, type);

            // 値
            uint i;
            for (i = 0; i < num; i++)
            {
                Value v = Value.CreateFromBuf(b, i, type);

                e.AddValue(v);
            }

            return e;
        }

        // 書き出す
        public void WriteToBuf(Buf b)
        {
            // 名前
            b.WriteStr(this.name, true);

            // 種類
            b.WriteInt((uint)this.type);

            // 項目数
            b.WriteInt(this.Count);

            // 値
            uint i;
            if (this.Count > Pack.MaxValueNum)
            {
                throw new OverflowException();
            }
            for (i = 0; i < this.Count; i++)
            {
                Value v = this.GetValue(i);

                v.WriteToBuf(b, this.type);
            }
        }
    }

    // Pack の値
    public class PackValue
    {
        public string StrValue = null;
        public string UniStrValue = null;
        public uint IntValue = 0;
        public ulong Int64Value = 0;
        public byte[] DataValue = null;
        public DateTime DateTimeValue = new DateTime(0);
        public Boolean BoolValue = false;
    }

    // Pack
    public class Pack
    {
        List<Element> elements; // 要素リスト
        bool elementsSorted;
        public const uint MaxValueNum = 65536 * 4;      // 1 つの ELEMENT に格納できる最大 VALUE 数
        public const uint MaxElementNum = 131072 * 4;   // 1 つの PACK に格納できる最大 ELEMENT 数
        public const uint MaxPackSize = (64 * 1024 * 1024 * 4); // シリアル化された PACK の最大サイズ

        // インデクサ
        public PackValue this[string name]
        {
            get
            {
                return this[name, 0];
            }
        }
        public PackValue this[string name, uint index]
        {
            get
            {
                PackValue p = new PackValue();
                p.IntValue = GetInt(name, index);
                p.Int64Value = GetInt64(name, index);
                p.StrValue = GetStr(name, index);
                p.UniStrValue = GetUniStr(name, index);
                p.DataValue = GetData(name, index);
                p.BoolValue = GetBool(name, index);

                try
                {
                    p.DateTimeValue = Util.ConvertDateTime(p.Int64Value);
                }
                catch
                {
                }

                return p;
            }
        }

        // コンストラクタ
        public Pack()
        {
            elements = new List<Element>();
            elementsSorted = true;
        }

        // エラーコードの設定
        public void SetError(uint code)
        {
            AddInt("Error", code);
        }

        // エラーコードの取得
        public uint GetError(uint code)
        {
            return GetInt("Error");
        }

        // 指定した要素があるかどうかチェック
        public bool HasMember(string name)
        {
            if (getElement(name) != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // 要素の追加
        void addElement(Element e)
        {
            elements.Add(e);
            elementsSorted = false;
        }
        public void AddStr(string name, string strValue)
        {
            AddStr(name, strValue, 0);
        }
        public void AddStr(string name, string strValue, uint index)
        {
            Element e = getElementAndCreateIfNotExists(name, ValueType.Str);
            e.AddValue(new Value(index, strValue));
        }

        public void AddUniStr(string name, string strValue)
        {
            AddUniStr(name, strValue, 0);
        }
        public void AddUniStr(string name, string strValue, uint index)
        {
            Element e = getElementAndCreateIfNotExists(name, ValueType.UniStr);
            e.AddValue(new Value(index, strValue));
        }

        public void AddInt(string name, uint intValue)
        {
            AddInt(name, intValue, 0);
        }
        public void AddInt(string name, uint intValue, uint index)
        {
            Element e = getElementAndCreateIfNotExists(name, ValueType.Int);
            e.AddValue(new Value(index, intValue));
        }

        public void AddBool(string name, bool boolValue)
        {
            AddBool(name, boolValue, 0);
        }
        public void AddBool(string name, bool boolValue, uint index)
        {
            AddInt(name, (uint)(boolValue ? 1 : 0), index);
        }

        public void AddDateTime(string name, DateTime dt)
        {
            AddDateTime(name, dt, 0);
        }
        public void AddDateTime(string name, DateTime dt, uint index)
        {
            AddInt64(name, Util.ConvertDateTime(dt), index);
        }

        public void AddInt64(string name, ulong int64Value)
        {
            AddInt64(name, int64Value, 0);
        }
        public void AddInt64(string name, ulong int64Value, uint index)
        {
            Element e = getElementAndCreateIfNotExists(name, ValueType.Int64);
            e.AddValue(new Value(index, int64Value));
        }

        public void AddData(string name, byte[] data)
        {
            AddData(name, data, 0);
        }
        public void AddData(string name, byte[] data, uint index)
        {
            Element e = getElementAndCreateIfNotExists(name, ValueType.Data);
            e.AddValue(new Value(index, data));
        }

        public void AddCert(string name, Cert cert)
        {
            AddCert(name, cert, 0);
        }
        public void AddCert(string name, Cert cert, uint index)
        {
            AddData(name, cert.ByteData, index);
        }

        // 要素の取得
        Element getElement(string name)
        {
            Element t = new Element(name, ValueType.Int);
            if (elementsSorted == false)
            {
                elements.Sort();
                elementsSorted = true;
            }
            int i = elements.BinarySearch(t);
            if (i < 0)
            {
                return null;
            }
            else
            {
                return elements[i];
            }
        }
        Element getElementAndCreateIfNotExists(string name, ValueType type)
        {
            Element e = getElement(name);
            if (e != null)
            {
                return e;
            }

            e = new Element(name, type);
            addElement(e);

            return e;
        }

        public uint GetCount(string name)
        {
            Element e = getElement(name);
            if (e == null)
            {
                return 0;
            }
            return e.Count;
        }

        public string GetStr(string name)
        {
            return GetStr(name, 0);
        }
        public string GetStr(string name, uint index)
        {
            Element e = getElement(name);
            if (e == null)
            {
                return null;
            }
            if (e.CheckType(ValueType.Str) == false)
            {
                return null;
            }
            Value v = e.GetValue(index);
            if (v == null)
            {
                return null;
            }
            return v.StrValue;
        }

        public string GetUniStr(string name)
        {
            return GetUniStr(name, 0);
        }
        public string GetUniStr(string name, uint index)
        {
            Element e = getElement(name);
            if (e == null)
            {
                return null;
            }
            if (e.CheckType(ValueType.UniStr) == false)
            {
                return null;
            }
            Value v = e.GetValue(index);
            if (v == null)
            {
                return null;
            }
            return v.StrValue;
        }

        public uint GetInt(string name)
        {
            return GetInt(name, 0);
        }
        public uint GetInt(string name, uint index)
        {
            Element e = getElement(name);
            if (e == null)
            {
                return 0;
            }
            if (e.CheckType(ValueType.Int) == false)
            {
                return 0;
            }
            Value v = e.GetValue(index);
            if (v == null)
            {
                return 0;
            }
            return v.IntValue;
        }

        public bool GetBool(string name)
        {
            return GetBool(name, 0);
        }
        public bool GetBool(string name, uint index)
        {
            return (GetInt(name, index) == 0 ? false : true);
        }

        public ulong GetInt64(string name)
        {
            return GetInt64(name, 0);
        }
        public ulong GetInt64(string name, uint index)
        {
            Element e = getElement(name);
            if (e == null)
            {
                return 0;
            }
            if (e.CheckType(ValueType.Int64) == false)
            {
                return 0;
            }
            Value v = e.GetValue(index);
            if (v == null)
            {
                return 0;
            }
            return v.Int64Value;
        }

        public byte[] GetData(string name)
        {
            return GetData(name, 0);
        }
        public byte[] GetData(string name, uint index)
        {
            Element e = getElement(name);
            if (e == null)
            {
                return null;
            }
            if (e.CheckType(ValueType.Data) == false)
            {
                return null;
            }
            Value v = e.GetValue(index);
            if (v == null)
            {
                return null;
            }
            return v.Data;
        }

        public Cert GetCert(string name)
        {
            return GetCert(name, 0);
        }
        public Cert GetCert(string name, uint index)
        {
            byte[] data = GetData(name, index);
            if (data == null)
            {
                return null;
            }
            try
            {
                Cert c = new Cert(data);

                return c;
            }
            catch
            {
                return null;
            }
        }

        // 読み込む
        public static Pack CreateFromBuf(Buf b)
        {
            Pack p = new Pack();
            uint i, num;

            if (b.Size > Pack.MaxPackSize)
            {
                throw new OverflowException();
            }

            num = b.ReadInt();

            if (num > Pack.MaxElementNum)
            {
                throw new OverflowException();
            }

            for (i = 0; i < num; i++)
            {
                p.addElement(Element.CreateFromBuf(b));
            }

            return p;
        }

        // バッファに変換
        public Buf WriteToBuf()
        {
            Buf b = new Buf();

            // 要素数
            b.WriteInt((uint)elements.Count);

            // 要素
            foreach (Element e in elements)
            {
                e.WriteToBuf(b);
            }

            b.SeekToBegin();

            return b;
        }

        // バイトデータ
        public byte[] ByteData
        {
            get
            {
                return WriteToBuf().ByteData;
            }
        }
    }
}

