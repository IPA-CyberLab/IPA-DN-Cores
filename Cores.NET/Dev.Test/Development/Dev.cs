// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// 開発中のクラスの一時置き場

#if true

#pragma warning disable CA2235 // Mark all non-serializable fields

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public class FileHistoryManagerPolicy : IValidatable
    {
        IReadOnlyList<PolicyEntry> List => InternalList;

        List<PolicyEntry> InternalList = new List<PolicyEntry>();

        public class PolicyEntry
        {
            public TimeSpan ThresholdSinceNow { get; }
            public TimeSpan IntervalBetweenNextFile { get; }
            public string Guid { get; }

            public PolicyEntry(TimeSpan thresholdSinceNow, TimeSpan intervalBetweenNextFile)
            {
                if (thresholdSinceNow < new TimeSpan(0)) throw new ArgumentOutOfRangeException(nameof(thresholdSinceNow));
                if (intervalBetweenNextFile < new TimeSpan(0, 0, 1)) throw new ArgumentOutOfRangeException(nameof(intervalBetweenNextFile));

                ThresholdSinceNow = thresholdSinceNow;
                IntervalBetweenNextFile = intervalBetweenNextFile;
                Guid = Str.NewGuid();
            }

            public override bool Equals(object? obj)
            {
                return this.Guid.Equals(((PolicyEntry)obj!).Guid);
            }

            public override int GetHashCode()
                => this.Guid.GetHashCode();
        }

        // 空ポリシー
        public FileHistoryManagerPolicy() { }

        // 標準的ポリシー
        public FileHistoryManagerPolicy(EnsureSpecial standard)
        {
            Add(new PolicyEntry(new TimeSpan(24, 0, 0), new TimeSpan(1, 0, 0)));

            Add(new PolicyEntry(new TimeSpan(7, 0, 0, 0), new TimeSpan(1, 0, 0, 0)));

            Add(new PolicyEntry(new TimeSpan(30, 0, 0, 0), new TimeSpan(7, 0, 0, 0)));

            Add(new PolicyEntry(TimeSpan.MaxValue, new TimeSpan(30, 0, 0, 0)));
        }

        public void Add(PolicyEntry e)
        {
            InternalList.Add(e);

            InternalList.Sort((x, y) => x.ThresholdSinceNow.CompareTo(y.ThresholdSinceNow));
        }

        public void Validate()
        {
            if (this.List.Where(x => x.ThresholdSinceNow == TimeSpan.MaxValue).Any() == false)
                throw new CoresException("There is no policy entry to describe TimeSpan.MaxValue.");
        }

        public PolicyEntry GetPolicyEntry(TimeSpan thresholdSinceNow)
        {
            foreach (var e in this.InternalList)
            {
                if (thresholdSinceNow <= e.ThresholdSinceNow)
                    return e;
            }

            throw new CoresException("There is no policy entry to describe TimeSpan.MaxValue.");
        }

        public static FileHistoryManagerPolicy GetTestPolicy()
        {
            FileHistoryManagerPolicy ret = new FileHistoryManagerPolicy();

            ret.Add(new PolicyEntry(new TimeSpan(0, 0, 10), new TimeSpan(0, 0, 3)));
            ret.Add(new PolicyEntry(new TimeSpan(0, 0, 30), new TimeSpan(0, 0, 10)));
            ret.Add(new PolicyEntry(new TimeSpan(0, 2, 0), new TimeSpan(0, 0, 30)));
            ret.Add(new PolicyEntry(TimeSpan.MaxValue, new TimeSpan(0, 1, 0)));

            return ret;
        }
    }

    public class FileHistoryManagerOptions
    {
        public FileHistoryManagerPolicy Policy { get; }
        public Func<string, ResultOrError<DateTimeOffset>> PathToDateTime { get; }

        public FileHistoryManagerOptions(Func<string, ResultOrError<DateTimeOffset>> pathToDateTime, FileHistoryManagerPolicy? policy = null)
        {
            if (policy == null) policy = new FileHistoryManagerPolicy(EnsureSpecial.Yes);

            policy.Validate();

            this.Policy = policy;

            this.PathToDateTime = pathToDateTime;
        }
    }

    // 時刻が進むにつれて良い具合に適当に古い不要ファイルを間引いて消してくれる履歴ファイルマネージャ
    public class FileHistoryManager
    {
        public FileHistoryManagerOptions Options { get; }

        public FileHistoryManager(FileHistoryManagerOptions options)
        {
            this.Options = options;
        }

        class Entry
        {
            public DateTimeOffset TimeStamp { get; }
            public string FullPath { get; }
            public TimeSpan TimeSpanSinceNow { get; }

            public Entry(DateTimeOffset timeStamp, string fullPath, DateTimeOffset now)
            {
                TimeStamp = timeStamp;
                FullPath = fullPath;
                TimeSpanSinceNow = (now - timeStamp);
            }
        }

        // ファイルパス一覧からエントリ一覧の生成
        List<Entry> GetEntryListFromFilePathList(IEnumerable<string> pathList, DateTimeOffset now)
        {
            List<Entry> ret = new List<Entry>();

            foreach (string path in pathList.Distinct())
            {
                ResultOrError<DateTimeOffset>? result = null;

                try
                {
                    result = Options.PathToDateTime(path);
                }
                catch { }

                if (result != null && result.IsOk)
                {
                    // 現在時刻よりも古いファイルのみを列挙の対象とする (現在時刻よりも新しいファイルは存在しないものとみなす)
                    if (result.Value <= now)
                    {
                        Entry e = new Entry(result.Value, path, now);

                        ret.Add(e);
                    }
                }
            }

            return ret;
        }

        // 現在存在しているファイル一覧と新たに作成しようとしている最新のファイル名を入力し、そのファイルを作成するべきかどうかを判断するメソッド
        public bool DetermineIsNewFileToCreate(IEnumerable<string> existingPathList, string newPath, DateTimeOffset now = default)
        {
            if (now == default) now = DateTimeOffset.Now;

            var currentList = GetEntryListFromFilePathList(existingPathList, now).OrderByDescending(x => x.TimeStamp);

            ResultOrError<DateTimeOffset>? newDt = null;
            try
            {
                newDt = Options.PathToDateTime(newPath);
            }
            catch { }

            if (newDt != null && newDt.IsOk)
            {
                // 現存ファイルリスト中にある一番新しいファイル
                Entry? latest = currentList.FirstOrDefault();

                if (latest == null) return true; // ファイルが 1 つもない

                if (latest.TimeStamp >= newDt)
                {
                    // 現存するファイルのほうが新しい
                    return false;
                }

                // 現在時刻からの経過時間をもとに適用すべきポリシーを取得する
                var policy = this.Options.Policy.GetPolicyEntry(newDt.Value - DateTime.Now);

                if ((newDt - latest.TimeStamp) < policy.IntervalBetweenNextFile)
                {
                    // ポリシーで指定された間隔以下しか時間差がないので保存をしない
                    return false;
                }

                // 保存をする
                return true;
            }
            else
            {
                // パースに失敗
                return false;
            }
        }

        // 現在存在しているファイル一覧を入力し、削除すべきファイル一覧のリストを出力するメソッド
        public List<string> GenerateFileListToDelete(IEnumerable<string> existingPathList, DateTimeOffset now = default)
        {
            List<string> ret = new List<string>();

            if (now == default) now = DateTimeOffset.Now;

            var currentList = GetEntryListFromFilePathList(existingPathList, now).OrderByDescending(x => x.TimeStamp);

            // 現在存在する全ファイルに対して適用されるポリシーごとにリストを作成する
            // (時刻の逆順になっているはずである)
            Dictionary<FileHistoryManagerPolicy.PolicyEntry, List<Entry>> groupByPolicy = new Dictionary<FileHistoryManagerPolicy.PolicyEntry, List<Entry>>();

            foreach (Entry e in currentList)
            {
                FileHistoryManagerPolicy.PolicyEntry policy = this.Options.Policy.GetPolicyEntry(e.TimeSpanSinceNow);

                if (groupByPolicy.TryGetValue(policy, out List<Entry>? list) == false)
                {
                    list = new List<Entry>();
                    groupByPolicy.Add(policy, list);
                }

                list!.Add(e);
            }

            // それぞれのポリシー内で各ファイルを削除するかどうかの判定を行なう
            foreach (var policy in groupByPolicy.Keys)
            {
                List<Entry> list = groupByPolicy[policy];

                // 古い順
                List<Entry> list2 = list.OrderBy(x => x.TimeStamp).ToList();

                Entry? std = null;

                foreach (var cur in list2)
                {
                    if (std == null)
                    {
                        // 基準ファイルの選定
                        std = cur;
                    }
                    else
                    {
                        // 基準ファイルとの時差を計算する
                        TimeSpan interval = cur.TimeStamp - std.TimeStamp;

                        if (interval < policy.IntervalBetweenNextFile)
                        {
                            // 時差が少なすぎるのでこのファイルは削除リストに投入する
                            ret.Add(cur.FullPath);
                        }
                        else
                        {
                            // 時差が大きいので このファイルを新たな基準ファイルとして選定し、削除はしない
                            std = cur;
                        }
                    }
                }
            }

            // 念のため Distinct をしてから返す
            ret = ret.Distinct().ToList();

            return ret;
        }
    }
}

#endif

