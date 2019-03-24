using System;
using System.Collections.Generic;
using System.Text;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    class GlobalInitializer
    {
        static GlobalInitializer()
        {
            Limbo.SInt = Time.Tick64;
            Limbo.ObjectSlow = Env.AppRootDir;
        }

        public GlobalInitializer() => Ensure();
        public static int Ensure() => 0;
    }
}
