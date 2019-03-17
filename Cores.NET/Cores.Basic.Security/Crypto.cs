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

using Org.BouncyCastle;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    // Rsa アルゴリズム
    class Rsa
    {
        byte[] data;
        Cert cert;
        static object lockObj = new object();

        public Rsa(byte[] data)
        {
            init(data);
        }
        public Rsa(string filename)
        {
            Buf b = Buf.ReadFromFile(filename);
            init(b.ByteData);
        }
        public Rsa(Buf b)
        {
            init(b.ByteData);
        }
        void init(byte[] data)
        {
            this.data = (byte[])data.Clone();
            this.cert = null;
        }

        public Rsa(Cert cert)
        {
            init(cert);
        }
        void init(Cert cert)
        {
            this.cert = (Cert)cert.Clone();
            this.data = null;
        }

        public byte[] SignData(byte[] data)
        {
            lock (lockObj)
            {
                byte[] ret;
                using (RsaInner rsa = new RsaInner(this.data, this.cert))
                {
                    ret = rsa.SignData(data);
                }
                return ret;
            }
        }

        public byte[] SignHash(byte[] hash)
        {
            lock (lockObj)
            {
                byte[] ret;
                using (RsaInner rsa = new RsaInner(this.data, this.cert))
                {
                    ret = rsa.SignHash(hash);
                }
                return ret;
            }
        }

        public bool VerifyData(byte[] data, byte[] sign)
        {
            lock (lockObj)
            {
                bool ret;
                using (RsaInner rsa = new RsaInner(this.data, this.cert))
                {
                    ret = rsa.VerifyData(data, sign);
                }
                return ret;
            }
        }

        public bool VerifyHash(byte[] hash, byte[] sign)
        {
            lock (lockObj)
            {
                bool ret;
                using (RsaInner rsa = new RsaInner(this.data, this.cert))
                {
                    ret = rsa.VerifyHash(hash, sign);
                }
                return ret;
            }
        }

        public byte[] Encrypt(byte[] data)
        {
            lock (lockObj)
            {
                using (RsaInner rsa = new RsaInner(this.data, this.cert))
                {
                    return rsa.Encrypt(data);
                }
            }
        }

        public byte[] Decrypt(byte[] data)
        {
            lock (lockObj)
            {
                using (RsaInner rsa = new RsaInner(this.data, this.cert))
                {
                    return rsa.Decrypt(data);
                }
            }
        }
    }

    // Rsa アルゴリズム (内部)
    class RsaInner : IDisposable
    {
        AsymmetricKeyParameter key;

        public RsaInner(byte[] data, Cert cert)
        {
            if (data != null)
            {
                init(data);
            }
            else
            {
                init(cert);
            }
        }
        public RsaInner(byte[] data)
        {
            init(data);
        }
        public RsaInner(string filename)
        {
            Buf b = Buf.ReadFromFile(filename);
            init(b.ByteData);
        }
        public RsaInner(Buf b)
        {
            init(b.ByteData);
        }
        void init(byte[] data)
        {
            PemReader pem = new PemReader(new StringReader(data.GetString_Ascii()));
            object o = pem.ReadObject();
            if (o is AsymmetricCipherKeyPair)
            {
                AsymmetricCipherKeyPair pair = (AsymmetricCipherKeyPair)o;

                o = pair.Private;
            }
            key = (AsymmetricKeyParameter)o;
        }

        public RsaInner(Cert cert)
        {
            init(cert);
        }
        void init(Cert cert)
        {
            PemReader pem = new PemReader(new StringReader(cert.PublicKey.GetString_Ascii()));
            key = (AsymmetricKeyParameter)pem.ReadObject();
        }

        public byte[] SignData(byte[] data)
        {
            byte[] hash = Secure.HashSHA1(data);
            return SignHash(hash);
        }

        public byte[] SignHash(byte[] hash)
        {
            hash = hash_for_sign(hash);
            ISigner signer = SignerUtilities.GetSigner("RSA");
            signer.Init(true, key);
            signer.BlockUpdate(hash, 0, hash.Length);
            return signer.GenerateSignature();
        }

        byte[] hash_for_sign(byte[] data)
        {
            byte[] padding_data = {
                    0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2B, 0x0E,
                    0x03, 0x02, 0x1A, 0x05, 0x00, 0x04, 0x14,
            };

            return Util.CombineByteArray(padding_data, data);
        }

        public bool VerifyData(byte[] data, byte[] sign)
        {
            byte[] hash = Secure.HashSHA1(data);
            return VerifyHash(hash, sign);
        }

        public bool VerifyHash(byte[] hash, byte[] sign)
        {
            hash = hash_for_sign(hash);
            ISigner signer = SignerUtilities.GetSigner("RSA");
            signer.Init(false, key);
            signer.BlockUpdate(hash, 0, hash.Length);
            return signer.VerifySignature(sign);
        }

        public byte[] Encrypt(byte[] data)
        {
            IAsymmetricBlockCipher rsa = new Pkcs1Encoding(new RsaEngine());
            rsa.Init(true, key);
            return rsa.ProcessBlock(data, 0, data.Length);
        }

        public byte[] Decrypt(byte[] data)
        {
            IAsymmetricBlockCipher rsa = new Pkcs1Encoding(new RsaEngine());
            rsa.Init(false, key);
            return rsa.ProcessBlock(data, 0, data.Length);
        }

        public void Dispose()
        {
        }
    }

    // 証明書
    class Cert
    {
        X509Certificate x509;
        static TimeSpan deleteOldCertSpan = new TimeSpan(0, 0, 30);
        static object lockObj = new Object();

        public X509Certificate X509Cert
        {
            get { return x509; }
        }

        public Rsa RsaPublicKey
        {
            get
            {
                return new Rsa(this);
            }
        }

        public Cert(byte[] data)
        {
            init(data);
        }
        public Cert(string filename)
        {
            init(IO.ReadFile(filename));
        }
        public Cert(Buf buf)
        {
            init(buf.ByteData);
        }
        void init(byte[] data)
        {
            PemReader cert_pem = new PemReader(new StringReader(data.GetString_Ascii()));
            x509 = (X509Certificate)cert_pem.ReadObject();
        }

        public byte[] Hash
        {
            get
            {
                return Secure.HashSHA1(x509.GetEncoded());
            }
        }

        public byte[] PublicKey
        {
            get
            {
                StringWriter w = new StringWriter();
                PemWriter pw = new PemWriter(w);
                pw.WriteObject(x509.GetPublicKey());
                return w.ToString().GetBytes_Ascii();
            }
        }

        public byte[] ByteData
        {
            get
            {
                StringWriter w = new StringWriter();
                PemWriter pw = new PemWriter(w);
                pw.WriteObject(x509);
                return w.ToString().GetBytes_Ascii();
            }
        }
        public Buf ToBuf()
        {
            return new Buf(ByteData);
        }
        public void ToFile(string filename)
        {
            ToBuf().WriteToFile(filename);
        }

        public Cert Clone()
        {
            return new Cert(this.ByteData);
        }
    }

}


