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

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    static partial class Util
    {
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
    }
}
