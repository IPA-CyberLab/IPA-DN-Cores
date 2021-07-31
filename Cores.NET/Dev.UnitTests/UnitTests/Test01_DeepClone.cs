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
// Unit Test #1

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

using Xunit;
using Xunit.Abstractions;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.UnitTest
{
    public class Test01_DeepClone : IClassFixture<CoresLibUnitTestFixtureInstance>
    {
        private readonly ITestOutputHelper Con;
        void Where([CallerFilePath] string fn = "", [CallerLineNumber] int l = 0, [CallerMemberName] string? f = null) => Con.WriteLine($"|{UnitTestTicks.TickString}: {Path.GetFileName(fn)}:{l} {f}() P: {Process.GetCurrentProcess().Id} T: {Thread.CurrentThread.ManagedThreadId}");

        public Test01_DeepClone(ITestOutputHelper output)
        {
            CoresLibUnitTestShared.Init();

            Con = output;
        }

        public static volatile int DeepClone_ConstructorCount;

        [Serializable]
        public class ElementClass
        {
            public ElementClass()
            {
                DeepClone_ConstructorCount++;
            }

            public int Int1;
            public string? Str1;

            private string? Str2 = null;
            private int Int2 = 0;

            public RootClass? Root;

            public void Dummy()
            {
                Limbo.ObjectVolatileSlow = Str2;
                Limbo.ObjectVolatileSlow = Int2;
            }
        }

        [Serializable]
        public class RootClass
        {
            public RootClass()
            {
                DeepClone_ConstructorCount++;
            }

            public string? Str1;
            public int Int1;

            private string? Str2 = null;
            private int Int2 = 0;

            public Dictionary<int, ElementClass>? Dict;

            public void Dummy()
            {
                Limbo.ObjectVolatileSlow = Str2;
                Limbo.ObjectVolatileSlow = Int2;
            }
        }

        [Theory]
        [InlineData(DeepCloneMethod.BinaryFormatter)]
        [InlineData(DeepCloneMethod.DeepCloner)]
        [InlineData(DeepCloneMethod.Default)]
        public void DeepCloneTest(DeepCloneMethod method)
        {
            Where();

            DeepClone_ConstructorCount = 0;

            RootClass a = new RootClass();

            a.Int1 = Util.RandSInt31();
            a.Str1 = a.Int1.ToString();

            int tmp = Util.RandSInt31();
            a._GetFieldReaderWriter(true).SetValue(a, "Int2", tmp);
            a._GetFieldReaderWriter(true).SetValue(a, "Str2", tmp.ToString());

            a.Dict = new Dictionary<int, ElementClass>();

            for (int i = 0; i < 1000; i++)
            {
                ElementClass e = new ElementClass();
                e.Int1 = Util.RandSInt31();
                e.Str1 = e.Int1.ToString();

                e._GetFieldReaderWriter(true).SetValue(e, "Int2", tmp);
                e._GetFieldReaderWriter(true).SetValue(e, "Str2", tmp.ToString());

                e.Root = a;

                a.Dict.Add(i, e);
            }

            DeepClone_ConstructorCount = 0;

            RootClass b = a._CloneDeep(method);

            Assert.Equal(0, DeepClone_ConstructorCount);
            Assert.False(object.ReferenceEquals(a, b));
            Assert.Equal(a.Dict.Count, b.Dict!.Count);

            for (int i = 0; i < a.Dict.Count; i++)
            {
                ElementClass e1 = a.Dict[i];
                ElementClass e2 = b.Dict[i];

                Assert.False(object.ReferenceEquals(e1, e2));
                Assert.Equal(e1.Int1, e2.Int1);
                Assert.Equal(e1.Str1, e2.Str1);
                Assert.True(e1.Str1._IsFilled());

                int e1_int2 = (int)e1._GetFieldReaderWriter(true).GetValue(e1, "Int2")!;
                string e1_str2 = (string)e1._GetFieldReaderWriter(true).GetValue(e1, "Str2")!;

                int e2_int2 = (int)e2._GetFieldReaderWriter(true).GetValue(e2, "Int2")!;
                string e2_str2 = (string)e2._GetFieldReaderWriter(true).GetValue(e2, "Str2")!;

                Assert.Equal(e1_int2, e2_int2);
                Assert.Equal(e1_str2, e2_str2);
                Assert.True(e1_str2._IsFilled());

                Assert.True(object.ReferenceEquals(e1.Root, a));
                Assert.True(object.ReferenceEquals(e2.Root, b));
            }

            Where();
        }
    }
}



