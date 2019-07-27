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
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.FileProviders;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.AccessControl;

#pragma warning disable CS1998

namespace IPA.Cores.Basic
{
    public partial class LocalFileSystem
    {
        public DirectoryPath DetermineRootPathWithMarkerFile(FilePath sampleFilePath, string markerFileName, string stopSearchFileExtensions = Consts.FileNames.DefaultStopRootSearchFileExtsForSafety)
        {
            try
            {
                DirectoryPath currentDir = sampleFilePath.GetParentDirectory();

                while (currentDir.IsRootDirectory == false)
                {
                    FileSystemEntity[] elements = currentDir.EnumDirectory(flags: EnumDirectoryFlags.NoGetPhysicalSize);

                    if (elements.Where(x => x.IsFile && x.Name._IsSamei(markerFileName)).Any())
                    {
                        // Found
                        return currentDir;
                    }

                    if (elements.Where(x => x.IsFile && x.Name._IsExtensionMatch(stopSearchFileExtensions)).Any())
                    {
                        return null;
                    }

                    if (elements.Where(x => x.IsFile && Consts.FileNames.AppRootMarkerFileNames.Where(marker => marker._IsSamei(x.Name)).Any()).Any())
                    {
                        return null;
                    }

                    currentDir = currentDir.GetParentDirectory();
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }

            return null;
        }

        public DirectoryPath ConfigPathStringToPhysicalDirectoryPath(string pathString)
        {
            pathString = pathString._NonNullTrim();

            if (pathString._IsEmpty())
                throw new ArgumentNullException(nameof(pathString));

            pathString = PathParser.NormalizeDirectorySeparator(pathString, true);

            if (PathParser.IsAbsolutePath(pathString))
            {
                return pathString;
            }
            else
            {
                pathString = "/" + PathParser.Linux.NormalizeDirectorySeparator(pathString, true);

                string[] elements = PathParser.Linux.SplitAbsolutePathToElementsUnixStyle(pathString);

                string tmp = PathParser.Linux.BuildAbsolutePathStringFromElements(elements);

                tmp = PathParser.Linux.NormalizeDirectorySeparator(tmp, true);

                if (tmp[0] != '/')
                    throw new ApplicationException("tmp[0] != '/'");

                tmp = tmp.Substring(1);

                tmp = PathParser.Combine(Env.AppRootDir, tmp, true);

                return tmp;
            }
        }
    }
}

