
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Reference from: https://gist.github.com/ayende/c2bb440bb448dc290132956c6a9fff3b

using IPA.Cores.Helper.Basic;
using IPA.Cores.Basic;


using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Diagnostics;
using System.Reflection;

using static IPA.Cores.Globals.Basic;
using System.Runtime.InteropServices;
using IPA.Cores.ClientApi.Acme;
using Newtonsoft.Json.Converters;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.FileProviders;
using System.Web;
using IPA.Cores.Basic.App.DaemonCenterLib;
using IPA.Cores.ClientApi.GoogleApi;


namespace IPA.TestDev
{
    partial class TestDevCommands
    {
        [ConsoleCommand(
            "Authenticode 署名の実施 (内部用)",
            "SignAuthenticodeInternal [filename] [/out:output] [/comment:string] [/driver:yes] [/cert:type]",
            "Authenticode 署名の実施 (内部用)")]
        static int SignAuthenticodeInternal(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "Input Filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("out"),
                new ConsoleParam("comment"),
                new ConsoleParam("driver"),
                new ConsoleParam("cert"),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string srcPath = vl.DefaultParam.StrValue;

            string dstPath = vl["out"].StrValue;
            if (dstPath._IsEmpty()) dstPath = srcPath;

            string comment = vl["comment"].StrValue;
            bool driver = vl["driver"].BoolValue;
            string cert = vl["cert"].StrValue;

            using (AuthenticodeSignClient ac = new AuthenticodeSignClient("https://127.0.0.1:7006/sign", "7BDBCA40E9C4CE374C7889CD3A26EE8D485B94153C2943C09765EEA309FCA13D"))
            {
                var srcData = Load(srcPath);

                var dstData = ac.SignSeInternalAsync(srcData, cert, driver ? "Driver" : "", comment._FilledOrDefault("Authenticode"))._GetResult();

                dstData._Save(dstPath, flags: FileFlags.AutoCreateDirectory);
            }

            return 0;
        }

        [ConsoleCommand(
            "自己署名証明書の作成",
            "CertSelfSignedGenerate [filename] /cn:hostName",
            "自己署名証明書の作成")]
        static int CertSelfSignedGenerate(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "Output filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("cn", ConsoleService.Prompt, "Common name: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string path = vl.DefaultParam.StrValue;
            string cn = vl["cn"].StrValue;

            PkiUtil.GenerateRsaKeyPair(2048, out PrivKey newKey, out _);

            Certificate newCert = new Certificate(newKey, new CertificateOptions(PkiAlgorithm.RSA, cn: cn.Trim(), c: "JP"));
            CertificateStore newCertStore = new CertificateStore(newCert, newKey);

            newCertStore.ExportPkcs12()._Save(path, FileFlags.AutoCreateDirectory);

            return 0;
        }

