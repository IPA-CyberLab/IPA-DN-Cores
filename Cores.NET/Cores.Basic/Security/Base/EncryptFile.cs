// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Encrypt File Utility


// Some parts from "xtssharp"
// https://bitbucket.org/garethl/xtssharp/src/0e6a81a823e98659b7b7c22b607fc2e2070a0710/
// 
// Copyright (c) 2010 Gareth Lennox (garethl@dwakn.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
//
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright notice,
//       this list of conditions and the following disclaimer in the documentation
//       and/or other materials provided with the distribution.
//     * Neither the name of Gareth Lennox nor the names of its
//       contributors may be used to endorse or promote products derived from this
//       software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
// THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Security.Cryptography;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Basic.Internal;

namespace IPA.Cores.Basic
{
    namespace Internal
    {
        /// <summary>
        /// XTS-AES-128 implementation
        /// </summary>
        public class XtsAes128 : Xts
        {
            private const int KEY_LENGTH = 128;
            private const int KEY_BYTE_LENGTH = KEY_LENGTH / 8;

            /// <summary>
            /// Creates a new instance
            /// </summary>
            protected XtsAes128(Func<SymmetricAlgorithm> create, byte[] key1, byte[] key2)
                : base(create, VerifyKey(KEY_LENGTH, key1), VerifyKey(KEY_LENGTH, key2))
            {
            }

            /// <summary>
            /// Creates a new implementation
            /// </summary>
            /// <param name="key1">First key</param>
            /// <param name="key2">Second key</param>
            /// <returns>Xts implementation</returns>
            /// <remarks>Keys need to be 128 bits long (i.e. 16 bytes)</remarks>
            public static Xts Create(byte[] key1, byte[] key2)
            {
                VerifyKey(KEY_LENGTH, key1);
                VerifyKey(KEY_LENGTH, key2);

                return new XtsAes128(AesManaged.Create, key1, key2);
            }

            /// <summary>
            /// Creates a new implementation
            /// </summary>
            /// <param name="key">Key to use</param>
            /// <returns>Xts implementation</returns>
            /// <remarks>Key need to be 256 bits long (i.e. 32 bytes)</remarks>
            public static Xts Create(byte[] key)
            {
                VerifyKey(KEY_LENGTH * 2, key);

                byte[] key1 = new byte[KEY_BYTE_LENGTH];
                byte[] key2 = new byte[KEY_BYTE_LENGTH];

                Buffer.BlockCopy(key, 0, key1, 0, KEY_BYTE_LENGTH);
                Buffer.BlockCopy(key, KEY_BYTE_LENGTH, key2, 0, KEY_BYTE_LENGTH);

                return new XtsAes128(AesManaged.Create, key1, key2);
            }
        }

        /// <summary>
        /// XTS-AES-256 implementation
        /// </summary>
        public class XtsAes256 : Xts
        {
            private const int KEY_LENGTH = 256;
            private const int KEY_BYTE_LENGTH = KEY_LENGTH / 8;

            /// <summary>
            /// Creates a new instance
            /// </summary>
            protected XtsAes256(Func<SymmetricAlgorithm> create, byte[] key1, byte[] key2)
                : base(create, VerifyKey(KEY_LENGTH, key1), VerifyKey(KEY_LENGTH, key2))
            {
            }

            /// <summary>
            /// Creates a new implementation
            /// </summary>
            /// <param name="key1">First key</param>
            /// <param name="key2">Second key</param>
            /// <returns>Xts implementation</returns>
            /// <remarks>Keys need to be 256 bits long (i.e. 32 bytes)</remarks>
            public static Xts Create(byte[] key1, byte[] key2)
            {
                VerifyKey(KEY_LENGTH, key1);
                VerifyKey(KEY_LENGTH, key2);
                
                return new XtsAes256(AesManaged.Create, key1, key2);
            }

            /// <summary>
            /// Creates a new implementation
            /// </summary>
            /// <param name="key">Key to use</param>
            /// <returns>Xts implementation</returns>
            /// <remarks>Keys need to be 512 bits long (i.e. 64 bytes)</remarks>
            public static Xts Create(byte[] key)
            {
                VerifyKey(KEY_LENGTH * 2, key);

                var key1 = new byte[KEY_BYTE_LENGTH];
                var key2 = new byte[KEY_BYTE_LENGTH];

                Buffer.BlockCopy(key, 0, key1, 0, KEY_BYTE_LENGTH);
                Buffer.BlockCopy(key, KEY_BYTE_LENGTH, key2, 0, KEY_BYTE_LENGTH);

                return new XtsAes256(AesManaged.Create, key1, key2);
            }
        }



