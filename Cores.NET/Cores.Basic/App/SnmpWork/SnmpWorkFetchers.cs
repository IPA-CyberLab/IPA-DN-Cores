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
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public static partial class SnmpWorkConfig
    {
        public static readonly Copenhagen<string> DefaultPingTarget = "8.8.8.8";
        public static readonly Copenhagen<string> DefaultSpeedTarget = "speed2.open.ad.jp";
        public static readonly Copenhagen<int> DefaultSpeedIntervalSecs = 1;
    }

    public class SnmpWorkFetcherSpeed : SnmpWorkFetcherBase
    {
        public SnmpWorkFetcherSpeed(SnmpWorkHost host, int pollingInterval = 0) : base(host, pollingInterval)
        {
        }

        protected override void InitImpl()
        {
        }

        int count = 0;

        protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
        {
            count++;

            SnmpWorkSettings settings = Host.Settings;

            string[] speedTargets = settings.SpeedTargets._Split(StringSplitOptions.RemoveEmptyEntries, ",", "/", " ", "\t");

            foreach (string target in speedTargets)
            {
                cancel.ThrowIfCancellationRequested();

                string[] tokens = target._Split(StringSplitOptions.RemoveEmptyEntries, ':');
                string host;
                int port = 9821;
                host = tokens[0];
                if (tokens.Length >= 2)
                {
                    port = tokens[1]._ToInt();
                }

                long downloadBps_1 = 0;
                long uploadBps_1 = 0;
                long downloadBps_32 = 0;
                long uploadBps_32 = 0;

                try
                {
                    IPAddress ipAddress = await LocalNet.GetIpAsync(host, cancel: cancel);

                    try
                    {
                        var downloadResult_1 = await SpeedTestClient.RunSpeedTestWithRetryAsync(LocalNet, ipAddress, port, 1, 2000, SpeedTestModeFlag.Download, 5, cancel);
                        if (count >= 2) await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(settings.SpeedIntervalsSec * 1000));

                        var downloadResult_32 = await SpeedTestClient.RunSpeedTestWithRetryAsync(LocalNet, ipAddress, port, 32, 2000, SpeedTestModeFlag.Download, 5, cancel);
                        if (count >= 2) await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(settings.SpeedIntervalsSec * 1000));

                        downloadBps_1 = downloadResult_1?.BpsDownload ?? 0;
                        downloadBps_32 = downloadResult_32?.BpsDownload ?? 0;
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }

                    try
                    {
                        var uploadResult_1 = await SpeedTestClient.RunSpeedTestWithRetryAsync(LocalNet, ipAddress, port, 1, 2000, SpeedTestModeFlag.Upload, 5, cancel);
                        if (count >= 2) await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(settings.SpeedIntervalsSec * 1000));

                        var uploadResult_32 = await SpeedTestClient.RunSpeedTestWithRetryAsync(LocalNet, ipAddress, port, 32, 2000, SpeedTestModeFlag.Upload, 5, cancel);
                        if (count >= 2) await cancel._WaitUntilCanceledAsync(Util.GenRandInterval(settings.SpeedIntervalsSec * 1000));

                        uploadBps_1 = uploadResult_1?.BpsUpload ?? 0;
                        uploadBps_32 = uploadResult_32?.BpsUpload ?? 0;
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                ret.Add($"{TruncateName(host, 20)}/x32_DownloadMbps", ((double)downloadBps_32 / 1000.0 / 1000.0).ToString("F3"));
                ret.Add($"{TruncateName(host, 20)}/x32_UploadMbps", ((double)uploadBps_32 / 1000.0 / 1000.0).ToString("F3"));

                ret.Add($"{TruncateName(host, 20)}/x1_DownloadMbps", ((double)downloadBps_1 / 1000.0 / 1000.0).ToString("F3"));
                ret.Add($"{TruncateName(host, 20)}/x1_UploadMbps", ((double)uploadBps_1 / 1000.0 / 1000.0).ToString("F3"));
            }
        }
    }

    public class SnmpWorkFetcherPing : SnmpWorkFetcherBase
    {
        static readonly Once FirstPing = new Once();

        IPAddress PingTargetAddress = null!;

        public SnmpWorkFetcherPing(SnmpWorkHost host, int pollingInterval = 0) : base(host, pollingInterval)
        {
        }

        protected override void InitImpl()
        {
            PingTargetAddress = IPUtil.StrToIP(SnmpWorkConfig.DefaultPingTarget)!;
        }

        protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
        {
            SnmpWorkSettings settings = Host.Settings;

            string[] pingTargets = settings.PingTargets._Split(StringSplitOptions.RemoveEmptyEntries, ",", "/", " ", "\t");

            foreach (string pingTarget in pingTargets)
            {
                cancel.ThrowIfCancellationRequested();

                bool ok = false;

                try
                {
                    IPAddress ipAddress = await LocalNet.GetIpAsync(pingTarget, cancel: cancel);

                    if (FirstPing.IsFirstCall())
                    {
                        // JIT 対策
                        try
                        {
                            await LocalNet.SendPingAsync(ipAddress, pingCancel: cancel);
                        }
                        catch { }
                    }

                    SendPingReply reply = await LocalNet.SendPingAsync(ipAddress, pingCancel: cancel);

                    if (reply.Ok)
                    {
                        double rtt = reply.RttDouble;

                        rtt = Math.Min(rtt, 2.0);

                        int ttl = reply.Ttl;

                        if (ttl == 0)
                        {
                            // Use ping command to get TTL
                            try
                            {
                                var result = await EasyExec.ExecAsync(ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? Consts.LinuxCommands.Ping6 : Consts.LinuxCommands.Ping,
                                    $"-W 1 -c 1 {ipAddress.ToString()}",
                                    cancel: cancel,
                                    throwOnErrorExitCode: false);

                                string[] lines = result.OutputStr._GetLines(true);

                                foreach (string line in lines)
                                {
                                    OneLineParams param = new OneLineParams(line, ' ', false);
                                    string ttlStr = param._GetStrFirst("ttl");
                                    if (ttlStr._IsFilled())
                                    {
                                        ttl = ttlStr._ToInt();
                                        break;
                                    }
                                }

                            }
                            catch (Exception ex)
                            {
                                ex._Debug();
                            }
                        }

                        ret.Add($"Ping ms - {TruncateName(pingTarget, 20)}", (rtt * 1000.0).ToString("F3"));
                        ret.Add($"Ttl - {TruncateName(pingTarget, 20)}", ttl.ToString());

                        ok = true;
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                if (ok == false)
                {
                    ret.Add($"Ping ms - {TruncateName(pingTarget, 20)}", "");
                    ret.Add($"Ttl - {TruncateName(pingTarget, 20)}", "0");
                }

                await cancel._WaitUntilCanceledAsync(100);
            }
        }
    }

    public class SnmpWorkFetcherNetwork : SnmpWorkFetcherBase
    {
        bool IsConnTrackOk = false;

        public SnmpWorkFetcherNetwork(SnmpWorkHost host, int pollingInterval = 0) : base(host, pollingInterval)
        {
        }

        protected override void InitImpl()
        {
            // ConnTrack コマンドが利用可能かどうか確認
            if (EasyExec.ExecAsync(Consts.LinuxCommands.ConnTrack, "-C")._TryGetResult() != default)
            {
                IsConnTrackOk = true;
            }
        }

        protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
        {
            if (IsConnTrackOk)
            {
                try
                {
                    // ConnTrack
                    var result = await EasyExec.ExecAsync(Consts.LinuxCommands.ConnTrack, "-C");

                    string valueStr = result.OutputStr._GetFirstFilledLineFromLines();

                    ret.Add($"ConnTrack Sessions", valueStr._ToInt().ToString());
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }

            if (true)
            {
                try
                {
                    // Sockets
                    string[] lines = (await Lfs.ReadStringFromFileAsync(Consts.LinuxPaths.SockStat, flags: FileFlags.NoCheckFileSize))._GetLines();

                    int numSockets = -1;
                    int numTcp = -1;
                    int numUdp = -1;

                    foreach (string line in lines)
                    {
                        string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, " ");

                        if (tokens.Length >= 3)
                        {
                            if (tokens[0]._IsSamei("sockets:"))
                            {
                                numSockets = tokens[2]._ToInt();
                            }

                            if (tokens[0]._IsSamei("TCP:"))
                            {
                                numTcp = tokens[2]._ToInt();
                            }

                            if (tokens[0]._IsSamei("UDP:"))
                            {
                                numUdp = tokens[2]._ToInt();
                            }
                        }
                    }

                    if (numSockets >= 0 && numTcp >= 0 && numUdp >= 0)
                    {
                        ret.Add($"Sockets", numSockets.ToString());
                        ret.Add($"TCP", numTcp.ToString());
                        ret.Add($"UDP", numUdp.ToString());
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
        }
    }

    public class SnmpWorkFetcherDisk : SnmpWorkFetcherBase
    {
        public SnmpWorkFetcherDisk(SnmpWorkHost host, int pollingInterval = 0) : base(host, pollingInterval)
        {
        }

        protected override void InitImpl() { }

        protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
        {
            var result = await EasyExec.ExecAsync(Consts.LinuxCommands.Df, "-a -B1");

            string[] lines = result.OutputStr._GetLines();

            string[] ignores = "/dev /run /sys /var /snap /tmp /proc"._Split(StringSplitOptions.RemoveEmptyEntries, " ");

            foreach (string line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');

                if (tokens.Length >= 6)
                {
                    string totalStr = tokens[1];
                    string availableStr = tokens[3];
                    string path = tokens[5];

                    if (path.StartsWith("/") && ignores.Where(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase)).Any() == false)
                    {
                        long total = totalStr._ToLong();
                        long available = availableStr._ToLong();

                        if (total >= 0 && available >= 0)
                        {
                            available = Math.Min(available, total);
                            ret.Add($"available - {TruncateName(path)}", NormalizeValue(((double)available * 100.0 / (double)total).ToString("F3")));
                        }
                    }
                }
            }
        }
    }

    public class SnmpWorkFetcherMemory : SnmpWorkFetcherBase
    {
        public SnmpWorkFetcherMemory(SnmpWorkHost host, int pollingInterval = 0) : base(host, pollingInterval)
        {
        }

        protected override void InitImpl() { }

        protected override async Task GetValueAsync(SortedDictionary<string, string> ret, RefInt nextPollingInterval, CancellationToken cancel = default)
        {
            var result = await EasyExec.ExecAsync(Consts.LinuxCommands.Free, "-b -w");

            string[] lines = result.OutputStr._GetLines();

            List<string> headers = new List<string>();

            KeyValueList<string, string> dataList = new KeyValueList<string, string>();

            foreach (string line in lines)
            {
                string[] tokens = line._Split(StringSplitOptions.RemoveEmptyEntries, ' ', '\t');

                if (tokens.Length >= 2)
                {
                    if (headers.Count == 0)
                    {
                        if (tokens[0]._IsSamei("total"))
                        {
                            // ヘッダ行
                            foreach (string token in tokens)
                            {
                                headers.Add(token);
                            }
                        }
                    }
                    else
                    {
                        // データ行
                        if (tokens[0]._IsSamei("Mem:"))
                        {
                            for (int i = 1; i < tokens.Length; i++)
                            {
                                if (headers.Count >= (i - 1))
                                {
                                    dataList.Add(headers[i - 1], tokens[i]);
                                }
                            }
                        }
                    }
                }
            }

            // total
            long total = dataList._GetStrFirst("total", "-1")._ToLong();
            long available = dataList._GetStrFirst("available", "-1")._ToLong();

            if (total >= 0 && available >= 0)
            {
                available = Math.Min(available, total);
                ret.Add($"available", NormalizeValue(((double)available * 100.0 / (double)total).ToString("F3")));
            }
        }
    }

    public class SnmpWorkFetcherTemperature : SnmpWorkFetcherBase
    {
        bool IsSensorsCommandOk = false;

        readonly KeyValueList<string, string> ThermalFiles = new KeyValueList<string, string>();

        public SnmpWorkFetcherTemperature(SnmpWorkHost host, int pollingInterval = 0) : base(host, pollingInterval)
        {
        }

        protected override void InitImpl()
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
                string value = (await Lfs.ReadStringFromFileAsync(thermalFile.Value))._GetFirstFilledLineFromLines();

                double d = ((double)value._ToInt()) / 1000.0;

                ret.Add($"{TruncateName(thermalFile.Key)}", NormalizeValue(d.ToString("F3")));
            }

            if (IsSensorsCommandOk)
            {
                try
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
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
        }
    }
}

#endif