        [ConsoleCommand(
            "開発用証明書の作成",
            "CertDevSignedGenerate [filename] /cn:hostName",
            "開発用証明書の作成")]
        static int CertDevSignedGenerate(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[filename]", ConsoleService.Prompt, "Output filename: ", ConsoleService.EvalNotEmpty, null),
                new ConsoleParam("cn", ConsoleService.Prompt, "Common name: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string path = vl.DefaultParam.StrValue;
            string cn = vl["cn"].StrValue;

            PkiUtil.GenerateRsaKeyPair(2048, out PrivKey newKey, out _);

            Certificate newCert = new Certificate(newKey, DevTools.CoresDebugCACert.PkiCertificateStore, new CertificateOptions(PkiAlgorithm.RSA, cn: cn.Trim(), c: "JP"));
            CertificateStore newCertStore = new CertificateStore(newCert, newKey);

            newCertStore.ExportPkcs12()._Save(path, FileFlags.AutoCreateDirectory);

            return 0;
        }

        public class DirectionCrossResults
        {
            public string? Start;
            public string? End;
            public string? Error;
            public string? StartAddress;
            public string? EndAddress;

            public TimeSpan Duration;
            public double DistanceKm;
            public string? RouteSummary;
        }

        [ConsoleCommand(
            "Google Maps 所要時間クロス表の作成",
            "GoogleMapsDirectionCross [dir]",
            "Google Maps 所要時間クロス表の作成",
            "[dir]:You can specify the directory.")]
        static int GoogleMapsDirectionCross(ConsoleService c, string cmdName, string str)
        {
            ConsoleParam[] args =
            {
                new ConsoleParam("[dir]", ConsoleService.Prompt, "Directory path: ", ConsoleService.EvalNotEmpty, null),
            };

            ConsoleParamValueList vl = c.ParseCommandList(cmdName, str, args);

            string dir = vl.DefaultParam.StrValue;

            string apiKey = Lfs.ReadStringFromFile(dir._CombinePath("ApiKey.txt"), oneLine: true);

            string srcListText = Lfs.ReadStringFromFile(dir._CombinePath("Source.txt"));
            string destListText = Lfs.ReadStringFromFile(dir._CombinePath("Destination.txt"));

            string[] srcList = srcListText._GetLines(removeEmpty: true);
            string[] destList = destListText._GetLines(removeEmpty: true);

            using var googleMapsApi = new GoogleMapsApi(new GoogleMapsApiSettings(apiKey: apiKey));

            DateTimeOffset departure = Util.GetStartOfDay(DateTime.Now.AddDays(2))._AsDateTimeOffset(isLocalTime: true);

            List<DirectionCrossResults> csv = new List<DirectionCrossResults>();

            foreach (string src in srcList)
            {
                foreach (string dest in destList)
                {
                    Console.WriteLine($"「{src}」 → 「{dest}」 ...");

                    DirectionCrossResults r = new DirectionCrossResults();

                    r.Start = src;
                    r.End = dest;

                    try
                    {
                        var result = googleMapsApi.CalcDurationAsync(src, dest, departure)._GetResult();

                        if (result.IsError == false)
                        {
                            r.StartAddress = result.StartAddress;
                            r.EndAddress = result.EndAddress;
                            r.Error = "";
                            r.Duration = result.Duration;
                            r.DistanceKm = result.DistanceKm;
                            r.RouteSummary = result.RouteSummary;

                            $"  {r.Duration} - {r.DistanceKm} km ({r.RouteSummary})"._Print();
                        }
                        else
                        {
                            r.Error = result.ErrorString;
                            r.Error._Print();
                        }
                    }
                    catch (Exception ex)
                    {
                        ex._Debug();
                        r.Error = ex.Message;
                    }

                    csv.Add(r);
                }
            }

            string csvText = csv._ObjectArrayToCsv(withHeader: true);

            Lfs.WriteStringToFile(dir._CombinePath("Result.csv"), csvText, writeBom: true);

            return 0;
        }
    }
}


class LetsEncryptClient
{
    public const string StagingV2 = "https://acme-staging-v02.api.letsencrypt.org/directory";

    private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
    };

    private static Dictionary<string, HttpClient> _cachedClients = new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);

    private static HttpClient GetCachedClient(string url)
    {
        if (_cachedClients.TryGetValue(url, out var value))
        {
            return value;
        }

        lock (Locker)
        {
            if (_cachedClients.TryGetValue(url, out value))
            {
                return value;
            }

            var httpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; },
            };

            value = new HttpClient(httpClientHandler)
            {
                BaseAddress = new Uri(url)
            };

            _cachedClients = new Dictionary<string, HttpClient>(_cachedClients, StringComparer.OrdinalIgnoreCase)
            {
                [url] = value
            };
            return value;
        }
    }





