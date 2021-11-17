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

namespace IPA.UnitTest;

public class Test02_Base : IClassFixture<CoresLibUnitTestFixtureInstance>
{
    private readonly ITestOutputHelper Con;
    void Where([CallerFilePath] string fn = "", [CallerLineNumber] int l = 0, [CallerMemberName] string? f = null) => Con.WriteLine($"|{UnitTestTicks.TickString}: {Path.GetFileName(fn)}:{l} {f}() P: {Process.GetCurrentProcess().Id} T: {Thread.CurrentThread.ManagedThreadId}");

    public Test02_Base(ITestOutputHelper output)
    {
        CoresLibUnitTestShared.Init();

        Con = output;
    }

    [Fact]
    public void DotNetHostedProcessEnvInfoTest()
    {
        if (Env.IsUnix)
        {
            Assert.True(Env.IsHostedByDotNetProcess);
            Assert.True(Env.DotNetHostProcessExeName._IsFilled());
        }

        Assert.True(Env.IsDotNetCore);
    }

    [Fact]
    public void DotNetUnixConsoleModeChangePreventTest()
    {
        if (Env.IsUnix)
        {
            UnixConsoleSpecialUtil.Test_DisableDotNetConsoleModeChange();
        }
    }

    [Fact]
    public void MemoryHelperTest()
    {
        Where();

        LeakChecker.Enter();

        Assert.True(MemoryHelper._UseFast);

        string rand = Str.GenRandStr();

        Memory<byte> mem1 = rand._GetBytes_Ascii();

        var seg1 = mem1._AsSegment();
        var data1 = seg1.ToArray();
        Assert.Equal(0, mem1.Span.SequenceCompareTo(data1));

        var seg1Slow = mem1._AsSegmentSlow();
        var data1Slow = seg1Slow.ToArray();
        Assert.Equal(0, mem1.Span.SequenceCompareTo(data1Slow));

        ReadOnlyMemory<byte> mem2 = rand._GetBytes_Ascii();

        var seg2 = mem2._AsSegment();
        var data2 = seg2.ToArray();
        Assert.Equal(0, mem2.Span.SequenceCompareTo(data2));

        var seg2Slow = mem2._AsSegmentSlow();
        var data2Slow = seg2Slow.ToArray();
        Assert.Equal(0, mem2.Span.SequenceCompareTo(data2Slow));

        Where();
    }

    [Fact]
    public void InternalTaskStructureAccessd()
    {
        Where();

        int num_queued = TaskUtil.GetQueuedTasksCount(-1);
        Assert.True(num_queued >= 0);

        int num_timered = TaskUtil.GetScheduledTimersCount(-1);
        Assert.True(num_timered >= 0);

        Where();
    }

    [Fact]
    public void StrLibTest()
    {
        Assert.False(""._IsNumber());
        Assert.False("A"._IsNumber());
        Assert.True("1"._IsNumber());
        Assert.True("-1"._IsNumber());
        Assert.True("１"._IsNumber());

        var hostAndPort = Str.ParseHostnaneAndPort("abc", 80);
        Assert.Equal("abc", hostAndPort.Item1);
        Assert.Equal(80, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort(" 1.2.3.4 :9821", 80);
        Assert.Equal("1.2.3.4", hostAndPort.Item1);
        Assert.Equal(9821, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort("2001:1:2:3::4:9821", 80);
        Assert.Equal("2001:1:2:3::4", hostAndPort.Item1);
        Assert.Equal(9821, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort("2001:1:2:3::4:fffe", 80);
        Assert.Equal("2001:1:2:3::4:fffe", hostAndPort.Item1);
        Assert.Equal(80, hostAndPort.Item2);

        hostAndPort = Str.ParseHostnaneAndPort("", 80);
        Assert.Equal("", hostAndPort.Item1);
        Assert.Equal(80, hostAndPort.Item2);
    }

    [Fact]
    public void SslLibTest()
    {
        Secure.CreateSslCreateCertificateContextWithFullChain(
            DevTools.TestSampleCert,
            new System.Security.Cryptography.X509Certificates.X509Certificate2Collection(DevTools.CoresDebugCACert.NativeCertificate2._SingleArray()),
            offline: true,
            errorWhenFailed: true);
    }
}