        /// <summary>
        /// Xts. See <see cref="XtsAes128"/> and <see cref="XtsAes256"/>.
        /// </summary>
        public class Xts
        {
            private readonly SymmetricAlgorithm _key1;
            private readonly SymmetricAlgorithm _key2;

            /// <summary>
            /// Creates a new Xts implementation.
            /// </summary>
            /// <param name="create">Function to create the implementations</param>
            /// <param name="key1">Key 1</param>
            /// <param name="key2">Key 2</param>
            protected Xts(Func<SymmetricAlgorithm> create, byte[] key1, byte[] key2)
            {
                if (create == null)
                    throw new ArgumentNullException("create");
                if (key1 == null)
                    throw new ArgumentNullException("key1");
                if (key2 == null)
                    throw new ArgumentNullException("key2");

                _key1 = create();
                _key2 = create();

                if (key1.Length != key2.Length)
                    throw new ArgumentException("Key lengths don't match");

                //set the key sizes
                _key1.KeySize = key1.Length * 8;
                _key2.KeySize = key2.Length * 8;

                //set the keys
                _key1.Key = key1;
                _key2.Key = key2;

                //ecb mode
                _key1.Mode = CipherMode.ECB;
                _key2.Mode = CipherMode.ECB;

                //no padding - we're always going to be writing full blocks
                _key1.Padding = PaddingMode.None;
                _key2.Padding = PaddingMode.None;

                //fixed block size of 128 bits.
                _key1.BlockSize = 16 * 8;
                _key2.BlockSize = 16 * 8;
            }

            /// <summary>
            /// Creates an xts encryptor
            /// </summary>
            public XtsCryptoTransform CreateEncryptor()
            {
                return new XtsCryptoTransform(_key1.CreateEncryptor(), _key2.CreateEncryptor(), false);
            }

            /// <summary>
            /// Creates an xts decryptor
            /// </summary>
            public XtsCryptoTransform CreateDecryptor()
            {
                return new XtsCryptoTransform(_key1.CreateDecryptor(), _key2.CreateEncryptor(), true);
            }

            /// <summary>
            /// Verify that the key is of an expected size of bits
            /// </summary>
            /// <param name="expectedSize">Expected size of the key in bits</param>
            /// <param name="key">The key</param>
            /// <returns>The key</returns>
            /// <exception cref="ArgumentNullException">If the key is null</exception>
            /// <exception cref="ArgumentException">If the key length does not match the expected length</exception>
            protected static byte[] VerifyKey(int expectedSize, byte[] key)
            {
                if (key == null)
                    throw new ArgumentNullException("key");

                if (key.Length * 8 != expectedSize)
                    throw new ArgumentException(string.Format("Expected key length of {0} bits, got {1}", expectedSize, key.Length * 8));

                return key;
            }
        }

