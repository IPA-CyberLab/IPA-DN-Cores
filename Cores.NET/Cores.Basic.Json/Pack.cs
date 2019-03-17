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
    partial class Pack
    {
        public void AddCert(string name, Cert cert)
        {
            AddCert(name, cert, 0);
        }
        public void AddCert(string name, Cert cert, uint index)
        {
            AddData(name, cert.ByteData, index);
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

    }
}
