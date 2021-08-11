﻿// IPA Cores.NET
// 
// Copyright (c) 2019- IPA CyberLab.
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

// Author: Daiyuu Nobori
// Description

#if CORES_CODES_IISUTIL

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Web.Administration;

using IPA.Cores.Basic;
using IPA.Cores.Helper.Basic;
using static IPA.Cores.Globals.Basic;

using IPA.Cores.Codes;
using IPA.Cores.Helper.Codes;
using static IPA.Cores.Globals.Codes;

namespace IPA.Cores.Codes
{
    public class IisAdmin : AsyncService
    {
        ServerManager Svr;
        X509Store CertStore;

        public IisAdmin()
        {
            try
            {
                this.Svr = new ServerManager();

                this.CertStore = new X509Store(StoreName.My, StoreLocation.LocalMachine);

                this.CertStore.Open(OpenFlags.ReadWrite);
            }
            catch
            {
                this.Svr._DisposeSafe();
                throw;
            }
        }

        public Dictionary<string, Certificate> GetCurrentMachineCertificateList()
        {
            Dictionary<string, Certificate> ret = new Dictionary<string, Certificate>(StrComparer.IgnoreCaseComparer);

            foreach (var item in this.CertStore.Certificates)
            {
                var cert = item.AsPkiCertificate();

                ret.Add(cert.DigestSHA1Str, cert);
            }

            return ret;
        }

        public class BindItem
        {
            public string SiteName = "";
            public string BindingInfo = "";
            public Certificate Cert = null!;
            public CertificateStore? NewCert;
        }

