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

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;

#pragma warning disable CS0219
#pragma warning disable CS0162


namespace IPA.TestDev
{
    static class TestClass
    {
        public static void Test()
        {
            var rfs = Res.Cores;
            {
                rfs.EnumDirectory("/", true).Where(x => x.IsDirectory == false).Select(x => x).PrintAsJson();

                string str = rfs.ReadStringFromFile(rfs.FindSingleFile("nek"));

                Con.WriteLine($"'{str}'");

                rfs.DeleteFile("/TestDev.Resource.Test.HelloWorld.txt");

                rfs.EnumDirectory("/", true).Where(x => x.IsDirectory == false).Select(x => x).PrintAsJson();
            }
            return;

            using (VirtualFileSystem vfs = new VirtualFileSystem(LeakChecker.SuperGrandLady, new VirtualFileSystemParams()))
            {
                vfs.CreateDirectory("/a");
                vfs.CreateDirectory("/b");
                vfs.CreateDirectory("/c");
                vfs.CreateDirectory("/a/a");
                vfs.CreateDirectory("/1/2/3");
                vfs.CreateDirectory("/1/2/4");
                vfs.CreateDirectory("/1/2/5");
                vfs.CreateDirectory("/z/4/1/2/3/4/5/6/7/7/8/9");
                vfs.DeleteDirectory("/a/a");
                //vfs.DeleteDirectory("/z", false);

                vfs.WriteDataToFile("/readme.txt", "Hello".GetBytes_Ascii());
                vfs.WriteDataToFile("/readme.txt", "a".GetBytes_Ascii());

                var span = vfs.ReadDataFromFile("/readme.txt");
                Con.WriteLine(span.GetString_Ascii());


                vfs.GetFileMetadata("/readme.txt").PrintAsJson();

                //vfs.DeleteFile("/readme.txt");

                vfs.EnumDirectory("/", true).Where(x => x.IsDirectory==false).Select(x=>x).PrintAsJson();
            }
        }
    }
}


