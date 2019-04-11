using System;
using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

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
