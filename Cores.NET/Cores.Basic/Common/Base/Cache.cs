// IPA Cores.NET
// 
// Copyright (c) 2018- IPA CyberLab.
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

using System;
using System.Collections.Generic;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    public enum CacheType
    {
        UpdateExpiresWhenAccess = 0,
        DoNotUpdateExpiresWhenAccess = 1,
    }

    public class Cache<TKey, TValue>
    {
        class Entry
        {
            public DateTime CreatedDateTime { get; }
            public DateTime UpdatedDateTime { get; private set; }
            public DateTime LastAccessedDateTime { get; private set; }
            public TKey Key { get; }

            TValue _value;
            public TValue Value
            {
                get
                {
                    LastAccessedDateTime = Time.NowHighResDateTimeUtc;
                    return this._value;
                }
                set
                {
                    this._value = value;
                    UpdatedDateTime = Time.NowHighResDateTimeUtc;
                    LastAccessedDateTime = Time.NowHighResDateTimeUtc;
                }
            }

            public Entry(TKey key, TValue value)
            {
                this.Key = key;
                this._value = value;
                LastAccessedDateTime = UpdatedDateTime = CreatedDateTime = Time.NowHighResDateTimeUtc;
            }

            public override int GetHashCode() => Key.GetHashCode();

            public override string ToString() => Key.ToString() + "," + _value.ToString();
        }

        public static readonly TimeSpan DefaultExpireSpan = new TimeSpan(0, 5, 0);
        public const CacheType DefaultCacheType = CacheType.UpdateExpiresWhenAccess;
        public TimeSpan ExpireSpan { get; private set; }
        public CacheType Type { get; private set; }
        Dictionary<TKey, Entry> list;
        CriticalSection LockObj;

        public Cache()
        {
            InternalInit(DefaultExpireSpan, DefaultCacheType);
        }
        public Cache(CacheType type)
        {
            InternalInit(DefaultExpireSpan, type);
        }
        public Cache(TimeSpan expireSpan)
        {
            InternalInit(expireSpan, DefaultCacheType);
        }
        public Cache(TimeSpan expireSpan, CacheType type)
        {
            InternalInit(expireSpan, type);
        }
        void InternalInit(TimeSpan expireSpan, CacheType type)
        {
            this.ExpireSpan = expireSpan;
            this.Type = type;

            list = new Dictionary<TKey, Entry>();
            LockObj = new CriticalSection();
        }

        public void Add(TKey key, TValue value)
        {
            lock (LockObj)
            {
                Entry e;

                DeleteExpired();

                if (list.ContainsKey(key) == false)
                {
                    e = new Entry(key, value);

                    list.Add(e.Key, e);

                    DeleteExpired();
                }
                else
                {
                    e = list[key];
                    e.Value = value;
                }
            }
        }

        public void Delete(TKey key)
        {
            lock (LockObj)
            {
                if (list.ContainsKey(key))
                {
                    list.Remove(key);
                }
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                lock (LockObj)
                {
                    DeleteExpired();

                    if (list.ContainsKey(key) == false)
                    {
                        return default(TValue);
                    }

                    return list[key].Value;
                }
            }
        }

        public TValue GetOrCreate(TKey key, Func<TKey, TValue> createProc)
        {
            lock (LockObj)
            {
                DeleteExpired();

                if (list.ContainsKey(key) == false)
                {
                    TValue newValue = createProc(key);

                    Add(key, newValue);

                    return newValue;
                }

                return list[key].Value;
            }
        }

        long LastDeleted = 0;

        void DeleteExpired()
        {
            bool doDelete = false;
            long now = Tick64.Value;
            long deleteInterval = ExpireSpan.Milliseconds / 10;

            lock (LockObj)
            {
                if (LastDeleted == 0 || now > (LastDeleted + deleteInterval))
                {
                    LastDeleted = now;
                    doDelete = true;
                }
            }

            if (doDelete == false)
            {
                return;
            }

            lock (LockObj)
            {
                List<Entry> o = new List<Entry>();
                DateTime expire = Time.NowHighResDateTimeUtc - this.ExpireSpan;

                foreach (Entry e in list.Values)
                {
                    if (this.Type == CacheType.UpdateExpiresWhenAccess)
                    {
                        if (e.LastAccessedDateTime < expire)
                        {
                            o.Add(e);
                        }
                    }
                    else
                    {
                        if (e.UpdatedDateTime < expire)
                        {
                            o.Add(e);
                        }
                    }
                }

                foreach (Entry e in o)
                {
                    list.Remove(e.Key);
                }
            }
        }
    }
}
