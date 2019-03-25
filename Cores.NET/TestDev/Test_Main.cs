using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace IPA.TestDev
{
    class Test1
    {
        public string Str;
        public int X;
    }

    static class MainClass
    {
        static object GetX(ref int num)
        {
            var u = new { Str = "Hello\n\"World", Int = num++, obj = new { Str2 = "猫", Int = num++ } };
            return u;
        }
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
                LogInfoOptions info = new LogInfoOptions()
                {
                    WithAppName = true,
                    WithKind = true,
                    WithMachineName = true,
                    WithPriority = true,
                };
                Logger g = new Logger(test.SingleLady, @"C:\tmp\deltest\log", "test", LogSwitchType.Hour, info, autoDeleteTotalMaxSize: 0);
                g.MaxPendingRecords = 100;

                TaskUtil.StartAsyncTaskAsync(async () =>
                {
                    string dummy = Str.MakeCharArray('x', 10) + "\nHello World\nHello World qqq\n\n";
                    //g.Add(new AsyncLogRecord("Hello"));
                    int num = 0;
                    while (test.Cancelled.IsCancellationRequested == false)
                    {
                        //await g.AddAsync(new LogRecord(DateTimeOffset.Now.AddHours(-1), Time.NowHighResDateTimeLocal.ToDtStr(withNanoSecs: true) + dummy), LogPendingTreatment.Wait);

                        //await g.AddAsync(new LogRecord(

                        Test1 t = new Test1()
                        {
                            Str = "Hello\n\"World",
                            X = num++,
                        };

                        //new { str = "Hello\nWorld", int1 = 3, obj = new { str2 = "Neko", int2 = 4 } }.GetInnerStr("", newLineString: ", ").PrintLine();

                        //List<object> o = new List<object>();
                        //o.Add(new { str = "Hello World D", int1 = 3, obj = new { str2 = "NekoA", int2 = 6 } });
                        //o.Add(new { str = "Hello World E", int1 = 4, obj = new { str2 = "NekoB", int2 = 7 } });
                        //o.Add(new { str = "Hello World F", int1 = 5, obj = new { str2 = "NekoC", int2 = 8 } });
                        //o.ToArray().GetInnerStr("", ", ").PrintLine();

                        //t.InnerPrint();

                        var u = new { Str = "Hello\n\"World", Int = num++, obj = new { Str2 = "猫", Int = num++ } };

                        await g.AddAsync(new LogRecord(GetX(ref num)));

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
