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
            public static readonly Copenhagen<int> DefaultReloadInterval = 60 * 1000;

            public static readonly Copenhagen<int> DefaultAcmeStartUpdateRemainingDays = 365;

            public static readonly Copenhagen<int> UpdateIntervalForAcmeQueueCheck = 1 * 1000;

            public static readonly Copenhagen<int> MaxAcmeQueueLen = 64;

            public static readonly Copenhagen<string> DefaultAcmeContactEmail = "coreslib.acme.default+changeme.__RAND__@gmail.com";
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
        public CertVault Vault { get; }

        public DirectoryPath DirName { get; }
        public FileSystem FileSystem => DirName.FileSystem;

        public CertificateStore Store { get; }
        public CertVaultCertType CertType { get; }

        public CertVaultCertificate(CertVault vault, CertificateStore store, CertVaultCertType certType)
        {
            if (certType != CertVaultCertType.DefaultCert) throw new ArgumentException("certType != CertVaultCertType.Default");

            this.Vault = vault;
            this.Store = store;
            this.CertType = certType;
        }

        public CertVaultCertificate(CertVault vault, DirectoryPath dirName, CertVaultCertType certType)
        {
            this.Vault = vault;

            if (certType.EqualsAny(CertVaultCertType.Acme, CertVaultCertType.Static) == false)
            {
                throw new ArgumentOutOfRangeException("certType");
            }

            try
            {
                dirName.CreateDirectory();
            }
            catch { }

            CertificateStore store = null;

            this.CertType = certType;
            this.DirName = dirName;

            if (certType == CertVaultCertType.Static)
            {
                // Static cert
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
            }
            else
            {
                // ACME cert
                FilePath fileName = DirName.Combine(DirName.GetThisDirectoryName() + Consts.Extensions.Certificate);

                if (fileName.IsFileExists())
                {
                    store = new CertificateStore(fileName.ReadDataFromFile().Span, this.Vault.AcmeCertKey);
                }
            }

            var test = store?.PrimaryContainer.CertificateList[0];

            this.Store = store;
        }
    }

    class CertVaultSettings : INormalizable, ICloneable
    {
        const string AcmeDefaultUrl = AcmeWellKnownServiceUrls.LetsEncryptStaging;

        public int ReloadInterval;
        public int AcmeStartUpdateRemainingDays;

        public bool UseAcme;
        public string AcmeContactEmail;
        public string AcmeServiceDirectoryUrl;
        public string[] AcmeAllowedFqdnList;
        public bool AcmeEnableFqdnIpCheck;

        public CertVaultSettings()
        {
        }

        public CertVaultSettings(EnsureSpecial defaultSetting)
        {
            this.UseAcme = true;
            this.AcmeServiceDirectoryUrl = AcmeDefaultUrl;
            this.ReloadInterval = CoresConfig.CertVaultSettings.DefaultReloadInterval;
            this.AcmeStartUpdateRemainingDays = CoresConfig.CertVaultSettings.DefaultAcmeStartUpdateRemainingDays;
            this.AcmeContactEmail = GenDefaultContactEmail();
            this.AcmeEnableFqdnIpCheck = true;

            Normalize();
        }

        static string GenDefaultContactEmail()
        {
            string str = CoresConfig.CertVaultSettings.DefaultAcmeContactEmail;
            str = str.Replace("__RAND__", Secure.RandSInt31().ToString());
            return str;
        }

        public object Clone() => this.MemberwiseClone();

        public void Normalize()
        {
            if (Str.CheckMailAddress(this.AcmeContactEmail) == false) this.AcmeContactEmail = GenDefaultContactEmail();
            if (this.AcmeServiceDirectoryUrl._IsEmpty()) this.AcmeServiceDirectoryUrl = AcmeDefaultUrl;
            this.ReloadInterval = Math.Max(this.ReloadInterval, 1000);
            this.AcmeStartUpdateRemainingDays = Math.Max(this.AcmeStartUpdateRemainingDays, 0);
            if (AcmeAllowedFqdnList == null) AcmeAllowedFqdnList = new string[0];
        }
    }

    class CertVault : AsyncServiceWithMainLoop
    {
        public DirectoryPath BaseDir { get; }
        public DirectoryPath StaticDir { get; }
        public DirectoryPath AcmeDir { get; }

        public FilePath SettingsFilePath { get; }
        public FilePath AcmeAccountKeyFilePath { get; }
        public PrivKey AcmeAccountKey { get; private set; }

        public FilePath AcmeCertKeyFilePath { get; }
        public PrivKey AcmeCertKey { get; private set; }

        public bool IsGlobalCertVault { get; }

        List<CertVaultCertificate> InternalCertList;

        readonly CriticalSection LockObj = new CriticalSection();

        volatile bool AcmeQueueUpdatedFlag = false;

        readonly CriticalSection AcmeQueueLockObj = new CriticalSection();

        List<string> AcmeQueue = new List<string>();

        CertVaultSettings Settings = null;

        readonly CertVaultSettings DefaultSettings;

        public CertVault(DirectoryPath baseDir, CertVaultSettings defaultSettings = null, bool isGlobalVault = false)
        {
            try
            {
                this.IsGlobalCertVault = IsGlobalCertVault;

                if (defaultSettings == null) defaultSettings = new CertVaultSettings(EnsureSpecial.Yes);

                this.DefaultSettings = (CertVaultSettings)defaultSettings.Clone();

                this.BaseDir = baseDir;

                this.StaticDir = this.BaseDir.GetSubDirectory("StaticCerts");

                this.AcmeDir = this.BaseDir.GetSubDirectory("AcmeCerts");

                this.SettingsFilePath = this.BaseDir.Combine(Consts.FileNames.CertVault_Settings);

                this.AcmeAccountKeyFilePath = this.AcmeDir.Combine(Consts.FileNames.CertVault_AcmeAccountKey);
                this.AcmeCertKeyFilePath = this.AcmeDir.Combine(Consts.FileNames.CertVault_AcmeCertKey);

                Reload();

                this.StartMainLoop(MainLoopAsync);
            }
            catch
            {
                this._DisposeSafe();
                throw;
            }
        }

        public void Reload()
        {
            lock (LockObj)
            {
                try
                {
                    InternalReload();
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }
        }

        bool IsAcmeCertUpdated = false;

        public async Task MainLoopAsync(CancellationToken cancel)
        {
            while (cancel.IsCancellationRequested == false)
            {
                Dbg.Where(this.Settings.ReloadInterval);
                try
                {
                    Reload();

                    IsAcmeCertUpdated = false;

                    try
                    {
                        await ProcessEnqueuedAcmeHostnameAsync(cancel);
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                    }

                    if (IsAcmeCertUpdated)
                    {
                        // ACME certificate is added. Reload
                        Reload();
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                await TaskUtil.AwaitWithPollAsync(this.Settings.ReloadInterval, CoresConfig.CertVaultSettings.UpdateIntervalForAcmeQueueCheck, () => this.AcmeQueueUpdatedFlag, cancel);
                this.AcmeQueueUpdatedFlag = false;
            }
        }

        bool CheckFqdnAllowedForAcme(string hostname)
        {
            try
            {
                var hosts = this.Settings.AcmeAllowedFqdnList;

                foreach (string host in hosts)
                {
                    CertificateHostName hn = new CertificateHostName(host);

                    if (hn.IsMatchForHost(hostname)) return true;
                }
            }
            catch { }

            return false;
        }

        async Task ProcessEnqueuedAcmeHostnameAsync(CancellationToken cancel)
        {
            List<string> queue;

            lock (AcmeQueueLockObj)
            {
                queue = AcmeQueue;
                AcmeQueue = new List<string>();
            }

            using (AcmeClient client = new AcmeClient(new AcmeClientOptions(this.Settings.AcmeServiceDirectoryUrl)))
            {
                AcmeAccount account = await client.LoginAccountAsync(this.AcmeAccountKey, ("mailto:" + this.Settings.AcmeContactEmail)._SingleArray(), cancel);

                foreach (string fqdn in queue)
                {
                    if (CheckFqdnAllowedForAcme(fqdn))
                    {
                        await ProcessAcmeFqdnAsync(account, fqdn, cancel);
                    }
                }
            }
        }

        bool IsCertificateDateTimeToUpdate(DateTime utc)
        {
            DateTime now = DateTime.UtcNow;

            if (utc < now) return true;

            TimeSpan ts = utc - now;

            if (ts.TotalDays <= this.Settings.AcmeStartUpdateRemainingDays)
            {
                return true;
            }

            return false;
        }

        async Task ProcessAcmeFqdnAsync(AcmeAccount account, string fqdn, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            DirectoryPath dir = this.AcmeDir.GetSubDirectory(fqdn);

            FilePath crtFileName = dir.Combine(dir.GetThisDirectoryName() + Consts.Extensions.Certificate);

            Certificate currentCert = null;
            if (crtFileName.IsFileExists(cancel))
            {
                try
                {
                    currentCert = new Certificate(crtFileName.ReadDataFromFile().Span);
                }
                catch { }
            }

            if (currentCert == null || IsCertificateDateTimeToUpdate(currentCert.CertData.NotAfter))
            {
                await AcmeIssueAsync(account, fqdn, crtFileName, cancel);
            }
        }

        async Task AcmeIssueAsync(AcmeAccount account, string fqdn, FilePath crtFileName, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            AcmeOrder order = await account.NewOrderAsync(fqdn, cancel);

            if (this.IsGlobalCertVault)
            {
                GlobalCertVault.SetAcmeAccountForChallengeResponse(account);
            }

            CertificateStore store = await order.FinalizeAsync(this.AcmeCertKey, cancel);

            IsAcmeCertUpdated = true;

            store.ExportChainedPem(out ReadOnlyMemory<byte> certData, out _);

            crtFileName.WriteDataToFile(certData, additionalFlags: FileFlags.AutoCreateDirectory);
        }

        void InternalReload()
        {
            List<CertVaultCertificate> list = new List<CertVaultCertificate>();

            // Create directories
            try { this.BaseDir.CreateDirectory(); } catch { }
            try { this.StaticDir.CreateDirectory(); } catch { }

            // Load settings
            CertVaultSettings tmpSettings = this.SettingsFilePath.ReadAndParseDataFile(ReadParseFlags.Both,
                data =>
                {
                    var ret = data._GetString_UTF8()._JsonToObject<CertVaultSettings>();
                    ret.Normalize();
                    return ret;
                },
                () =>
                {
                    return this.DefaultSettings._ObjectToJson()._GetBytes_UTF8(true);
                },
                t => t._ObjectToJson()._GetBytes_UTF8(true));

            this.Settings = tmpSettings;

            if (this.Settings.UseAcme)
            {
                try { this.AcmeDir.CreateDirectory(); } catch { }

                // Create an ACME account key if not exists
                this.AcmeAccountKey = this.AcmeAccountKeyFilePath.ReadAndParseDataFile(ReadParseFlags.ForceInitOnParseError,
                    data => new PrivKey(data.Span),
                    () =>
                    {
                        PkiUtil.GenerateEcdsaKeyPair(256, out PrivKey key, out _);
                        return key.Export();
                    });

                // Create an ACME certificate key if not exists
                this.AcmeCertKey = this.AcmeCertKeyFilePath.ReadAndParseDataFile(ReadParseFlags.ForceInitOnParseError,
                    data => new PrivKey(data.Span),
                    () =>
                    {
                        PkiUtil.GenerateRsaKeyPair(2048, out PrivKey key, out _);
                        return key.Export();
                    });
            }

            // Initialize the DefaultCert
            FilePath defaultCertPath = this.StaticDir.Combine(Consts.FileNames.CertVault_DefaultCert);

            CertificateStore defaultCert = defaultCertPath.ReadAndParseDataFile(ReadParseFlags.ForceInitOnParseError,
                data => new CertificateStore(data.Span),
                () =>
                {
                    PkiUtil.GenerateRsaKeyPair(2048, out PrivKey key, out _);
                    Certificate cert = new Certificate(key, new CertificateOptions(PkiAlgorithm.RSA, cn: Consts.Strings.DefaultCertCN + "_" + Env.MachineName, c: "US", expires: Util.MaxDateTimeOffsetValue));
                    CertificateStore store = new CertificateStore(cert, key);
                    return store.ExportPkcs12();
                });


            CertVaultCertificate defaultVaultCert = new CertVaultCertificate(this, defaultCert, CertVaultCertType.DefaultCert);
            list.Add(defaultVaultCert);

            // Enumerate StaticCerts
            ReloadCertsDir(list, this.StaticDir, CertVaultCertType.Static);

            if (this.Settings.UseAcme)
            {
                ReloadCertsDir(list, this.AcmeDir, CertVaultCertType.Acme);
            }

            this.InternalCertList = list;
        }

        void ReloadCertsDir(List<CertVaultCertificate> list, DirectoryPath dirName, CertVaultCertType type)
        {
            try
            {
                // Enumerate subdir
                var subdirs = dirName.GetDirectories();

                foreach (DirectoryPath subdir in subdirs)
                {
                    ReloadCertsSubDir(list, subdir, type);
                }
            }
            catch (Exception ex)
            {
                ex._Debug();
            }
        }

        void ReloadCertsSubDir(List<CertVaultCertificate> list, DirectoryPath dirName, CertVaultCertType type)
        {
            try
            {
                CertVaultCertificate cert = new CertVaultCertificate(this, dirName, type);

                if (cert.Store != null)
                {
                    list.Add(cert);
                }
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
            hostname = Str.NormalizeFqdn(hostname);

            List<CertVaultCertificate> list = InternalCertList;

            List<MatchResult> candidates = new List<MatchResult>();

            foreach (CertVaultCertificate cert in list)
            {
                if (cert.Store != null)
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
            }

            var sorted = candidates.OrderByDescending(x => x.MatchType);

            MatchResult selected = sorted.First();

            matchType = selected.MatchType;

            if (this.Settings.UseAcme)
            {
                if (hostname._IsEmpty() == false && matchType == CertificateHostnameType.DefaultCert && hostname.Split('.').Length >= 2 && Str.CheckFqdn(hostname))
                {
                    // Add the request hostname to the ACME queue
                    if (AcmeQueue.Count < CoresConfig.CertVaultSettings.MaxAcmeQueueLen)
                    {
                        lock (this.AcmeQueueLockObj)
                        {
                            if (AcmeQueue.Contains(hostname) == false)
                            {
                                AcmeQueue.Add(hostname);

                                this.AcmeQueueUpdatedFlag = true;
                            }
                        }
                    }
                }
            }

            return selected.VaultCert.Store;
        }

        protected override void CancelImpl(Exception ex)
        {
            base.CancelImpl(ex);
        }

        protected override async Task CleanupImplAsync(Exception ex)
        {
            try
            {
                await this.MainLoopToWaitComplete;
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }

        protected override void DisposeImpl(Exception ex)
        {
            try
            {
            }
            finally
            {
                base.DisposeImpl(ex);
            }
        }
    }

    static class GlobalCertVault
    {
        public static readonly StaticModule Module = new StaticModule(InitModule, FreeModule);

        static Singleton<CertVault> Singleton = null;

        public static DirectoryPath BaseDir { get; private set; } = null;

        static AcmeAccount AcmeAccountForChallengeResponse = null;

        static void InitModule()
        {
            BaseDir = Path.Combine(Env.AppLocalDir, "Config", "CertVault");

            Singleton = new Singleton<CertVault>(() =>
            {
                try
                {
                    BaseDir.CreateDirectory();
                }
                catch { }

                Util.PutGitIgnoreFileOnDirectory(BaseDir);

                CertVault vault = new CertVault(BaseDir, isGlobalVault: true);

                return vault;
            });
        }

        public static CertVault GetCertVault() => Singleton;

        public static void SetAcmeAccountForChallengeResponse(AcmeAccount account)
        {
            AcmeAccountForChallengeResponse = account;
        }

        public static AcmeAccount GetAcmeAccountForChallengeResponse() => AcmeAccountForChallengeResponse;

        static void FreeModule()
        {
            Singleton._DisposeSafe();
            Singleton = null;

            BaseDir = null;
            AcmeAccountForChallengeResponse = null;
        }
    }
}

#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;

