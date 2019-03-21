using System;
using System.Threading;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace IPA.TestDev.Basic.Common
{
    static class MainClass
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("TestDev.Basic.Common 45");

                PipeTest.TestMain();
            }
            finally
            {
                LeakChecker.Print();
            }
        }
    }
}
