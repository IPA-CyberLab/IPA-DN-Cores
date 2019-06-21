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

#if CORES_BASIC_JSON
#if CORES_BASIC_SECURITY

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;

using IPA.Cores.Basic;
using IPA.Cores.ClientApi.Acme;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;
using System.Security.Cryptography.X509Certificates;

namespace IPA.Cores.Basic
{
    static partial class CoresConfig
    {
        public static partial class CertVaultSettings
        {
            public static readonly Copenhagen<int> DefaultReloadInterval = 60 * 1000;

            public static readonly Copenhagen<int> DefaultMaxAcmeCerts = 3;

            public static readonly Copenhagen<int> AcmeRenewSuppressIntervalAfterLastError = 8 * 60 * 60 * 1000;

            public static readonly Copenhagen<int> UpdateIntervalForAcmeQueueCheck = 222;

            public static readonly Copenhagen<int> MaxAcmeQueueLen = 64;

            public static readonly Copenhagen<int> DnsTimeout = 5 * 1000;
            public static readonly Copenhagen<int> DnsTryCount = 3;
            public static readonly Copenhagen<int> DnsTryInterval = 250;

            public static readonly Copenhagen<int> CertificateSelectorCacheLifetime = 1 * 1000;

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
                    store = null;
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
                else
                {
                    store = null;
                }
            }

            Certificate test = store?.PrimaryContainer.CertificateList[0];

            if (test != null)
            {
                if (test.PublicKey.Equals(store.PrimaryContainer.PrivateKey.PublicKey) == false)
                {
                    Con.WriteDebug($"CertVault: The public key certificate in the directory '{dirName}' doesn't match to the private key.");
                    store = null;
                }
            }

