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
using System.IO;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

using IPA.Cores.Helper.Basic;
using System.Threading;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

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

    static class ChaChaPoly
    {
        public const int AeadChaCha20Poly1305MacSize = 16;
        public const int AeadChaCha20Poly1305NonceSize = 12;
        public const int AeadChaCha20Poly1305KeySize = 32;

        static readonly byte[] zero15 = new byte[15];

        static bool crypto_aead_chacha20poly1305_ietf_decrypt_detached(Memory<byte> m, ReadOnlyMemory<byte> c, ReadOnlyMemory<byte> mac, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
        {
            var kk = k.AsSegment();
            var nn = npub.AsSegment();
            var cc = c.AsSegment();
            var aa = ad.AsSegment();
            var mm = m.AsSegment();

            byte[] block0 = new byte[64];

            ChaCha7539Engine ctx = new ChaCha7539Engine();
            ctx.Init(true, new ParametersWithIV(new KeyParameter(kk.Array, kk.Offset, kk.Count), nn.Array, nn.Offset, nn.Count));
            ctx.ProcessBytes(block0, 0, block0.Length, block0, 0);

            Poly1305 state = new Poly1305();
            state.Init(new KeyParameter(block0, 0, AeadChaCha20Poly1305KeySize));

            state.BlockUpdate(aa.Array, aa.Offset, aa.Count);
            if ((aa.Count % 16) != 0)
                state.BlockUpdate(zero15, 0, 16 - (aa.Count % 16));

            state.BlockUpdate(cc.Array, cc.Offset, cc.Count);
            if ((cc.Count % 16) != 0)
                state.BlockUpdate(zero15, 0, 16 - (cc.Count % 16));

            byte[] slen = BitConverter.GetBytes((ulong)aa.Count);
            if (Env.IsBigEndian) Array.Reverse(slen);
            state.BlockUpdate(slen, 0, slen.Length);

            byte[] mlen = BitConverter.GetBytes((ulong)cc.Count);
            if (Env.IsBigEndian) Array.Reverse(mlen);
            state.BlockUpdate(mlen, 0, mlen.Length);

            byte[] computed_mac = new byte[AeadChaCha20Poly1305MacSize];
            state.DoFinal(computed_mac, 0);

            if (computed_mac.AsSpan().SequenceEqual(mac.Span) == false)
            {
                return false;
            }

            ctx.ProcessBytes(cc.Array, cc.Offset, cc.Count, mm.Array, mm.Offset);

            return true;
        }

        static void crypto_aead_chacha20poly1305_ietf_encrypt_detached(Memory<byte> c, ReadOnlyMemory<byte> mac, ReadOnlyMemory<byte> m, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
        {
            var kk = k.AsSegment();
            var nn = npub.AsSegment();
            var cc = c.AsSegment();
            var aa = ad.AsSegment();
            var mm = m.AsSegment();

            byte[] block0 = new byte[64];

            ChaCha7539Engine ctx = new ChaCha7539Engine();
            ctx.Init(true, new ParametersWithIV(new KeyParameter(kk.Array, kk.Offset, kk.Count), nn.Array, nn.Offset, nn.Count));
            ctx.ProcessBytes(block0, 0, block0.Length, block0, 0);

            Poly1305 state = new Poly1305();
            state.Init(new KeyParameter(block0, 0, AeadChaCha20Poly1305KeySize));

            state.BlockUpdate(aa.Array, aa.Offset, aa.Count);
            if ((aa.Count % 16) != 0)
                state.BlockUpdate(zero15, 0, 16 - (aa.Count % 16));

            ctx.ProcessBytes(mm.Array, mm.Offset, mm.Count, cc.Array, cc.Offset);

            state.BlockUpdate(cc.Array, cc.Offset, cc.Count);
            if ((cc.Count % 16) != 0)
                state.BlockUpdate(zero15, 0, 16 - (cc.Count % 16));

            byte[] slen = BitConverter.GetBytes((ulong)aa.Count);
            if (Env.IsBigEndian) Array.Reverse(slen);
            state.BlockUpdate(slen, 0, slen.Length);

            byte[] mlen = BitConverter.GetBytes((ulong)mm.Count);
            if (Env.IsBigEndian) Array.Reverse(mlen);
            state.BlockUpdate(mlen, 0, mlen.Length);

            var macmac = mac.AsSegment();
            state.DoFinal(macmac.Array, macmac.Offset);
        }

        static void crypto_aead_chacha20poly1305_ietf_encrypt(Memory<byte> c, ReadOnlyMemory<byte> m, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
        {
            crypto_aead_chacha20poly1305_ietf_encrypt_detached(c.Slice(0, c.Length - AeadChaCha20Poly1305MacSize),
                c.Slice(c.Length - AeadChaCha20Poly1305MacSize, AeadChaCha20Poly1305MacSize),
                m, ad, npub, k);
        }

        static bool crypto_aead_chacha20poly1305_ietf_decrypt(Memory<byte> m, ReadOnlyMemory<byte> c, ReadOnlyMemory<byte> ad, ReadOnlyMemory<byte> npub, ReadOnlyMemory<byte> k)
        {
            //return crypto_aead_chacha20poly1305_ietf_decrypt_detached(m.Slice(0, c.Length - AeadChaCha20Poly1305MacSize), c.Slice(0, c.Length - AeadChaCha20Poly1305MacSize),
            //    c.Slice(c.Length - AeadChaCha20Poly1305MacSize, AeadChaCha20Poly1305MacSize),
            //    ad, npub, k);
            return crypto_aead_chacha20poly1305_ietf_decrypt_detached(m, c.Slice(0, c.Length - AeadChaCha20Poly1305MacSize),
                c.Slice(c.Length - AeadChaCha20Poly1305MacSize, AeadChaCha20Poly1305MacSize),
                ad, npub, k);
        }

        public static void Aead_ChaCha20Poly1305_Ietf_Encrypt(Memory<byte> dest, ReadOnlyMemory<byte> src, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> nonce, ReadOnlyMemory<byte> aad)
        {
            crypto_aead_chacha20poly1305_ietf_encrypt(dest, src, aad, nonce, key);
        }

        public static bool Aead_ChaCha20Poly1305_Ietf_Decrypt(Memory<byte> dest, ReadOnlyMemory<byte> src, ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> nonce, ReadOnlyMemory<byte> aad)
        {
            return crypto_aead_chacha20poly1305_ietf_decrypt(dest, src, aad, nonce, key);
        }

        public static void Aead_ChaCha20Poly1305_Ietf_Test()
        {
            string nonce_hex = "07 00 00 00 40 41 42 43 44 45 46 47";
            string plaintext_hex =
                "4c 61 64 69 65 73 20 61 6e 64 20 47 65 6e 74 6c " +
                "65 6d 65 6e 20 6f 66 20 74 68 65 20 63 6c 61 73 " +
                "73 20 6f 66 20 27 39 39 3a 20 49 66 20 49 20 63 " +
                "6f 75 6c 64 20 6f 66 66 65 72 20 79 6f 75 20 6f " +
                "6e 6c 79 20 6f 6e 65 20 74 69 70 20 66 6f 72 20 " +
                "74 68 65 20 66 75 74 75 72 65 2c 20 73 75 6e 73 " +
                "63 72 65 65 6e 20 77 6f 75 6c 64 20 62 65 20 69 " +
                "74 2e";
            string aad_hex = "50 51 52 53 c0 c1 c2 c3 c4 c5 c6 c7";
            string key_hex = "80 81 82 83 84 85 86 87 88 89 8a 8b 8c 8d 8e 8f " +
                "90 91 92 93 94 95 96 97 98 99 9a 9b 9c 9d 9e 9f";

            string rfc_mac = "1a:e1:0b:59:4f:09:e2:6a:7e:90:2e:cb:d0:60:06:91".Replace(':', ' ');
            string rfc_enc = "d3 1a 8d 34 64 8e 60 db 7b 86 af bc 53 ef 7e c2 " +
                "a4 ad ed 51 29 6e 08 fe a9 e2 b5 a7 36 ee 62 d6 " +
                "3d be a4 5e 8c a9 67 12 82 fa fb 69 da 92 72 8b " +
                "1a 71 de 0a 9e 06 0b 29 05 d6 a5 b6 7e cd 3b 36 " +
                "92 dd bd 7f 2d 77 8b 8c 98 03 ae e3 28 09 1b 58 " +
                "fa b3 24 e4 fa d6 75 94 55 85 80 8b 48 31 d7 bc " +
                "3f f4 de f0 8e 4b 7a 9d e5 76 d2 65 86 ce c6 4b " +
                "61 16";

            var nonce = nonce_hex.GetHexBytes().AsMemory();
            var plaintext = plaintext_hex.GetHexBytes().AsMemory();
            var aad = aad_hex.GetHexBytes().AsMemory();
            var key = key_hex.GetHexBytes().AsMemory();
            var encrypted = new byte[plaintext.Length + AeadChaCha20Poly1305MacSize].AsMemory();
            var decrypted = new byte[plaintext.Length].AsMemory();

            Console.WriteLine("Aead_ChaCha20Poly1305_Ietf_Test()");

            Aead_ChaCha20Poly1305_Ietf_Encrypt(encrypted, plaintext, key, nonce, aad);

            string encrypted_hex = encrypted.Slice(0, plaintext.Length).ToArray().GetHexString(" ");
            string mac_hex = encrypted.Slice(plaintext.Length, AeadChaCha20Poly1305MacSize).ToArray().GetHexString(" ");

            Console.WriteLine($"Encrypted:\n{encrypted_hex}\n");

            Console.WriteLine($"MAC:\n{mac_hex}\n");

            var a = rfc_enc.GetHexBytes();
            if (encrypted.Slice(0, plaintext.Length).Span.SequenceEqual(a) == false)
            {
                throw new ApplicationException("encrypted != rfc_enc");
            }

            Console.WriteLine("Check OK.");

            if (Aead_ChaCha20Poly1305_Ietf_Decrypt(decrypted, encrypted, key, nonce, aad) == false)
            {
                throw new ApplicationException("Decrypt failed.");
            }
            else
            {
                Console.WriteLine("Decrypt OK.");

                if (plaintext.Span.SequenceEqual(decrypted.Span))
                {
                    Console.WriteLine("Same OK.");
                }
                else
                {
                    throw new ApplicationException("Different !!!");
                }
            }
        }
    }

}


