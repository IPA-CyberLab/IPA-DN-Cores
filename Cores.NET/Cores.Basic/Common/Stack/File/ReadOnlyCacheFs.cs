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
using Microsoft.Extensions.FileProviders;

namespace IPA.Cores.Basic;

public class ReadOnlyCacheFileSystemParam : ViewFileSystemParams
{
    public ReadOnlyCacheFileSystemParam(FileSystem underlayFileSystem, FileSystemMode mode = FileSystemMode.Default, bool disposeUnderlay = false)
        : base(underlayFileSystem, underlayFileSystem.PathParser.Style == FileSystemStyle.Windows ? PathParser.GetInstance(FileSystemStyle.Mac) : underlayFileSystem.PathParser, mode, disposeUnderlay)
    // Use the Mac OS X path parser if the underlay file system is Windows
    {
        // Windows ファイルシステムではそのままでは利用できません (Chroot すれば OK です)
        if (underlayFileSystem.PathParser.Style == FileSystemStyle.Windows)
        {
            throw new CoresLibException("underlayFileSystem.PathParser.Style must not be FileSystemStyle.Windows");
        }
    }
}

public class ReadOnlyCacheFileSystem : ViewFileSystem
{
    protected new ReadOnlyCacheFileSystemParam Params => (ReadOnlyCacheFileSystemParam)base.Params;

    public ReadOnlyCacheFileSystem(ViewFileSystemParams param) : base(param)
    {
    }

    protected override Task CreateDirectoryImplAsync(string directoryPath, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task DeleteDirectoryImplAsync(string directoryPath, bool recursive, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task DeleteFileImplAsync(string path, FileFlags flags = FileFlags.None, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task MoveDirectoryImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task MoveFileImplAsync(string srcPath, string destPath, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task SetDirectoryMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override Task SetFileMetadataImplAsync(string path, FileMetadata metadata, CancellationToken cancel = default)
        => throw new NotImplementedException();

    protected override async Task<FileObject> CreateFileImplAsync(FileParameters option, CancellationToken cancel = default)
    {
        await UpdateCacheIfNecessaryAsync(cancel);

        return await base.CreateFileImplAsync(option, cancel);
    }

    // 必要に応じてキャッシュを更新する
    async Task UpdateCacheIfNecessaryAsync(CancellationToken cancel = default)
    {
        await UpdateCacheCoreAsync(cancel);
    }

    public class FsCacheData
    {
    }

    // キャッシュを更新する
    async Task UpdateCacheCoreAsync(CancellationToken cancel = default)
    {
        await this.UnderlayFileSystem.EnumDirectoryAsync("/", true, cancel: cancel);
    }
}


