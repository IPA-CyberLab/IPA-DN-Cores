// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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
using System.Collections;

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
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Signers;

namespace IPA.Cores.Basic
{
    namespace Internal
    {
        public class SimplePasswordFinder : IPasswordFinder
        {
            public string Password { get; }
            public SimplePasswordFinder(string password)
            {
                this.Password = password._NonNull();
            }

            public char[] GetPassword() => this.Password.ToCharArray();

            public static IPasswordFinder Get(string password)
            {
                if (password._IsNullOrZeroLen())
                    return null;
                else
                    return new SimplePasswordFinder(password);
            }
        }
    }

    [Flags]
    public enum PkiAlgorithm
    {
        Unknown = 0,
        RSA,
        ECDSA,
    }

    [Flags]
    public enum PkiShaSize
    {
        SHA256 = 0,
        SHA384,
        SHA512,
    }

    public class CertificateStoreContainer
    {
        public string Alias { get; }

        public List<Certificate> CertificateList { get; } = new List<Certificate>();

        public PrivKey PrivateKey { get; }

        public CertificateStoreContainer(string alias, Certificate[] certList, PrivKey privateKey)
        {
            this.Alias = alias;

            this.PrivateKey = privateKey;

            foreach (var cert in certList)
            {
                this.CertificateList.Add(cert);
            }
        }
    }

    public class CertificateStore
    {
        public IReadOnlyDictionary<string, CertificateStoreContainer> Containers => InternalContainers;

        Dictionary<string, CertificateStoreContainer> InternalContainers { get; } = new Dictionary<string, CertificateStoreContainer>(StrComparer.IgnoreCaseComparer);

        public IEnumerable<string> Aliases => InternalContainers.Keys;

        public CertificateStoreContainer PrimaryContainer => InternalContainers.Values.Single();

        public Certificate PrimaryCertificate => PrimaryContainer.CertificateList.First();
        public PrivKey PrimaryPrivateKey => PrimaryContainer.PrivateKey;

        public CertificateStore(ReadOnlySpan<byte> chainedCertData, ReadOnlySpan<byte> privateKey, string password = null)
            : this(chainedCertData, new PrivKey(privateKey, password)) { }

        public CertificateStore(ReadOnlySpan<byte> chainedCertData, PrivKey privateKey)
            : this(CertificateUtil.ImportChainedCertificates(chainedCertData), privateKey) { }

        public CertificateStore(IEnumerable<Certificate> chainedCertList, PrivKey privateKey)
        {
            this.InternalContainers.Add("default", new CertificateStoreContainer("default", chainedCertList.ToArray(), privateKey));

            InitFields();
        }

        public CertificateStore(PalX509Certificate certificate)
            : this(certificate.ExportCertificateAndKeyAsP12().Span)
        {
        }

        public CertificateStore(Certificate singleCert, PrivKey privateKey)
            : this(singleCert._SingleList(), privateKey) { }

        public CertificateStore(ReadOnlySpan<byte> pkcs12, string password = null)
        {
            password = password._NonNull();

            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(pkcs12);
                ms._SeekToBegin();

                Pkcs12Store p12 = new Pkcs12Store(ms, password.ToCharArray());

                foreach (object aliasObject in p12.Aliases)
                {
                    string alias = (string)aliasObject;

                    if (alias._IsNullOrZeroLen() == false)
                    {
                        AsymmetricKeyParameter privateKeyParam = null;

                        AsymmetricKeyEntry key = p12.GetKey(alias);
                        if (key != null)
                        {
                            if (key.Key.IsPrivate == false)
                            {
                                throw new ApplicationException("Key.IsPrivate == false");
                            }

                            privateKeyParam = key.Key;
                        }

                        X509CertificateEntry[] certs = p12.GetCertificateChain(alias);

                        List<Certificate> certList = new List<Certificate>();

                        if (certs != null)
                        {
                            foreach (X509CertificateEntry cert in certs)
                            {
                                Certificate certObj = new Certificate(cert.Certificate);

                                certList.Add(certObj);
                            }
                        }

                        if (certList.Count >= 1)
                        {
                            PrivKey privateKey = null;

                            if (privateKeyParam != null)
                            {
                                privateKey = new PrivKey(new AsymmetricCipherKeyPair(certList[0].PublicKey.PublicKeyData, privateKeyParam));
                            }
                            else
                            {
                                throw new ApplicationException("No private key found.");
                            }

                            CertificateStoreContainer container = new CertificateStoreContainer(alias, certList.ToArray(), privateKey);

                            this.InternalContainers.Add(alias, container);
                        }
                    }
                }

                if (this.InternalContainers.Count == 0)
                {
                    throw new ApplicationException("There are no certificate aliases in the PKCS#12 file.");
                }
            }

