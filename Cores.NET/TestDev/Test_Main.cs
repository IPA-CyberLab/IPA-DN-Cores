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
            try
            {
                Console.WriteLine("TestDev.Full.Common 48");

                //JsonRpcTest.TestMain();
            }
            finally
            {
                LeakChecker.Print();
            }
        }
    }
}
