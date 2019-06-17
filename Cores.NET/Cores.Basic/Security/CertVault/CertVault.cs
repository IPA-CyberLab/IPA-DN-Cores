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
    enum CertVaultCertType
    {
        DefaultCert = 0,
        Acme = 50,
        Static = 100,
    }

    class CertVaultCertificate
    {
        public DirectoryPath DirName { get; }
        public FileSystem FileSystem => DirName.FileSystem;

        public CertificateStore Store { get; }
        public CertVaultCertType CertType { get; }

        public CertVaultCertificate(CertificateStore store, CertVaultCertType certType)
        {
            if (certType != CertVaultCertType.DefaultCert) throw new ArgumentException("certType != CertVaultCertType.Default");

            this.Store = store;
            this.CertType = certType;
        }

        public CertVaultCertificate(DirectoryPath dirName, CertVaultCertType certType)
        {
            try
            {
                dirName.CreateDirectory();
            }
            catch { }

            this.CertType = certType;
            this.DirName = dirName;

            var files = DirName.EnumDirectory().Where(x => x.IsDirectory == false);

            string p12file = files.Where(x => x.Name._IsExtensionMatch(Consts.Extensions.Filter_Pkcs12s)).SingleOrDefault()?.FullPath;

            string certfile = files.Where(x => x.Name._IsExtensionMatch(Consts.Extensions.Filter_Certificates)).SingleOrDefault()?.FullPath;
            string keyfile = files.Where(x => x.Name._IsExtensionMatch(Consts.Extensions.Filter_Keys)).SingleOrDefault()?.FullPath;

            string passwordfile = files.Where(x => x.Name._IsSamei(Consts.FileNames.CertVault_Password)).SingleOrDefault()?.FullPath;
            string password = null;

            if (passwordfile != null)
            {
                password = FileSystem.ReadStringFromFile(passwordfile, oneLine: true);

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

            var test = store.PrimaryContainer.CertificateList[0];

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

        List<CertVaultCertificate> InternalCertList;

        readonly CriticalSection LockObj = new CriticalSection();

        public CertVault(DirectoryPath baseDir)
        {
            this.BaseDir = baseDir;
            this.StaticDir = this.BaseDir.GetSubDirectory("StaticCerts");
            this.AcmeDir = this.BaseDir.GetSubDirectory("AcmeCerts");

            this.AcmeAccountFilePath = this.AcmeDir.Combine(Consts.FileNames.CertVault_AcmeAccountKey);

            InternalEnumCertificate();
        }

        public void Update()
        {
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
            List<CertVaultCertificate> list = new List<CertVaultCertificate>();

            // Create directories
            try { this.BaseDir.CreateDirectory(); } catch { }
            try { this.StaticDir.CreateDirectory(); } catch { }
            try { this.AcmeDir.CreateDirectory(); } catch { }

            // Create an ACME account key if not exists
            this.AcmeAccountKey = this.AcmeAccountFilePath.ReadAndParseDataFile(true,
                data => new PrivKey(data.Span),
                () =>
                {
                    PkiUtil.GenerateEcdsaKeyPair(256, out PrivKey key, out _);
                    return key.Export();
                });

            // Initialize the DefaultCert
            FilePath defaultCertPath = this.StaticDir.Combine(Consts.FileNames.CertVault_DefaultCert);

            CertificateStore defaultCert = defaultCertPath.ReadAndParseDataFile(true,
                data => new CertificateStore(data.Span),
                () =>
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey key, out _);
                    Certificate cert = new Certificate(key, new CertificateOptions(PkiAlgorithm.RSA, cn: Consts.Strings.DefaultCertCN + "_" + Env.MachineName, c: "US", expires: Util.MaxDateTimeOffsetValue));
                    CertificateStore store = new CertificateStore(cert, key);
                    return store.ExportPkcs12();
                });


            CertVaultCertificate defaultVaultCert = new CertVaultCertificate(defaultCert, CertVaultCertType.DefaultCert);
            list.Add(defaultVaultCert);

            // Enumerate StaticCerts
            EnumerateCertsDir(list, this.StaticDir, CertVaultCertType.Static);
            EnumerateCertsDir(list, this.AcmeDir, CertVaultCertType.Acme);

            this.InternalCertList = list;
        }

        void EnumerateCertsDir(List<CertVaultCertificate> list, DirectoryPath dirName, CertVaultCertType type)
        {
            try
            {
                // Enumerate subdir
                var subdirs = dirName.GetDirectories();

                foreach (DirectoryPath subdir in subdirs)
                {
                    EnumerateCertsSubDir(list, subdir, type);
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        void EnumerateCertsSubDir(List<CertVaultCertificate> list, DirectoryPath dirName, CertVaultCertType type)
        {
            try
            {
                CertVaultCertificate cert = new CertVaultCertificate(dirName, type);

                list.Add(cert);
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        class MatchResult
        {
            public CertVaultCertificate VaultCert;
            public CertificateHostnameType MatchType;
        }

        public CertificateStore SelectBestFitCertificate(string hostname, out CertificateHostnameType matchType)
        {
            hostname = hostname._NonNullTrim().ToLower().TrimEnd('.');

            List<CertVaultCertificate> list = InternalCertList;

            List<MatchResult> candidates = new List<MatchResult>();

            foreach (CertVaultCertificate cert in list)
            {
                var pc = cert.Store.PrimaryContainer;
                if (pc.CertificateList.Count >= 1)
                {
                    var cert2 = pc.CertificateList[0];
                    if (cert2.IsMatchForHost(hostname, out CertificateHostnameType mt) || cert.CertType == CertVaultCertType.DefaultCert)
                    {
                        if (cert.CertType == CertVaultCertType.DefaultCert) mt = CertificateHostnameType.DefaultCert;

                        MatchResult r = new MatchResult
                        {
                            MatchType = mt,
                            VaultCert = cert,
                        };

                        candidates.Add(r);
                    }
                }
            }

            var sorted = candidates.OrderByDescending(x => x.MatchType);

            MatchResult selected = sorted.First();

            matchType = selected.MatchType;

            return selected.VaultCert.Store;
        }
    }
}

#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