            this.Store = store;
        }
    }

    class CertVaultSettings : INormalizable, ICloneable
    {
        const string AcmeDefaultUrl = AcmeClientOptions.DefaultEntryPointUrl;

        public int ReloadIntervalMsecs;
        public int MaxAcmeCerts;

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
            this.ReloadIntervalMsecs = CoresConfig.CertVaultSettings.DefaultReloadInterval;
            this.AcmeContactEmail = GenDefaultContactEmail();
            this.AcmeEnableFqdnIpCheck = true;
            this.MaxAcmeCerts = CoresConfig.CertVaultSettings.DefaultMaxAcmeCerts;

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
            this.ReloadIntervalMsecs = Math.Max(this.ReloadIntervalMsecs, 1000);
            if (AcmeAllowedFqdnList == null) AcmeAllowedFqdnList = new string[1] { "*" };
            if (this.MaxAcmeCerts <= 0) this.MaxAcmeCerts = CoresConfig.CertVaultSettings.DefaultMaxAcmeCerts;
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

        readonly CertificateStore DefaultCertificate;

        public TcpIpSystem TcpIp { get; }

        readonly SyncCache<HashSet<string>> AcmeExpiresUpdateFailedList = new SyncCache<HashSet<string>>(CoresConfig.CertVaultSettings.AcmeRenewSuppressIntervalAfterLastError,
            CacheFlags.IgnoreUpdateError, () => new HashSet<string>(StrComparer.IgnoreCaseComparer));

        readonly SyncCache<string, CertificateStore> CertificateSelectorCache;

        readonly SyncCache<string, CertificateStore> CertificateSelectorCache_NoAcme;

        public CertVault(DirectoryPath baseDir, CertVaultSettings defaultSettings = null, CertificateStore defaultCertificate = null, TcpIpSystem tcpIp = null, bool isGlobalVault = false)
        {
            try
            {
                this.DefaultCertificate = defaultCertificate;

                this.TcpIp = tcpIp ?? LocalNet;

                this.IsGlobalCertVault = isGlobalVault;

                if (defaultSettings == null) defaultSettings = new CertVaultSettings(EnsureSpecial.Yes);

                this.DefaultSettings = (CertVaultSettings)defaultSettings.Clone();

                this.BaseDir = baseDir;

                this.StaticDir = this.BaseDir.GetSubDirectory("StaticCerts");

                this.AcmeDir = this.BaseDir.GetSubDirectory("AcmeCerts");

                this.SettingsFilePath = this.BaseDir.Combine(Consts.FileNames.CertVault_Settings);

                this.AcmeAccountKeyFilePath = this.AcmeDir.Combine(Consts.FileNames.CertVault_AcmeAccountKey);
                this.AcmeCertKeyFilePath = this.AcmeDir.Combine(Consts.FileNames.CertVault_AcmeCertKey);

                this.CertificateSelectorCache = new SyncCache<string, CertificateStore>(CoresConfig.CertVaultSettings.CertificateSelectorCacheLifetime, CacheFlags.IgnoreUpdateError, hostname => this.SelectBestFitCertificate(hostname, out _, false));
                this.CertificateSelectorCache_NoAcme = new SyncCache<string, CertificateStore>(CoresConfig.CertVaultSettings.CertificateSelectorCacheLifetime, CacheFlags.IgnoreUpdateError, hostname => this.SelectBestFitCertificate(hostname, out _, true));

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
                try
                {
                    Reload();

                    if (this.Settings.UseAcme)
                    {
                        IsAcmeCertUpdated = false;

                        // Process newly requested ACME certs
                        try
                        {
                            if (this.InternalCertList.Where(x => x.CertType == CertVaultCertType.Acme).Count() < this.Settings.MaxAcmeCerts)
                            {
                                await ProcessEnqueuedAcmeHostnameAsync(cancel);
                            }
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }

                        if (IsAcmeCertUpdated)
                        {
                            // ACME certificate is added. Reload
                            Reload();
                            IsAcmeCertUpdated = false;
                        }

                        // Process expiring or expires ACME certs
                        try
                        {
                            await ProcessExpiringOrExpiredAcmeCertsAsync(cancel);
                        }
                        catch (Exception ex)
                        {
                            ex._Debug();
                        }

                        if (IsAcmeCertUpdated)
                        {
                            // ACME certificate is added. Reload
                            Reload();
                            IsAcmeCertUpdated = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }

                await TaskUtil.AwaitWithPollAsync(this.Settings.ReloadIntervalMsecs, CoresConfig.CertVaultSettings.UpdateIntervalForAcmeQueueCheck, () => this.AcmeQueueUpdatedFlag, cancel);
                this.AcmeQueueUpdatedFlag = false;
            }
        }

        async Task ProcessExpiringOrExpiredAcmeCertsAsync(CancellationToken cancel)
        {
            List<CertVaultCertificate> list = this.InternalCertList;

            AcmeClient client = null;
            AcmeAccount account = null;

            try
            {
                foreach (CertVaultCertificate cert in list)
                {
                    if (cert.CertType == CertVaultCertType.Acme)
                    {
                        if (cert.Store != null)
                        {
                            string certHostName = cert.DirName.GetThisDirectoryName();

                            if (AcmeExpiresUpdateFailedList.Get().Contains(certHostName) == false)
                            {
                                try
                                {
                                    var certData = cert.Store.PrimaryContainer.CertificateList[0].CertData;

                                    if (IsCertificateDateTimeToUpdate(certData.NotBefore, certData.NotAfter))
                                    {
                                        if (account == null)
                                        {
                                            client = new AcmeClient(new AcmeClientOptions(this.Settings.AcmeServiceDirectoryUrl, this.TcpIp));
                                            account = await client.LoginAccountAsync(this.AcmeAccountKey, ("mailto:" + this.Settings.AcmeContactEmail)._SingleArray(), cancel);
                                        }

                                        try
                                        {
                                            await ProcessAcmeFqdnAsync(account, certHostName, cancel);
                                        }
                                        catch (Exception ex)
                                        {
                                            AcmeExpiresUpdateFailedList.Get().Add(certHostName);
                                            ex._Debug();
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ex._Debug();
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (client != null)
                {
                    client._DisposeSafe();
                }
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

            using (AcmeClient client = new AcmeClient(new AcmeClientOptions(this.Settings.AcmeServiceDirectoryUrl, this.TcpIp)))
            {
                AcmeAccount account = await client.LoginAccountAsync(this.AcmeAccountKey, ("mailto:" + this.Settings.AcmeContactEmail)._SingleArray(), cancel);

                foreach (string fqdn in queue)
                {
                    if (CheckFqdnAllowedForAcme(fqdn))
                    {
                        if (this.Settings.AcmeEnableFqdnIpCheck == false || (await CheckFqdnHasIpAddressOfThisLocalHostAsync(fqdn, cancel)))
                        {
                            await ProcessAcmeFqdnAsync(account, fqdn, cancel);
                        }
                    }
                }
            }
        }

        async Task<bool> CheckFqdnHasIpAddressOfThisLocalHostAsync(string fqdn, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            DnsResponse dnsResults = await TaskUtil.RetryAsync((c) => this.TcpIp.QueryDnsAsync(new DnsGetIpQueryParam(fqdn, DnsQueryOptions.Default, CoresConfig.CertVaultSettings.DnsTimeout), c),
                retryInterval: CoresConfig.CertVaultSettings.DnsTryInterval, tryCount: CoresConfig.CertVaultSettings.DnsTryCount);

            //Con.WriteLine(dnsResults.IPAddressList.Select(x => x.ToString())._Combine(","));

            HashSet<IPAddress> globalIpList = await this.TcpIp.GetLocalHostPossibleIpAddressListAsync(cancel);

            foreach (IPAddress ip in dnsResults.IPAddressList)
            {
                if (globalIpList.Contains(ip))
                {
                    return true;
                }
            }

            return false;
        }

        bool IsCertificateDateTimeToUpdate(DateTime notBefore, DateTime notAfter)
        {
            DateTime now = DateTime.UtcNow;

            DateTime middle = new DateTime((notBefore.Ticks + notAfter.Ticks) / 2);

            return (middle < now);
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
                    currentCert = CertificateUtil.ImportChainedCertificates(crtFileName.ReadDataFromFile().Span).First();
                }
                catch (Exception ex)
                {
                    ex._Debug();
                }
            }

            if (currentCert == null || IsCertificateDateTimeToUpdate(currentCert.CertData.NotBefore, currentCert.CertData.NotAfter))
            {
                //Con.WriteLine($"fqdn = {fqdn}, currentCert = {currentCert}, crtFileName = {crtFileName}");

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

            CertificateStore defaultCert = null;

            if (this.DefaultCertificate != null)
            {
                defaultCert = this.DefaultCertificate;
            }
            else
            {
                defaultCert = defaultCertPath.ReadAndParseDataFile(ReadParseFlags.ForceInitOnParseError,
                    data => new CertificateStore(data.Span),
                    () =>
                    {
                        if (this.DefaultCertificate != null)
                        {
                            return this.DefaultCertificate.ExportPkcs12();
                        }
                        else
                        {
                            PkiUtil.GenerateRsaKeyPair(2048, out PrivKey key, out _);
                            Certificate cert = new Certificate(key, new CertificateOptions(PkiAlgorithm.RSA, cn: Consts.Strings.DefaultCertCN + "_" + Env.MachineName, c: "US", expires: Util.MaxDateTimeOffsetValue));
                            CertificateStore store = new CertificateStore(cert, key);
                            return store.ExportPkcs12();
                        }
                    });
            }

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
                    if (type != CertVaultCertType.Acme || cert.Store.PrimaryContainer.CertificateList[0].CertData.NotAfter >= DateTime.UtcNow)
                    {
                        list.Add(cert);
                    }
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

        public CertificateStore SelectBestFitCertificate(string hostname, out CertificateHostnameType matchType, bool disableAcme = false)
        {
            hostname = hostname._NonNullTrim();

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

            if (this.Settings.UseAcme && disableAcme == false)
            {
                if (hostname._IsEmpty() == false && matchType == CertificateHostnameType.DefaultCert && hostname.Split('.').Length >= 2 && Str.CheckFqdn(hostname))
                {
                    // Add the request hostname to the ACME queue
                    if (AcmeQueue.Count < CoresConfig.CertVaultSettings.MaxAcmeQueueLen)
                    {
                        if (list.Where(x => x.CertType == CertVaultCertType.Acme).Count() < this.Settings.MaxAcmeCerts)
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
            }

            return selected.VaultCert.Store;
        }

        public CertificateStore CertificateStoreSelector(string sniHostname, bool disableAcme)
        {
            if (disableAcme == false)
            {
                return CertificateSelectorCache[sniHostname];
            }
            else
            {
                return CertificateSelectorCache_NoAcme[sniHostname];
            }
        }

        readonly SyncCache<string, PalX509Certificate> X509CertificateCache = new SyncCache<string, PalX509Certificate>(CoresConfig.CertVaultSettings.CertificateSelectorCacheLifetime);

        public PalX509Certificate X509CertificateSelector(string sniHostname, bool disableAcme)
        {
            sniHostname = sniHostname._NonNullTrim();

            CertificateStore store = CertificateStoreSelector(sniHostname, disableAcme);

            string sha1 = store.DigestSHA1Str;

            PalX509Certificate ret = X509CertificateCache[sha1];
            if (ret == null)
            {
                ret = store.GetX509Certificate();
                X509CertificateCache[sha1] = ret;
            }
            return ret;
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

        static CertificateStore DefaultCertificate = null;

        static Singleton<CertVault> Singleton = null;

        public static DirectoryPath BaseDir { get; private set; } = null;

        static AcmeAccount AcmeAccountForChallengeResponse = null;

        static void InitModule()
        {
            BaseDir = Path.Combine(Env.AppLocalDir, "Config", "CertVault");

            DefaultCertificate = null;

            Singleton = new Singleton<CertVault>(() =>
            {
                try
                {
                    BaseDir.CreateDirectory();
                }
                catch { }

                Util.PutGitIgnoreFileOnDirectory(BaseDir);

                CertVault vault = new CertVault(BaseDir, isGlobalVault: true, defaultCertificate: DefaultCertificate);

                return vault;
            });
        }

        public static CertVault GetGlobalCertVault() => Singleton;

        public static void SetAcmeAccountForChallengeResponse(AcmeAccount account)
        {
            AcmeAccountForChallengeResponse = account;
        }

        public static void SetDefaultCertificate(CertificateStore cert)
        {
            DefaultCertificate = cert;
        }

        public static AcmeAccount GetAcmeAccountForChallengeResponse() => AcmeAccountForChallengeResponse;

        static void FreeModule()
        {
            Singleton._DisposeSafe();
            Singleton = null;

            BaseDir = null;
            AcmeAccountForChallengeResponse = null;
            DefaultCertificate = null;
        }
    }
}

#endif  // CORES_BASIC_JSON
#endif  // CORES_BASIC_SECURITY;