            InitFields();
        }

        Singleton<PalX509Certificate> X509CertificateSingleton;

        public ReadOnlyMemory<byte> DigestSHA1Data { get; private set; }
        public string DigestSHA1Str { get; private set; }

        public ReadOnlyMemory<byte> DigestSHA256Data { get; private set; }
        public string DigestSHA256Str { get; private set; }

        public ReadOnlyMemory<byte> DigestSHA512Data { get; private set; }
        public string DigestSHA512Str { get; private set; }

        void InitFields()
        {
            X509CertificateSingleton = new Singleton<PalX509Certificate>(GetX509CertificateInternal);

            if (this.Containers.Count == 1 && this.PrimaryContainer.CertificateList.Count >= 1)
            {
                Certificate cert = this.PrimaryContainer.CertificateList[0];

                if (cert != null)
                {
                    this.DigestSHA1Data = cert.DigestSHA1Data;
                    this.DigestSHA1Str = cert.DigestSHA1Str;

                    this.DigestSHA256Data = cert.DigestSHA256Data;
                    this.DigestSHA256Str = cert.DigestSHA256Str;

                    this.DigestSHA512Data = cert.DigestSHA512Data;
                    this.DigestSHA512Str = cert.DigestSHA512Str;
                }
            }
        }

        PalX509Certificate GetX509CertificateInternal()
        {
            PalX509Certificate x509 = new PalX509Certificate(this.ExportPkcs12().Span);

            return x509;
        }

        public PalX509Certificate GetX509Certificate() => X509CertificateSingleton;

        public PalX509Certificate X509Certificate => GetX509Certificate();

        public Pkcs12Store ToPkcs12Store()
        {
            Pkcs12Store ret = new Pkcs12Store();

            foreach (CertificateStoreContainer container in this.InternalContainers.Values)
            {
                ret.SetKeyEntry(container.Alias, new AsymmetricKeyEntry(container.PrivateKey.PrivateKeyData.Private), container.CertificateList.Select(x => new X509CertificateEntry(x.CertData)).ToArray());
            }

            return ret;
        }

        public ReadOnlyMemory<byte> ExportPkcs12(string password = null)
        {
            password = password._NonNull();

            using (MemoryStream ms = new MemoryStream())
            {
                Pkcs12Store p12 = ToPkcs12Store();

                p12.Save(ms, password.ToCharArray(), PkiUtil.NewSecureRandom());

                return ms.ToArray();
            }
        }

