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
using IPA.Cores.Basic.Internal;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using Microsoft.AspNetCore.Server.IIS.Core;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace IPA.Cores.Basic
{
    public class XtsAesRandomAccessMetaData
    {
        public int Version;
        public long VirtualSize;
        public string SaltedPassword = "";
        public string MasterKeyEncryptedByPassword = "";
    }

    public class XtsAesRandomAccess : SectorBasedRandomAccessBase<byte>
    {
        public const int XtsAesSectorSize = 4096;
        public const int XtsAesMetaDataSize = 1 * 4096;
        public const int XtsAesKeySize = 64;
        public const string EncryptMetadataHeaderString = "!!__[MetaData:IPA.Cores.Basic.XtsAesRandomAccess]__!!\r\n";
        public static readonly ReadOnlyMemory<byte> EncryptMetadataHeaderData = EncryptMetadataHeaderString._GetBytes_Ascii();

        string CurrentPassword;
        ReadOnlyMemory<byte> CurrentMasterKey;
        XtsAesRandomAccessMetaData CurrentMetaData = null!;
        Xts CurrentXts = null!;
        XtsCryptoTransform CurrentEncrypter = null!;
        XtsCryptoTransform CurrentDescrypter = null!;

        public XtsAesRandomAccess(IRandomAccess<byte> physical, string password, bool disposeObject = false, int metaDataFlushInterval = 0)
            : base(physical, XtsAesSectorSize, XtsAesMetaDataSize, disposeObject, metaDataFlushInterval)
        {
            this.CurrentPassword = password;
        }

        async Task WriteMetaDataAsync(CancellationToken cancel = default)
        {
            // ファイルのヘッダを書き込む
            string jsonString = this.CurrentMetaData._ObjectToJson();

            MemoryBuffer<byte> tmp = new MemoryBuffer<byte>();
            tmp.Write(EncryptMetadataHeaderData);
            tmp.Write(Str.CrLf_Bytes);
            tmp.Write(jsonString._GetBytes_UTF8());
            tmp.Write(Str.CrLf_Bytes);
            tmp.Write(Str.CrLf_Bytes);

            if (tmp.Length > XtsAesMetaDataSize)
            {
                throw new CoresException($"XtsAesRandomAccess: tmp.Length ({tmp.Length}) > XtsAesMetaDataSize");
            }

            int remainSize = XtsAesMetaDataSize - tmp.Length;

            if (remainSize >= 2)
            {
                tmp.Write(new byte[remainSize - 2]);
                tmp.Write(Str.CrLf_Bytes);
            }
            else
            {
                tmp.Write(new byte[remainSize]);
            }

            await this.PhysicalWriteAsync(0, tmp, cancel);
        }

        protected override async Task InitMetadataImplAsync(CancellationToken cancel = default)
        {
            Memory<byte> tmp = new byte[XtsAesMetaDataSize];

            // ヘッダの読み込みを試行する
            int readSize = await this.PhysicalReadAsync(0, tmp, cancel);
            if (readSize == XtsAesMetaDataSize)
            {
                // ファイルの内容が 1 バイト以上存在する
                if (tmp._SliceHead(EncryptMetadataHeaderData.Length)._MemCompare(EncryptMetadataHeaderData) != 0)
                {
                    // ファイル内容が存在し、かつ暗号化されていないファイルである
                    throw new CoresException("XtsAesRandomAccess: The file body is not encrypted file. No headers found.");
                }

                var jsonString = tmp.Slice(EncryptMetadataHeaderData.Length)._GetString_UTF8(untilNullByte: true);

                XtsAesRandomAccessMetaData? metaData = jsonString._JsonToObject<XtsAesRandomAccessMetaData>();
                if (metaData == null)
                    throw new CoresException("XtsAesRandomAccess: The XtsAesRandomAccessMetaData JSON header parse failed.");

                // バージョン番号チェック
                if (metaData.Version != 1)
                    throw new CoresException($"XtsAesRandomAccess: Unsupported version: {metaData.Version}");

                // パスワード検査
                if (Secure.VeritySaltedPassword(metaData.SaltedPassword, this.CurrentPassword) == false)
                    throw new CoresException("XtsAesRandomAccess: Incorrect password.");

                // 秘密鍵解読
                var decrypted = ChaChaPoly.EasyDecryptWithPassword(metaData.MasterKeyEncryptedByPassword._GetHexBytes(), this.CurrentPassword);
                decrypted.ThrowIfException();

                // 秘密鍵サイズ検査
                if (decrypted.Value.Length != XtsAesKeySize)
                    throw new CoresException("XtsAesRandomAccess: decrypted.Value.Length != XtsAesKeySize");

                this.CurrentMasterKey = decrypted.Value;

                this.CurrentMetaData = metaData;
            }
            else if (readSize == 0)
            {
                // ファイルの内容が存在しない

                // マスターキーを新規作成する
                this.CurrentMasterKey = Secure.Rand(XtsAesKeySize);

                // メタデータを新規作成する
                var metaData = new XtsAesRandomAccessMetaData
                {
                    Version = 1,
                    VirtualSize = 0,
                    SaltedPassword = Secure.SaltPassword(this.CurrentPassword),
                    MasterKeyEncryptedByPassword = ChaChaPoly.EasyEncryptWithPassword(this.CurrentMasterKey, this.CurrentPassword)._GetHexString(),
                };

                this.CurrentMetaData = metaData;

                // メタデータを書き込みする
                await WriteMetaDataAsync(cancel);
            }
            else
            {
                // 不正 ここには来ないはず
                throw new CoresException($"XtsAesRandomAccess: Invalid readSize: {readSize}");
            }
            
            // XTS を作成
            this.CurrentXts = XtsAes256.Create(this.CurrentMasterKey.ToArray());
            this.CurrentEncrypter = this.CurrentXts.CreateEncryptor();
            this.CurrentDescrypter = this.CurrentXts.CreateDecryptor();
        }

        protected override Task<long> ReadVirtualSizeImplAsync(CancellationToken cancel = default)
        {
            return TR(this.CurrentMetaData.VirtualSize);
        }

        protected override async Task WriteVirtualSizeImplAsync(long virtualSize, CancellationToken cancel = default)
        {
            this.CurrentMetaData.VirtualSize = virtualSize;

            await WriteMetaDataAsync(cancel);
        }

        protected override void TransformSectorImpl(Memory<byte> dest, ReadOnlyMemory<byte> src, long sectorNumber, bool logicalToPhysical)
        {
            checked
            {
                if (dest.Length != src.Length) throw new ArgumentOutOfRangeException("dest.Length != src.Length");
                if (dest.Length != SectorSize) throw new ArgumentOutOfRangeException("dest.Length != SectorSize");

                var destSeg = dest._AsSegment();
                var srcSeg = src._AsSegment();

                XtsCryptoTransform transform = logicalToPhysical ? this.CurrentDescrypter : this.CurrentEncrypter;

                transform.TransformBlock(srcSeg.Array!, srcSeg.Offset, srcSeg.Count, destSeg.Array!, destSeg.Offset, (ulong)sectorNumber);
            }
        }
    }

    namespace Tests
    {
        public static class SectorBasedRandomAccessTest
        {
            public static void Test()
            {
                Async(async () =>
                {
                    using var file = await Lfs.CreateAsync(@"c:\tmp\test.dat");

                    using var t = new SectorBasedRandomAccessSimpleTest(file, 10, true);

                    await t.WriteRandomAsync(0, "012345678901234567890"._GetBytes());
                    await t.WriteRandomAsync(5, "Hello World    x"._GetBytes());
                    await t.SetFileSizeAsync(31);
                });

                Async(async () =>
                {
                    using var file = await Lfs.OpenAsync(@"c:\tmp\test.dat", writeMode: true);

                    using var t = new SectorBasedRandomAccessSimpleTest(file, 10, true);

                    long size = await t.GetFileSizeAsync();
                    size._Print();

                    await t.WriteRandomAsync(12, "0"._GetBytes());

                    //await t.SetFileSizeAsync(size - 1);
                });
            }
        }
    }
}

#endif

