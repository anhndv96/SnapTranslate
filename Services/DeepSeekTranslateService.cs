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

        public async Task<TranslateResult> TranslateAsync(string text, string target = "vi")
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

            var requestBody = new
            {
                model = Model,
                messages = _messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                temperature = 0.2
            };

            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

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
    }
}
