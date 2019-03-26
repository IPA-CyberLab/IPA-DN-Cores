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
        static object GetX(int num = 123)
        {
            var u = new { Str = "Hello\n\"World\n1\r\n2\r\n3", Int = num++, obj = new { Str2 = "猫", Int = num++ } };

            return u;
        }
        static void TestMain()
        {
            //int a = 123;
            //object nullObj = null;
            //a.GetObjectDump("").Print();
            //"Hello".GetBytes_Ascii().GetObjectDump("").Print();
            //new { v = new int[] { 1, 2, 3 } }.GetObjectDump("").Print();
            //nullObj.GetObjectDump("").Print();
            //GetX().GetObjectDump("").Print();

            AppConfig.GlobalLogRouteMachineSettings.LogRootDir.Set(@"c:\tmp\log1");
            AppConfig.Logger.DefaultMaxPendingRecords.Set(1000);
            AppConfig.Logger.DefaultAutoDeleteTotalMinSize.Set(1000000);
            AppConfig.Logger.DefaultMaxLogSize.Set( 10000000);
            //Console.WriteLine(AppConfig.GlobalLogRouteMachine.LogRootDir);

            Console.WriteLine(GetX());

            var x = GetX();

            Con.WriteLine(x.ToString());
            Con.WriteLine();
            Con.WriteLine();
            Con.WriteLine();
            Con.WriteLine(x.ToString());

            //Task.Delay(100).Wait();

            return;
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
                Logger g = new Logger(test.SingleLady, @"C:\tmp\deltest\log", "test", "test", LogSwitchType.Hour, info, autoDeleteTotalMinSize: 0);
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

                        var u = new { Str = "Hello\n\"World", Int = num++, obj = new { Str2 = (string)null, Int = num++, Bytes = new byte[] { 1, 2, 3 }, LogPriority = LogPriority.Information } };

                        await g.AddAsync(new LogRecord(u));

                        await Task.Delay(1);
                        //break;
                    }
                }, leakCheck: true).AddToLady(test.SingleLady);

                g.DiscardPendingDataOnDispose = false;
                test.EnterKeyPrompt();
            }

            Con.ReadLine("Quit?");
        }

        class X
        {
            public string XX = "xxx\nyyy";
            public int y = 1243;
            public object Null = null;
        }

        static void Main(string[] args)
        {
            //Dbg.SetDebugMode();
            //Dbg.SetDebugMode(DebugMode.ReleaseNoAllLogsOutput);

            try
            {
                //Dbg.WriteLine("Hello");
                //Dbg.Where("Hello");
                Con.WriteLine(new X());
                Con.WriteLine(null);
                while (true)
                {
                    string s = Console.ReadLine();
                    if (s.IsEmpty()) break;
                    Con.WriteLine(s);
                }
                //TestMain();
            }
            finally
            {
                LeakChecker.Print();
            }
        }
    }
}
