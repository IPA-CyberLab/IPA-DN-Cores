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
            //DateTimeOffset.Now.ToDtStr().Print();

            //return;

            //JsonRpcTest.TestMain();

            //List<long> o = new List<long>();

            //while (true)
            //{
            //    long sys64 = Time.SystemTime64;
            //    long tick64 = Time.Tick64;
            //    DateTime dt64 = Time.Time64ToDateTime(sys64);

            //    Con.WriteLine($"Now: {tick64} = {sys64} = {dt64.ToLocalTime()}");

            //    o.Add(tick64);

            //    ThreadObj.Sleep(500);

            //    foreach (long v in o)
            //    {
            //        Con.WriteLine($"Hist: {v} = {Time.Tick64ToDateTime(v).ToLocalTime()}");
            //    }
            //}

            using (AsyncTester test = new AsyncTester(true))
            {
                Logger g = new Logger(test.SingleLady, @"C:\tmp\deltest\log", "test", LogSwitchType.Hour, autoDeleteTotalMaxSize: 0);
                g.MaxPendingRecords = 100;

                TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    string dummy = Str.MakeCharArray('x', 10);
                    //g.Add(new AsyncLogRecord("Hello"));
                    while (test.Cancelled.IsCancellationRequested == false)
                    {
                        Dbg.Where();
                        await g.AddAsync(new LogRecord(DateTimeOffset.Now.AddHours(-1), Time.NowHighResDateTimeLocal.ToDtStr(with_nanosecs: true) + dummy), LogPendingTreatment.Wait);
                        await Task.Delay(1);
                        //break;
                    }
                }, leakCheck: true).AddToLady(test.SingleLady);

                g.DiscardPendingDataOnDispose = false;
                test.EnterKeyPrompt();
            }

            Con.ReadLine("Quit?");
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
