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
using System.Runtime.InteropServices;

using YamlDotNet;
using YamlDotNet.Serialization;

using IPA.DN.CoreUtil.Helper.Basic;

namespace IPA.Cores.Basic
{
    public static class Yaml
    {
        public static string Serialize(object obj)
        {
            SerializerBuilder sb = new SerializerBuilder();
            sb.EmitDefaults();
            Serializer s = sb.Build();
            StringWriter w = new StringWriter();
            s.Serialize(w, obj, obj.GetType());
            return w.ToString();
        }

        public static T Deserialize<T>(string str)
        {
            DeserializerBuilder db = new DeserializerBuilder();
            db.IgnoreUnmatchedProperties();
            Deserializer d = db.Build();
            return d.Deserialize<T>(str);
        }
    }
}

