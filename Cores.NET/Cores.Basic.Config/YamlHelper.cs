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
    static class HelperYaml
    {
        public static string ObjectToYaml(this object obj) => Yaml.Serialize(obj);
        public static T YamlToObject<T>(this string str) => Yaml.Deserialize<T>(str);
    }
}
