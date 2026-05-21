using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SnapTranslate.Models;

namespace SnapTranslate.Services
{
    public class DeepSeekTranslateService : ITranslateService
    {
        private const string ApiUrl = "https://api.deepseek.com/chat/completions";
        private const string Model = "deepseek-v4-flash";
        private const int MaxTokens = 10000;
        
        private const string SystemPromptVi = @"Bạn là dịch giả manga chuyên nghiệp. Lịch sử trò chuyện bên dưới chỉ dùng làm ngữ cảnh tham khảo.

Nhiệm vụ bắt buộc:
- CHỈ DỊCH đoạn văn bản được yêu cầu cuối cùng sang tiếng Việt.
- Trả lời TRỰC TIẾP bằng bản dịch, không giải thích, không lặp lại, không tự ý viết tiếp câu chuyện.";

        private const string SystemPromptEn = @"You are a professional manga translator. The chat history below is only for context reference.

Mandatory tasks:
- Translate ONLY the last requested text into English.
- Answer DIRECTLY with the translation, no explanation, no repetition, do not continue the story.";

        private class Message
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }

        private readonly HttpClient _httpClient;
        private Tiktoken.Encoder? _encoder;
        private readonly List<Message> _messages;
        private DateTime _lastRequestTime;
        private string _lastTarget = "vi";

