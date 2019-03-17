using System;
using System.Collections.Generic;

namespace IPA.Cores.Basic
{
    enum CacheType
    {
        UpdateExpiresWhenAccess = 0,
        DoNotUpdateExpiresWhenAccess = 1,
    }

    class Cache<TKey, TValue>
    {
        class Entry
        {
            DateTime createdDateTime;
            public DateTime CreatedDateTime
            {
                get { return createdDateTime; }
            }
            DateTime updatedDateTime;
            public DateTime UpdatedDateTime
            {
                get { return updatedDateTime; }
            }
            DateTime lastAccessedDateTime;
            public DateTime LastAccessedDateTime
            {
                get { return lastAccessedDateTime; }
            }

            TKey key;
            public TKey Key
            {
                get
                {
                    return key;
                }
            }

            TValue value;
            public TValue Value
            {
                get
                {
                    lastAccessedDateTime = Time.NowDateTimeUtc;
                    return this.value;
                }
                set
                {
                    this.value = value;
                    updatedDateTime = Time.NowDateTimeUtc;
                    lastAccessedDateTime = Time.NowDateTimeUtc;
                }
            }

            public Entry(TKey key, TValue value)
            {
                this.key = key;
                this.value = value;
                lastAccessedDateTime = updatedDateTime = createdDateTime = Time.NowDateTimeUtc;
            }

            public override int GetHashCode()
            {
                return key.GetHashCode();
            }

            public override string ToString()
            {
                return key.ToString() + "," + value.ToString();
            }
        }

        public static readonly TimeSpan DefaultExpireSpan = new TimeSpan(0, 5, 0);
        public const CacheType DefaultCacheType = CacheType.UpdateExpiresWhenAccess;

        TimeSpan expireSpan;
        public TimeSpan ExpireSpan
        {
            get { return expireSpan; }
        }
        CacheType type;
        public CacheType Type
        {
            get { return type; }
        }
        Dictionary<TKey, Entry> list;
        object lockObj;

        public Cache()
        {
            init(DefaultExpireSpan, DefaultCacheType);
        }
        public Cache(CacheType type)
        {
            init(DefaultExpireSpan, type);
        }
        public Cache(TimeSpan expireSpan)
        {
            init(expireSpan, DefaultCacheType);
        }
        public Cache(TimeSpan expireSpan, CacheType type)
        {
            init(expireSpan, type);
        }
        void init(TimeSpan expireSpan, CacheType type)
        {
            this.expireSpan = expireSpan;
            this.type = type;

            list = new Dictionary<TKey, Entry>();
            lockObj = new object();
        }

        public void Add(TKey key, TValue value)
        {
            lock (lockObj)
            {
                Entry e;

                deleteExpired();

                if (list.ContainsKey(key) == false)
                {
                    e = new Entry(key, value);

                    list.Add(e.Key, e);

                    deleteExpired();
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
            lock (lockObj)
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
                lock (lockObj)
                {
                    deleteExpired();

                    if (list.ContainsKey(key) == false)
                    {
                        return default(TValue);
                    }

                    return list[key].Value;
                }
            }
        }

        long last_deleted = 0;

        void deleteExpired()
        {
            bool do_delete = false;
            long now = Tick64.Value;
            long delete_inveral = expireSpan.Milliseconds / 10;

            lock (lockObj)
            {
                if (last_deleted == 0 || now > (last_deleted + delete_inveral))
                {
                    last_deleted = now;
                    do_delete = true;
                }
            }

            if (do_delete == false)
            {
                return;
            }

            lock (lockObj)
            {
                List<Entry> o = new List<Entry>();
                DateTime expire = Time.NowDateTimeUtc - this.expireSpan;

                foreach (Entry e in list.Values)
                {
                    if (this.type == CacheType.UpdateExpiresWhenAccess)
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