        public void UpdateCerts(IEnumerable<CertificateStore> certsList)
        {
            List<BindItem> bindItems = new List<BindItem>();

            // サーバーに存在するすべての証明書リストを取得
            var currentCertDict = GetCurrentMachineCertificateList();

            // サーバーに存在するすべての証明書バインディングを取得
            foreach (var site in Svr.Sites.Where(x => x.Name._IsFilled()).OrderBy(x => x.Name))
            {
                foreach (var bind in site.Bindings.Where(x => x.BindingInformation._IsFilled()).OrderBy(x => x.BindingInformation))
                {
                    if (bind.Protocol._IsSamei("https"))
                    {
                        if (bind.CertificateHash != null && bind.CertificateHash.Length >= 1)
                        {
                            string hash = bind.CertificateHash._GetHexString();

                            if (currentCertDict.TryGetValue(hash, out Certificate? cert))
                            {
                                BindItem item = new BindItem
                                {
                                    BindingInfo = bind.BindingInformation,
                                    Cert = cert,
                                    SiteName = site.Name,
                                };

                                bindItems.Add(item);
                            }
                        }
                    }
                    else if (bind.Protocol._IsSamei("ftp"))
                    {
                        var ftpServer = site.GetChildElement("ftpServer");
                        if (ftpServer != null)
                        {
                            var security = ftpServer.GetChildElement("security");
                            if (security != null)
                            {
                                var ssl = security.GetChildElement("ssl");
                                if (ssl != null)
                                {
                                    string hash = (string)ssl.Attributes["serverCertHash"].Value;
                                    if (hash._IsFilled())
                                    {
                                        hash = hash._NormalizeHexString();

                                        if (currentCertDict.TryGetValue(hash, out Certificate? cert))
                                        {
                                            BindItem item = new BindItem
                                            {
                                                BindingInfo = "ftp",
                                                Cert = cert,
                                                SiteName = site.Name,
                                            };

                                            bindItems.Add(item);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // サーバーに存在するすべての証明書バインディングについて検討し、更新すべき証明書をマーク
            // ただしもともと有効期限が 約 3 年間よりも長い証明書が登録されている場合は、意図的に登録されているオレオレ証明書であるので、更新対象としてマークしない
            foreach (var item in bindItems.Where(x => x.Cert.ExpireSpan < Consts.Numbers.MaxCertExpireSpanTargetForUpdate))
            {
                var cert = item.Cert;

                List<CertificateStore> newCandidateCerts = new List<CertificateStore>();

                // 現存する証明書の DNS 名を全部調べる
                foreach (var certDns in cert.HostNameList)
                {
                    if (certDns.Type == CertificateHostnameType.SingleHost)
                    {
                        // aaa.example.org のような普通のホスト名
                        var candidates = certsList.GetHostnameMatchedCertificatesList(certDns.HostName);

                        candidates._DoForEach(x => newCandidateCerts.Add(x.Item1));
                    }
                    else if (certDns.Type == CertificateHostnameType.Wildcard)
                    {
                        // *.example.org のようなワイルドカードホスト名
                        var candidates = certsList.Where(x => x.PrimaryCertificate.HostNameList.Any(x => x.Type == CertificateHostnameType.Wildcard && x.HostName._IsSamei(certDns.HostName)));

                        candidates._DoForEach(x => newCandidateCerts.Add(x));
                    }
                }

                // 更新候補に挙げられた証明書リストの中で最も有効期限が長いものを選択
                var bestCert = newCandidateCerts.OrderByDescending(x => x.PrimaryCertificate.NotAfter).ThenBy(x => x.DigestSHA1Str).FirstOrDefault();
                if (bestCert != null)
                {
                    if (bestCert.DigestSHA1Str._IsSameHex(cert.DigestSHA1Str) == false)
                    {
                        // この最も有効期限が長い候補の証明書と、現在登録されている証明書との有効期限を比較し、候補証明書のほうが発行日が新しければ更新する
                        if (bestCert.NotBefore > cert.NotBefore)
                        {
                            // 更新対象としてマーク
                            item.NewCert = bestCert;
                        }
                    }
                }
            }

            // 更新対象としてマークされた証明書を証明書ストアに書き込む
            HashSet<string> newCertsHashList = new HashSet<string>(StrComparer.IgnoreCaseComparer);
            bindItems.Where(x => x.NewCert != null).Select(x => x.NewCert!.DigestSHA1Str!)._DoForEach(x => newCertsHashList.Add(x));

            foreach (var hash in newCertsHashList.OrderBy(x => x))
            {
                if (currentCertDict.ContainsKey(hash) == false)
                {
                    var certToWrite = certsList.Where(x => x.DigestSHA1Str._IsSamei(hash)).First();

                    this.CertStore.Add(certToWrite.X509Certificate);
                }
            }

            // 念のため証明書ストアにすべての証明書の書き込みが成功したかどうか検査する
            var certDict2 = GetCurrentMachineCertificateList();

            foreach (var hash in newCertsHashList.OrderBy(x => x))
            {
                var cert = certDict2[hash];
                if (cert.DigestSHA1Str._IsSameHex(hash) == false)
                {
                    throw new CoresLibException("Invalid certificate status! hash: " + hash);
                }
            }

            int numUpdates = 0;

            // IIS のバインディング設定を更新する
            foreach (var site in Svr.Sites.Where(x => x.Name._IsFilled()).OrderBy(x => x.Name))
            {
                foreach (var bind in site.Bindings.Where(x => x.BindingInformation._IsFilled()).OrderBy(x => x.BindingInformation))
                {
                    if (bind.Protocol._IsSamei("https"))
                    {
                        var item = bindItems.Where(x => x.NewCert != null && x.SiteName == site.Name && x.BindingInfo == bind.BindingInformation).SingleOrDefault();

                        if (item != null)
                        {
                            bind.CertificateHash = item.NewCert!.DigestSHA1Str._GetHexBytes();

                            Con.WriteLine($"UpdatedHttpsCert: Site = '{item.SiteName}', Binding = '{item.BindingInfo}', newCert = '{item.NewCert}', oldCert = '{item.Cert}'");

                            numUpdates++;
                        }
                    }
                    else if (bind.Protocol._IsSamei("ftp"))
                    {
                        var ftpServer = site.GetChildElement("ftpServer");
                        if (ftpServer != null)
                        {
                            var security = ftpServer.GetChildElement("security");
                            if (security != null)
                            {
                                var ssl = security.GetChildElement("ssl");
                                if (ssl != null)
                                {
                                    string hash = (string)ssl.Attributes["serverCertHash"].Value;
                                    if (hash._IsFilled())
                                    {
                                        hash = hash._NormalizeHexString();

                                        var item = bindItems.Where(x => x.NewCert != null && x.SiteName == site.Name && x.BindingInfo == "ftp").SingleOrDefault();

                                        if (item != null)
                                        {
                                            ssl["serverCertHash"] = item.NewCert!.DigestSHA1Str;

                                            Con.WriteLine($"UpdatedFtpCert: Site = '{item.SiteName}', Binding = '{item.BindingInfo}', newCert = '{item.NewCert}', oldCert = '{item.Cert}'");

                                            numUpdates++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (numUpdates >= 1)
            {
                Con.WriteLine($"Total {numUpdates} certs updated.");

                Svr.CommitChanges();
            }
            else
            {
                Con.WriteLine($"No certs updated.");
            }
        }

        public void Test()
        {
            foreach (var site in Svr.Sites)
            {
                site.Name._Print();
                foreach (var b in site.Bindings)
                {
                    b.CertificateHash._GetHexString()._Print();
                    b.CertificateStoreName._Print();
                }
            }
        }

        protected override async Task CleanupImplAsync(Exception? ex)
        {
            try
            {
                try
                {
                    this.CertStore.Close();
                }
                catch (Exception ex2)
                {
                    ex2._Debug();
                }
                this.CertStore._DisposeSafe();
            }
            finally
            {
                await base.CleanupImplAsync(ex);
            }
        }
    }
}

#endif

