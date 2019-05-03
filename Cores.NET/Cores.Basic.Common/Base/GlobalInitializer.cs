using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    static class GlobalInitializer
    {
        public static void Ensure()
        {
            try
            {
                NormalInitializeOnce();
            }
            catch { }
        }

        static readonly Once once;
        static void NormalInitializeOnce()
        {
            if (once.IsFirstCall() == false) return;

            // Start the global reporter
            var reporter = CoresRuntimeStatReporter.Reporter;
        }
    }
}
