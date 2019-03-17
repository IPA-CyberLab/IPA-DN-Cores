using System;
using System.Threading;
using System.Linq;
using System.Linq.Expressions;
using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Reflection;
using System.Drawing;
using Dapper;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    class DbConsoleDebugPrinterProvider : ILoggerProvider
    {
        Ref<bool> enable_console_logger;

        public DbConsoleDebugPrinterProvider(Ref<bool> enable_console_logger)
        {
            this.enable_console_logger = enable_console_logger;
        }

        public ILogger CreateLogger(string categoryName) => new DbConsoleDebugPrinter(this.enable_console_logger);
        public void Dispose() { }
    }

    class DbConsoleDebugPrinter : ILogger
    {
        Ref<bool> enable_console_logger;

        public DbConsoleDebugPrinter(Ref<bool> enable_console_logger)
        {
            this.enable_console_logger = enable_console_logger;
        }

        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel)
        {
            if (enable_console_logger == false) return false;

            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Information:
                case LogLevel.None:
                    return false;
            }
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (eventId.ToString().IndexOf("CommandExecuting", StringComparison.InvariantCultureIgnoreCase) == -1)
            {
                return;
            }
            StringWriter w = new StringWriter();
            w.WriteLine("----------");
            if (state != null) w.Write($"State: {state}");
            if (exception != null) w.Write($", Exception: {exception.Message}");
            w.WriteLine();
            w.WriteLine("----------");
            string str = w.ToString();
            Dbg.WriteLine(w.ToString().TrimCrlf());
        }
    }

    // データベース値
    class DatabaseValue
    {
        public bool IsNull
        {
            get
            {
                if (Object == null) return true;
                if (Object == (object)DBNull.Value) return true;
                return false;
            }
        }

        public DateTime DateTime => ((DateTime)Object).NormalizeDateTime();
        public DateTimeOffset DateTimeOffset => ((DateTimeOffset)Object).NormalizeDateTimeOffset();
        public string String => (string)Object;
        public double Double => (double)Object;
        public int Int => (int)Object;
        public uint UInt => (uint)Object;
        public long Int64 => (long)Object;
        public ulong UInt64 => (ulong)Object;
        public bool Bool => (bool)Object;
        public byte[] Data => (byte[])Object;
        public short Short => (short)Object;
        public ushort UShort => (ushort)Object;
        public byte Byte => (byte)Object;
        public sbyte SByte => (sbyte)Object;

        public object Object { get; }

        public DatabaseValue(object value)
        {
            this.Object = value;
        }

        public override string ToString()
        {
            return this.Object.ToString();
        }
    }

    // 行
    class Row
    {
        public readonly DatabaseValue[] ValueList;
        public readonly string[] FieldList;

        public Row(DatabaseValue[] ValueList, string[] FieldList)
        {
            this.ValueList = ValueList;
            this.FieldList = FieldList;
        }

        public DatabaseValue this[string name]
        {
            get
            {
                int i;
                for (i = 0; i < this.FieldList.Length; i++)
                {
                    if (this.FieldList[i].Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return this.ValueList[i];
                    }
                }

                throw new ApplicationException("Field \"" + name + "\" not found.");
            }
        }

        public override string ToString()
        {
            string[] strs = new string[this.ValueList.Length];
            int i;
            for (i = 0; i < this.ValueList.Length; i++)
            {
                strs[i] = this.ValueList[i].ToString();
            }

            return Str.CombineStringArray(strs, ", ");
        }
    }

    // データ
    class Data : IEnumerable
    {
        public readonly Row[] RowList;
        public readonly string[] FieldList;

        public Data(Database db)
        {
            SqlDataReader r = db.DataReader;

            int i;
            int num = r.FieldCount;

            List<string> fields_list = new List<string>();

            for (i = 0; i < num; i++)
            {
                fields_list.Add(r.GetName(i));
            }

            this.FieldList = fields_list.ToArray();

            List<Row> row_list = new List<Row>();
            while (db.ReadNext())
            {
                DatabaseValue[] values = new DatabaseValue[this.FieldList.Length];

                for (i = 0; i < this.FieldList.Length; i++)
                {
                    values[i] = db[this.FieldList[i]];
                }

                row_list.Add(new Row(values, this.FieldList));
            }

            this.RowList = row_list.ToArray();
        }

        public IEnumerator GetEnumerator()
        {
            int i;
            for (i = 0; i < this.RowList.Length; i++)
            {
                yield return this.RowList[i];
            }
        }
    }

    // Using トランザクション
    class UsingTran : IDisposable
    {
        Database db;

        internal UsingTran(Database db)
        {
            this.db = db;
        }

        object lock_obj = new object();

        public void Commit()
        {
            this.db.Commit();
        }

        public void Dispose()
        {
            Database db = null;
            lock (lock_obj)
            {
                if (this.db != null)
                {
                    db = this.db;
                    this.db = null;
                }
            }

            if (db != null)
            {
                db.Cancel();
            }
        }
    }

    // デッドロック再試行設定
    class DeadlockRetryConfig
    {
        public readonly int RetryAverageInterval;
        public readonly int RetryCount;

        public DeadlockRetryConfig(int RetryAverageInterval, int RetryCount)
        {
            this.RetryAverageInterval = RetryAverageInterval;
            this.RetryCount = RetryCount;
        }
    }

    // データベースアクセス
    class Database : IDisposable
    {
        SqlConnection con = null;
        SqlTransaction tran = null;
        SqlDataReader reader = null;
        public const int DefaultCommandTimeoutSecs = 60;
        public int CommandTimeoutSecs { get; set; } = DefaultCommandTimeoutSecs;

        public static readonly DeadlockRetryConfig DefaultDeadlockRetryConfig = new DeadlockRetryConfig(4000, 10);

        public DeadlockRetryConfig DeadlockRetryConfig = DefaultDeadlockRetryConfig;

        public SqlConnection Connection
        {
            get
            {
                return con;
            }
        }

        public SqlTransaction Transaction
        {
            get
            {
                return tran;
            }
        }

        public SqlDataReader DataReader
        {
            get
            {
                return reader;
            }
        }

        // コンストラクタ
        public Database(string dbStr)
        {
            con = new SqlConnection(dbStr);
            con.Open();
        }

        // バルク書き込み
        public void BulkWrite(string tableName, DataTable dt)
        {
            using (SqlBulkCopy bc = new SqlBulkCopy(this.con, SqlBulkCopyOptions.Default, tran))
            {
                bc.BulkCopyTimeout = CommandTimeoutSecs;
                bc.DestinationTableName = tableName;
                bc.WriteToServer(dt);
            }
        }

        // クエリの実行
        public void Query(string commandStr, params object[] args)
        {
            closeQuery();
            SqlCommand cmd = buildCommand(commandStr, args);

            reader = cmd.ExecuteReader();
        }
        public int QueryWithNoReturn(string commandStr, params object[] args)
        {
            closeQuery();
            SqlCommand cmd = buildCommand(commandStr, args);

            return cmd.ExecuteNonQuery();
        }
        public DatabaseValue QueryWithValue(string commandStr, params object[] args)
        {
            closeQuery();
            SqlCommand cmd = buildCommand(commandStr, args);

            return new DatabaseValue(cmd.ExecuteScalar());
        }

        // 値の取得
        public DatabaseValue this[string name]
        {
            get
            {
                object o = reader[name];

                return new DatabaseValue(o);
            }
        }

        // 最後に挿入した ID
        public int LastID
        {
            get
            {
                return (int)((decimal)this.QueryWithValue("SELECT @@@@IDENTITY").Object);
            }
        }
        public long LastID64
        {
            get
            {
                return (long)((decimal)this.QueryWithValue("SELECT @@@@IDENTITY").Object);
            }
        }

        // すべて読み込み
        public Data ReadAllData()
        {
            return new Data(this);
        }

        // 次の行の取得
        public bool ReadNext()
        {
            if (reader == null)
            {
                return false;
            }
            if (reader.Read() == false)
            {
                return false;
            }

            return true;
        }

        // クエリの終了
        void closeQuery()
        {
            if (reader != null)
            {
                reader.Close();
                reader.Dispose();
                reader = null;
            }
        }

        // コマンドの構築
        SqlCommand buildCommand(string commandStr, params object[] args)
        {
            StringBuilder b = new StringBuilder();
            int i, len, n;
            len = commandStr.Length;
            List<SqlParameter> sqlParams = new List<SqlParameter>();

            n = 0;
            for (i = 0; i < len; i++)
            {
                char c = commandStr[i];

                if (c == '@')
                {
                    if ((commandStr.Length > (i + 1)) && commandStr[i + 1] == '@')
                    {
                        b.Append(c);
                        i++;
                    }
                    else
                    {
                        string argName = "@ARG_" + n;
                        b.Append(argName);

                        SqlParameter p = buildParameter(argName, args[n++]);
                        sqlParams.Add(p);
                    }
                }
                else
                {
                    b.Append(c);
                }
            }

            SqlCommand cmd = new SqlCommand(b.ToString(), con, tran);
            foreach (SqlParameter p in sqlParams)
            {
                cmd.Parameters.Add(p);
            }

            cmd.CommandTimeout = this.CommandTimeoutSecs;

            return cmd;
        }

        // オブジェクトを SQL パラメータに変換
        SqlParameter buildParameter(string name, object o)
        {
            Type t = null;

            try
            {
                t = o.GetType();
            }
            catch
            {
            }

            if (o == null)
            {
                SqlParameter p = new SqlParameter(name, DBNull.Value);
                return p;
            }
            else if (t == typeof(System.String))
            {
                string s = (string)o;
                SqlParameter p = new SqlParameter(name, SqlDbType.NVarChar, s.Length);
                p.Value = s;
                return p;
            }
            else if (t == typeof(System.Int16) || t == typeof(System.UInt16))
            {
                short s = (short)o;
                SqlParameter p = new SqlParameter(name, SqlDbType.SmallInt);
                p.Value = s;
                return p;
            }
            else if (t == typeof(System.Byte) || t == typeof(System.SByte))
            {
                byte b = (byte)o;
                SqlParameter p = new SqlParameter(name, SqlDbType.TinyInt);
                p.Value = b;
                return p;
            }
            else if (t == typeof(System.Int32) || t == typeof(System.UInt32))
            {
                int i = (int)o;
                SqlParameter p = new SqlParameter(name, SqlDbType.Int);
                p.Value = i;
                return p;
            }
            else if (t == typeof(System.Int64) || t == typeof(System.UInt64))
            {
                long i = (long)o;
                SqlParameter p = new SqlParameter(name, SqlDbType.BigInt);
                p.Value = i;
                return p;
            }
            else if (t == typeof(System.Boolean))
            {
                bool b = (bool)o;
                SqlParameter p = new SqlParameter(name, SqlDbType.Bit);
                p.Value = b;
                return p;
            }
            else if (t == typeof(System.Byte[]))
            {
                byte[] b = (byte[])o;
                SqlParameter p = new SqlParameter(name, SqlDbType.Image, b.Length);
                p.Value = b;
                return p;
            }
            else if (t == typeof(System.DateTime))
            {
                DateTime d = (DateTime)o;
                d = d.NormalizeDateTime();
                SqlParameter p = new SqlParameter(name, SqlDbType.DateTime);
                p.Value = d;
                return p;
            }
            else if (t == typeof(System.DateTimeOffset))
            {
                DateTimeOffset d = (DateTimeOffset)o;
                d = d.NormalizeDateTimeOffset();
                SqlParameter p = new SqlParameter(name, SqlDbType.DateTimeOffset);
                p.Value = d;
                return p;
            }

            throw new ArgumentException($"Unsupported type: '{t.Name}'");
        }

        // リソースの解放
        public void Dispose()
        {
            closeQuery();
            Cancel();
            if (con != null)
            {
                con.Close();
                con.Dispose();
                con = null;
            }
        }

        public delegate bool TransactionalTask();

        // トランザクションの実行 (匿名デリゲートを用いた再試行処理も実施)
        public void Tran(TransactionalTask task)
        {
            Tran(IsolationLevel.Serializable, null, task);
        }
        public void Tran(IsolationLevel iso, TransactionalTask task)
        {
            Tran(iso, null, task);
        }
        public void Tran(IsolationLevel iso, DeadlockRetryConfig retry_config, TransactionalTask task)
        {
            if (retry_config == null)
            {
                retry_config = this.DeadlockRetryConfig;
            }

            int num_retry = 0;

            LABEL_RETRY:
            try
            {
                using (UsingTran u = this.UsingTran(iso))
                {
                    if (task())
                    {
                        u.Commit();
                    }
                }
            }
            catch (SqlException sqlex)
            {
                if (sqlex.Number == 1205)
                {
                    // デッドロック発生
                    num_retry++;
                    if (num_retry <= retry_config.RetryCount)
                    {
                        Kernel.SleepThread(Secure.Rand31i() % retry_config.RetryAverageInterval);

                        goto LABEL_RETRY;
                    }

                    throw;
                }
                else
                {
                    throw;
                }
            }
        }

        // トランザクションの開始 (UsingTran オブジェクト作成)
        public UsingTran UsingTran()
        {
            return UsingTran(IsolationLevel.Unspecified);
        }
        public UsingTran UsingTran(IsolationLevel iso)
        {
            UsingTran t = new UsingTran(this);

            Begin(iso);

            return t;
        }

        // トランザクションの開始
        public void Begin()
        {
            Begin(IsolationLevel.Unspecified);
        }
        public void Begin(IsolationLevel iso)
        {
            closeQuery();

            if (iso == IsolationLevel.Unspecified)
            {
                tran = con.BeginTransaction();
            }
            else
            {
                tran = con.BeginTransaction(iso);
            }
        }

        // トランザクションのコミット
        public void Commit()
        {
            if (tran == null)
            {
                return;
            }

            closeQuery();
            tran.Commit();
            tran.Dispose();
            tran = null;
        }

        // トランザクションのロールバック
        public void Cancel()
        {
            if (tran == null)
            {
                return;
            }

            closeQuery();
            tran.Rollback();
            tran.Dispose();
            tran = null;
        }

        // データベースのテーブルのクラスで値を上書きする
        public static T DbOverwriteValues<T>(T dst, T src)
        {
            return (T)DbOverwriteValues((object)dst, (object)src);
        }
        public static object DbOverwriteValues(object dst, object src)
        {
            if (dst.GetType() != src.GetType())
            {
                throw new ApplicationException("DbOverwriteValues: dst.GetType() != src.GetType()");
            }
            object ret = Util.CloneObject_UsingXml(dst);
            Type t = dst.GetType();
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                var ptype = p.PropertyType;
                object value = p.GetValue(src);
                if (value != null)
                {
                    bool ok = true;
                    if (ptype == typeof(DateTime))
                    {
                        DateTime d = (DateTime)p.GetValue(src);
                        if (Util.IsZero(d))
                        {
                            ok = false;
                        }
                    }
                    else if (ptype == typeof(DateTimeOffset))
                    {
                        DateTimeOffset d = (DateTimeOffset)p.GetValue(src);
                        if (Util.IsZero(d))
                        {
                            ok = false;
                        }
                    }
                    else if (ptype == typeof(int))
                    {
                        int i = (int)p.GetValue(src);
                        if (i == 0) ok = false;
                    }
                    else if (ptype == typeof(long))
                    {
                        long i = (long)p.GetValue(src);
                        if (i == 0) ok = false;
                    }
                    else if (ptype == typeof(decimal))
                    {
                        decimal i = (decimal)p.GetValue(src);
                        if (i == 0) ok = false;
                    }
                    else if (ptype == typeof(double))
                    {
                        double i = (double)p.GetValue(src);
                        if (i == 0) ok = false;
                    }
                    else if (ptype == typeof(float))
                    {
                        float i = (float)p.GetValue(src);
                        if (i == 0) ok = false;
                    }

                    if (ok)
                    {
                        p.SetValue(ret, value);
                    }
                }
            }

            return ret;
        }

        // データベースのテーブルのクラスで Non NULL を強制する
        public static object DbEnforceNonNull(object obj)
        {
            if (obj == null) return null;

            Type t = obj.GetType();

            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                var ptype = p.PropertyType;
                if (ptype.IsNullable() == false)
                {
                    if (ptype == typeof(string))
                    {
                        string s = (string)p.GetValue(obj);
                        if (s == null) p.SetValue(obj, "");
                    }
                    else if (ptype == typeof(DateTime))
                    {
                        DateTime d = (DateTime)p.GetValue(obj);
                        if (d.IsZeroDateTime()) p.SetValue(obj, Util.ZeroDateTimeValue);
                    }
                    else if (ptype == typeof(DateTimeOffset))
                    {
                        DateTimeOffset d = (DateTimeOffset)p.GetValue(obj);
                        if (d.IsZeroDateTime()) p.SetValue(obj, Util.ZeroDateTimeOffsetValue);
                    }
                }
            }

            return obj;
        }
    }
}
