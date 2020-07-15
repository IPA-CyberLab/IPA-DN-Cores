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
    // JSON をパースするとこの型が出てくる
    public class LogJsonParseAsRuntimeStat
    {
        public DateTimeOffset? TimeStamp;
        public CoresRuntimeStat? Data;
        public string? TypeName;    // "CoresRuntimeStat"
        public string? Kind;        // "Stat"
        public string? Priority;
        public string? Tag;         // "Snapshot"
        public string? AppName;
        public string? MachineName;
        public string? Guid;
    }

    public class LogStatMemoryLeakAnalyzerCsvRow
    {
        public DateTime Dt;
        public long Mem;
    }

    // stat ログをもとにメモリリークしていないかどうか分析するためのユーティリティクラス
    public static class LogStatMemoryLeakAnalyzer
    {
        public static List<LogStatMemoryLeakAnalyzerCsvRow> AnalyzeLogFiles(string logDir)
        {
            Dictionary<DateTime, long> table = new Dictionary<DateTime, long>();

            var files = Lfs.EnumDirectory(logDir).Where(x => x.IsFile && x.Name._IsExtensionMatch(".log")).OrderBy(x => x.Name, StrComparer.IgnoreCaseComparer);

            foreach (var file in files)
            {
                file.FullPath._Print();

                using var f = Lfs.Open(file.FullPath);
                using var stream = f.GetStream();
                var r = new BinaryLineReader(stream);
                while (true)
                {
                    List<Memory<byte>>? list = r.ReadLines();
                    if (list == null) break;

                    foreach (var data in list)
                    {
                        string line = data._GetString_UTF8();

                        try
                        {
                            var lineData = line._JsonToObject<LogJsonParseAsRuntimeStat>();

                            if (lineData != null)
                            {
                                if (lineData.TypeName == "CoresRuntimeStat" && lineData.Tag == "Snapshot")
                                {
                                    CoresRuntimeStat? stat = lineData.Data;
                                    if (stat != null)
                                    {
                                        if (stat.Mem != 0)
                                        {
                                            DateTime dt = lineData.TimeStamp!.Value.LocalDateTime.Date;
                                            if (table.TryAdd(dt, stat.Mem) == false)
                                            {
                                                table[dt] = Math.Min(table[dt], stat.Mem);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }
                    }
                }
            }

            List<LogStatMemoryLeakAnalyzerCsvRow> ret = new List<LogStatMemoryLeakAnalyzerCsvRow>();

            var dates = table.Keys.OrderBy(x => x);
            if (dates.Any())
            {
                for (DateTime date = dates.First(); date <= dates.Last(); date = date.AddDays(1))
                {
                    long mem = table.GetValueOrDefault(date, 0);

                    ret.Add(new LogStatMemoryLeakAnalyzerCsvRow { Dt = date, Mem = mem });
                }
            }

            return ret;
        }
    }
}

#endif

