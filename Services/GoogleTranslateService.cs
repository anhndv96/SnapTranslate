using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SnapTranslate.Services
{
    public class GoogleTranslateService : ITranslateService
    {
        private readonly HttpClient _httpClient;
        private TranslateTokens? _tokens;
        private DateTime _tokenTime;

        private class TranslateTokens
        {
            public string FSid { get; set; } = "";
            public string At { get; set; } = "";
            public string Bl { get; set; } = "";
        }

        public GoogleTranslateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (httpClient == null)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(15);
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://translate.google.com/");
            }
        }

        private async Task EnsureTokensAsync()
        {
            if (_tokens != null && (DateTime.UtcNow - _tokenTime).TotalMinutes < 30)
                return;

            await RefreshTokensAsync().ConfigureAwait(false);
        }

        private async Task RefreshTokensAsync()
        {
            using var response = await _httpClient.GetAsync("https://translate.google.com/").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var fSidMatch = Regex.Match(html, "\"FdrFJe\":\"([^\"]+)\"");
            if (!fSidMatch.Success)
                fSidMatch = Regex.Match(html, "\"cfb2h\":\"([^\"]+)\"");

            var atMatch = Regex.Match(html, "\"SNlM0e\":\"([^\"]+)\"");

            _tokens = new TranslateTokens
            {
                FSid = fSidMatch.Success ? fSidMatch.Groups[1].Value : "",
                At = atMatch.Success ? atMatch.Groups[1].Value : "",
                Bl = fSidMatch.Success ? fSidMatch.Groups[1].Value : "",
            };

            _tokenTime = DateTime.UtcNow;
        }

        public async Task<TranslateResult> TranslateAsync(string text, string target = "vi", Action<string>? onChunkReceived = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new TranslateResult { Translated = "", Detected = "unknown" };

            try
            {
                await EnsureTokensAsync().ConfigureAwait(false);

                var fReqPayload = BuildFReqPayload(text, target);

                var url = "https://translate.google.com/_/TranslateWebserverUi/data/batchexecute";
                var queryParams = new Dictionary<string, string>
                {
                    ["rpcids"] = "MkEWBc",
                    ["source-path"] = "/",
                    ["f.sid"] = _tokens!.FSid,
                    ["bl"] = _tokens.Bl,
                    ["hl"] = "vi",
                    ["soc-app"] = "1",
                    ["soc-platform"] = "1",
                    ["soc-device"] = "1",
                    ["_reqid"] = "12345",
                    ["rt"] = "c",
                };

                using var queryContent = new FormUrlEncodedContent(queryParams);
                var queryString = await queryContent.ReadAsStringAsync().ConfigureAwait(false);
                var fullUrl = $"{url}?{queryString}";

                var postData = new Dictionary<string, string>
                {
                    ["f.req"] = fReqPayload,
                    ["at"] = _tokens.At,
                };
                using var dataContent = new FormUrlEncodedContent(postData);

                string raw;
                using (var response = await _httpClient.PostAsync(fullUrl, dataContent).ConfigureAwait(false))
                {
                    raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // If token expired, retry once
                    if ((int)response.StatusCode == 400 || (int)response.StatusCode == 403)
                    {
                        await RefreshTokensAsync().ConfigureAwait(false);
                        queryParams["f.sid"] = _tokens!.FSid;
                        queryParams["bl"] = _tokens.Bl;
                        using var retryQueryContent = new FormUrlEncodedContent(queryParams);
                        queryString = await retryQueryContent.ReadAsStringAsync().ConfigureAwait(false);
                        fullUrl = $"{url}?{queryString}";
                        postData["at"] = _tokens.At;
                        using var retryDataContent = new FormUrlEncodedContent(postData);
                        using var retryResponse = await _httpClient.PostAsync(fullUrl, retryDataContent).ConfigureAwait(false);
                        raw = await retryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }

                return ParseTranslateResult(raw);
            }
            catch (Exception)
            {
                return new TranslateResult { Translated = "Lỗi kết nối dịch thuật...", Detected = "error" };
            }
        }

        /// <summary>
        /// Builds the f.req payload. Structure matches the Python gtran_scrapper:
        /// [[["MkEWBc", json.dumps([[text, "auto", target, 1, null, 2], []]), null, "generic"]]]
        /// </summary>
        private string BuildFReqPayload(string text, string target)
        {
            // Inner params: [[text, "auto", target, 1, null, 2], []]
            var innerArray = new object?[] { text, "auto", target, 1, null, 2 };
            var innerParams = new object?[] { innerArray, Array.Empty<object>() };
            var innerParamsJson = JsonSerializer.Serialize(innerParams);

            // Wrap in MkEWBc RPC call structure
            // JsonSerializer will escape quotes in innerParamsJson automatically
            var rpcCall = new object?[] { "MkEWBc", innerParamsJson, null, "generic" };
            var rpcArray = new object?[] { rpcCall };
            var outerPayload = new object?[] { rpcArray };
            var payloadJson = JsonSerializer.Serialize(outerPayload);

            return payloadJson;
        }

        private TranslateResult ParseTranslateResult(string raw)
        {
            foreach (var line in raw.Split('\n'))
            {
                if (!line.Contains("MkEWBc"))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // The response is: [[["MkEWBc","...response data...",null,"generic"]]]
                    // Root[0][2] contains the JSON-encoded inner response
                    var innerStr = root[0][2].GetString();
                    if (innerStr == null) continue;

                    using var innerDoc = JsonDocument.Parse(innerStr);
                    var innerRoot = innerDoc.RootElement;

                    // innerRoot[0][2] = detected language code
                    var detected = innerRoot[0][2].GetString() ?? "";
                    
                    var translated = "";
                    try
                    {
                        var sentencesList = innerRoot[1][0][0][5];
                        if (sentencesList.ValueKind == JsonValueKind.Array)
                        {
                            var parts = new List<string>();
                            foreach (var sentence in sentencesList.EnumerateArray())
                            {
                                if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0)
                                {
                                    var part = sentence[0].GetString();
                                    if (!string.IsNullOrEmpty(part))
                                    {
                                        parts.Add(part);
                                    }
                                }
                            }
                            translated = string.Concat(parts);
                        }
                    }
                    catch
                    {
                        translated = innerRoot[1][0][0][5][0][0].GetString() ?? "";
                    }

                    return new TranslateResult { Translated = translated, Detected = detected };
                }
                catch
                {
                    continue;
                }
            }

            throw new InvalidOperationException("Không parse được kết quả dịch");
        }
    }

    public class TranslateResult
    {
        public string Translated { get; set; } = "";
        public string Detected { get; set; } = "";
        
        public int TotalTokens { get; set; }
        public int CacheHitTokens { get; set; }
        public int CacheMissTokens { get; set; }
    }
}