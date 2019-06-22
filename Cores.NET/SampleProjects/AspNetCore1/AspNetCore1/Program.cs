using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace AspNetCore1
{
    public class Program
    {
        public static int Main(string[] args)
        {
            //System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

            Console.WriteLine($"IsServerGC = {System.Runtime.GCSettings.IsServerGC}");
            Console.WriteLine($"LatencyMode = {System.Runtime.GCSettings.LatencyMode}");
            Console.WriteLine($"LargeObjectHeapCompactionMode = {System.Runtime.GCSettings.LargeObjectHeapCompactionMode}");

            const string appName = "AspNetCore1";

            return StandardMainFunctions.DaemonMain.DoMain(new CoresLibOptions(CoresMode.Application, appName, DebugMode.Debug, false, false), args,
                getDaemonProc: () => new HttpServerDaemon<Startup>(appName, appName, new HttpServerOptions
                    {
                        HttpPortsList = 80._SingleList(),
                        HttpsPortsList = 443._SingleList(),
                        UseKestrelWithIPACoreStack = true,
                        DebugKestrelToConsole = false,
                }));
        }
    }
}
