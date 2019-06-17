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

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using IPA.Cores.Basic;
using IPA.Cores.ClientApi.Acme;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class CertVaultSettings
        {
        }
    }

    [Flags]
    enum CertVaultHostType
    {
        OneHost = 0,
        Wildcard,
    }

    [Flags]
    enum CertVaultCertType
    {
        Static = 0,
        Acme,
    }

    class CertVaultCertificate
    {
        public DirectoryPath DirName { get; }
        public FileSystem FileSystem => DirName.FileSystem;

        public CertificateStore Store { get; }
        public CertVaultHostType HostType { get; }
        public CertVaultCertType CertType { get; }

        public CertVaultCertificate(DirectoryPath dirName, CertVaultCertType certType)
        {
            try
            {
                dirName.CreateDirectory();
            }
            catch { }

            this.CertType = certType;

            var files = DirName.EnumDirectory().Where(x => x.IsDirectory == false);

            string p12file = files.Where(x => x.Name._IsExtensionMatch(Consts.Extensions.Filter_Pkcs12s)).SingleOrDefault()?.FullPath;

            string certfile = files.Where(x => x.Name._IsExtensionMatch(Consts.Extensions.Filter_Certificates)).SingleOrDefault()?.FullPath;
            string keyfile = files.Where(x => x.Name._IsExtensionMatch(Consts.Extensions.Filter_Keys)).SingleOrDefault()?.FullPath;

            string passwordfile = files.Where(x => x.Name._IsSamei(Consts.FileNames.CertVault_Password)).SingleOrDefault()?.FullPath;
            string password = null;

            if (passwordfile != null)
            {
                password = FileSystem.ReadStringFromFile(passwordfile);

                if (password._IsEmpty()) password = null;
            }

            CertificateStore store = null;
            if (p12file != null)
            {
                store = new CertificateStore(FileSystem.ReadDataFromFile(p12file).Span, password);
            }
            else if (certfile != null && keyfile != null)
            {
                store = new CertificateStore(FileSystem.ReadDataFromFile(certfile).Span, FileSystem.ReadDataFromFile(keyfile).Span, password);
            }
            else
            {
                if (this.CertType == CertVaultCertType.Static)
                {
                    throw new ApplicationException($"Either PKCS#12 or PEM file is found on the directory \"{this.DirName.PathString}\".");
                }
            }

            this.Store = store;
        }
    }

    class CertVault
    {
        public DirectoryPath BaseDir { get; }
        public DirectoryPath StaticDir { get; }
        public DirectoryPath AcmeDir { get; }

        public FilePath AcmeAccountFilePath { get; }
        public PrivKey AcmeAccountKey { get; private set; }

        readonly CriticalSection LockObj = new CriticalSection();

        public CertVault(DirectoryPath baseDir)
        {
            this.BaseDir = baseDir;
            this.StaticDir = this.BaseDir.GetSubDirectory("StaticCerts");
            this.AcmeDir = this.BaseDir.GetSubDirectory("AcmeCerts");

            try { this.BaseDir.CreateDirectory(); } catch { }
            try { this.StaticDir.CreateDirectory(); } catch { }
            try { this.AcmeDir.CreateDirectory(); } catch { }

            this.AcmeAccountFilePath = this.AcmeDir.Combine(Consts.FileNames.CertVault_AcmeAccountKey);

            InternalEnumCertificate();
        }

        void InternalEnumCertificate()
        {
            lock (LockObj)
            {
                InternalEnumCertificateMain();
            }
        }

        void InternalEnumCertificateMain()
        {
            // Create an ACME account key if not exists
            this.AcmeAccountKey = this.AcmeAccountFilePath.ReadAndParseDataFile(true,
                data => new PrivKey(data.Span),
                () =>
                {
                    PkiUtil.GenerateEcdsaKeyPair(256, out PrivKey key, out _);
                    return key.Export();
                });
        }
    }
}

#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

