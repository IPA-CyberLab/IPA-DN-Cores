using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;

using IPA.Cores.Helper.Basic;

namespace IPA.Cores.Basic
{
    class SubnetSpaceSubnet<T> where T: class
    {
        public IPAddr Address;
        public int SubnetLength;
        public List<T> DataList;

        public T GetDataFirst() => this.DataList.GetFirstOrNull();

        internal List<(int sort_key, T data)> tmp_sort_list = new List<(int sort_key, T data)>();

        int hash_code;

        public SubnetSpaceSubnet() { }

        public SubnetSpaceSubnet(IPAddr address, int subnet_len, T data) : this(address, subnet_len, new T[] { data }.ToList()) { }

        public SubnetSpaceSubnet(IPAddr address, int subnet_len, List<T> data_list = null)
        {
            this.Address = address;
            this.SubnetLength = subnet_len;
            this.DataList = data_list;

            if (this.DataList == null)
            {
                this.DataList = new List<T>();
            }

            if (this.SubnetLength > FullRoute.GetMaxSubnetSize(Address.AddressFamily))
            {
                throw new ApplicationException("subnet_len is too large.");
            }

            byte[] addr_bytes = this.Address.GetBytes();

            if (addr_bytes.Length == 4)
            {
                hash_code = BitConverter.ToInt32(addr_bytes, 0);
                hash_code ^= SubnetLength;
            }
            else if (addr_bytes.Length == 16)
            {
                hash_code = 0;

                hash_code ^= BitConverter.ToInt32(addr_bytes, 0);
                hash_code ^= BitConverter.ToInt32(addr_bytes, 4);
                hash_code ^= BitConverter.ToInt32(addr_bytes, 8);
                hash_code ^= BitConverter.ToInt32(addr_bytes, 12);
                hash_code ^= SubnetLength;
            }
            else
            {
                throw new ApplicationException("addr_bytes.Length");
            }
        }

        public ulong CalcNumIPs()
        {
            if (Address.AddressFamily == AddressFamily.InterNetwork)
            {
                return (ulong)(1UL << (32 - this.SubnetLength));
            }
            else
            {
                int v = 64 - this.SubnetLength;
                if (v < 0)
                {
                    v = 0;
                }
                return (ulong)(1UL << v);
            }
        }

        public override string ToString() => this.Address.ToString() + "/" + this.SubnetLength.ToString();

        public override int GetHashCode() => hash_code;

        public override bool Equals(object obj)
        {
            SubnetSpaceSubnet<T> other = obj as SubnetSpaceSubnet<T>;
            if (other == null) return false;
            return this.Address.Equals(other.Address) && (this.SubnetLength == other.SubnetLength);
        }

        public string GetBinaryString()
            => this.Address.GetBinaryString().Substring(0, this.SubnetLength);

        public byte[] GetBinaryBytes()
            => Util.CopyByte(this.Address.GetBinaryBytes(), 0, this.SubnetLength);

        public bool Contains(IPAddr target)
        {
            if (this.Address.AddressFamily != target.AddressFamily) return false;

            string target_str = target.GetBinaryString();
            string subnet_str = this.GetBinaryString();

            return target_str.StartsWith(subnet_str);
        }

        public static int CompareBySubnetLength(SubnetSpaceSubnet<T> x, SubnetSpaceSubnet<T> y)
        {
            return x.SubnetLength.CompareTo(y.SubnetLength);
        }
    }

    class SubnetSpace<T> where T : class
    {
        public AddressFamily AddressFamily;
        public RadixTrie<SubnetSpaceSubnet<T>> Trie;
        bool is_readonly = false;

        public SubnetSpace()
        {
            is_readonly = true;
        }

        public SubnetSpace(AddressFamily address_family)
        {
            is_readonly = false;
            this.AddressFamily = address_family;
        }

        // バッチ処理のためサブネット情報を追加する
        List<(IPAddress address, int subnet_length, T data, int data_sort_key)> batch_add_list = new List<(IPAddress address, int subnet_length, T data, int data_sort_key)>();
        public void BatchAddOne(IPAddress address, int subnet_length, T data, int data_sort_key)
        {
            if (is_readonly) throw new ApplicationException("The SubnetSpace object is readonly.");

            batch_add_list.Add((address, subnet_length, data, data_sort_key));
        }

        public void BatchAddFinish()
        {
            if (is_readonly) throw new ApplicationException("The SubnetSpace object is readonly.");

            SetData(batch_add_list.ToArray());
            batch_add_list.Clear();
        }

        // サブネット情報を投入する
        public void SetData((IPAddress address, int subnet_length, T data, int data_sort_key)[] items)
        {
            if (is_readonly) throw new ApplicationException("The SubnetSpace object is readonly.");

            // 重複するものを 1 つにまとめる
            Distinct<SubnetSpaceSubnet<T>> distinct = new Distinct<SubnetSpaceSubnet<T>>();

            foreach (var item in items)
            {
                SubnetSpaceSubnet<T> s = new SubnetSpaceSubnet<T>(IPAddr.FromAddress(item.address), item.subnet_length);

                s = distinct.AddOrGet(s);

                s.tmp_sort_list.Add((item.data_sort_key, item.data));
            }

            SubnetSpaceSubnet<T>[] subnets = distinct.Values;

            foreach (SubnetSpaceSubnet<T> subnet in subnets)
            {
                // tmp_sort_list の内容を sort_key に基づき逆ソートする
                subnet.tmp_sort_list.Sort((a, b) =>
                {
                    return -a.sort_key.CompareTo(b.sort_key);
                });

                // ソート済みオブジェクトを順に保存する
                subnet.DataList = new List<T>();
                foreach (var a in subnet.tmp_sort_list)
                {
                    subnet.DataList.Add(a.data);
                }
            }

            List<SubnetSpaceSubnet<T>> subnets_list = subnets.ToList();

            subnets_list.Sort(SubnetSpaceSubnet<T>.CompareBySubnetLength);

            var trie = new RadixTrie<SubnetSpaceSubnet<T>>();

            foreach (var subnet in subnets_list)
            {
                var node = trie.Insert(subnet.GetBinaryBytes());

                node.Object = subnet;
            }

            this.Trie = trie;

            is_readonly = true;
        }

        // 検索をする
        public SubnetSpaceSubnet<T> Lookup(IPAddress address)
        {
            if (address.AddressFamily != this.AddressFamily)
            {
                throw new ApplicationException("addr.AddressFamily != this.AddressFamily");
            }

            RadixTrie<SubnetSpaceSubnet<T>> trie = this.Trie;

            if (trie == null)
            {
                return null;
            }

            byte[] key = IPAddr.FromAddress(address).GetBinaryBytes();
            RadixNode<SubnetSpaceSubnet<T>> n = trie.Lookup(key);
            if (n == null)
            {
                return null;
            }

            n = n.TraverseParentNonNull();
            if (n == null)
            {
                return null;
            }

            return n.Object;
        }
    }
}

