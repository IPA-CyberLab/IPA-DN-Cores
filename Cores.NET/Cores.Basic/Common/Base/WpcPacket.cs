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
// Description

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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public class WpcItemList : List<WpcItem>
    {
        public static WpcItemList Parse(string str)
            => Parse(str._GetBytes_Ascii()._AsReadOnlyMemoryBuffer());

        public static WpcItemList Parse(IBuffer<byte> buf)
        {
            WpcItemList ret = new WpcItemList();

            while (true)
            {
                if (WpcItem.Parse(buf, out string name, out ReadOnlyMemory<byte> data) == false)
                {
                    break;
                }

                ret.Add(name, data);
            }

            return ret;
        }

        public WpcItem Find(string name, int index = 0)
        {
            return this.Where(x => x.Name._IsSamei(name) && x.Index == index).Single();
        }

        public void Add(string name, ReadOnlyMemory<byte> data)
        {
            name = name.ToUpper();

            int index = this.Where(x => x.Name._IsSamei(name)).Count();

            this.Add(new WpcItem(name, index, data));
        }

        public void Emit(IBuffer<byte> buf)
        {
            this._DoForEach(x => x.Emit(buf));
        }

        public MemoryBuffer<byte> ToPacketBinary()
        {
            MemoryBuffer<byte> ret = new MemoryBuffer<byte>();

            this.Emit(ret);

            return ret;
        }

        public string ToPacketString()
            => this.ToPacketBinary().Span._GetString_Ascii();
    }

    public class WpcItem
    {
        public string Name { get; }
        public int Index { get; }

        public ReadOnlyMemory<byte> Data { get; }

        public WpcItem(string name, int index, ReadOnlyMemory<byte> data)
        {
            if (name.Length != 4) throw new ArgumentException("name.Length != 4");
            this.Name = name.ToUpper();
            this.Index = index;
            this.Data = data;
        }

        public static bool Parse(IBuffer<byte> buf, out string name, out ReadOnlyMemory<byte> data)
        {
            name = "";
            data = default;

            var nameBuf = buf.Read(4, allowPartial: true);
            if (nameBuf.Length != 4) return false;
            name = nameBuf._GetString_Ascii().ToUpper();

            var sizeStrBuf = buf.Read(10, allowPartial: true);
            if (sizeStrBuf.Length != 10) return false;

            int size = sizeStrBuf._GetString_Ascii()._ToInt();
            size = Math.Max(size, 0);
            if (size > Pack.MaxPackSize) throw new CoresException($"size ({size}) > Pack.MaxPackSize ({Pack.MaxPackSize})");

            var dataBuf = buf.Read(size, allowPartial: true);
            if (dataBuf.Length != size) return false;
            string dataStr = dataBuf._GetString_Ascii();
            data = Str.Base64Decode(Str.Safe64ToBase64(dataStr));

            return true;
        }

        public void Emit(IBuffer<byte> buf)
        {
            var nameBuf = this.Name.ToUpper()._GetBytes_Ascii();
            if (nameBuf.Length != 4) throw new CoresLibException("nameBuf.Length != 4");

            string dataStr = Str.Base64ToSafe64(Str.Base64Encode(this.Data));
            var dataBuf = dataStr._GetBytes_Ascii();

            int dataSize = dataBuf.Length;

            string dataSizeStr = dataSize.ToString("0000000000");
            var dataSizeStrData = dataSizeStr._GetBytes_Ascii();

            buf.Write(nameBuf);
            buf.Write(dataSizeStrData);
            buf.Write(dataBuf);
        }
    }
}

#endif

