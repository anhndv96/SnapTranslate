using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SnapTranslate.Services
{
    public class LlmOpenAiService : ITranslateService
    {
        private const string Model = "local-model";
        private const int MaxTokens = 4096;
        private const int ReserveTokens = 800;
        private const string SystemPromptVi = @"Bạn là dịch giả manga chuyên nghiệp.

Nhiệm vụ:
- Dịch mọi ngôn ngữ sang tiếng Việt tự nhiên
- Không thêm giải thích
- Không bỏ sót ý";

        private const string SystemPromptEn = @"You are a professional manga translator.

Task:
- Translate any language into natural English
- No extra explanations
- Do not omit any meaning";

        private class Message
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
        }

        private readonly HttpClient _httpClient;
        private Tiktoken.Encoder? _encoder;
        private readonly LinkedList<Message> _messages;
        private string _lastTarget = "vi";

        public LlmOpenAiService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (httpClient == null)
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(120);
            }

            _messages = new LinkedList<Message>();
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
            _messages.AddLast(new Message { Role = "system", Content = prompt });
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

        private int EstimateMessagesTokens()
        {
            int total = 0;
            foreach (var m in _messages)
            {
                total += CountTokens(m.Role) + CountTokens(m.Content);
            }
            return total;
        }

        private void TrimIfNeeded()
        {
            while (true)
            {
                int tokens = EstimateMessagesTokens();
                if (tokens <= (MaxTokens - ReserveTokens))
                    break;
                
                if (_messages.Count > 2 && _messages.First != null)
                {
                    // Keep system prompt, remove the next two (user and assistant pair)
                    var next = _messages.First.Next;
                    if (next != null)
                    {
                        var nextNext = next.Next;
                        _messages.Remove(next);
                        if (nextNext != null)
                        {
                            _messages.Remove(nextNext);
                        }
                    }
                }
                else
                {
                    break;
                }
            }
        }

        public async Task<TranslateResult> TranslateAsync(string text, string target = "vi")
        {
            if (string.IsNullOrWhiteSpace(text))
                return new TranslateResult { Translated = "", Detected = "unknown" };

            if (target != _lastTarget)
            {
                ResetContext(target);
            }

            // Add user message
            _messages.AddLast(new Message { Role = "user", Content = text });
            TrimIfNeeded();

            var requestBody = new
            {
                model = Model,
                messages = _messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                temperature = 0.2
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var endpointUrl = string.IsNullOrWhiteSpace(SnapTranslate.Models.AppSettings.Current.LocalLlmUrl)
                    ? "http://localhost:1234/v1/chat/completions"
                    : SnapTranslate.Models.AppSettings.Current.LocalLlmUrl;
                using var response = await _httpClient.PostAsync(endpointUrl, jsonContent).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                
                var contentStr = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                
                // Add assistant response to context
                _messages.AddLast(new Message { Role = "assistant", Content = contentStr });

                return new TranslateResult { Translated = contentStr, Detected = LanguageDetector.Detect(text) };
            }
            catch (Exception ex)
            {
                // Remove the user message if failed to translate
                if (_messages.Last != null && _messages.Last.Value.Role == "user")
                    _messages.RemoveLast();

                return new TranslateResult { Translated = $"Lỗi LLM: {ex.Message}", Detected = "error" };
            }
        }
    }
}
