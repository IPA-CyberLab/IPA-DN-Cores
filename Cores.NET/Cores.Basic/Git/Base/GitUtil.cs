using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using LibGit2Sharp;

namespace IPA.Cores.Basic
{
    static class GitUtil
    {
        public static void Clone(string destDir, string srcUrl, CloneOptions options = null)
        {
            Repository.Clone(srcUrl, destDir, options);
        }
    }
}
