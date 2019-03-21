using System;
using System.Threading;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace IPA.TestDev
{
    static class MainClass
    {
        static void Main(string[] args)
        {
            Dbg.SetDebugMode();

            try
            {
                Console.WriteLine("TestDev program  a a a aa!");
                
                JsonRpcTest.TestMain();
            }
            finally
            {
                LeakChecker.Print();
            }
        }
    }
}
