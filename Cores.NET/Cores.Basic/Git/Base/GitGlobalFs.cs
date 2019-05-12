// IPA Cores.NET
// 
// Copyright (c) 2018-2019 IPA CyberLab.
// Copyright (c) 2003-2018 Daiyuu Nobori.
// Copyright (c) 2013-2018 SoftEther VPN Project, University of Tsukuba, Japan.
// All Rights Reserved.
// 
// License: The Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
// THIS SOFTWARE IS DEVELOPED IN JAPAN, AND DISTRIBUTED FROM JAPAN, UNDER
// JAPANESE LAWS. YOU MUST AGREE IN ADVANCE TO USE, COPY, MODIFY, MERGE, PUBLISH,
// DISTRIBUTE, SUBLICENSE, AND/OR SELL COPIES OF THIS SOFTWARE, THAT ANY
// JURIDICAL DISPUTES WHICH ARE CONCERNED TO THIS SOFTWARE OR ITS CONTENTS,
// AGAINST US (IPA CYBERLAB, DAIYUU NOBORI, SOFTETHER VPN PROJECT OR OTHER
// SUPPLIERS), OR ANY JURIDICAL DISPUTES AGAINST US WHICH ARE CAUSED BY ANY KIND
// OF USING, COPYING, MODIFYING, MERGING, PUBLISHING, DISTRIBUTING, SUBLICENSING,
// AND/OR SELLING COPIES OF THIS SOFTWARE SHALL BE REGARDED AS BE CONSTRUED AND
// CONTROLLED BY JAPANESE LAWS, AND YOU MUST FURTHER CONSENT TO EXCLUSIVE
// JURISDICTION AND VENUE IN THE COURTS SITTING IN TOKYO, JAPAN. YOU MUST WAIVE
// ALL DEFENSES OF LACK OF PERSONAL JURISDICTION AND FORUM NON CONVENIENS.
// PROCESS MAY BE SERVED ON EITHER PARTY IN THE MANNER AUTHORIZED BY APPLICABLE
// LAW OR COURT RULE.

#if CORES_BASIC_GIT

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class GitGlobalFsSettings
        {
            public static readonly Copenhagen<Func<string>> GetLocalCacheRootDirProc = new Func<string>(() => Env.LocalPathParser.Combine(Env.AppRootDir, "GitLocal"));
        }
    }

    [Serializable]
    [DataContract]
    class GitGlobalFsRepository
    {
        [DataMember]
        public string Name;

        [DataMember]
        public string SrcUrl;

        [DataMember]
        public string LocalWorkDir;

        [DataMember]
        public DateTime LastFetch;

        public GitRepository Repository;
    }


    [Serializable]
    [DataContract]
    class GitGlobalFsState
    {
        [DataMember]
        public List<GitGlobalFsRepository> RepositoryList = new List<GitGlobalFsRepository>();
    }

    static class GitGlobalFs
    {
        public static StaticModule Module = new StaticModule(InitModule, FreeModule);

        public static readonly string LocalCacheRootDir = CoresConfig.GitGlobalFsSettings.GetLocalCacheRootDirProc.Value();
        public static readonly string HiveDir = Env.LocalPathParser.Combine(LocalCacheRootDir, "State");
        public static readonly string RepoDir = Env.LocalPathParser.Combine(LocalCacheRootDir, "Repo");

        public const string HiveDataName = "State";

        static Singleton<Hive> HiveSingleton;
        static Hive Hive => HiveSingleton;

        static Singleton<HiveData<GitGlobalFsState>> DataSingleton;
        static HiveData<GitGlobalFsState> Data => DataSingleton;

        static void InitModule()
        {
            HiveSingleton = new Singleton<Hive>(() => new Hive(new HiveOptions(HiveDir)));
            DataSingleton = new Singleton<HiveData<GitGlobalFsState>>(() => new HiveData<GitGlobalFsState>(GitGlobalFs.Hive, HiveDataName, () => new GitGlobalFsState(), HiveSyncPolicy.None));
        }

        static void FreeModule()
        {
            DataSingleton._DisposeSafe();
            DataSingleton = null;

            HiveSingleton._DisposeSafe();
            HiveSingleton = null;
        }

        static string GenerateNewRepositoryDirName(string repositoryName)
        {
            repositoryName = repositoryName.Trim().ToLower();

            DateTime now = DateTime.Now;
            for (int i = 1; ; i++)
            {
                string name = $"{Lfs.PathParser.MakeSafeFileName(repositoryName)}_{now.Year:D4}{now.Month:D2}{now.Day:D2}_{Env.ProcessId}_{i:D5}";
                string fullPath = Lfs.PathParser.Combine(RepoDir, name);

                if (Lfs.IsDirectoryExists(fullPath) == false)
                {
                    return name;
                }
            }
        }

        static void StartRepository(string name, string srcUrl)
        {
            lock (Data.DataLock)
            {
                GitGlobalFsRepository repoData = Data.Data.RepositoryList.Where(x => x.Name._IsSamei(name)).SingleOrDefault();

                if (repoData == null)
                {
                    string dirName = GenerateNewRepositoryDirName(name);
                    string dirFullPath = Lfs.PathParser.Combine(RepoDir, dirName);
                    GitUtil.Clone(dirFullPath, srcUrl);
                    GitRepository gitRepo = new GitRepository(dirFullPath);
                    repoData = new GitGlobalFsRepository()
                    {
                        Name = name,
                        SrcUrl = srcUrl,
                        LocalWorkDir = dirName,
                        Repository = gitRepo,
                        LastFetch = DateTime.UtcNow,
                    };

                    Data.Data.RepositoryList.Add(repoData);
                }
                else
                {
                    if (repoData.Repository == null)
                    {
                        string dirName = repoData.Name;
                        string dirFullPath = Lfs.PathParser.Combine(RepoDir, dirName);
                        GitRepository gitRepo = new GitRepository(dirFullPath);
                        repoData.Repository = gitRepo;
                    }
                }
            }
        }
    }
}

#endif // CORES_BASIC_GIT

