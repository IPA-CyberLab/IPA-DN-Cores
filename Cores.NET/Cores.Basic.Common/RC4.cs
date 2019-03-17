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

namespace IPA.Cores.Basic
{
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
}
