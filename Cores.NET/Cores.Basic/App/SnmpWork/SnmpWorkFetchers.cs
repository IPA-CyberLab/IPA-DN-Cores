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
// Snmp Work Daemon Util 実際の値を取得するクラス群

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
    public class SnmpWorkFetcherTemperature : SnmpWorkFetcherBase
    {
        readonly bool IsSensorsCommandOk = false;

        readonly KeyValueList<string, string> ThermalFiles = new KeyValueList<string, string>();

        public SnmpWorkFetcherTemperature(int pollingInterval = 0) : base(pollingInterval)
        {
            // sensors コマンドが利用可能かどうか確認
            if (EasyExec.ExecAsync(Consts.LinuxCommands.Sensors, "-u")._TryGetResult() != default)
            {
                IsSensorsCommandOk = true;
            }

            // /sys/class/thermal/ から取得可能な値一覧を列挙
            FileSystemEntity[]? dirList = null;
            try
            {
                dirList = Lfs.EnumDirectory(Consts.LinuxPaths.SysThermal);
            }
            catch { }

            if (dirList != null)
            {
                foreach (var dir in dirList)
                {
                    string fileName = Lfs.PathParser.Combine(dir.FullPath, "temp");

                    if (Lfs.IsFileExists(fileName))
                    {
                        try
                        {
                            Lfs.ReadStringFromFile(fileName);

                            ThermalFiles.Add(dir.Name, fileName);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
        {
            // sys/class/thermal/ から温度取得
            foreach (var thermalFile in this.ThermalFiles)
            {
                string value = Lfs.ReadStringFromFile(thermalFile.Value)._GetFirstFilledLineFromLines();

                double d = ((double)value._ToInt()) / 1000.0;

                ret.Add($"{TruncateName(thermalFile.Key)}", NormalizeValue(d.ToString("F3")));
            }

            if (IsSensorsCommandOk)
            {
                // Sensors コマンドで温度取得
                var result = await EasyExec.ExecAsync(Consts.LinuxCommands.Sensors, "-u", cancel: cancel);

                string[] lines = result.OutputStr._GetLines(true);

                string groupName = "";
                string fieldName = "";

                foreach (string line2 in lines)
                {
                    string line = line2.TrimEnd();

                    if (line.StartsWith(" ") == false && line._InStr(":") == false)
                    {
                        // グループ名
                        groupName = line.Trim();
                    }
                    else if (line.StartsWith(" ") == false && line.EndsWith(":"))
                    {
                        // 値名
                        fieldName = line.Substring(0, line.Length - 1);
                    }
                    else if (line.StartsWith(" ") && line._GetKeyAndValue(out string key, out string value, ":"))
                    {
                        // 値サブ名 : 値
                        key = key.Trim();
                        value = value.Trim();

                        if (key.EndsWith("_input", StringComparison.OrdinalIgnoreCase))
                        {
                            ret.Add($"{TruncateName(groupName)}/{TruncateName(fieldName)}", NormalizeValue(value));
                        }
                    }
                }
            }
        }
    }
}

#endif