        public void ExportChainedPem(out ReadOnlyMemory<byte> certFile, out ReadOnlyMemory<byte> privateKeyFile, string password = null)
        {
            certFile = this.PrimaryContainer.CertificateList.ExportChainedCertificates();

            privateKeyFile = this.PrimaryContainer.PrivateKey.Export(password);
        }
    }

    public class PrivKey
    {
        public AsymmetricCipherKeyPair PrivateKeyData { get; }

        public PkiAlgorithm Algorithm { get; private set; }

        public RsaKeyParameters RsaParameters => (RsaPrivateCrtKeyParameters)PrivateKeyData.Private;
        public ECPrivateKeyParameters EcdsaParameters => (ECPrivateKeyParameters)PrivateKeyData.Private;

        public PubKey PublicKey { get; private set; }

        public int BitsSize { get; private set; }

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

                if (data == null) throw new ArgumentException("Importing data parse failed.");

                this.PrivateKeyData = data;
            }

            InitFields();
        }

        void InitFields()
        {
            this.PublicKey = new PubKey(this.PrivateKeyData.Public);

            switch (this.PrivateKeyData.Private)
            {
                case RsaPrivateCrtKeyParameters rsa:
                    this.Algorithm = PkiAlgorithm.RSA;
                    this.BitsSize = rsa.Modulus.BitLength;
                    break;

                case ECPrivateKeyParameters ecdsa:
                    this.Algorithm = PkiAlgorithm.ECDSA;
                    this.BitsSize = ecdsa.Parameters.Curve.FieldSize;
                    break;
            }
        }

        public ISigner GetSigner(PkiShaSize? shaSize = null)
        {
            return GetSigner(PkiUtil.GetSignatureAlgorithmOid(this.Algorithm, shaSize, this.BitsSize));
        }

        public ISigner GetSigner(string algorothmOrOid)
        {
            ISigner ret = SignerUtilities.GetSigner(algorothmOrOid);

            ret.Init(true, this.PrivateKeyData.Private);

            return ret;
        }

        public ReadOnlyMemory<byte> Export(string password = null)
        {
            using (StringWriter w = new StringWriter())
            {
                PemWriter pem = new PemWriter(w);

                if (password._IsNullOrZeroLen())
                    pem.WriteObject(this.PrivateKeyData);
                else
                    pem.WriteObject(this.PrivateKeyData, "DESEDE", password.ToCharArray(), PkiUtil.NewSecureRandom());

                w.Flush();

                return w.ToString()._GetBytes_UTF8();
            }
        }
    }

    public class PubKey : IEquatable<PubKey>
    {
        public AsymmetricKeyParameter PublicKeyData { get; }

        public PkiAlgorithm Algorithm { get; private set; }

        public RsaKeyParameters RsaParameters => (RsaKeyParameters)PublicKeyData;
        public ECPublicKeyParameters EcdsaParameters => (ECPublicKeyParameters)PublicKeyData;

        public int BitsSize { get; private set; }

        public PubKey(AsymmetricKeyParameter data)
        {
            if (data.IsPrivate) throw new ArgumentException("the key is private.");

            this.PublicKeyData = data;

            InitFields();
        }

        public PubKey(ReadOnlySpan<byte> import)
        {
            import = ConvertToPemIfDer(import);

            using (StringReader r = new StringReader(import._GetString_UTF8()))
            {
                PemReader pem = new PemReader(r);

                object obj = pem.ReadObject();

                AsymmetricKeyParameter data = (AsymmetricKeyParameter)obj;

                if (data == null) throw new ArgumentException("Importing data parse failed.");

                if (data.IsPrivate) throw new ArgumentException("the key is private.");
                this.PublicKeyData = data;
            }

            InitFields();
        }

        void InitFields()
        {
            switch (this.PublicKeyData)
            {
                case RsaKeyParameters rsa:
                    this.Algorithm = PkiAlgorithm.RSA;
                    this.BitsSize = rsa.Modulus.BitLength;
                    break;

                case ECPublicKeyParameters ecdsa:
                    this.Algorithm = PkiAlgorithm.ECDSA;
                    this.BitsSize = ecdsa.Parameters.Curve.FieldSize;
                    break;
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

        public ISigner GetVerifier(PkiShaSize? shaSize = null)
        {
            ISigner ret = SignerUtilities.GetSigner(PkiUtil.GetSignatureAlgorithmOid(this.Algorithm, shaSize, this.BitsSize));

            ret.Init(false, this.PublicKeyData);

            return ret;
        }
    }

    public class CertificateOptions
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
        public PkiShaSize ShaSize;
        public PkiAlgorithm Algorithm;

        public CertificateOptions(PkiAlgorithm algorithm, string cn = null, string o = null, string ou = null, string c = null, string st = null, string l = null, string e = null, Memory<byte> serial = default, DateTimeOffset? expires = null, string[] subjectAltNames = null, PkiShaSize shaSize = PkiShaSize.SHA256)
        {
            this.Algorithm = algorithm;
            this.CN = cn._NonNullTrim();
            this.O = o._NonNullTrim();
            this.OU = ou._NonNullTrim();
            this.C = c._NonNullTrim();
            this.ST = st._NonNullTrim();
            this.L = l._NonNullTrim();
            this.E = e._NonNullTrim();
            this.Serial = serial._CloneMemory();
            this.ShaSize = shaSize;
            if (this.Serial.IsEmpty)
            {
                this.Serial = Secure.Rand(16);
                this.Serial.Span[0] = (byte)(this.Serial.Span[0] & 0x7f);
            }
            this.Expires = expires ?? Util.MaxDateTimeOffsetValue;
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

        public string GetSignatureAlgorithmOid()
        {
            return PkiUtil.GetSignatureAlgorithmOid(this.Algorithm, this.ShaSize);
        }
    }

    [Flags]
    public enum CertificateHostnameType
    {
        DefaultCert = 0,
        Wildcard = 50,
        SingleHost = 100,
    }

    public class CertificateHostName
    {
        public string HostName { get; }
        public string WildcardEndWith { get; }
        public CertificateHostnameType Type { get; }

        public CertificateHostName(string hostName)
        {
            this.HostName = hostName._NonNullTrim();

            this.Type = CertificateHostnameType.SingleHost;

            if (hostName.StartsWith("*.") || HostName == "*")
            {
                this.Type = CertificateHostnameType.Wildcard;

                this.WildcardEndWith = hostName.Substring(1);
            }
        }

        public bool IsMatchForHost(string hostname)
        {
            if (this.HostName == "*") return true;

            if (hostname._IsEmpty()) return false;

            if (this.Type == CertificateHostnameType.Wildcard)
            {
                int i = hostname.IndexOf(".");
                if (i == -1) return false;
                string tmp = hostname.Substring(i);
                if (this.WildcardEndWith._IsSamei(tmp))
                {
                    return true;
                }
                return false;
            }
            else
            {
                return this.HostName._IsSamei(hostname);
            }
        }
    }

    public class Certificate
    {
        public X509Certificate CertData { get; }

        public PubKey PublicKey { get; private set; }

        public ReadOnlyMemory<byte> DigestSHA1Data { get; private set; }
        public string DigestSHA1Str { get; private set; }

        public ReadOnlyMemory<byte> DigestSHA256Data { get; private set; }
        public string DigestSHA256Str { get; private set; }

        public ReadOnlyMemory<byte> DigestSHA512Data { get; private set; }
        public string DigestSHA512Str { get; private set; }

        public IList<CertificateHostName> HostNameList => HostNameListInternal;

        List<CertificateHostName> HostNameListInternal = new List<CertificateHostName>();

        public Certificate(PalX509Certificate cert)
        {
            ReadOnlyMemory<byte> data = cert.ExportCertificate();

            Asn1InputStream decoder = new Asn1InputStream(data.ToArray());

            Asn1Object obj = decoder.ReadObject();
            Asn1Sequence seq = Asn1Sequence.GetInstance(obj);

            X509CertificateStructure st = X509CertificateStructure.GetInstance(seq);

            this.CertData = new X509Certificate(st);

            InitFields();
        }

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

                if (data == null) throw new ArgumentException("Importing data parse failed.");

                this.CertData = data;

                InitFields();
            }
        }

        // 上位 CA によって署名されている証明書の作成
        public Certificate(PrivKey signKey, CertificateStore parentCertificate, CertificateOptions options)
        {
            X509Name name = options.GenerateName();
            X509V3CertificateGenerator gen = new X509V3CertificateGenerator();

            gen.SetSerialNumber(new BigInteger(options.Serial.ToArray()));
            gen.SetIssuerDN(parentCertificate.PrimaryCertificate.CertData.IssuerDN);
            gen.SetSubjectDN(name);
            gen.SetNotBefore(DateTime.Now.AddDays(-1));
            gen.SetNotAfter(options.Expires.UtcDateTime);
            gen.SetPublicKey(signKey.PublicKey.PublicKeyData);

            X509Extension extConst = new X509Extension(true, new DerOctetString(new BasicConstraints(false)));
            gen.AddExtension(X509Extensions.BasicConstraints, true, extConst.GetParsedValue());

            X509Extension extBasicUsage = new X509Extension(false, new DerOctetString(new KeyUsage(KeyUsage.DigitalSignature | KeyUsage.NonRepudiation | KeyUsage.KeyEncipherment | KeyUsage.DataEncipherment | KeyUsage.KeyCertSign | KeyUsage.CrlSign)));
            gen.AddExtension(X509Extensions.KeyUsage, false, extBasicUsage.GetParsedValue());

            X509Extension extExtendedUsage = new X509Extension(false, new DerOctetString(new ExtendedKeyUsage(KeyPurposeID.IdKPServerAuth, KeyPurposeID.IdKPClientAuth, KeyPurposeID.IdKPCodeSigning, KeyPurposeID.IdKPEmailProtection,
                KeyPurposeID.IdKPIpsecEndSystem, KeyPurposeID.IdKPIpsecTunnel, KeyPurposeID.IdKPIpsecUser, KeyPurposeID.IdKPTimeStamping, KeyPurposeID.IdKPOcspSigning)));
            gen.AddExtension(X509Extensions.ExtendedKeyUsage, false, extExtendedUsage.GetParsedValue());

            X509Extension altName = new X509Extension(false, new DerOctetString(options.GenerateAltNames()));
            gen.AddExtension(X509Extensions.SubjectAlternativeName, false, altName.GetParsedValue());

            this.CertData = gen.Generate(new Asn1SignatureFactory(options.GetSignatureAlgorithmOid(), parentCertificate.PrimaryPrivateKey.PrivateKeyData.Private, PkiUtil.NewSecureRandom()));

            InitFields();
        }

        // 自己署名証明書の作成
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

            this.CertData = gen.Generate(new Asn1SignatureFactory(options.GetSignatureAlgorithmOid(), selfSignKey.PrivateKeyData.Private, PkiUtil.NewSecureRandom()));

            InitFields();
        }

        void InitFields()
        {
            byte[] publicKeyBytes = this.CertData.CertificateStructure.SubjectPublicKeyInfo.GetDerEncoded();

            this.PublicKey = new PubKey(publicKeyBytes);

            HashSet<string> dnsNames = new HashSet<string>();

            ICollection altNamesList = this.CertData.GetSubjectAlternativeNames();

            if (altNamesList != null)
            {
                try
                {
                    foreach (List<object> altName in altNamesList)
                    {
                        try
                        {
                            int type = (int)altName[0];

                            if (type == GeneralName.DnsName)
                            {
                                string value = (string)altName[1];

                                dnsNames.Add(value.ToLower());
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            IList subjectKeyList = this.CertData.SubjectDN.GetOidList();
            IList subjectValuesList = this.CertData.SubjectDN.GetValueList();
            if (subjectKeyList != null && subjectValuesList != null)
            {
                for (int i = 0; i < subjectKeyList.Count; i++)
                {
                    try
                    {
                        DerObjectIdentifier key = (DerObjectIdentifier)subjectKeyList[i];
                        string value = (string)subjectValuesList[i];
                        if (key.Equals(X509Name.CN))
                        {
                            dnsNames.Add(value.ToLower());
                        }
                    }
                    catch { }
                }
            }

            this.HostNameListInternal = new List<CertificateHostName>();

            foreach (string fqdn in dnsNames)
            {
                HostNameListInternal.Add(new CertificateHostName(fqdn));
            }

            byte[] der = this.CertData.GetEncoded();

            this.DigestSHA1Data = Secure.HashSHA1(der);
            this.DigestSHA1Str = this.DigestSHA1Data._GetHexString();

            this.DigestSHA256Data = Secure.HashSHA256(der);
            this.DigestSHA256Str = this.DigestSHA256Data._GetHexString();

            this.DigestSHA512Data = Secure.HashSHA512(der);
            this.DigestSHA512Str = this.DigestSHA512Data._GetHexString();
        }

        public bool IsMatchForHost(string hostname, out CertificateHostnameType matchType)
        {
            foreach (var cn in this.HostNameList)
            {
                if (cn.IsMatchForHost(hostname))
                {
                    matchType = cn.Type;
                    return true;
                }
            }

            matchType = CertificateHostnameType.SingleHost;
            return false;
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

    public class Csr
    {
        Pkcs10CertificationRequest Request;

        public Csr(PrivKey priv, CertificateOptions options)
        {
            X509Name subject = options.GenerateName();
            GeneralNames alt = options.GenerateAltNames();
            X509Extension altName = new X509Extension(false, new DerOctetString(alt));

            List<object> oids = new List<object>()
            {
                X509Extensions.SubjectAlternativeName,
            };

            List<object> values = new List<object>()
            {
                altName,
            };

            X509Extensions x509exts = new X509Extensions(oids, values);
            X509Attribute attr = new X509Attribute(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest.Id, new DerSet(x509exts));

            AttributePkcs attr2 = new AttributePkcs(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest, new DerSet(x509exts));

            this.Request = new Pkcs10CertificationRequest(new Asn1SignatureFactory(options.GetSignatureAlgorithmOid(), priv.PrivateKeyData.Private, PkiUtil.NewSecureRandom()),
                subject, priv.PublicKey.PublicKeyData, new DerSet(attr2));
        }

        public ReadOnlyMemory<byte> ExportPem()
        {
            using (StringWriter w = new StringWriter())
            {
                PemWriter pem = new PemWriter(w);

                pem.WriteObject(this.Request);

                w.Flush();

                return w.ToString()._GetBytes_UTF8();
            }
        }

        public ReadOnlyMemory<byte> ExportDer()
        {
            return this.Request.GetDerEncoded();
        }
    }

    public static class CertificateUtil
    {
        public static ReadOnlyMemory<byte> ExportChainedCertificates(this IEnumerable<Certificate> certList)
        {
            StringWriter w = new StringWriter();
            foreach (Certificate cert in certList)
            {
                string certBody = cert.Export()._GetString_UTF8();

                w.WriteLine($"subject: {cert.CertData.SubjectDN.ToString()}");
                w.WriteLine($"issuer: {cert.CertData.IssuerDN.ToString()}");
                w.WriteLine(certBody);
            }

            return w.ToString()._GetBytes_UTF8();
        }

        public static Certificate[] ImportChainedCertificates(ReadOnlySpan<byte> data)
        {
            List<Certificate> ret = new List<Certificate>();

            string[] lines = data._GetString_UTF8()._GetLines();

            int mode = 0;

            StringWriter current = new StringWriter();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                switch (mode)
                {
                    case 0:
                        if (line == "-----BEGIN CERTIFICATE-----")
                        {
                            mode = 1;
                            current.WriteLine(line);
                        }
                        break;

                    case 1:
                        current.WriteLine(line);
                        if (line == "-----END CERTIFICATE-----")
                        {
                            mode = 0;

                            string body = current.ToString();
                            current = new StringWriter();

                            Certificate cert = new Certificate(body._GetBytes_UTF8());

                            ret.Add(cert);
                        }
                        break;
                }
            }

            if (ret.Count == 0)
            {
                throw new ApplicationException("No certificates found on the text file.");
            }

            return ret.ToArray();
        }
    }

    public static class PkiUtil
    {
        public static SecureRandom NewSecureRandom() => SecureRandom.GetInstance("SHA1PRNG");

        public static void GenerateKeyPair(PkiAlgorithm algorithm, int bits, out PrivKey privateKey, out PubKey publicKey)
        {
            switch (algorithm)
            {
                case PkiAlgorithm.RSA:
                    GenerateRsaKeyPair(bits, out privateKey, out publicKey);
                    break;

                case PkiAlgorithm.ECDSA:
                    GenerateEcdsaKeyPair(bits, out privateKey, out publicKey);
                    break;

                default:
                    throw new ArgumentException("algorithm");
            }
        }

        public static void GenerateRsaKeyPair(int bits, out PrivKey privateKey, out PubKey publicKey)
        {
            KeyGenerationParameters param = new KeyGenerationParameters(NewSecureRandom(), bits);
            RsaKeyPairGenerator gen = new RsaKeyPairGenerator();
            gen.Init(param);
            AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();

            privateKey = new PrivKey(pair);
            publicKey = new PubKey(pair.Public);
        }

        public static void GenerateEcdsaKeyPair(int bits, out PrivKey privateKey, out PubKey publicKey)
        {
            KeyGenerationParameters param = new KeyGenerationParameters(NewSecureRandom(), bits);
            ECKeyPairGenerator gen = new ECKeyPairGenerator("ECDSA");
            gen.Init(param);
            AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();

            privateKey = new PrivKey(pair);
            publicKey = new PubKey(pair.Public);
        }

        public static string GetSignatureAlgorithmOid(PkiAlgorithm algorithm, PkiShaSize? shaSize = null, int size = 0)
        {
            string alg;

            if (shaSize == null)
            {
                if (size >= 512)
                    shaSize = PkiShaSize.SHA512;
                else if (size >= 384)
                    shaSize = PkiShaSize.SHA384;
                else
                    shaSize = PkiShaSize.SHA256;
            }

            switch (algorithm)
            {
                case PkiAlgorithm.RSA:
                    switch (shaSize)
                    {
                        default:
                            alg = PkcsObjectIdentifiers.Sha256WithRsaEncryption.Id;
                            break;

                        case PkiShaSize.SHA384:
                            alg = PkcsObjectIdentifiers.Sha384WithRsaEncryption.Id;
                            break;

                        case PkiShaSize.SHA512:
                            alg = PkcsObjectIdentifiers.Sha512WithRsaEncryption.Id;
                            break;
                    }
                    break;

                case PkiAlgorithm.ECDSA:
                    switch (shaSize)
                    {
                        default:
                            alg = X9ObjectIdentifiers.ECDsaWithSha256.Id;
                            break;

                        case PkiShaSize.SHA384:
                            alg = X9ObjectIdentifiers.ECDsaWithSha384.Id;
                            break;

                        case PkiShaSize.SHA512:
                            alg = X9ObjectIdentifiers.ECDsaWithSha512.Id;
                            break;
                    }
                    break;

                default:
                    throw new ArgumentException("selfSignKey: Unknown key algorithm");
            }

            return alg;
        }
    }

    public partial class PalX509Certificate
    {
        Singleton<Certificate> PkiCertificateSingleton;
        Singleton<CertificateStore> PkiCertificateStoreSingleton;

        public Certificate PkiCertificate => PkiCertificateSingleton;
        public CertificateStore PkiCertificateStore => PkiCertificateStoreSingleton;

        // PalX509Certificate の追加的な初期化
        partial void InitPkiFields()
        {
            PkiCertificateSingleton = new Singleton<Certificate>(() => new Certificate(this));

            PkiCertificateStoreSingleton = new Singleton<CertificateStore>(() => new CertificateStore(this));
        }
    }
}

namespace IPA.Cores.Helper.Basic
{
    public static class PkiHelper
    {
        public static byte[] Sign(this ISigner signer, byte[] data, int offset = 0, int size = DefaultSize)
        {
            size = size._DefaultSize(data.Length - offset);

            signer.Reset();

            signer.BlockUpdate(data, offset, size);

            return signer.GenerateSignature();
        }

        public static bool Verify(this ISigner signer, byte[] signature, byte[] data, int offset = 0, int size = DefaultSize)
        {
            size = size._DefaultSize(data.Length - offset);

            signer.Reset();

            signer.BlockUpdate(data, offset, size);

            return signer.VerifySignature(signature);
        }
    }
}

#endif // CORES_BASIC_SECURITY

