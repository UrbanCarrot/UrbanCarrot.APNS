using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using APNS.Models;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace APNS
{
    public interface IApnsClient
    {
        [NotNull]
        [ItemNotNull]
        Task<ApnsResponse> SendAsync(ApnsPush push, CancellationToken ct=default);
    }

    public class ApnsClient: IApnsClient
    {
        private const string DevelopmentEndpoint = "https://api.sandbox.push.apple.com";
        private const string ProductionEndpoint = "https://api.push.apple.com";

        private readonly string _keyId;
        private readonly string _teamId;
        private readonly string _bundleId;

        private readonly CngKey _key;
        
        private string _jwt;
        private DateTime _lastJwtGenerationTime;
        private readonly object _jwtRefreshLock = new object();

        private readonly HttpClient _http;
        private readonly bool _useCert;

        private readonly bool _isVoipCert;

        private bool _useSandbox;
        private bool _useBackupPort;

        public ApnsClient(HttpClient http, [NotNull] X509Certificate certificate)
        {
            _http = http;

            var split = certificate.Subject.Split(new[] {"0.9.2342.19200300.100.1.1="},
                StringSplitOptions.RemoveEmptyEntries);

            if (split.Length != 2)
            {
                split = certificate.Subject.Split(new[] {"userId="}, StringSplitOptions.RemoveEmptyEntries);
            }
            
            if (split.Length != 2)
            {
                // if subject prints `uid=xxx` instead of `0.9.2342.19200300.100.1.1=xxx`
                split = certificate.Subject.Split(new[] { "uid=" }, StringSplitOptions.RemoveEmptyEntries);
            }
            
            if (split.Length != 2)
                throw new InvalidOperationException("Provided certificate does not appear to be a valid APNs certificate.");
            
            var topic = split[1];
            _isVoipCert = topic.EndsWith(".voip");
            _bundleId = split[1].Replace(".voip", "");
            _useCert = true;
        }

        public ApnsClient([NotNull] HttpClient http, [NotNull] CngKey key, [NotNull] string keyId, [NotNull] string teamId, [NotNull] string bundleId)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _key = key ?? throw new ArgumentNullException(nameof(key));

            _keyId = keyId ?? throw new ArgumentNullException(nameof(keyId),
                $"Make sure {nameof(ApnsJwtOptions)}.{nameof(ApnsJwtOptions.KeyId)} is set to a non-null value.");

            _teamId = teamId ?? throw new ArgumentNullException(nameof(teamId),
                $"Make sure {nameof(ApnsJwtOptions)}.{nameof(ApnsJwtOptions.TeamId)} is set to a non-null value.");

            _bundleId = bundleId ?? throw new ArgumentNullException(nameof(bundleId),
                $"Make sure {nameof(ApnsJwtOptions)}.{nameof(ApnsJwtOptions.BundleId)} is set to a non-null value.");
        }
        
        public async Task<ApnsResponse> SendAsync(ApnsPush push, CancellationToken ct=default)
        {
            if (_useCert)
            {
                if (_isVoipCert && push.Type != ApnsPushType.Voip)
                    throw new InvalidOperationException(
                        "Provided certificate can only be used to send 'voip' type pushes.");
            }

            var payload = push.GeneratePayload();

            var url = UrlBuilder(push);

            var request = new HttpRequestMessage(HttpMethod.Post, url);

            request.Version = new Version(2, 0);
            request.Headers.Add("apns-priority", push.Priority.ToString());
            request.Headers.Add("apns-push-type", push.Type.ToString().ToLowerInvariant());
            request.Headers.Add("apns-topic", GetTopic(push.Type));

            if (!_useCert)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetOrGenerateJwt());
            }

            if (push.Expiration.HasValue)
            {
                var exp = push.Expiration.Value;

                request.Headers.Add("apns-expiration",
                    exp == DateTimeOffset.MinValue ? "0" : exp.ToUnixTimeSeconds().ToString());
            }

            request.Content = new JsonContent(payload);

            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var statusCode = (int) response.StatusCode;
            
            if (statusCode == 200)
                return ApnsResponse.Successful();

            ApnsErrorResponsePayload errorResponsePayload;

            try
            {
                errorResponsePayload = JsonConvert.DeserializeObject<ApnsErrorResponsePayload>(responseContent);
            }
            catch (JsonException exception)
            {
                return ApnsResponse.Error(ApnsResponseReason.Unknown, $"Status: {statusCode}, reason: {responseContent ?? "Not specified"}.");
            }
            
            Debug.Assert(errorResponsePayload != null);
            return ApnsResponse.Error(errorResponsePayload.Reason, errorResponsePayload.ReasonRaw);
        }

        public static ApnsClient CreateUsingJwt([NotNull] HttpClient http, [NotNull] ApnsJwtOptions options)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (options == null) throw new ArgumentNullException(nameof(options));

            string certContent;

            if (options.CertFilePath != null)
            {
                Debug.Assert(options.CertContent == null);
                certContent = File.ReadAllText(options.CertFilePath);
            }
            else if (options.CertContent != null)
            {
                Debug.Assert(options.CertFilePath == null);
                certContent = options.CertContent;
            }
            else
            {
                throw new ArgumentException("Either certificate file path or certificate contents must be provided.",
                    nameof(options));
            }
            
            certContent = certContent.Replace("\r", "").Replace("\n", "")
                .Replace("-----BEGIN PRIVATE KEY-----", "").Replace("-----END PRIVATE KEY-----", "");
            
            var key = CngKey.Import(Convert.FromBase64String(certContent), CngKeyBlobFormat.Pkcs8PrivateBlob);
            
            return new ApnsClient(http, key, options.KeyId, options.TeamId, options.BundleId);
        }
        
        public static ApnsClient CreateUsingCert([NotNull] X509Certificate2 cert)
        {
            if (cert == null) throw new ArgumentNullException(nameof(cert));

            var handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            handler.ClientCertificates.Add(cert);
            var client = new HttpClient(handler);

            return CreateUsingCustomHttpClient(client, cert);
        }

        public static ApnsClient CreateUsingCustomHttpClient([NotNull] HttpClient httpClient, [NotNull] X509Certificate2 cert)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (cert == null) throw new ArgumentNullException(nameof(cert));

            var apns = new ApnsClient(httpClient, cert);
            return apns;
        }

        public static ApnsClient CreateUsingCert([NotNull] string pathToCert, string certPassword = null)
        {
            if (string.IsNullOrWhiteSpace(pathToCert))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(pathToCert));

            var cert = new X509Certificate2(pathToCert, certPassword);
            return CreateUsingCert(cert);
        }

        public ApnsClient UseSandbox()
        {
            _useSandbox = true;
            return this;
        }
        
        public ApnsClient UseBackupPort()
        {
            _useBackupPort = true;
            return this;
        }

        private string GetOrGenerateJwt()
        {
            lock (_jwtRefreshLock)
            {
                return GetOrGenerateJwtInternal();
            }

            string GetOrGenerateJwtInternal()
            {
                if (_lastJwtGenerationTime > DateTime.UtcNow - TimeSpan.FromMinutes(20))
                    return _jwt;
                
                var now = DateTimeOffset.UtcNow;

                var header = JsonConvert.SerializeObject((new { alg = "ES256", kid = _keyId }));
                var payload = JsonConvert.SerializeObject(new { iss = _teamId, iat = now.ToUnixTimeSeconds() });
                
                var headerBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(header));
                var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
                var unsignedJwtData = $"{headerBase64}.{payloadBase64}";

                byte[] signature;

                using (var dsa = new ECDsaCng(_key))
                {
                    dsa.HashAlgorithm = CngAlgorithm.Sha256;
                    signature = dsa.SignData(Encoding.UTF8.GetBytes(unsignedJwtData));
                }
                
                _jwt = $"{unsignedJwtData}.{Convert.ToBase64String(signature)}";
                _lastJwtGenerationTime = now.UtcDateTime;
                
                return _jwt;
            }
        }

        private string UrlBuilder(ApnsPush push)
        {
            return (_useSandbox ? DevelopmentEndpoint : ProductionEndpoint)
                + (_useBackupPort ? ":2197" : ":443")
                + "/3/device/"
                + (push.Token ?? push.VoipToken);
        }
        
        private string GetTopic(ApnsPushType pushType)
        {
            switch (pushType)
            {
                case ApnsPushType.Background:
                case ApnsPushType.Alert:
                    return _bundleId;
                    break;
                case ApnsPushType.Voip:
                    return _bundleId + ".voip";
                case ApnsPushType.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(pushType), pushType, null);
            }
        }
    }
}