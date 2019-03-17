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
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace IPA.Cores.Basic
{
    class FileLogger
    {
        object lockObj;
        string logDir;
        string lastFileName;
        IO fs;
        public bool Flush = false;

        public FileLogger(string logDir)
        {
            lockObj = new Object();

            SetLogDir(logDir);

            lastFileName = "";

            fs = null;
        }

        public void SetLogDir(string logDir)
        {
            lock (lockObj)
            {
                this.logDir = IO.InnerFilePath(logDir);
            }
        }

        string generateFileName(DateTime dt)
        {
            return string.Format("{0:0000}{1:00}{2:00}.log", dt.Year, dt.Month, dt.Day);
        }

        string generateFullFileName(DateTime dt)
        {
            lock (lockObj)
            {
                return IO.CombinePath(logDir, generateFileName(dt));
            }
        }

        void write(DateTime now, byte[] data, bool flush)
        {
            lock (lockObj)
            {
                string filename = generateFullFileName(now);

                if (logDir == null || logDir == "")
                {
                    return;
                }

                if (IO.IsDirExists(logDir) == false)
                {
                    if (IO.MakeDir(logDir) == false)
                    {
                        return;
                    }
                }

                if (lastFileName != filename || fs == null)
                {
                    if (fs != null)
                    {
                        try
                        {
                            fs.Close();
                        }
                        catch
                        {
                        }
                    }

                    fs = IO.FileCreateOrAppendOpen(filename);
                }

                lastFileName = filename;

                fs.Write(data);

                if (flush)
                {
                    fs.Flush();
                }
            }
        }

        public void Write(params string[] strings)
        {
            StringBuilder b = new StringBuilder();
            int i;
            for (i = 0; i < strings.Length; i++)
            {
                string s2 = normalizeStr(strings[i]);

                b.Append(s2);

                if (i != (strings.Length - 1))
                {
                    b.Append(",");
                }
            }

            Write(b.ToString());
        }

        public void Write(string str)
        {
            try
            {
                lock (lockObj)
                {
                    DateTime now = DateTime.Now;
                    string nowStr = Str.DateTimeToDtstr(now, true);

                    string tmp = nowStr + "," + str + "\r\n";

                    write(now, Str.Utf8Encoding.GetBytes(tmp), Flush);
                }
            }
            catch
            {
            }
        }

        string normalizeStr(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(",", ";");
        }

        public void Close()
        {
            if (fs != null)
            {
                try
                {
                    fs.Close();
                }
                catch
                {
                }

                fs = null;
            }
        }
    }
}
