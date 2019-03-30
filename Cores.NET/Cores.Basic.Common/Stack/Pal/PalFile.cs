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

using IPA.Cores.Helper.Basic;

#pragma warning disable CS0162

namespace IPA.Cores.Basic
{
    class PalFileSystem : FileSystem
    {
        public PalFileSystem(AsyncCleanuperLady lady) : base(lady)
        {
        }

        protected override Task<FileObject> CreateFileImplAsync(FileParameters fileParams, CancellationToken cancel = default)
            => PalFileHandle.CreateFileAsync(this, fileParams, cancel);
    }

    class PalFileHandle : FileObject
    {
        protected PalFileHandle(FileSystem fileSystem, FileParameters fileParams) : base(fileSystem, fileParams) { }

        FileStream fs;

        public static async Task<FileObject> CreateFileAsync(FileSystem fileSystem, FileParameters fileParams, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            PalFileHandle f = new PalFileHandle(fileSystem, fileParams);

            await f.CreateAsync(cancel);

            return f;
        }

        protected override async Task CreateAsync(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();

            try
            {
                fs = new FileStream(FileParams.Path, FileParams.Mode, FileParams.Access, FileParams.Share, 4096, FileOptions.Asynchronous);

                await base.CreateAsync(cancel);
            }
            catch
            {
                fs.DisposeSafe();
                throw;
            }
        }

        protected override async Task CloseImplAsync()
        {
            Dbg.Where();
            fs.DisposeSafe();
            fs = null;

            await Task.CompletedTask;
        }

        protected override async Task<long> GetCurrentPositionImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;
            return fs.Position;
        }

        protected override async Task<long> GetFileSizeImplAsync(CancellationToken cancel = default)
        {
            await Task.CompletedTask;
            return fs.Length;
        }
        protected override async Task SetFileSizeImplAsync(long size, CancellationToken cancel = default)
        {
            fs.SetLength(size);
            await Task.CompletedTask;
        }

        protected override async Task FlushImplAsync(CancellationToken cancel = default)
        {
            await fs.FlushAsync(cancel);
        }

        protected override async Task<int> ReadImplAsync(long position, bool seekRequested, Memory<byte> data, CancellationToken cancel = default)
        {
            if (seekRequested)
                fs.Seek(position, SeekOrigin.Begin);

            return await fs.ReadAsync(data, cancel);
        }

        protected override async Task WriteImplAsync(long position, bool seekRequested, ReadOnlyMemory<byte> data, CancellationToken cancel = default)
        {
            if (seekRequested)
                fs.Seek(position, SeekOrigin.Begin);

            await fs.WriteAsync(data, cancel);
        }
    }
}

