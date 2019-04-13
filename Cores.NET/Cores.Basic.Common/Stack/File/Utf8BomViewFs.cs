﻿// IPA Cores.NET
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
    class AutoUtf8BomFileObject : ViewFileObject
    {
        public bool HasBom { get; private set; } = false;
        public long HeaderOffset { get; private set; } = 0;

        public AutoUtf8BomFileObject(AutoUtf8BomViewFileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams)
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
                            fileSize = 3;
                        }
                    }
                    else if (fileSize >= 3)
                    {
                        Memory<byte> tmp = new byte[3];
                        if (await underlayFileObject.ReadRandomAsync(0, tmp, cancel) == tmp.Length)
                        {
                            if (tmp.Span.SequenceEqual(Str.BOM_UTF_8.Span))
                            {
                                HasBom = true;
                            }
                        }
                    }

                    HeaderOffset = HasBom ? 3 : 0;

                    fileSize -= HeaderOffset;

                    long currentPosition = 0;
                    if (FileParams.Mode == FileMode.Append)
                        currentPosition = fileSize;

                    InitAndCheckFileSizeAndPosition(currentPosition, fileSize, cancel);

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

        protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            checked
            {
                long physicalSize = size + this.HeaderOffset;
                await this.UnderlayFile.SetFileSizeAsync(physicalSize, cancel);
            }
        }

        protected override async Task<int> ReadRandomImplAsync(long position, Memory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                long physicalPosition = position + this.HeaderOffset;
                return await this.UnderlayFile.ReadRandomAsync(physicalPosition, data, cancel);
            }
        }

        protected override async Task WriteRandomImplAsync(long position, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            checked
            {
                long physicalPosition = position + this.HeaderOffset;
                await this.UnderlayFile.WriteRandomAsync(physicalPosition, data, cancel);
            }
        }
    }

    class AutoUtf8BomViewFileSystem : ViewFileSystem
    {
        public static readonly ReadOnlyMemory<byte> Utf8Bom = Str.BOM_UTF_8;

        public AutoUtf8BomViewFileSystem(FileSystemBase underlayFileSystem) : base(underlayFileSystem, null)
        {
        }

        protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
        {
            AutoUtf8BomFileObject fileObj = new AutoUtf8BomFileObject(this, option);

            await fileObj._InternalCreateFileAsync(cancel);

            return fileObj;
        }

        protected override async Task<FileMetadata> GetFileMetadataImplAsync(string path, FileMetadataGetFlags flags = FileMetadataGetFlags.None, CancellationToken cancel = default)
        {
            FileMetadata physicalMetadata = await UnderlayFileSystem.GetFileMetadataAsync(path, flags, cancel);

            try
            {
                long headerOffset = 0;
                long physicalSize = physicalMetadata.Size;

                using (FileObject physicalFile = await UnderlayFileSystem.OpenAsync(path))
                {
                    byte[] bomRead = new byte[3];
                    Memory<byte> tmp = new byte[3];
                    if (await physicalFile.ReadRandomAsync(0, tmp, cancel) == tmp.Length)
                    {
                        if (tmp.Span.SequenceEqual(Str.BOM_UTF_8.Span))
                        {
                            headerOffset = 3;
                        }
                    }

                    physicalSize = await physicalFile.GetFileSizeAsync(true, cancel) - headerOffset;
                    if (physicalSize >= 0)
                    {
                        physicalMetadata.Size = physicalSize;
                    }
                }
            }
            catch { }

            return physicalMetadata;
        }
    }
}