        public DeepSeekTranslateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (httpClient == null)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(120);
            }

            _messages = new List<Message>();
            ResetContext("vi");
        }

        private Tiktoken.Encoder? GetEncoder()
        {
            if (_encoder == null)
            {
                try
                {
                    _encoder = Tiktoken.ModelToEncoder.For("gpt-3.5-turbo");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load Tiktoken encoder lazily: {ex.Message}");
                }
            }
            return _encoder;
        }

        private void ResetContext(string target)
        {
            _messages.Clear();
            string prompt = target == "en" ? SystemPromptEn : SystemPromptVi;
            _messages.Add(new Message { Role = "system", Content = prompt });
            _lastTarget = target;
        }

        private int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            try
            {
                var encoder = GetEncoder();
                if (encoder != null)
                {
                    return encoder.CountTokens(text);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error using Tiktoken encoder: {ex.Message}");
            }
            // Fallback: estimate ~4 characters per token
            return text.Length / 4 + 1;
        }

        private int EstimateTotalTokens()
        {
            int total = 0;
            foreach (var m in _messages)
            {
                total += CountTokens(m.Role) + CountTokens(m.Content);
            }
            return total;
        }

        public Task<TranslateResult> TranslateAsync(string text, string target = "vi", Action<string>? onChunkReceived = null)
        {
            return Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return new TranslateResult { Translated = "", Detected = "unknown" };

                string apiKey = AppSettings.Current.DeepSeekApiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    return new TranslateResult { Translated = "Lỗi: Chưa cấu hình DeepSeek API Key. Hãy mở cài đặt (hình bánh răng).", Detected = "error" };
                }

                // Flush context if target language changed or inactive for 1 hour
                if (target != _lastTarget || (_lastRequestTime > DateTime.MinValue && (DateTime.Now - _lastRequestTime).TotalHours >= 1))
                {
                    ResetContext(target);
                }

                var userMsg = new Message { Role = "user", Content = $"Dịch đoạn sau:\n{text}" };
                
                // Rebuild context completely if token limit is exceeded
                int projectedTokens = EstimateTotalTokens() + CountTokens(userMsg.Role) + CountTokens(userMsg.Content) + 1000;
                if (projectedTokens > MaxTokens)
                {
                    ResetContext(target);
                }

                _messages.Add(userMsg);

                object requestBody;
                if (onChunkReceived != null)
                {
                    requestBody = new
                    {
                        model = Model,
                        messages = _messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                        temperature = 0.2,
                        stream = true,
                        stream_options = new { include_usage = true }
                    };
                }
                else
                {
                    requestBody = new
                    {
                        model = Model,
                        messages = _messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                        temperature = 0.2
                    };
                }

                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                if (onChunkReceived != null)
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                    try
                    {
                        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            var errStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            
                            // If Bad Request, try falling back immediately to streaming without stream_options
                            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || errStr.Contains("stream_options") || errStr.Contains("extra inputs"))
                            {
                                response.Dispose();
                                return await TranslateStreamWithoutOptionsAsync(text, target, onChunkReceived, apiKey).ConfigureAwait(false);
                            }

                            _messages.RemoveAt(_messages.Count - 1);
                            return new TranslateResult { Translated = $"Lỗi DeepSeek API: {response.StatusCode} - {errStr}", Detected = "error" };
                        }

                        var contentBuilder = new StringBuilder();
                        int totalTokens = 0, hitTokens = 0, missTokens = 0;

                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            while (!reader.EndOfStream)
                            {
                                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                if (line.StartsWith("data:"))
                                {
                                    var data = line.Substring("data:".Length).Trim();
                                    if (data == "[DONE]") break;

                                    // THÊM DÒNG NÀY: Bỏ qua ngay các chuỗi rỗng hoặc không phải JSON để chống văng Exception
                                    if (string.IsNullOrEmpty(data) || !data.StartsWith("{")) continue;

                                    try
                                    {
                                        using var doc = JsonDocument.Parse(data);
                                        var root = doc.RootElement;

                                        // 1. Lấy Nội Dung (Xử lý an toàn tuyệt đối với null)
                                        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                                        {
                                            var choice = choices[0];
                                            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                                            {
                                                // Chỉ đọc content nếu nó TỒN TẠI và LÀ CHUỖI (Bỏ qua chunk có content = null)
                                                if (delta.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                                                {
                                                    var content = contentProp.GetString();
                                                    if (!string.IsNullOrEmpty(content))
                                                    {
                                                        contentBuilder.Append(content);
                                                        onChunkReceived?.Invoke(content);
                                                    }
                                                }
                                            }
                                        }

                                        // 2. Lấy Thông Tin Token (Xử lý an toàn với usage = null)
                                        if (root.TryGetProperty("usage", out var usageProp) && usageProp.ValueKind == JsonValueKind.Object)
                                        {
                                            if (usageProp.TryGetProperty("total_tokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
                                                totalTokens = tt.GetInt32();
                                            if (usageProp.TryGetProperty("prompt_cache_hit_tokens", out var ht) && ht.ValueKind == JsonValueKind.Number)
                                                hitTokens = ht.GetInt32();
                                            if (usageProp.TryGetProperty("prompt_cache_miss_tokens", out var mt) && mt.ValueKind == JsonValueKind.Number)
                                                missTokens = mt.GetInt32();
                                        }
                                    }
                                    catch
                                    {
                                        // Ignore JSON parse errors on invalid/incomplete stream chunks
                                    }
                                }
                            }
                        }

                        var contentStr = contentBuilder.ToString();
                        _messages.Add(new Message { Role = "assistant", Content = contentStr });
                        _lastRequestTime = DateTime.Now;

                        return new TranslateResult
                        {
                            Translated = contentStr,
                            Detected = LanguageDetector.Detect(text),
                            TotalTokens = totalTokens,
                            CacheHitTokens = hitTokens,
                            CacheMissTokens = missTokens
                        };
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            return await TranslateStreamWithoutOptionsAsync(text, target, onChunkReceived, apiKey).ConfigureAwait(false);
                        }
                        catch
                        {
                            if (_messages.LastOrDefault()?.Role == "user")
                                _messages.RemoveAt(_messages.Count - 1);

                            return new TranslateResult { Translated = $"Lỗi DeepSeek: {ex.Message}", Detected = "error" };
                        }
                    }
                }
                else
                {
                    try
                    {
                        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            var errStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            _messages.RemoveAt(_messages.Count - 1);
                            return new TranslateResult { Translated = $"Lỗi DeepSeek API: {response.StatusCode} - {errStr}", Detected = "error" };
                        }

                        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(responseString);
                        var root = doc.RootElement;
                        
                        var contentStr = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                        
                        int totalTokens = 0, hitTokens = 0, missTokens = 0;
                        if (root.TryGetProperty("usage", out var usageProp))
                        {
                            if (usageProp.TryGetProperty("total_tokens", out var tt)) totalTokens = tt.GetInt32();
                            if (usageProp.TryGetProperty("prompt_cache_hit_tokens", out var ht)) hitTokens = ht.GetInt32();
                            if (usageProp.TryGetProperty("prompt_cache_miss_tokens", out var mt)) missTokens = mt.GetInt32();
                        }
                        
                        _messages.Add(new Message { Role = "assistant", Content = contentStr });
                        _lastRequestTime = DateTime.Now;

                        return new TranslateResult { 
                            Translated = contentStr, 
                            Detected = LanguageDetector.Detect(text),
                            TotalTokens = totalTokens,
                            CacheHitTokens = hitTokens,
                            CacheMissTokens = missTokens
                        };
                    }
                    catch (Exception ex)
                    {
                        if (_messages.LastOrDefault()?.Role == "user")
                            _messages.RemoveAt(_messages.Count - 1);

                        return new TranslateResult { Translated = $"Lỗi DeepSeek: {ex.Message}", Detected = "error" };
                    }
                }
            });
        }

        private async Task<TranslateResult> TranslateStreamWithoutOptionsAsync(string text, string target, Action<string> onChunkReceived, string apiKey)
        {
            var requestBody = new
            {
                model = Model,
                messages = _messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                temperature = 0.2,
                stream = true
            };

            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            try
            {
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (_messages.LastOrDefault()?.Role == "user")
                        _messages.RemoveAt(_messages.Count - 1);
                    return new TranslateResult { Translated = $"Lỗi DeepSeek API: {response.StatusCode} - {errStr}", Detected = "error" };
                }

                var contentBuilder = new StringBuilder();

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new System.IO.StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (line.StartsWith("data:"))
                        {
                            var data = line.Substring("data:".Length).Trim();
                            if (data == "[DONE]") break;

                            // THÊM DÒNG NÀY: Bỏ qua ngay các chuỗi rỗng hoặc không phải JSON để chống văng Exception
                            if (string.IsNullOrEmpty(data) || !data.StartsWith("{")) continue;

                            try
                            {
                                using var doc = JsonDocument.Parse(data);
                                var root = doc.RootElement;

                                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                                {
                                    var choice = choices[0];
                                    if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentProp))
                                    {
                                        var content = contentProp.GetString();
                                        if (!string.IsNullOrEmpty(content))
                                        {
                                            contentBuilder.Append(content);
                                            onChunkReceived(content);
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore JSON parse errors on invalid/incomplete stream chunks
                            }
                        }
                    }
                }

                var contentStr = contentBuilder.ToString();
                _messages.Add(new Message { Role = "assistant", Content = contentStr });
                _lastRequestTime = DateTime.Now;

                return new TranslateResult
                {
                    Translated = contentStr,
                    Detected = LanguageDetector.Detect(text),
                    TotalTokens = 0,
                    CacheHitTokens = 0,
                    CacheMissTokens = 0
                };
            }
            catch (Exception ex)
            {
                if (_messages.LastOrDefault()?.Role == "user")
                    _messages.RemoveAt(_messages.Count - 1);

                return new TranslateResult { Translated = $"Lỗi DeepSeek: {ex.Message}", Detected = "error" };
            }
        }
    }
}
