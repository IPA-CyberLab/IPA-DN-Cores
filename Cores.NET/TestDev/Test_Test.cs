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
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

#pragma warning disable CS0219
#pragma warning disable CS0162


namespace IPA.TestDev
{
    [Serializable]
    [DataContract]
    class TestData
    {
        [DataMember]
        public int A;
        [DataMember]
        public string B;
        [DataMember]
        public int C;
    }

    static class TestClass
    {
        public static void Test()
        {
            MemoryBuffer<byte> mem2 = new MemoryBuffer<byte>();
            mem2.Write("ABC".GetBytes_Ascii());
            mem2.WriteZero(4);
            mem2.Write("DEF".GetBytes_Ascii());
            var x = Util.GetSparseChunks(mem2.Memory, 2);



            var f = Lfs.Create(@"d:\tmp\190506\sparse1.txt", flags: FileOperationFlags.AutoCreateDirectory | FileOperationFlags.SparseFile);

            MemoryBuffer<byte> mem = new MemoryBuffer<byte>();
            mem.Write("\r\n\r\nHello World\r\n\r\n".GetBytes_Ascii());
            mem.WriteZero(10_000_000);
            mem.Write("\r\n\r\nHello World\r\n\r\n".GetBytes_Ascii());
            mem.WriteZero(10_000_000);
            //mem.Write("\r\n\r\nHello World 2\r\n\r\n".GetBytes_Ascii());
            f.Write(mem);


            f.Close();
        }
    }
}


