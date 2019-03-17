using System;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Sql;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Text;
using System.Configuration;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Web;
using System.IO;
using System.Drawing;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    static partial class Dbg
    {
        public static void Report(string name, object obj) => Report(name, obj.ObjectToJson(compact: true));
    }

    partial class GlobalIntervalReporter
    {
        public void Report(string name, object obj)
            => Report(name, obj.ObjectToJson(compact: true));
    }
}
