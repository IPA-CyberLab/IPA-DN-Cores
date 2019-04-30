using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    class GlobalInitializer
    {
        static GlobalInitializer()
        {
            Limbo.SInt = Time.Tick64;
            Limbo.ObjectSlow = Env.AppRootDir;

            var ensureReporter = CoresRuntimeStatReporter.Reporter;
        }

        public GlobalInitializer() => Ensure();
        public static int Ensure() => 0;


    }
}
