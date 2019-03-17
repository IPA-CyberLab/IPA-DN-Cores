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
using System.Runtime.InteropServices;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using IPA.Cores.Basic;

namespace IPA.Cores.Helper.Basic
{
    static class HelperJson
    {
        public static string ObjectToJson(this object obj, bool include_null = false, bool escape_html = false, int? max_depth = Json.DefaultMaxDepth, bool compact = false, bool reference_handling = false) => Json.Serialize(obj, include_null, escape_html, max_depth, compact, reference_handling);
        public static T JsonToObject<T>(this string str, bool include_null = false, int? max_depth = Json.DefaultMaxDepth) => Json.Deserialize<T>(str, include_null, max_depth);
        public static object JsonToObject(this string str, Type type, bool include_null = false, int? max_depth = Json.DefaultMaxDepth) => Json.Deserialize(str, type, include_null, max_depth);
        public static T ConvertJsonObject<T>(this object obj, bool include_null = false, int? max_depth = Json.DefaultMaxDepth, bool reference_handling = false) => Json.ConvertObject<T>(obj, include_null, max_depth, reference_handling);
        public static object ConvertJsonObject(this object obj, Type type, bool include_null = false, int? max_depth = Json.DefaultMaxDepth, bool reference_handling = false) => Json.ConvertObject(obj, type, include_null, max_depth, reference_handling);
        public static dynamic JsonToDynamic(this string str) => Json.DeserializeDynamic(str);
        public static ulong GetObjectHash(this object o) => Util.GetObjectHash(o);
    }
}
