using System;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace DepTest.Common
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Con.WriteLine("Hello Neko!");
            }
            finally
            {
                LeakChecker.Print();
            }
        }
    }
}
