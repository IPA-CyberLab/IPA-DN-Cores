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

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

namespace IPA.Cores.Basic
{
    // Secure クラス
    class Secure
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
        public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadPkcs12(byte[] data, string password = null)
        {
            password = password.NonNull();
            return new System.Security.Cryptography.X509Certificates.X509Certificate2(data, password, System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet);
        }
        public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadPkcs12(string filename, string password = null)
        {
            return LoadPkcs12(IO.ReadFile(filename), password);
        }
        public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadPkcs12(string embeddedResourceName, Type assemblyType)
        {
            return LoadPkcs12(IO.ReadEmbeddedFileData(embeddedResourceName, assemblyType));
        }

        public static CertSelectorCallback StaticServerCertSelector(X509Certificate2 cert) => (obj, sni) => cert;
    }

    static class ExeSignChecker
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

    class RC4 : ICloneable
    {
        uint x, y;
        uint[] state;

        public RC4(byte[] key)
        {
            state = new uint[256];

            uint i, t, u, ki, si;

            x = 0;
            y = 0;

            for (i = 0; i < 256; i++)
            {
                state[i] = i;
            }

            ki = si = 0;
            for (i = 0; i < 256; i++)
            {
                t = state[i];

                si = (si + key[ki] + t) & 0xff;
                u = state[si];
                state[si] = t;
                state[i] = u;
                if (++ki >= key.Length)
                {
                    ki = 0;
                }
            }
        }

        private RC4()
        {
        }

        public object Clone()
        {
            RC4 rc4 = new RC4();

            rc4.x = this.x;
            rc4.y = this.y;
            rc4.state = (uint[])this.state.Clone();

            return rc4;
        }

        public byte[] Encrypt(byte[] src)
        {
            return Encrypt(src, src.Length);
        }
        public byte[] Encrypt(byte[] src, int len)
        {
            return Encrypt(src, 0, len);
        }
        public byte[] Encrypt(byte[] src, int offset, int len)
        {
            byte[] dst = new byte[len];

            uint x, y, sx, sy;
            x = this.x;
            y = this.y;

            int src_i = 0, dst_i = 0, end_src_i;

            for (end_src_i = src_i + len; src_i != end_src_i; src_i++, dst_i++)
            {
                x = (x + 1) & 0xff;
                sx = state[x];
                y = (sx + y) & 0xff;
                state[x] = sy = state[y];
                state[y] = sx;
                dst[dst_i] = (byte)(src[src_i + offset] ^ state[(sx + sy) & 0xff]);
            }

            this.x = x;
            this.y = y;

            return dst;
        }
        public void SkipDecrypt(int len)
        {
            SkipEncrypt(len);
        }
        public void SkipEncrypt(int len)
        {
            uint x, y, sx, sy;
            x = this.x;
            y = this.y;

            int src_i = 0, dst_i = 0, end_src_i;

            for (end_src_i = src_i + len; src_i != end_src_i; src_i++, dst_i++)
            {
                x = (x + 1) & 0xff;
                sx = state[x];
                y = (sx + y) & 0xff;
                state[x] = sy = state[y];
                state[y] = sx;
            }

            this.x = x;
            this.y = y;
        }

        public byte[] Decrypt(byte[] src)
        {
            return Decrypt(src, src.Length);
        }
        public byte[] Decrypt(byte[] src, int len)
        {
            return Decrypt(src, 0, len);
        }
        public byte[] Decrypt(byte[] src, int offset, int len)
        {
            return Encrypt(src, offset, len);
        }
    }

    // 証明書関係
    delegate X509Certificate2 CertSelectorCallback(object param, string sni);
}
