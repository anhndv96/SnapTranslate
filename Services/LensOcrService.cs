using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using SnapTranslate.Models;
using ProtoBuf;

namespace SnapTranslate.Services
{
    public class LensOcrService
    {
        private const string ApiUrl = "https://lensfrontend-pa.googleapis.com/v1/crupload";
        private const string ApiKey = "AIzaSyDr2UxVnv_U85AbhhY8XSHSIavUW0DC-sY";
        
        private readonly HttpClient _httpClient;

        public LensOcrService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<OcrResult> PerformOcrAsync(byte[] imageBytes, int width, int height)
        {
            try
            {
                var request = BuildRequest(imageBytes, width, height);

                using var ms = new MemoryStream();
                Serializer.Serialize(ms, request);
                var requestBytes = ms.ToArray();

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                requestMessage.Headers.Add("X-Goog-Api-Key", ApiKey);

                var content = new ByteArrayContent(requestBytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
                requestMessage.Content = content;

                using var response = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var responseBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                using var responseMs = new MemoryStream(responseBytes);
                var serverResponse = Serializer.Deserialize<LensOverlayServerResponse>(responseMs);

                return ParseResponse(serverResponse);
            }
            catch (Exception ex)
            {
                return new OcrResult { Error = $"Lỗi: {ex.Message}" };
            }
        }

        private LensOverlayServerRequest BuildRequest(byte[] imageBytes, int width, int height)
        {
            var random = new Random();

            return new LensOverlayServerRequest
            {
                ObjectsRequest = new LensOverlayObjectsRequest
                {
                    RequestContext = new LensOverlayRequestContext
                    {
                        RequestId = new LensOverlayRequestId
                        {
                            Uuid = (ulong)random.NextInt64(0, long.MaxValue),
                            SequenceId = 1,
                        },
                        ClientContext = new LensOverlayClientContext
                        {
                            Platform = Platform.WEB,
                            Surface = Surface.CHROMIUM,
                            AppId = "",
                            LocaleContext = new LocaleContext
                            {
                                Language = "vi",
                                Region = "VN",
                                TimeZone = "Asia/Bangkok",
                            },
                            RenderingContext = new RenderingContext
                            {
                                RenderingEnvironment = 14, // RENDERING_ENV_LENS_OVERLAY
                            },
                            ClientLoggingData = new ClientLoggingData
                            {
                                IsHistoryEligible = false,
                            },
                        },
                    },
                    ImageData = new ImageData
                    {
                        Payload = new ImagePayload
                        {
                            ImageBytes = imageBytes,
                        },
                        ImageMetadata = new ImageMetadata
                        {
                            Width = width,
                            Height = height,
                        },
                    },
                },
            };
        }

        private OcrResult ParseResponse(LensOverlayServerResponse serverResponse)
        {
            if (serverResponse.Error != null && serverResponse.Error.ErrorType != 0)
            {
                return new OcrResult { Error = "Lỗi từ server Lens" };
            }

            var objectsResponse = serverResponse.ObjectsResponse;
            if (objectsResponse?.Text?.TextLayout == null)
            {
                return new OcrResult { Text = "" };
            }

            var fullText = "";
            foreach (var paragraph in objectsResponse.Text.TextLayout.Paragraphs)
            {
                foreach (var line in paragraph.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        fullText += word.PlainText;
                        if (!string.IsNullOrEmpty(word.TextSeparator))
                            fullText += word.TextSeparator;
                    }
                }
                fullText += "\n";
            }

            return new OcrResult { Text = fullText.Trim() };
        }
    }

    public class OcrResult
    {
        public string? Error { get; set; }
        public string Text { get; set; } = "";
        public bool HasError => !string.IsNullOrEmpty(Error);
    }
}