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

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Diagnostics;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class AutoUtf8BomFile : ViewFileObject
    {
        public bool HasBom { get; private set; } = false;
        public long HeaderOffset { get; private set; } = 0;

        public AutoUtf8BomFile(ViewFileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams)
        {
        }

        protected override async Task<FileObject> CreateUnderlayFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            checked
            {
                FileObject underlayFileObject = await base.CreateUnderlayFileImplAsync(option, cancel);
                try
                {
                    HasBom = false;

                    long fileSize = await underlayFileObject.GetFileSizeAsync(cancel: cancel);
                    if (fileSize == 0)
                    {
                        if (option.Access.Bit(FileAccess.Write))
                        {
                            await underlayFileObject.WriteRandomAsync(0, Str.BOM_UTF_8, cancel);
                            HasBom = true;
                        }
                    }
                    else if (fileSize >= 3)
                    {
                        Memory<byte> tmp = new byte[3];
                        await underlayFileObject.ReadRandomAsync(0, tmp, cancel);
                        if (tmp.Span.SequenceEqual(Str.BOM_UTF_8.Span))
                        {
                            HasBom = true;
                        }
                    }

                    HeaderOffset = HasBom ? 3 : 0;

                    await InitAndCheckFileSizeAndPositionAsync(underlayFileObject.Position - HeaderOffset, cancel);

                    return underlayFileObject;
                }
                catch
                {
                    await underlayFileObject.CloseAsync();
                    underlayFileObject.DisposeSafe();
                    throw;
                }
            }
        }

        protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            checked
            {
                long size = await this.UnderlayFile.GetFileSizeAsync(cancel: cancel);
                long virtualSize = size - this.HeaderOffset;
                if (virtualSize < 0)
                    throw new FileException(FileParams.Path, $"GetFileSizeImplAsync: virtualSize = {virtualSize}, size = {size}");
                return virtualSize;
            }
        }

        protected override Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            return base.ReadRandomImplAsync(position, data, cancel);
        }

        protected override Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            return base.SetFileSizeImplAsync(size, cancel);
        }

        protected override Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            return base.WriteRandomImplAsync(position, data, cancel);
        }
    }

    class AutoUtf8BomFs : ViewFileSystem
    {
        public static readonly ReadOnlyMemory<byte> Utf8Bom = Str.BOM_UTF_8;

        public AutoUtf8BomFs(AsyncCleanuperLady lady, FileSystemBase underlayFileSystem) : base(lady, underlayFileSystem, null)
        {
        }

        protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            AutoUtf8BomFile fileObj = new AutoUtf8BomFile(this, option);

            await fileObj._InternalCreateFileAsync(cancel);

            return fileObj;
        }

        protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.None, CancellationToken cancel = default)
        {
            return await base.GetFileMetadataImplAsync(path, flags, cancel);
        }
    }
}

