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

using IPA.DN.CoreUtil.Helper.Basic;

namespace IPA.Cores.Basic
{
    // Rsa アルゴリズム
    public class Rsa
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
    public class Cert
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

    // Secure クラス
    public class Secure
    {
        static RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
        static MD5 md5 = new MD5CryptoServiceProvider();
        public const uint SHA1Size = 20;
        public const uint MD5Size = 16;
        static object rand_lock = new object();

        // 乱数
        public static byte[] Rand(uint size)
        {
            lock (rand_lock)
            {
                byte[] ret = new byte[size];
                rng.GetBytes(ret);
                return ret;
            }
        }
        public static uint Rand32()
        {
            return BitConverter.ToUInt32(Rand(4), 0);
        }
        public static ulong Rand64()
        {
            return BitConverter.ToUInt64(Rand(8), 0);
        }
        public static ushort Rand16()
        {
            return BitConverter.ToUInt16(Rand(2), 0);
        }
        public static int Rand32i()
        {
            return BitConverter.ToInt32(Rand(4), 0);
        }
        public static long Rand64i()
        {
            return BitConverter.ToInt64(Rand(8), 0);
        }
        public static short Rand16i()
        {
            return BitConverter.ToInt16(Rand(2), 0);
        }
        public static int Rand31i()
        {
            while (true)
            {
                int i = Rand32i();
                if (i >= 0)
                {
                    return i;
                }
            }
        }
        public static long Rand63i()
        {
            while (true)
            {
                long i = Rand64i();
                if (i >= 0)
                {
                    return i;
                }
            }
        }
        public static short Rand15i()
        {
            while (true)
            {
                short i = Rand16i();
                if (i >= 0)
                {
                    return i;
                }
            }
        }
        public static byte Rand8()
        {
            return Rand(1)[0];
        }
        public static bool Rand1()
        {
            return (Rand32() % 2) == 0;
        }

        // MD5
        public static byte[] HashMD5(byte[] data)
        {
            byte[] ret;

            ret = md5.ComputeHash(data);

            return ret;
        }

        // SHA1
        public static byte[] HashSHA1(byte[] data)
        {
            SHA1 sha1 = new SHA1Managed();

            return sha1.ComputeHash(data);
        }

        // SHA256
        public static byte[] HashSHA256(byte[] data)
        {
            SHA256 sha256 = new SHA256Managed();

            return sha256.ComputeHash(data);
        }

        // PKCS パディング
        public static byte[] PkcsPadding(byte[] srcData, int destSize)
        {
            int srcSize = srcData.Length;

            if ((srcSize + 11) > destSize)
            {
                throw new OverflowException();
            }

            int randSize = destSize - srcSize - 3;
            byte[] rand = Secure.Rand((uint)randSize);

            Buf b = new Buf();
            b.WriteByte(0x00);
            b.WriteByte(0x02);
            b.Write(rand);
            b.WriteByte(0x00);
            b.Write(srcData);

            return b.ByteData;
        }

        // パスワードハッシュの生成
        public const int PasswordSaltSize = 16;
        public const int PasswordKeySize = 32;
        public const int PasswordIterations = 1234;
        public static string SaltPassword(string password, byte[] salt = null)
        {
            if (salt == null)
            {
                salt = Secure.Rand(PasswordSaltSize);
            }

            byte[] pw = password.NonNull().GetBytes_UTF8();
            byte[] src = pw;

            for (int i = 0; i < PasswordIterations; i++)
            {
                src = Secure.HashSHA256(src.CombineByte(salt));
            }

            return src.CombineByte(salt).GetHexString();
        }

        // パスワードハッシュの検証
        public static bool VeritySaltedPassword(string hash, string password)
        {
            byte[] data = hash.GetHexBytes();
            if (data.Length != (PasswordSaltSize + PasswordKeySize))
            {
                throw new ApplicationException("data.Length != (PasswordSaltSize + PasswordKeySize)");
            }

            byte[] pw = data.ExtractByte(0, PasswordKeySize);
            byte[] salt = data.ExtractByte(PasswordKeySize, PasswordSaltSize);

            string hash2 = SaltPassword(password, salt);

            return hash.GetHexBytes().IsSameByte(hash2.GetHexBytes());
        }

        // PKCS 証明書の読み込み
        public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadPkcs12(byte[] data, string password)
        {
            password = password.NonNull();
            return new System.Security.Cryptography.X509Certificates.X509Certificate2(data, password, System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet);
        }
        public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadPkcs12(string filename, string password)
        {
            return LoadPkcs12(IO.ReadFile(filename), password);
        }
    }

    public static class ExeSignChecker
    {
        public static bool IsKernelModeSignedFile(string fileName)
        {
            return IsKernelModeSignedFile(IO.ReadFile(fileName));
        }

        public static bool IsKernelModeSignedFile(byte[] data)
        {
            string str = Str.AsciiEncoding.GetString(data);

            if (str.IndexOf("Microsoft Code Verification Root") != -1 &&
                str.IndexOf("http://crl.microsoft.com/pki/crl/products/MicrosoftCodeVerifRoot.crl") != -1)
            {
                return true;
            }

            return false;
        }

