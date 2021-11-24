// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.TestDev;

partial class TestDevCommands
{
    [ConsoleCommand(
        "Test command",
        "Test [arg]",
        "This is a test command.",
        "[arg]:You can specify an argument.")]
    static int Test(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[arg]", null, null, null, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        TestClass.Test();

        return 0;
    }


    [ConsoleCommand(
        "Execute SSL Test Suite",
        "SslTestSuite [host:port] [/parallel:num=1] [/interval:msecs=0] [/ignore:ignore_list]",
        "Execute SSL Test Suite")]
    static int SslTestSuite(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[host:port]", ConsoleService.Prompt, "<Host:port>: ", ConsoleService.EvalNotEmpty, null),
            new ConsoleParam("parallel"),
            new ConsoleParam("interval"),
            new ConsoleParam("ignore"),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string hostAndPort = vl.DefaultParam.StrValue;
        int parallel = vl["parallel"].IntValue;
        int interval = vl["interval"].IntValue;
        string ignList = vl["ignore"].StrValue;

        bool ret = false;

        Async(async () =>
        {
            ret = await LtsOpenSslTool.TestSuiteAsync(hostAndPort, parallel, interval, ignList);
        });

        if (ret == false)
        {
            Con.WriteLine();
            Con.WriteLine("Error occured.");
            return -1;
        }

        return 0;
    }

    [ConsoleCommand(
    "Normalize Src Code Text with BOM and UTF-8",
    "NormalizeSrcCodeText [dir]",
    "Normalize Src Code Text with BOM and UTF-8",
    "[dir]: Dir name")]
    static int NormalizeSrcCodeText(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
            new ConsoleParam("[dir]", ConsoleService.Prompt, "Directory name: ", ConsoleService.EvalNotEmpty, null),
        };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dir = vl.DefaultParam.StrValue;

        DirectoryWalker walk = new DirectoryWalker();

        walk.WalkDirectory(dir,
            (pathInfo, entities, cancel) =>
            {
                foreach (FileSystemEntity entity in entities)
                {
                    if (entity.IsDirectory == false && entity.Name._MultipleWildcardMatch(Consts.Extensions.Wildcard_SourceCode_NormalizeBomUtf8_Include, Consts.Extensions.Wildcard_SourceCode_NormalizeBomUtf8_Exclude))
                    {
                        var body = pathInfo.FileSystem.ReadDataFromFile(entity.FullPath);

                        string str = body._GetString();

                        str = str._NormalizeCrlf(CrlfStyle.CrLf);

                        pathInfo.FileSystem.WriteStringToFile(entity.FullPath, str, FileFlags.WriteOnlyIfChanged, encoding: Str.Utf8Encoding, writeBom: true);
                    }
                }
                return true;
            },
            exceptionHandler: (pathInfo, err, cancel) =>
            {
                return true;
            });

        return 0;
    }

    [ConsoleCommand(
        "IPA NTT East Source Code Header Setter",
        "IpaNttSrcHeaderSet [dir]",
        "IPA NTT East Source Code Header Setter.",
        "[dir]: Dir name")]
    static int IpaNttSrcHeaderSet(ConsoleService c, string cmdName, string str)
    {
        ConsoleParam[] args =
        {
                new ConsoleParam("[dir]", ConsoleService.Prompt, "Directory name: ", ConsoleService.EvalNotEmpty, null),
            };

        ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

        string dir = vl.DefaultParam.StrValue;

        DirectoryWalker walk = new DirectoryWalker();

        string existingCommentLine = "// Copyright (c) IPA CyberLab of Industrial Cyber Security Center.";
        string existingCommentLineOld = "// Copyright (c) NTT East Special Affairs Bureau.";
        string newCommentLine = "// Copyright (c) NTT-East Impossible Telecom Mission Group.";

        string old1 = "// AGAINST US (IPA CYBERLAB, SOFTETHER PROJECT, SOFTETHER CORPORATION, DAIYUU NOBORI";
        string new1 = "// AGAINST US (IPA, NTT-EAST, SOFTETHER PROJECT, SOFTETHER CORPORATION, DAIYUU NOBORI";

        string old2 = "// COMPLETELY AT YOUR OWN RISK. THE IPA CYBERLAB HAS DEVELOPED AND";
        string new2 = "// COMPLETELY AT YOUR OWN RISK. IPA AND NTT-EAST HAS DEVELOPED AND";

        walk.WalkDirectory(dir,
            (pathInfo, entities, cancel) =>
            {
                foreach (FileSystemEntity entity in entities)
                {
                    if (entity.IsDirectory == false && entity.Name._IsExtensionMatch(Consts.Extensions.Filter_SourceCodes))
                    {
                        var body = pathInfo.FileSystem.ReadDataFromFile(entity.FullPath);

                        string str = body._GetString();
                        string[] lines = str._GetLines();

                        bool updated = false;
                        bool ok = true;

                        List<string> tmp = new List<string>();

                        foreach (var line2 in lines)
                        {
                            string line = line2;

                            if (line._IsSameiTrim(old1))
                            {
                                line = new1;
                                updated = true;
                            }
                            if (line._IsSameiTrim(old2))
                            {
                                line = new2;
                                updated = true;
                            }

                            if (line._IsSameiTrim(existingCommentLineOld) == false)
                            {
                                tmp.Add(line);

                                if (line._IsSameiTrim(existingCommentLine))
                                {
                                    tmp.Add(newCommentLine);
                                    updated = true;
                                }
                                else if (line._IsSameiTrim(newCommentLine))
                                {
                                    ok = false;
                                }
                            }
                        }

                        if (ok && updated)
                        {
                            string newStr = tmp._LinesToStr(Str.NewLine_Str_Windows);
                            entity.FullPath._Print();
                            pathInfo.FileSystem.WriteStringToFile(entity.FullPath, newStr, writeBom: true);
                        }
                    }
                }
                return true;
            },
            exceptionHandler: (pathInfo, err, cancel) =>
            {
                return true;
            });

        return 0;
    }
}

