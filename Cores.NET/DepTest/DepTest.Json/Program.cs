using System;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace DepTest.Common
{
    class Program
    {
        static int Main(string[] args)
        {
            int ret = -1;

            CoresLib.Init(new CoresLibOptions(CoresMode.Application, "DepTest", DebugMode.Debug, defaultPrintStatToConsole: false, defaultRecordLeakFullStack: false), args);
            try
            {
                Con.WriteLine("Hello World !");
            }
            finally
            {
                CoresLib.Free();
            }

            return ret;
        }
    }
}