#nullable disable

    /// <summary>
    ///     In our scenario, we assume a single single wizard progressing
    ///     and the locking is basic to the wizard progress. Adding explicit
    ///     locking to be sure that we are not corrupting disk state if user
    ///     is explicitly calling stuff concurrently (running the setup wizard
    ///     from two tabs?)
    /// </summary>
    private static readonly object Locker = new object();

    private Jws _jws;
    private readonly string _path;
    private readonly string _url;
    private string _nonce;
    private RSACryptoServiceProvider _accountKey;
    private RegistrationCache _cache;
    private HttpClient _client;
    private Directory _directory;
    private List<AuthorizationChallenge> _challenges = new List<AuthorizationChallenge>();
    private Order _currentOrder;

    public LetsEncryptClient(string url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        var hash = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(url));
        var file = Jws.Base64UrlEncoded(hash) + ".lets-encrypt.cache.json";
        _path = Path.Combine(home, file);
    }

    public async Task Init(string email, CancellationToken token = default(CancellationToken))
    {
        _accountKey = new RSACryptoServiceProvider(4096);
        _client = GetCachedClient(_url);
        (_directory, _) = await SendAsyncStd<Directory>(HttpMethod.Get, new Uri("directory", UriKind.Relative), null, token);

        /*if (File.Exists(_path))
        {
            bool success;
            try
            {
                lock (Locker)
                {
                    _cache = JsonConvert.DeserializeObject<RegistrationCache>(File.ReadAllText(_path));
                }

                _accountKey.ImportCspBlob(_cache.AccountKey);
                _jws = new Jws(_accountKey, _cache.Id);
                success = true;
            }
            catch
            {
                success = false;
                // if we failed for any reason, we'll just
                // generate a new registration
            }

            if (success)
            {
                return;
            }
        }*/

        _jws = new Jws(_accountKey, null);

        string newAccountUrl = _directory.NewAccount.ToString().Replace("https:", "https:");

        //newAccountUrl = "https://pc37.sehosts.com/a";

        var (account, response) = await SendAsync2<Account>(IPA.Cores.Basic.HttpClientCore.HttpMethod.Post, new Uri(newAccountUrl), new Account
        {
            // we validate this in the UI before we get here, so that is fine
            TermsOfServiceAgreed = true,
            Contacts = new[] { "mailto:" + email },
        }, token);
        _jws.SetKeyId(account);

        if (account.Status != "valid")
            throw new InvalidOperationException("Account status is not valid, was: " + account.Status + Environment.NewLine + response);

        lock (Locker)
        {
            _cache = new RegistrationCache
            {
                Location = account.Location,
                AccountKey = _accountKey.ExportCspBlob(true),
                Id = account.Id,
                Key = account.Key
            };
            File.WriteAllText(_path,
                JsonConvert.SerializeObject(_cache, Formatting.Indented));
        }
    }


    private async Task<(TResult Result, string Response)> SendAsyncStd<TResult>(HttpMethod method, Uri uri, object message, CancellationToken token) where TResult : class
    {
        IPA.Cores.ClientApi.Acme.AcmeClient ac = new IPA.Cores.ClientApi.Acme.AcmeClient(new IPA.Cores.ClientApi.Acme.AcmeClientOptions());

        _nonce = await ac.GetNonceAsync();

        message._PrintAsJson();

        var request = new HttpRequestMessage(method, uri);

        string json = "";

        if (message != null)
        {
            var encodedMessage = _jws.Encode(message, new JwsHeader
            {
                Nonce = _nonce,
                Url = uri
            });
            json = JsonConvert.SerializeObject(encodedMessage, jsonSettings);

            json._Print();

            encodedMessage._PrintAsJson();

            request.Content = new StringContent(json, Encoding.UTF8, "application/jose+json");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jose+json");
        }

        var response = await _client.SendAsync(request, token).ConfigureAwait(false);

        //_nonce = response.Headers.GetValues("Replay-Nonce").First();

        if (response.Content.Headers.ContentType.MediaType == "application/problem+json")
        {
            var problemJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var problem = JsonConvert.DeserializeObject<Problem>(problemJson);
            problem.RawJson = problemJson;
            throw new LetsEncrytException(problem, response);
        }

        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (typeof(TResult) == typeof(string)
            && response.Content.Headers.ContentType.MediaType == "application/pem-certificate-chain")
        {
            return ((TResult)(object)responseText, null);
        }

        var responseContent = JObject.Parse(responseText).ToObject<TResult>();

        if (responseContent is IHasLocation ihl)
        {
            if (response.Headers.Location != null)
                ihl.Location = response.Headers.Location;
        }

        responseText._Print();

        return (responseContent, responseText);
    }

    private async Task<(TResult Result, string Response)> SendAsync2<TResult>(IPA.Cores.Basic.HttpClientCore.HttpMethod method, Uri uri, object message, CancellationToken token) where TResult : class
    {
        IPA.Cores.ClientApi.Acme.AcmeClient ac = new IPA.Cores.ClientApi.Acme.AcmeClient(new IPA.Cores.ClientApi.Acme.AcmeClientOptions());

        _nonce = await ac.GetNonceAsync();

        message._PrintAsJson();

        var request = new IPA.Cores.Basic.HttpClientCore.HttpRequestMessage(method, uri);

        string json = "";

        if (message != null)
        {
            var encodedMessage = _jws.Encode(message, new JwsHeader
            {
                Nonce = _nonce,
                Url = uri
            });
            json = JsonConvert.SerializeObject(encodedMessage, jsonSettings);

            json._Print();

            encodedMessage._PrintAsJson();

            request.Content = new IPA.Cores.Basic.HttpClientCore.StringContent(json, Encoding.UTF8, "application/jose+json");
            request.Content.Headers.ContentType = new IPA.Cores.Basic.HttpClientCore.MediaTypeHeaderValue("application/jose+json");
        }


        var webapi = new WebApi(new WebApiOptions(new WebApiSettings() { AllowAutoRedirect = true }));

        var response = await webapi.Client.SendAsync(request, token).ConfigureAwait(false);




        //_nonce = response.Headers.GetValues("Replay-Nonce").First();

        if (response.Content.Headers.ContentType.MediaType == "application/problem+json")
        {
            var problemJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var problem = JsonConvert.DeserializeObject<Problem>(problemJson);
            problemJson._Print();
            problem.RawJson = problemJson;
            throw new ApplicationException();
        }

        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (typeof(TResult) == typeof(string)
            && response.Content.Headers.ContentType.MediaType == "application/pem-certificate-chain")
        {
            return ((TResult)(object)responseText, null);
        }

        var responseContent = JObject.Parse(responseText).ToObject<TResult>();

        if (responseContent is IHasLocation ihl)
        {
            if (response.Headers.Location != null)
                ihl.Location = response.Headers.Location;
        }

        responseText._Print();


        return (responseContent, responseText);
    }

    private async Task<(TResult Result, string Response)> SendAsync<TResult>(HttpMethod method, Uri uri, object message, CancellationToken token) where TResult : class
    {
        IPA.Cores.ClientApi.Acme.AcmeClient ac = new IPA.Cores.ClientApi.Acme.AcmeClient(new IPA.Cores.ClientApi.Acme.AcmeClientOptions());

        _nonce = await ac.GetNonceAsync();

        message._PrintAsJson();

        var request = new HttpRequestMessage(method, uri);

        string json = "";

        if (message != null)
        {
            var encodedMessage = _jws.Encode(message, new JwsHeader
            {
                Nonce = _nonce,
                Url = uri
            });
            json = JsonConvert.SerializeObject(encodedMessage, jsonSettings);

            json._Print();

            encodedMessage._PrintAsJson();

            request.Content = new StringContent(json, Encoding.UTF8, "application/jose+json");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/jose+json");
        }

        //var response = await _client.SendAsync(request, token).ConfigureAwait(false);

        var webapi = new WebApi(new WebApiOptions(new WebApiSettings() { SslAcceptAnyCerts = true, Timeout = CoresConfig.AcmeClientSettings.ShortTimeout }, null));

        var webret = await webapi.SimplePostJsonAsync(WebMethods.POST, uri.ToString(), json, default, "application/jose+json");


        webret.Data._GetString_Ascii()._Print();

        return default;

        /*
        //_nonce = response.Headers.GetValues("Replay-Nonce").First();

        if (response.Content.Headers.ContentType.MediaType == "application/problem+json")
        {
            var problemJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var problem = JsonConvert.DeserializeObject<Problem>(problemJson);
            problem.RawJson = problemJson;
            throw new LetsEncrytException(problem, response);
        }

        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (typeof(TResult) == typeof(string)
            && response.Content.Headers.ContentType.MediaType == "application/pem-certificate-chain")
        {
            return ((TResult)(object)responseText, null);
        }

        var responseContent = JObject.Parse(responseText).ToObject<TResult>();

        if (responseContent is IHasLocation ihl)
        {
            if (response.Headers.Location != null)
                ihl.Location = response.Headers.Location;
        }

        return (responseContent, responseText);*/
    }


    public async Task<Dictionary<string, string>> NewOrder(string[] hostnames, CancellationToken token = default(CancellationToken))
    {
        _challenges.Clear();
        var (order, response) = await SendAsync<Order>(HttpMethod.Post, _directory.NewOrder, new Order
        {
            Expires = DateTime.UtcNow.AddDays(2),
            Identifiers = hostnames.Select(hostname => new OrderIdentifier
            {
                Type = "dns",
                Value = hostname
            }).ToArray()
        }, token);

        if (order.Status != "pending")
            throw new InvalidOperationException("Created new order and expected status 'pending', but got: " + order.Status + Environment.NewLine +
                response);
        _currentOrder = order;
        var results = new Dictionary<string, string>();
        foreach (var item in order.Authorizations)
        {
            var (challengeResponse, responseText) = await SendAsync<AuthorizationChallengeResponse>(HttpMethod.Get, item, null, token);
            if (challengeResponse.Status == "valid")
                continue;

            if (challengeResponse.Status != "pending")
                throw new InvalidOperationException("Expected autorization status 'pending', but got: " + order.Status +
                    Environment.NewLine + responseText);

            var challenge = challengeResponse.Challenges.First(x => x.Type == "dns-01");
            _challenges.Add(challenge);
            var keyToken = _jws.GetKeyAuthorization(challenge.Token);
            using (var sha256 = SHA256.Create())
            {
                var dnsToken = Jws.Base64UrlEncoded(sha256.ComputeHash(Encoding.UTF8.GetBytes(keyToken)));
                results[challengeResponse.Identifier.Value] = dnsToken;
            }
        }

        return results;
    }

    public async Task CompleteChallenges(CancellationToken token = default(CancellationToken))
    {
        for (var index = 0; index < _challenges.Count; index++)
        {
            var challenge = _challenges[index];

            while (true)
            {
                var (result, responseText) = await SendAsync<AuthorizationChallengeResponse>(HttpMethod.Post, challenge.Url, new AuthorizeChallenge
                {
                    KeyAuthorization = _jws.GetKeyAuthorization(challenge.Token)
                }, token);

                if (result.Status == "valid")
                    break;
                if (result.Status != "pending")
                    throw new InvalidOperationException("Failed autorization of " + _currentOrder.Identifiers[index].Value + Environment.NewLine + responseText);

                await Task.Delay(500);
            }
        }
    }

    public async Task<(X509Certificate2 Cert, RSA PrivateKey)> GetCertificate(CancellationToken token = default(CancellationToken))
    {
        var key = new RSACryptoServiceProvider(4096);
        var csr = new CertificateRequest("CN=" + _currentOrder.Identifiers[0].Value,
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        foreach (var host in _currentOrder.Identifiers)
            san.AddDnsName(host.Value);

        csr.CertificateExtensions.Add(san.Build());

        var (response, responseText) = await SendAsync<Order>(HttpMethod.Post, _currentOrder.Finalize, new FinalizeRequest
        {
            CSR = Jws.Base64UrlEncoded(csr.CreateSigningRequest())
        }, token);

        while (response.Status != "valid")
        {
            (response, responseText) = await SendAsync<Order>(HttpMethod.Get, response.Location, null, token);

            if (response.Status == "processing")
            {
                await Task.Delay(500);
                continue;
            }
            throw new InvalidOperationException("Invalid order status: " + response.Status + Environment.NewLine +
                responseText);
        }
        var (pem, _) = await SendAsync<string>(HttpMethod.Get, response.Certificate, null, token);

        var cert = new X509Certificate2(Encoding.UTF8.GetBytes(pem));

        _cache.CachedCerts[_currentOrder.Identifiers[0].Value] = new CertificateCache
        {
            Cert = pem,
            Private = key.ExportCspBlob(true)
        };

        lock (Locker)
        {
            File.WriteAllText(_path,
                JsonConvert.SerializeObject(_cache, Formatting.Indented));
        }

        return (cert, key);
    }

    public class CachedCertificateResult
    {
        public RSA PrivateKey;
        public string Certificate;
    }

    public bool TryGetCachedCertificate(List<string> hosts, out CachedCertificateResult value)
    {
        value = null;
        if (_cache.CachedCerts.TryGetValue(hosts[0], out var cache) == false)
        {
            return false;
        }

        var cert = new X509Certificate2(cache.Cert);

        // if it is about to expire, we need to refresh
        if ((cert.NotAfter - DateTime.UtcNow).TotalDays < 14)
            return false;

        var rsa = new RSACryptoServiceProvider(4096);
        rsa.ImportCspBlob(cache.Private);

        value = new CachedCertificateResult
        {
            Certificate = cache.Cert,
            PrivateKey = rsa
        };
        return true;
    }


    public string GetTermsOfServiceUri(CancellationToken token = default(CancellationToken))
    {
        return _directory.Meta.TermsOfService;
    }

    public void ResetCachedCertificate(IEnumerable<string> hostsToRemove)
    {
        foreach (var host in hostsToRemove)
        {
            _cache.CachedCerts.Remove(host);
        }
    }


    private class RegistrationCache
    {
        public readonly Dictionary<string, CertificateCache> CachedCerts = new Dictionary<string, CertificateCache>(StringComparer.OrdinalIgnoreCase);
        public byte[] AccountKey;
        public string Id;
        public Jwk Key;
        public Uri Location;
    }

    private class CertificateCache
    {
        public string Cert;
        public byte[] Private;
    }

    private class AuthorizationChallengeResponse
    {
        [JsonProperty("identifier")]
        public OrderIdentifier Identifier { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("expires")]
        public DateTime? Expires { get; set; }

        [JsonProperty("wildcard")]
        public bool Wildcard { get; set; }

        [JsonProperty("challenges")]
        public AuthorizationChallenge[] Challenges { get; set; }
    }

    private class AuthorizeChallenge
    {
        [JsonProperty("keyAuthorization")]
        public string KeyAuthorization { get; set; }

    }

    private class AuthorizationChallenge
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

    }

    private class Jwk
    {
        [JsonProperty("kty")]
        public string KeyType { get; set; }

        [JsonProperty("kid")]
        public string KeyId { get; set; }

        [JsonProperty("use")]
        public string Use { get; set; }

        [JsonProperty("n")]
        public string Modulus { get; set; }

        [JsonProperty("e")]
        public string Exponent { get; set; }

        [JsonProperty("d")]
        public string D { get; set; }

        [JsonProperty("p")]
        public string P { get; set; }

        [JsonProperty("q")]
        public string Q { get; set; }

        [JsonProperty("dp")]
        public string DP { get; set; }

        [JsonProperty("dq")]
        public string DQ { get; set; }

        [JsonProperty("qi")]
        public string InverseQ { get; set; }

        [JsonProperty("alg")]
        public string Algorithm { get; set; }
    }

    private class Directory
    {
        [JsonProperty("keyChange")]
        public Uri KeyChange { get; set; }

        [JsonProperty("newNonce")]
        public Uri NewNonce { get; set; }

        [JsonProperty("newAccount")]
        public Uri NewAccount { get; set; }

        [JsonProperty("newOrder")]
        public Uri NewOrder { get; set; }

        [JsonProperty("revokeCert")]
        public Uri RevokeCertificate { get; set; }

        [JsonProperty("meta")]
        public DirectoryMeta Meta { get; set; }
    }

    private class DirectoryMeta
    {
        [JsonProperty("termsOfService")]
        public string TermsOfService { get; set; }
    }

    public class Problem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("detail")]
        public string Detail { get; set; }

        public string RawJson { get; set; }
    }

    public class LetsEncrytException : Exception
    {
        public LetsEncrytException(Problem problem, HttpResponseMessage response)
            : base($"{problem.Type}: {problem.Detail}")
        {
            Problem = problem;
            Response = response;
        }

        public Problem Problem { get; }

        public HttpResponseMessage Response { get; }
    }

    private class JwsMessage
    {
        [JsonProperty("header")]
        public JwsHeader Header { get; set; }

        [JsonProperty("protected")]
        public string Protected { get; set; }

        [JsonProperty("payload")]
        public string Payload { get; set; }

        [JsonProperty("signature")]
        public string Signature { get; set; }
    }

    private class JwsHeader
    {
        public JwsHeader()
        {
        }

        public JwsHeader(string algorithm, Jwk key)
        {
            Algorithm = algorithm;
            Key = key;
        }

        [JsonProperty("alg")]
        public string Algorithm { get; set; }

        [JsonProperty("jwk")]
        public Jwk Key { get; set; }


        [JsonProperty("kid")]
        public string KeyId { get; set; }


        [JsonProperty("nonce")]
        public string Nonce { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }
    }

    private interface IHasLocation
    {
        Uri Location { get; set; }
    }

    private class Order : IHasLocation
    {
        public Uri Location { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("expires")]
        public DateTime? Expires { get; set; }

        [JsonProperty("identifiers")]
        public OrderIdentifier[] Identifiers { get; set; }

        [JsonProperty("notBefore")]
        public DateTime? NotBefore { get; set; }

        [JsonProperty("notAfter")]
        public DateTime? NotAfter { get; set; }

        [JsonProperty("error")]
        public Problem Error { get; set; }

        [JsonProperty("authorizations")]
        public Uri[] Authorizations { get; set; }

        [JsonProperty("finalize")]
        public Uri Finalize { get; set; }

        [JsonProperty("certificate")]
        public Uri Certificate { get; set; }
    }

    private class OrderIdentifier
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

    }

    private class Account : IHasLocation
    {
        [JsonProperty("termsOfServiceAgreed")]
        public bool TermsOfServiceAgreed { get; set; }

        [JsonProperty("contact")]
        public string[] Contacts { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("key")]
        public Jwk Key { get; set; }

        [JsonProperty("initialIp")]
        public string InitialIp { get; set; }

        [JsonProperty("orders")]
        public Uri Orders { get; set; }

        public Uri Location { get; set; }
    }

    private class FinalizeRequest
    {
        [JsonProperty("csr")]
        public string CSR { get; set; }
    }

    private class Jws
    {
        private readonly Jwk _jwk;
        private readonly RSA _rsa;

        public Jws(RSA rsa, string keyId)
        {
            _rsa = rsa ?? throw new ArgumentNullException(nameof(rsa));

            var publicParameters = rsa.ExportParameters(false);

            _jwk = new Jwk
            {
                KeyType = "RSA",
                Exponent = Base64UrlEncoded(publicParameters.Exponent),
                Modulus = Base64UrlEncoded(publicParameters.Modulus),
                KeyId = keyId
            };
        }

        public JwsMessage Encode<TPayload>(TPayload payload, JwsHeader protectedHeader)
        {
            protectedHeader.Algorithm = "RS256";
            if (_jwk.KeyId != null)
            {
                protectedHeader.KeyId = _jwk.KeyId;
            }
            else
            {
                protectedHeader.Key = _jwk;
            }

            var message = new JwsMessage
            {
                Payload = Base64UrlEncoded(JsonConvert.SerializeObject(payload)),
                Protected = Base64UrlEncoded(JsonConvert.SerializeObject(protectedHeader))
            };

            message.Signature = Base64UrlEncoded(
                _rsa.SignData(Encoding.ASCII.GetBytes(message.Protected + "." + message.Payload),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));

            return message;
        }

        private string GetSha256Thumbprint()
        {
            var json = "{\"e\":\"" + _jwk.Exponent + "\",\"kty\":\"RSA\",\"n\":\"" + _jwk.Modulus + "\"}";

            using (var sha256 = SHA256.Create())
            {
                return Base64UrlEncoded(sha256.ComputeHash(Encoding.UTF8.GetBytes(json)));
            }
        }

        public string GetKeyAuthorization(string token)
        {
            return token + "." + GetSha256Thumbprint();
        }

        public static string Base64UrlEncoded(string s)
        {
            return Base64UrlEncoded(Encoding.UTF8.GetBytes(s));
        }

        public static string Base64UrlEncoded(byte[] arg)
        {
            var s = Convert.ToBase64String(arg); // Regular base64 encoder
            s = s.Split('=')[0]; // Remove any trailing '='s
            s = s.Replace('+', '-'); // 62nd char of encoding
            s = s.Replace('/', '_'); // 63rd char of encoding
            return s;
        }

        internal void SetKeyId(Account account)
        {
            _jwk.KeyId = account.Id;
        }
    }
}
