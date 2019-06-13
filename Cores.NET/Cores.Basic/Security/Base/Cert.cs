// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

#if CORES_BASIC_SECURITY

using System;
using System.IO;
using System.Text;
using System.Linq;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;

using IPA.Cores.Basic;
using IPA.Cores.Basic.Legacy;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using System.Security.Cryptography;
using System.Collections.Generic;

namespace IPA.Cores.Basic
{
    namespace Internal
    {
        class SimplePasswordFinder : IPasswordFinder
        {
            public string Password { get; }
            public SimplePasswordFinder(string password)
            {
                this.Password = password._NonNull();
            }

            public char[] GetPassword() => this.Password.ToCharArray();

            public static IPasswordFinder Get(string password)
            {
                if (password._IsNullOrLen0String())
                    return null;
                else
                    return new SimplePasswordFinder(password);
            }
        }
    }

    class CertificateStoreContainer
    {
        public string Name { get; }

        List<Certificate> InternalCertificates = new List<Certificate>();

        public IList<Certificate> Certificate => InternalCertificates;

        public CertificateStoreContainer(string name, Certificate[] certList)
        {
            this.Name = name;

            foreach (var cert in certList)
            {
                this.InternalCertificates.Add(cert);
            }
        }
    }

    class CertificateStore
    {
        Dictionary<string, CertificateStoreContainer> InternalContainers = new Dictionary<string, CertificateStoreContainer>();

        public IReadOnlyDictionary<string, CertificateStoreContainer> Containers => InternalContainers;