        enum SignChecker_MemoryAllocator { HGlobal, CoTaskMem };
        enum SignChecker_UiChoice { All = 1, NoUI, NoBad, NoGood };
        enum SignChecker_StateAction { Ignore = 0, Verify, Close, AutoCache, AutoCacheFlush };
        enum SignChecker_UnionChoice { File = 1, Catalog, Blob, Signer, Cert };
        enum SignChecker_RevocationCheckFlags { None = 0, WholeChain };
        enum SignChecker_TrustProviderFlags
        {
            UseIE4Trust = 1,
            NoIE4Chain = 2,
            NoPolicyUsage = 4,
            RevocationCheckNone = 16,
            RevocationCheckEndCert = 32,
            RevocationCheckChain = 64,
            RecovationCheckChainExcludeRoot = 128,
            Safer = 256,
            HashOnly = 512,
            UseDefaultOSVerCheck = 1024,
            LifetimeSigning = 2048
        };
        enum SignChecker_UIContext { Execute = 0, Install };

        [DllImport("Wintrust.dll", PreserveSig = true, SetLastError = false)]
        static extern uint WinVerifyTrust(IntPtr hWnd, IntPtr pgActionID, IntPtr pWinTrustData);

        sealed class SignCheckerUnmanagedPointer : IDisposable
        {
            private IntPtr m_ptr;
            private SignChecker_MemoryAllocator m_meth;
            public SignCheckerUnmanagedPointer(IntPtr ptr, SignChecker_MemoryAllocator method)
            {
                m_meth = method;
                m_ptr = ptr;
            }

            ~SignCheckerUnmanagedPointer()
            {
                Dispose(false);
            }

            void Dispose(bool disposing)
            {
                if (m_ptr != IntPtr.Zero)
                {
                    if (m_meth == SignChecker_MemoryAllocator.HGlobal)
                    {
                        Marshal.FreeHGlobal(m_ptr);
                    }
                    else if (m_meth == SignChecker_MemoryAllocator.CoTaskMem)
                    {
                        Marshal.FreeCoTaskMem(m_ptr);
                    }
                    m_ptr = IntPtr.Zero;
                }

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }

            public static implicit operator IntPtr(SignCheckerUnmanagedPointer ptr)
            {
                return ptr.m_ptr;
            }
        }

        struct WINTRUST_FILE_INFO : IDisposable
        {
            public WINTRUST_FILE_INFO(string fileName, Guid subject)
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
                pcwszFilePath = fileName;

                if (subject != Guid.Empty)
                {
                    tmp = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));
                    Marshal.StructureToPtr(subject, tmp, false);
                }
                else
                {
                    tmp = IntPtr.Zero;
                }
                hFile = IntPtr.Zero;
            }
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr tmp;

            public void Dispose()
            {
                Dispose(true);
            }

            private void Dispose(bool disposing)
            {
                if (tmp != IntPtr.Zero)
                {
                    Marshal.DestroyStructure(this.tmp, typeof(Guid));
                    Marshal.FreeHGlobal(this.tmp);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA : IDisposable
        {
            public WINTRUST_DATA(WINTRUST_FILE_INFO fileInfo)
            {
                this.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                pInfoStruct = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                Marshal.StructureToPtr(fileInfo, pInfoStruct, false);
                dwUnionChoice = SignChecker_UnionChoice.File;
                pPolicyCallbackData = IntPtr.Zero;
                pSIPCallbackData = IntPtr.Zero;
                dwUIChoice = SignChecker_UiChoice.NoUI;
                fdwRevocationChecks = SignChecker_RevocationCheckFlags.WholeChain;
                dwStateAction = SignChecker_StateAction.Ignore;
                hWVTStateData = IntPtr.Zero;
                pwszURLReference = IntPtr.Zero;
                dwProvFlags = SignChecker_TrustProviderFlags.RevocationCheckChain;

                dwUIContext = SignChecker_UIContext.Execute;
            }

            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPCallbackData;
            public SignChecker_UiChoice dwUIChoice;
            public SignChecker_RevocationCheckFlags fdwRevocationChecks;
            public SignChecker_UnionChoice dwUnionChoice;
            public IntPtr pInfoStruct;
            public SignChecker_StateAction dwStateAction;
            public IntPtr hWVTStateData;
            private IntPtr pwszURLReference;
            public SignChecker_TrustProviderFlags dwProvFlags;
            public SignChecker_UIContext dwUIContext;

            public void Dispose()
            {
                Dispose(true);
            }

            private void Dispose(bool disposing)
            {
                if (dwUnionChoice == SignChecker_UnionChoice.File)
                {
                    WINTRUST_FILE_INFO info = new WINTRUST_FILE_INFO();
                    Marshal.PtrToStructure(pInfoStruct, info);
                    info.Dispose();
                    Marshal.DestroyStructure(pInfoStruct, typeof(WINTRUST_FILE_INFO));
                }

                Marshal.FreeHGlobal(pInfoStruct);
            }
        }

        public static bool CheckFileDigitalSignature(string fileName)
        {
            Guid wintrust_action_generic_verify_v2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");
            WINTRUST_FILE_INFO fileInfo = new WINTRUST_FILE_INFO(fileName, Guid.Empty);
            WINTRUST_DATA data = new WINTRUST_DATA(fileInfo);

            uint ret = 0;

            using (SignCheckerUnmanagedPointer guidPtr = new SignCheckerUnmanagedPointer(Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid))), SignChecker_MemoryAllocator.HGlobal))
            using (SignCheckerUnmanagedPointer wvtDataPtr = new SignCheckerUnmanagedPointer(Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA))), SignChecker_MemoryAllocator.HGlobal))
            {
                IntPtr pGuid = guidPtr;
                IntPtr pData = wvtDataPtr;

                Marshal.StructureToPtr(wintrust_action_generic_verify_v2, pGuid, false);
                Marshal.StructureToPtr(data, pData, false);

                ret = WinVerifyTrust(IntPtr.Zero, pGuid, pData);
            }

            if (ret != 0)
            {
                return false;
            }

            return true;
        }
    }
}


