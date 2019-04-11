using System;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace DepTest.Common
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            Con.WriteLine("Hello Neko!");

            LeakChecker.Print();
        }
    }
}
