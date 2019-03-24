using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace IPA.TestDev
{
    static class MainClass
    {
        static void TestMain()
        {
            //JsonRpcTest.TestMain();

            List<long> o = new List<long>();

            while (true)
            {
                long sys64 = Time.SystemTime64;
                long tick64 = Time.Tick64;
                DateTime dt64 = Time.Time64ToDateTime(sys64);

                Con.WriteLine($"Now: {tick64} = {sys64} = {dt64.ToLocalTime()}");

                o.Add(tick64);

                ThreadObj.Sleep(500);

                foreach (long v in o)
                {
                    Con.WriteLine($"Hist: {v} = {Time.Tick64ToDateTime(v).ToLocalTime()}");
                }

                if (o.Count >= 5)
                {
                    Con.WriteLine("exit");
                    return;
                }
            }
        }

        static void Main(string[] args)
        {
            Dbg.SetDebugMode();

            try
            {
                TestMain();
            }
            finally
            {
                LeakChecker.Print();
            }
        }
    }
}
