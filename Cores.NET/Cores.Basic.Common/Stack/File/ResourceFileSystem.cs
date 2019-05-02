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
using System.Reflection;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Collections.Immutable;

namespace IPA.Cores.Basic
{
    class VfsResourceFile : VfsRandomAccessFile
    {
        protected new ResourceFileSystem FileSystem => (ResourceFileSystem)base.FileSystem;
        readonly Assembly Assembly;
        public string ResourceName { get; }

        public VfsResourceFile(ResourceFileSystem fileSystem, string fileName) : base(fileSystem, fileName)
        {
            this.Assembly = FileSystem.Params.Assembly;
            this.ResourceName = fileName;
        }

        protected override IRandomAccess<byte> GetSharedRandomAccessBaseImpl()
        {
            return new StreamRandomAccessWrapper(Assembly.GetManifestResourceStream(ResourceName));
        }
    }

    class ResourceFileSystemParam : VirtualFileSystemParams
    {
        public Assembly Assembly { get; }

        public ResourceFileSystemParam(Assembly assembly)
        {
            this.Assembly = assembly;
        }
    }

    class ResourceFileSystem : VirtualFileSystem
    {
        public static Singleton<Assembly, ResourceFileSystem> Singleton = new Singleton<Assembly, ResourceFileSystem>((asm) => new ResourceFileSystem(new ResourceFileSystemParam(asm)));

        public new ResourceFileSystemParam Params => (ResourceFileSystemParam)base.Params;

        public ResourceFileSystem(ResourceFileSystemParam param) : base(param)
        {
            this.Params.EasyAccessPathFindMode.Set(EasyAccessPathFindMode.MostMatch);

            string[] names = Params.Assembly.GetManifestResourceNames();

            foreach (string name in names)
            {
                string fullPath = this.PathParser.Combine("/", name);

                using (this.AddFileAsync(new FileParameters(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite),
                    async (newFilename, newFileOption, c) =>
                    {
                        await Task.CompletedTask;
                        return new VfsResourceFile(this, name);
                    }).GetResult())
                {
                }
            }
        }
    }
}

