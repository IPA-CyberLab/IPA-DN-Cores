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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using LibGit2Sharp;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Concurrent;

namespace IPA.Cores.Basic
{
    static class GitUtil
    {
        public static void Clone(string destDir, string srcUrl, CloneOptions options = null)
        {
            Repository.Clone(srcUrl, destDir, options);
        }
    }

    class GitCommit
    {
        static readonly FileSystemPathParser Parser = FileSystemPathParser.GetInstance(FileSystemStyle.Linux);

        public GitRepository Repository { get; }
        public Commit Commit { get; }
        public DateTimeOffset TimeStamp => Commit.Author.When;
        public Tree RootTree => Commit.Tree;

        readonly Singleton<string, Tree> DirectoryTreeCache;
        readonly Singleton<string, FileSystemEntity[]> DirectoryItemCache;

        public GitCommit(GitRepository repository, Commit commit)
        {
            this.Repository = repository;
            this.Commit = commit;
            this.DirectoryTreeCache = new Singleton<string, Tree>(path => GetDirectoryInternal(path), LeakCounterKind.DoNotTrack, Parser.PathStringComparer);
            this.DirectoryItemCache = new Singleton<string, FileSystemEntity[]>(path => GetDirectoryItemsInternal(path), LeakCounterKind.DoNotTrack, Parser.PathStringComparer);
        }

        public FileSystemEntity[] GetDirectoryItems(string dirPath)
            => this.DirectoryItemCache[dirPath];

        FileSystemEntity[] GetDirectoryItemsInternal(string dirPath)
        {
            Tree dir = GetDirectory(dirPath);

            return GetDirectoryItemsInternal(dirPath, dir);
        }

        FileSystemEntity[] GetDirectoryItemsInternal(string dirPath, Tree tree)
        {
            dirPath = Parser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(dirPath);

            List<FileSystemEntity> ret = new List<FileSystemEntity>();

            FileSystemEntity cur = new FileSystemEntity()
            {
                FullPath = dirPath,
                Name = ".",
                Attributes = FileAttributes.Directory,
                CreationTime = this.TimeStamp,
                LastWriteTime = this.TimeStamp,
                LastAccessTime = this.TimeStamp,
            };

            ret.Add(cur);

            foreach (var item in tree)
            {
                if (item.TargetType == TreeEntryTargetType.Tree)
                {
                    Tree subTree = (Tree)item.Target;
                    FileSystemEntity e = new FileSystemEntity()
                    {
                        FullPath = Parser.Combine(dirPath, item.Name),
                        Name = item.Name,
                        Attributes = FileAttributes.Directory,
                        CreationTime = this.TimeStamp,
                        LastWriteTime = this.TimeStamp,
                        LastAccessTime = this.TimeStamp,
                    };
                    ret.Add(e);
                }
                else if (item.TargetType == TreeEntryTargetType.Blob)
                {
                    Blob blob = (Blob)item.Target;
                    FileSystemEntity e = new FileSystemEntity()
                    {
                        FullPath = Parser.Combine(dirPath, item.Name),
                        Name = item.Name,
                        Attributes = FileAttributes.Normal,
                        Size = blob.Size,
                        PhysicalSize = blob.Size,
                        CreationTime = this.TimeStamp,
                        LastWriteTime = this.TimeStamp,
                        LastAccessTime = this.TimeStamp,
                    };
                    ret.Add(e);
                }
            }

            return ret.ToArray();
        }

        public Tree GetDirectory(string dirPath)
            => DirectoryTreeCache[dirPath];

        Tree GetDirectoryInternal(string dirPath)
        {
            dirPath = Parser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(dirPath);

            string[] elements = Parser.SplitAbsolutePathToElementsUnixStyle(dirPath);

            Tree currentTree = this.RootTree;

            for (int i = 0; i < elements.Length; i++)
            {
                string name = elements[i];

                TreeEntry entry = currentTree[name];

                if (entry == null)
                    throw new ArgumentException($"The path \"{dirPath}\" not found. There is no \"{name}\" object in the database.");

                if (entry.TargetType == TreeEntryTargetType.Tree)
                {
                    Tree nextTree = (Tree)entry.Target;

                    currentTree = nextTree;
                }
                else
                {
                    throw new ArgumentException($"The path \"{dirPath}\" is not a directory.");
                }
            }

            return currentTree;
        }
    }

    class GitRepository : AsyncService
    {
        public string WorkDir { get; }
        Repository Repository { get; }

        static readonly FileSystemPathParser Parser = FileSystemPathParser.GetInstance(FileSystemStyle.Linux);

        public GitRepository(string workDir, CancellationToken cancel = default) : base(cancel)
        {
            try
            {
                this.WorkDir = Env.LocalFileSystemPathInterpreter.NormalizeDirectorySeparatorAndCheckIfAbsolutePath(workDir);

                this.Repository = new Repository(this.WorkDir);
            }
            catch (Exception ex)
            {
                this._DisposeSafe(ex);
                throw;
            }
        }

        public FileMetadata GetMetadata(string path, bool isDirectory, DateTimeOffset baseTimeStamp)
        {
            path = Parser.NormalizeUnixStylePathWithRemovingRelativeDirectoryElements(path);

            if (path.StartsWith("/")) path = path.Substring(1);

            IEnumerable<LogEntry> logEntryList = this.Repository.Commits.QueryBy(path, new CommitFilter() { SortBy = CommitSortStrategies.Topological });

            var sortForFirst = logEntryList.OrderBy(x => x.Commit.Author.When);
            var sortForLast = logEntryList.OrderByDescending(x => x.Commit.Author.When);

            var first = sortForFirst.First().Commit;

            var last = sortForLast.Where(x => x.Commit.Author.When <= baseTimeStamp).First().Commit;

            FileAuthorMetadata author = new FileAuthorMetadata()
            {
                AuthorEmail = last.Author.Email,
                AuthorName = last.Author.Name,
                AuthorTimeStamp = last.Author.When,

                CommitterEmail = last.Committer.Email,
                CommitterName = last.Committer.Name,
                CommitterTimeStamp = last.Committer.When,

                Message = last.Message,
                CommitId = last.Id.ToString(),
            };

            return new FileMetadata(isDirectory: isDirectory,
                creationTime: first.Author.When,
                lastWriteTime: last.Author.When,
                lastAccessTime: last.Author.When,
                author: author);
        }

        public Task<GitCommit> FindCommitAsync(string commitId, CancellationToken cancel = default)
        {
            return this.RunCriticalProcessAsync(true, cancel, c =>
            {
                ObjectId objId = new ObjectId(commitId);

                Commit commit = this.Repository.Lookup<Commit>(objId);

                if (commit == null)
                    throw new ArgumentException($"Commit \"{objId.Sha}\" not found.");

                GitCommit ret = new GitCommit(this, commit);

                return Task.FromResult(ret);
            });
        }

        public Task FetchAsync(string url, CancellationToken cancel = default)
        {
            return this.RunCriticalProcessAsync(true, cancel, c =>
            {
                this.Repository.Network.Fetch(url, new string[0]);
                return Task.CompletedTask;
            });
        }

        protected override void DisposeImpl(Exception ex)
        {
            try
            {
                this.Repository._DisposeSafe();
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }
    }
}

#endif // CORES_BASIC_GIT

