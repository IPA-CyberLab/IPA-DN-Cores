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
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

using IPA.Cores.Basic;
using IPA.Cores.Basic.DnsLib;
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Net.Sockets;

using Newtonsoft.Json;
using System.Data;
using System.Reflection;

namespace IPA.Cores.Basic
{
    public class HadbCodeTest
    {
        public class Dyn : HadbDynamicConfig
        {
            public string Hello { get; set; } = "";
        }

        public string SystemName;

        public HadbCodeTest()
        {
            this.SystemName = ("HADB_CODE_TEST_" + Str.DateTimeToYymmddHHmmssLong(DtNow) + "_" + Env.MachineName + "_" + Str.GenerateRandomDigit(8)).ToUpper();
        }

        public class Sys : HadbSqlBase<Mem, Dyn>
        {
            public Sys(HadbSqlSettings settings, Dyn dynamicConfig) : base(settings, dynamicConfig) { }
        }

        public class User : HadbData
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string AuthKey { get; set; } = "";
            public string FullName { get; set; } = "";
            public string Company { get; set; } = "";
            public string LastIp { get; set; } = "";

            public int Int1 { get; set; } = 0;

            public override void Normalize()
            {
                this.Id = this.Id._NonNullTrim();
                this.Name = this.Name._NonNullTrim();
                this.AuthKey = this.AuthKey._NonNullTrim();
                this.Company = this.Company._NonNullTrim();
                this.FullName = this.FullName._NonNullTrim();
                this.LastIp = this.LastIp._NormalizeIp();
            }

            public override HadbKeys GetKeys()
            {
                return new HadbKeys(this.Id, this.Name, this.AuthKey);
            }

            public override HadbLabels GetLabels()
            {
                return new HadbLabels(this.Company, this.LastIp);
            }
        }

        public class Mem : HadbMemDataBase
        {
            protected override List<Type> GetDefinedUserDataTypesImpl()
            {
                List<Type> ret = new List<Type>();
                ret.Add(typeof(User));
                return ret;
            }
        }


        public const string TestDbServer = "10.40.0.103";
        public const string TestDbName = "TEST_DN_DBSVC1";
        public const string TestDbReadUser = "sql_test_dn_dbsvc1_reader";
        public const string TestDbWriteUser = "sql_test_dn_dbsvc1_writer";
        public const string TestDbPassword = "testabc";

