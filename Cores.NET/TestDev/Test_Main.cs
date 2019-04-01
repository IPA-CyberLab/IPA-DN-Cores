using System;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.GlobalFunctions.Basic;

namespace IPA.TestDev
{
    class Test1
    {
        public string Str = "Hello";
        public int X = 123;

        public byte[] ByteData = "Hello".GetBytes_Ascii();

        public List<string> a = new List<string>(new string[] { "a", "b", "c" });
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
            TryRetBool(() => Lfs.EnableBackupPrivilege(), noDebugMessage: true);

            //string longFileName = @"c:\tmp\LongTest\11111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111\33333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333\55555555556666666666555555555566666666665555555555666666666655555555556666666666555555555566666666665555555555666666666655555555556666666666555555555566666666665555555555666666666655555555556666666666555555555566666666665555555555.txt";

            //while (true)
            //{
            //    using (var logFile = Lfs.Open(longFileName, readPartial: true, writeMode: false))
            //    {
            //        //Con.WriteLine($"size: {logFile.GetFileSize(true)}");
            //        //logFile.GetStream().ReadToEnd().GetString_UTF8().Print();
            //        logFile.SeekToEnd();
            //        //logFile.Write("Hello2".GetBytes_Ascii());
            //    }
            //}

            //return;


            //string dirname = @"c:\tmp\LongTest\11111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111\33333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333\";

            //Directory.GetFiles(dirname).ToList().ForEach(x => Console.WriteLine(x));

            //File.Copy(@"C:\pack\Bz153\Bz.exe", Path.Combine(dirname, "test.exe"));

            //return;

            //var ret = FileSystem.Local.EnumDirectory(@"c:\tmp\yagi\..\yagi\", recursive: true);
            //ret.ObjectToJson().Print();

            //System.Security.AccessControl.FileSecurity ss = new System.Security.AccessControl.FileSecurity(Env.AppRootDir, System.Security.AccessControl.AccessControlSections.Access);
            //Console.WriteLine(ss.ObjectToJson());

            //Lfs.DirectoryWalker.WalkDirectory(@"\\pc34\d$\tmp\dirtest\", (dirList, cancel) =>
            //{
            //    Con.WriteLine("----------");
            //    Con.WriteLine(dirList.ObjectToJson());
            //    return true;
            //},
            //(path, exp) =>
            //{
            //    Con.WriteLine($"**** Error: {exp.Message}");
            //    return true;
            //},
            //recursive: true
            //);

            using (var file1 = Lfs.Open("d:\\tmp\\dirtest\\1\\1.txt", backupMode: true))
            {
                file1.GetStream().ReadToEnd().GetString_UTF8().Print();
            }

            //using (var file1 = Lfs.Open("d:\\tmp\\dirtest\\1\\1.txt", backupMode: true, writeMode: true))
            //{
            //    file1.SeekToEnd();
            //    file1.GetStream().Write((DateTimeOffset.Now.ToDtStr() + "\r\n").GetBytes_UTF8());
            //}

            return;


            var f = FileSystem.Local.Create(@"c:\tmp\LongTest\11111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111222222222211111111112222222222111111111122222222221111111111\33333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333444444444433333333334444444444333333333344444444443333333333\xxx.txt");
            f.Close();
            return;
            f.MicroOperationSize = 1024 * 1024;
            CancellationTokenSource cts = new CancellationTokenSource(4000);

            byte[] testData = Str.MakeCharArray('x', 100000000).GetBytes_Ascii();
            Console.WriteLine("Test start");
            Task t2 = TaskUtil.StartAsyncTaskAsync(async () =>
            {
                while (true)
                await f.WriteAsync(testData, cts.Token);
                Console.WriteLine("Write Completed.");
            });

            Console.ReadLine();

            Console.WriteLine("Cancelling...");
            //f.Close();
            cts.Cancel();
            Console.WriteLine("Canceled.");
            //t2.Wait();



            f.Write("Hello World".GetBytes_Ascii());

            f.Seek(-6, SeekOrigin.Current);

            Con.WriteLine(f.Position);

            f.Write("123".GetBytes_Ascii());

            Con.WriteLine(f.Position);

            Con.WriteLine(f.Seek(0, SeekOrigin.Begin));

            Memory<byte> buf = new byte[12];
            f.Read(buf);

            var st = f.GetStream();

            st.Position = 0;
            st.WriteAsync("Nekoo".GetBytes_Ascii());

            f.SetFileSize(0);

            st.Write("Nekoo".GetBytes_Ascii());

            Console.WriteLine($"'{buf.ToArray().GetString_Ascii()}'");

            //f.Close();

            //f.Close();

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

                        var u = new { Str = "Hello\n\"World", Int = num++, obj = new { Str2 = (string)null, Int = num++, Bytes = new byte[] { 1, 2, 3 }, LogPriority = LogPriority.Info } };

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
                TestMain();
            }
            finally
            {
                LeakChecker.Print();
            }
        }
    }
}