        public CertificateStore(ReadOnlySpan<byte> pkcs12Import, string password = null)
        {
            password = password._NonNull();

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(pkcs12Import);
                ms._SeekToBegin();

                Pkcs12Store p12 = new Pkcs12Store(ms, password.ToCharArray());

                foreach (object aliasObject in p12.Aliases)
                {
                    string alias = (string)aliasObject;

                    if (alias._IsNullOrLen0String() == false)
                    {
                        X509CertificateEntry[] certs = p12.GetCertificateChain(alias);

                        List<Certificate> cerList = new List<Certificate>();

                        foreach (X509CertificateEntry cert in certs)
                        {
                            cerList.Add(new Certificate(cert.Certificate));
                        }

                        CertificateStoreContainer container = new CertificateStoreContainer(alias, cerList.ToArray());
                        this.InternalContainers.Add(alias, container);
                    }
                }

                if (this.Containers.Count == 0)
                {
                    throw new ApplicationException("There are no certificate aliases in the PKCS#12 file.");
                }
            }
        }
    }

    class PrivKey
    {
        public AsymmetricCipherKeyPair PrivateKeyData { get; }

        public PubKey PublicKey { get; private set; }

        public PrivKey(AsymmetricCipherKeyPair data)
        {
            this.PrivateKeyData = data;

            InitFields();
        }

        public PrivKey(ReadOnlySpan<byte> import, string password = null)
        {
            using (StringReader r = new StringReader(import._GetString_UTF8()))
            {
                PemReader pem = new PemReader(r, Internal.SimplePasswordFinder.Get(password));

                object obj = pem.ReadObject();

                AsymmetricCipherKeyPair data = (AsymmetricCipherKeyPair)obj;

                this.PrivateKeyData = data;
            }

            InitFields();
        }

        void InitFields()
        {
            this.PublicKey = new PubKey(this.PrivateKeyData.Public);
        }

        public ReadOnlyMemory<byte> Export(string password = null)
        {
            using (StringWriter w = new StringWriter())
            {
                PemWriter pem = new PemWriter(w);

                if (password._IsNullOrLen0String())
                    pem.WriteObject(this.PrivateKeyData);
                else
                    pem.WriteObject(this.PrivateKeyData, "DESEDE", password.ToCharArray(), RsaUtil.NewSecureRandom());

                w.Flush();

                return w.ToString()._GetBytes_UTF8();
            }
        }
    }

    class PubKey : IEquatable<PubKey>
    {
        public AsymmetricKeyParameter PublicKeyData { get; }

        public PubKey(AsymmetricKeyParameter data)
        {
            if (data.IsPrivate) throw new ArgumentException("the key is private.");
            this.PublicKeyData = data;
        }

        public PubKey(ReadOnlySpan<byte> import)
        {
            import = ConvertToPemIfDer(import);

            using (StringReader r = new StringReader(import._GetString_UTF8()))
            {
                PemReader pem = new PemReader(r);

                object obj = pem.ReadObject();

                AsymmetricKeyParameter data = (AsymmetricKeyParameter)obj;

                if (data.IsPrivate) throw new ArgumentException("the key is private.");
                this.PublicKeyData = data;
            }
        }

        public ReadOnlyMemory<byte> Export()
        {
            using (StringWriter w = new StringWriter())
            {
                PemWriter pem = new PemWriter(w);

                pem.WriteObject(this.PublicKeyData);

                w.Flush();

                return w.ToString()._GetBytes_UTF8();
            }
        }

        static ReadOnlySpan<byte> ConvertToPemIfDer(ReadOnlySpan<byte> src)
        {
            string test = src._GetString_Ascii();
            if (test._InStr("-----BEGIN", false)) return src;

            string str = "-----BEGIN PUBLIC KEY-----\n" + Str.Base64Encode(src.ToArray()) + "\n" + "-----END PUBLIC KEY-----\n\n";

            return str._GetBytes_Ascii();
        }

        public bool CheckIfPrivateKeyCorrespond(PrivKey privateKey)
        {
            return this.Equals(privateKey.PublicKey);
        }

        public bool Equals(PubKey other)
        {
            return this.PublicKeyData.Equals(other.PublicKeyData);
        }
    }

    class CertificateOptions
    {
        public string CN;
        public string O;
        public string OU;
        public string C;
        public string ST;
        public string L;
        public string E;
        public Memory<byte> Serial;
        public DateTimeOffset Expires;
        public SortedSet<string> SubjectAlternativeNames = new SortedSet<string>();

        public CertificateOptions(string cn = null, string o = null, string ou = null, string c = null, string st = null, string l = null, string e = null, Memory<byte> serial = default, DateTimeOffset? expires = null, string[] subjectAltNames = null)
        {
            this.CN = cn._NonNullTrim();
            this.O = o._NonNullTrim();
            this.OU = ou._NonNullTrim();
            this.C = c._NonNullTrim();
            this.ST = st._NonNullTrim();
            this.L = l._NonNullTrim();
            this.E = e._NonNullTrim();
            this.Serial = serial._CloneMemory();
            if (this.Serial.IsEmpty)
            {
                this.Serial = new byte[1] { 1 };
            }
            this.Expires = expires ?? new DateTime(2099, 12, 30, 0, 0, 0)._AsDateTimeOffset(false);
            this.SubjectAlternativeNames.Add(this.CN);

            if (subjectAltNames != null)
            {
                subjectAltNames.Where(x => x._IsEmpty() == false)._DoForEach(x => this.SubjectAlternativeNames.Add(x.Trim()));
            }
        }

        Dictionary<DerObjectIdentifier, string> GenerateDictionary()
        {
            Dictionary<DerObjectIdentifier, string> ret = new Dictionary<DerObjectIdentifier, string>();

            Add(X509Name.CN, this.CN);
            Add(X509Name.O, this.O);
            Add(X509Name.OU, this.OU);
            Add(X509Name.C, this.C);
            Add(X509Name.ST, this.ST);
            Add(X509Name.L, this.L);
            Add(X509Name.E, this.E);

            return ret;

            void Add(DerObjectIdentifier key, string value)
            {
                if (value._IsEmpty() == false)
                {
                    ret.Add(key, value);
                }
            }
        }

        List<DerObjectIdentifier> GetOrdering(IEnumerable<DerObjectIdentifier> keys)
        {
            List<DerObjectIdentifier> ord = new List<DerObjectIdentifier>()
            {
                X509Name.CN,
                X509Name.C,
                X509Name.ST,
                X509Name.L,
                X509Name.E,
                X509Name.O,
                X509Name.OU,
            };

            return ord.Where(x => keys.Contains(x)).ToList();
        }

        public X509Name GenerateName()
        {
            Dictionary<DerObjectIdentifier, string> dic = GenerateDictionary();
            return new X509Name(GetOrdering(dic.Keys), dic);
        }

        public GeneralNames GenerateAltNames()
        {
            List<GeneralName> o = new List<GeneralName>();
            this.SubjectAlternativeNames._DoForEach(x => o.Add(new GeneralName(GeneralName.DnsName, x)));
            return new GeneralNames(o.ToArray());
        }
    }

    class Certificate
    {
        public X509Certificate CertData { get; }

        public PubKey PublicKey { get; private set; }

        public Certificate(X509Certificate cert)
        {
            this.CertData = cert;

            InitFields();
        }

        public Certificate(ReadOnlySpan<byte> import)
        {
            using (StringReader r = new StringReader(import._GetString_UTF8()))
            {
                PemReader pem = new PemReader(r);

                object obj = pem.ReadObject();

                X509Certificate data = (X509Certificate)obj;

                this.CertData = data;

                InitFields();
            }
        }

        public Certificate(PrivKey selfSignKey, CertificateOptions options)
        {
            X509Name name = options.GenerateName();
            X509V3CertificateGenerator gen = new X509V3CertificateGenerator();

            gen.SetSerialNumber(new BigInteger(options.Serial.ToArray()));
            gen.SetIssuerDN(name);
            gen.SetSubjectDN(name);
            gen.SetNotBefore(DateTime.Now.AddDays(-1));
            gen.SetNotAfter(options.Expires.UtcDateTime);
            gen.SetPublicKey(selfSignKey.PublicKey.PublicKeyData);

            X509Extension extConst = new X509Extension(true, new DerOctetString(new BasicConstraints(true)));
            gen.AddExtension(X509Extensions.BasicConstraints, true, extConst.GetParsedValue());

            X509Extension extBasicUsage = new X509Extension(false, new DerOctetString(new KeyUsage(KeyUsage.DigitalSignature | KeyUsage.NonRepudiation | KeyUsage.KeyEncipherment | KeyUsage.DataEncipherment | KeyUsage.KeyCertSign | KeyUsage.CrlSign)));
            gen.AddExtension(X509Extensions.KeyUsage, false, extBasicUsage.GetParsedValue());

            X509Extension extExtendedUsage = new X509Extension(false, new DerOctetString(new ExtendedKeyUsage(KeyPurposeID.IdKPServerAuth, KeyPurposeID.IdKPClientAuth, KeyPurposeID.IdKPCodeSigning, KeyPurposeID.IdKPEmailProtection,
                KeyPurposeID.IdKPIpsecEndSystem, KeyPurposeID.IdKPIpsecTunnel, KeyPurposeID.IdKPIpsecUser, KeyPurposeID.IdKPTimeStamping, KeyPurposeID.IdKPOcspSigning)));
            gen.AddExtension(X509Extensions.ExtendedKeyUsage, false, extExtendedUsage.GetParsedValue());

            X509Extension altName = new X509Extension(false, new DerOctetString(options.GenerateAltNames()));
            gen.AddExtension(X509Extensions.SubjectAlternativeName, false, altName.GetParsedValue());

            this.CertData = gen.Generate(new Asn1SignatureFactory(PkcsObjectIdentifiers.Sha256WithRsaEncryption.Id, selfSignKey.PrivateKeyData.Private, RsaUtil.NewSecureRandom()));

            InitFields();
        }

        void InitFields()
        {
            byte[] publicKeyBytes = this.CertData.CertificateStructure.SubjectPublicKeyInfo.GetDerEncoded();

            this.PublicKey = new PubKey(publicKeyBytes);
        }

        public ReadOnlyMemory<byte> Export()
        {
            using (StringWriter w = new StringWriter())
            {
                PemWriter pem = new PemWriter(w);

                pem.WriteObject(this.CertData);

                w.Flush();

                return w.ToString()._GetBytes_UTF8();
            }
        }
    }

    static class RsaUtil
    {
        public static SecureRandom NewSecureRandom() => SecureRandom.GetInstance("SHA1PRNG");

        public static void GenerateRsaKeyPair(int bits, out PrivKey privateKey, out PubKey publicKey)
        {
            KeyGenerationParameters param = new KeyGenerationParameters(NewSecureRandom(), bits);
            RsaKeyPairGenerator gen = new RsaKeyPairGenerator();
            gen.Init(param);
            AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();

            privateKey = new PrivKey(pair);
            publicKey = new PubKey(pair.Public);
        }
    }
}

#endif // CORES_BASIC_SECURITY