        public async Task Test1Async()
        {
            var settings = new HadbSqlSettings(SystemName,
                new SqlDatabaseConnectionSetting(TestDbServer, TestDbName, TestDbReadUser, TestDbPassword),
                new SqlDatabaseConnectionSetting(TestDbServer, TestDbName, TestDbWriteUser, TestDbPassword))
            {
                Debug_NoAutoDbUpdate = true,
            };

            await using Sys sys1 = new Sys(settings, new Dyn() { Hello = "Hello World" });
            await using Sys sys2 = new Sys(settings, new Dyn() { Hello = "Hello World" });

            sys1.Start();
            await sys1.WaitUntilReadyForAtomicAsync();

            sys2.Start();
            await sys2.WaitUntilReadyForAtomicAsync();

            // Dynamic Config が DB に正しく反映されているか
            await sys1.TranAsync(true, async tran =>
            {
                var db = (tran as HadbSqlBase<Mem, Dyn>.HadbSqlTran)!.Db;

                var rows = await db.EasySelectAsync<HadbSqlConfigRow>("select * from HADB_CONFIG where CONFIG_SYSTEMNAME = @CONFIG_SYSTEMNAME", new
                {
                    CONFIG_SYSTEMNAME = SystemName,
                });

                Dbg.TestTrue((rows.Where(x => x.CONFIG_NAME == "HadbReloadIntervalMsecsLastOk").Single()).CONFIG_VALUE._ToInt() == Consts.HadbDynamicConfigDefaultValues.HadbReloadIntervalMsecsLastOk);
                Dbg.TestTrue((rows.Where(x => x.CONFIG_NAME == "Hello").Single()).CONFIG_VALUE == "Hello World");

                var helloRow = rows.Where(x => x.CONFIG_NAME == "Hello").Single();
                helloRow.CONFIG_VALUE = "Neko";
                await db.EasyUpdateAsync(helloRow);

                return true;
            });

            // DB の値を変更した後、Dynamic Config が正しくメモリに反映されるか
            await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
            Dbg.TestTrue(sys1.CurrentDynamicConfig.Hello == "Neko");

            await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
            Dbg.TestTrue(sys2.CurrentDynamicConfig.Hello == "Neko");

            if (true)
            {
                string u1_uid = "";
                string u2_uid = "";
                string u3_uid = "";

                await sys1.TranAsync(true, async tran =>
                {
                    User u = new User() { Id = "u1", Name = "User1", AuthKey = "a001", Company = "NTT", LastIp = "A123:b456:0001::c789", FullName = "Tanaka", Int1 = 100 };
                    var obj = await tran.AtomicAddAsync(u);
                    u1_uid = obj.Uid;
                    return true;
                });

                {
                    await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys2.FastGet<User>(u1_uid);
                    var u = obj!.GetData<User>();
                    Dbg.TestTrue(u.Id == "u1");
                    Dbg.TestTrue(u.Name == "User1");
                    Dbg.TestTrue(u.AuthKey == "a001");
                    Dbg.TestTrue(u.Company == "NTT");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                }

                await sys1.TranAsync(true, async tran =>
                {
                    User u = new User() { Id = "u2", Name = "User2", AuthKey = "a002", Company = "NTT", LastIp = "af80:b456:0001::c789", FullName = "Yamada", Int1 = 200 };
                    var obj = await tran.AtomicAddAsync(u);
                    u2_uid = obj.Uid;
                    return true;
                });

                await sys1.TranAsync(true, async tran =>
                {
                    User u = new User() { Id = "u3", Name = "User3", AuthKey = "a003", Company = "IPA", LastIp = "A123:b456:0001::c789", FullName = "Unagi", Int1 = 300 };
                    var obj = await tran.AtomicAddAsync(u);
                    u3_uid = obj.Uid;
                    return true;
                });

                {
                    await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys2.FastSearchByKey<User>(new HadbKeys("", "", "A003"));
                    var u = obj!.GetData<User>();
                    Dbg.TestTrue(u.Id == "u3");
                    Dbg.TestTrue(u.Name == "User3");
                    Dbg.TestTrue(u.AuthKey == "a003");
                    Dbg.TestTrue(u.Company == "IPA");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                }

                await sys1.TranAsync(false, async tran =>
                {
                    var obj = await tran.AtomicGetAsync<User>(u1_uid);
                    var u = obj!.GetData<User>();
                    Dbg.TestTrue(u.Id == "u1");
                    Dbg.TestTrue(u.Name == "User1");
                    Dbg.TestTrue(u.AuthKey == "a001");
                    Dbg.TestTrue(u.Company == "NTT");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                    return false;
                });

                await sys1.TranAsync(false, async tran =>
                {
                    var obj = await tran.AtomicSearchByKeyAsync<User>(new HadbKeys("", "uSER2"));
                    var u = obj!.GetData<User>();
                    Dbg.TestTrue(u.Id == "u2");
                    Dbg.TestTrue(u.Name == "User2");
                    Dbg.TestTrue(u.AuthKey == "a002");
                    Dbg.TestTrue(u.Company == "NTT");
                    Dbg.TestTrue(u.LastIp == "af80:b456:1::c789");
                    return false;
                });

                await sys1.TranAsync(false, async tran =>
                {
                    var obj = await tran.AtomicSearchByLabelsAsync(new User { Company = "ntt" });
                    var list = obj.Select(x => x.GetData<User>()).OrderBy(x => x.Id, StrComparer.IgnoreCaseTrimComparer);
                    Dbg.TestTrue(list.Count() == 2);
                    var list2 = list.ToArray();
                    Dbg.TestTrue(list2[0].Id == "u1");
                    Dbg.TestTrue(list2[1].Id == "u2");
                    return false;
                });


                // 高速更新のテスト
                {
                    await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys2.FastSearchByLabels(new User { Company = "ipa" }).Single();
                    var u = obj.GetData<User>();
                    Dbg.TestTrue(u.Id == "u3");
                    Dbg.TestTrue(u.Name == "User3");
                    Dbg.TestTrue(u.AuthKey == "a003");
                    Dbg.TestTrue(u.Company == "IPA");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                    obj.FastUpdate<User>(x =>
                    {
                        x.Int1++;
                        return true;
                    });

                    await sys2.LazyUpdateCoreAsync(EnsureSpecial.Yes);
                }

                {
                    await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys2.FastSearchByLabels(new User { Company = "ntt", LastIp = "A123:b456:0001:0000:0000:0000:0000:c789" }).Single();
                    var u = obj.GetData<User>();
                    Dbg.TestTrue(u.Id == "u1");
                    Dbg.TestTrue(u.Name == "User1");
                    Dbg.TestTrue(u.AuthKey == "a001");
                    Dbg.TestTrue(u.Company == "NTT");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                    obj.FastUpdate<User>(x =>
                    {
                        x.Int1++;
                        return true;
                    });
                    await sys2.LazyUpdateCoreAsync(EnsureSpecial.Yes);
                }

                await sys1.ReloadCoreAsync(EnsureSpecial.Yes);

                {
                    await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys1.FastSearchByLabels(new User { Company = "ipa" }).Single();
                    var u = obj.GetData<User>();
                    Dbg.TestTrue(u.Id == "u3");
                    Dbg.TestTrue(u.Name == "User3");
                    Dbg.TestTrue(u.AuthKey == "a003");
                    Dbg.TestTrue(u.Company == "IPA");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                    Dbg.TestTrue(u.Int1 == 301);
                    obj.FastUpdate<User>(x =>
                    {
                        x.Int1++;
                        return true;
                    });
                    await sys1.LazyUpdateCoreAsync(EnsureSpecial.Yes);
                }

                {
                    await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys1.FastSearchByLabels(new User { Company = "ntt", LastIp = "A123:b456:0001:0000:0000:0000:0000:c789" }).Single();
                    var u = obj.GetData<User>();
                    Dbg.TestTrue(u.Id == "u1");
                    Dbg.TestTrue(u.Name == "User1");
                    Dbg.TestTrue(u.AuthKey == "a001");
                    Dbg.TestTrue(u.Company == "NTT");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                    obj.FastUpdate<User>(x =>
                    {
                        x.Int1++;
                        return true;
                    });
                    await sys1.LazyUpdateCoreAsync(EnsureSpecial.Yes);
                }

                // sys2 でデータ編集 途中で Abort してメモリに影響がないかどうか確認
                try
                {
                    await sys2.TranAsync(true, async tran =>
                    {
                        var obj = await tran.AtomicSearchByKeyAsync(new User { AuthKey = "  A001  " });
                        var u = obj!.GetData<User>();
                        Dbg.TestTrue(u.FullName == "Tanaka");
                        Dbg.TestTrue(u.Int1 == 102);

                        u.Company = "SoftEther";
                        u.AuthKey = "x001";
                        u.Id = "new_u1";
                        u.Name = "User1New";
                        u.FullName = "Neko";
                        u.LastIp = "2001:af80:0000::0001";
                        u.Int1++;

                        throw new CoresException("Abort!");
                        //return true;
                    });
                }
                catch { }

                Dbg.TestTrue(sys2.MemDb!._IndexedKeysTable.Count == (4 * 3));
                Dbg.TestTrue(sys2.MemDb!._IndexedLabelsTable.Sum(x => x.Value.Count) == (2 * 3));

                {
                    // AtomicSearchByKeyAsync を実行した結果 sys2 のメモリが更新されていないことを検査
                    var obj = sys2.FastSearchByLabels(new User { Company = "ntt", LastIp = "A123:b456:0001:0000:0000:0000:0000:c789" }).Single();
                    var u = obj.GetData<User>();
                    Dbg.TestTrue(u.Id == "u1");
                    Dbg.TestTrue(u.Name == "User1");
                    Dbg.TestTrue(u.AuthKey == "a001");
                    Dbg.TestTrue(u.Company == "NTT");
                    Dbg.TestTrue(u.LastIp == "a123:b456:1::c789");
                }

                // sys2 でデータ編集コミット --> sys1 でメモリ更新 --> sys1 で高速更新 --> sys2 で観測できるか?
                await sys2.TranAsync(true, async tran =>
                {
                    var obj = await tran.AtomicSearchByKeyAsync(new User { AuthKey = "  A001  " });
                    var u = obj!.GetData<User>();
                    Dbg.TestTrue(u.FullName == "Tanaka");
                    Dbg.TestTrue(u.Int1 == 102);

                    u.Company = "SoftEther";
                    u.AuthKey = "x001";
                    u.Id = "new_u1";
                    u.Name = "User1New";
                    u.FullName = "Neko";
                    u.LastIp = "2001:af80:0000::0001";
                    u.Int1++;

                    await tran.AtomicUpdateAsync(obj);

                    return true;
                });

                Dbg.TestTrue(sys2.MemDb!._IndexedKeysTable.Count == (4 * 3));
                Dbg.TestTrue(sys2.MemDb!._IndexedLabelsTable.Sum(x => x.Value.Count) == (2 * 3));
                Dbg.TestTrue(sys2.MemDb!._IndexedLabelsTable.Count == (2 * 3));

                {
                    // AtomicSearchByKeyAsync を実行した結果 sys2 のメモリも自動で更新されたかどうか検査
                    var u = sys2.FastSearchByLabels(new User { Company = "softether", LastIp = "2001:af80:0000:0000:0000:0000:0000:0001" }).Single().GetData<User>();
                    Dbg.TestTrue(u.FullName == "Neko");
                    Dbg.TestTrue(u.Int1 == 103);
                }

                await sys1.ReloadCoreAsync(EnsureSpecial.Yes);

                {
                    await sys1.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys1.FastGet<User>(u1_uid);
                    var u = obj!.GetData<User>();
                    Dbg.TestTrue(u.Id == "new_u1");
                    Dbg.TestTrue(u.Name == "User1New");
                    Dbg.TestTrue(u.AuthKey == "x001");
                    Dbg.TestTrue(u.Company == "SoftEther");
                    Dbg.TestTrue(u.LastIp == "2001:af80::1");
                    Dbg.TestTrue(u.Int1 == 103);
                    obj.FastUpdate<User>(x =>
                    {
                        x.Int1++;
                        return true;
                    });
                    await sys1.LazyUpdateCoreAsync(EnsureSpecial.Yes);
                }

                await sys2.ReloadCoreAsync(EnsureSpecial.Yes);

                {
                    await sys2.ReloadCoreAsync(EnsureSpecial.Yes);
                    var obj = sys2.FastGet<User>(u1_uid);
                    var u = obj!.GetData<User>();
                    Dbg.TestTrue(u.Id == "new_u1");
                    Dbg.TestTrue(u.Name == "User1New");
                    Dbg.TestTrue(u.AuthKey == "x001");
                    Dbg.TestTrue(u.Company == "SoftEther");
                    Dbg.TestTrue(u.LastIp == "2001:af80::1");
                    Dbg.TestTrue(u.Int1 == 104);

                    // 追加テスト: キー値またはラベル値の変更でエラーが発生することを確認
                    obj.FastUpdate<User>(x =>
                    {
                        x.AuthKey = " X001 ";
                        return true;
                    });
                    obj.FastUpdate<User>(x =>
                    {
                        x.Company = "SOFTETHER";
                        return true;
                    });
                    Dbg.TestException(() =>
                    {
                        obj.FastUpdate<User>(x =>
                        {
                            x.AuthKey = "CAT";
                            return true;
                        });
                    });
                    Dbg.TestException(() =>
                    {
                        obj.FastUpdate<User>(x =>
                        {
                            x.Company = "NEKO";
                            return true;
                        });
                    });
                }

            }
        }
    }
}

#endif