        /// <summary>
        /// The actual Xts cryptography transform
        /// </summary>
        /// <remarks>
        /// The reason that it doesn't implement ICryptoTransform, as the interface is different.
        /// 
        /// Most of the logic was taken from the LibTomCrypt project - http://libtom.org and 
        /// converted to C#
        /// </remarks>
#pragma warning disable CA1063 // Implement IDisposable Correctly
        public class XtsCryptoTransform : IDisposable
#pragma warning restore CA1063 // Implement IDisposable Correctly
        {
            private readonly byte[] _cc = new byte[16];
            private readonly bool _decrypting;
            private readonly ICryptoTransform _key1;
            private readonly ICryptoTransform _key2;

            private readonly byte[] _pp = new byte[16];
            private readonly byte[] _t = new byte[16];
            private readonly byte[] _tweak = new byte[16];

            /// <summary>
            /// Creates a new transform
            /// </summary>
            /// <param name="key1">Transform 1</param>
            /// <param name="key2">Transform 2</param>
            /// <param name="decrypting">Is this a decryption transform?</param>
            public XtsCryptoTransform(ICryptoTransform key1, ICryptoTransform key2, bool decrypting)
            {
                if (key1 == null)
                    throw new ArgumentNullException("key1");

                if (key2 == null)
                    throw new ArgumentNullException("key2");

                _key1 = key1;
                _key2 = key2;
                _decrypting = decrypting;
            }

            #region IDisposable Members

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            /// <filterpriority>2</filterpriority>
#pragma warning disable CA1063 // Implement IDisposable Correctly
            public void Dispose()
#pragma warning restore CA1063 // Implement IDisposable Correctly
            {
                _key1.Dispose();
                _key2.Dispose();
            }

            #endregion

            /// <summary>
            /// Transforms a single block.
            /// </summary>
            /// <param name="inputBuffer"> The input for which to compute the transform.</param>
            /// <param name="inputOffset">The offset into the input byte array from which to begin using data.</param>
            /// <param name="inputCount">The number of bytes in the input byte array to use as data.</param>
            /// <param name="outputBuffer">The output to which to write the transform.</param>
            /// <param name="outputOffset">The offset into the output byte array from which to begin writing data.</param>
            /// <param name="sector">The sector number of the block</param>
            /// <returns>The number of bytes written.</returns>
            public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset, ulong sector)
            {
                FillArrayFromSector(_tweak, sector);

                int lim;

                /* get number of blocks */
                var m = inputCount >> 4;
                var mo = inputCount & 15;

                /* encrypt the tweak */
                _key2.TransformBlock(_tweak, 0, _tweak.Length, _t, 0);

                /* for i = 0 to m-2 do */
                if (mo == 0)
                    lim = m;
                else
                    lim = m - 1;

                for (var i = 0; i < lim; i++)
                {
                    TweakCrypt(inputBuffer, inputOffset, outputBuffer, outputOffset, _t);
                    inputOffset += 16;
                    outputOffset += 16;
                }

                /* if ptlen not divide 16 then */
                if (mo > 0)
                {
                    if (_decrypting)
                    {
                        Buffer.BlockCopy(_t, 0, _cc, 0, 16);
                        MultiplyByX(_cc);

                        /* CC = tweak encrypt block m-1 */
                        TweakCrypt(inputBuffer, inputOffset, _pp, 0, _cc);

                        /* Cm = first ptlen % 16 bytes of CC */
                        int i;
                        for (i = 0; i < mo; i++)
                        {
                            _cc[i] = inputBuffer[16 + i + inputOffset];
                            outputBuffer[16 + i + outputOffset] = _pp[i];
                        }

                        for (; i < 16; i++)
                        {
                            _cc[i] = _pp[i];
                        }

                        /* Cm-1 = Tweak encrypt PP */
                        TweakCrypt(_cc, 0, outputBuffer, outputOffset, _t);
                    }
                    else
                    {
                        /* CC = tweak encrypt block m-1 */
                        TweakCrypt(inputBuffer, inputOffset, _cc, 0, _t);

                        /* Cm = first ptlen % 16 bytes of CC */
                        int i;
                        for (i = 0; i < mo; i++)
                        {
                            _pp[i] = inputBuffer[16 + i + inputOffset];
                            outputBuffer[16 + i + outputOffset] = _cc[i];
                        }

                        for (; i < 16; i++)
                        {
                            _pp[i] = _cc[i];
                        }

                        /* Cm-1 = Tweak encrypt PP */
                        TweakCrypt(_pp, 0, outputBuffer, outputOffset, _t);
                    }
                }

                return inputCount;
            }

            /// <summary>
            /// Fills a byte array from a sector number
            /// </summary>
            /// <param name="value">The destination</param>
            /// <param name="sector">The sector number</param>
            private static void FillArrayFromSector(byte[] value, ulong sector)
            {
                value[7] = (byte)((sector >> 56) & 255);
                value[6] = (byte)((sector >> 48) & 255);
                value[5] = (byte)((sector >> 40) & 255);
                value[4] = (byte)((sector >> 32) & 255);
                value[3] = (byte)((sector >> 24) & 255);
                value[2] = (byte)((sector >> 16) & 255);
                value[1] = (byte)((sector >> 8) & 255);
                value[0] = (byte)(sector & 255);
            }

            /// <summary>
            /// Performs the Xts TweakCrypt operation
            /// </summary>
            private void TweakCrypt(byte[] inputBuffer, int inputOffset, byte[] outputBuffer, int outputOffset, byte[] t)
            {
                for (var x = 0; x < 16; x++)
                {
                    outputBuffer[x + outputOffset] = (byte)(inputBuffer[x + inputOffset] ^ t[x]);
                }

                _key1.TransformBlock(outputBuffer, outputOffset, 16, outputBuffer, outputOffset);

                for (var x = 0; x < 16; x++)
                {
                    outputBuffer[x + outputOffset] = (byte)(outputBuffer[x + outputOffset] ^ t[x]);
                }

                MultiplyByX(t);
            }

            /// <summary>
            /// Multiply by x
            /// </summary>
            /// <param name="i">The value to multiply by x (LFSR shift)</param>
            private static void MultiplyByX(byte[] i)
            {
                byte t = 0, tt = 0;

                for (var x = 0; x < 16; x++)
                {
                    tt = (byte)(i[x] >> 7);
                    i[x] = (byte)(((i[x] << 1) | t) & 0xFF);
                    t = tt;
                }

                if (tt > 0)
                    i[0] ^= 0x87;
            }
        }
    }
}

#endif

