using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SnapTranslate.Services;
using Moq;
using Moq.Protected;
using SnapTranslate.Models;

namespace SnapTranslate.Tests
{
    [TestClass]
    public class TranslateServicesTests
    {
        [TestMethod]
        public async Task LlmOpenAiService_TranslateAsync_Success()
        {
            // Arrange
            var responseJson = @"{
                ""choices"": [
                    {
                        ""message"": {
                            ""content"": ""Bản dịch test""
                        }
                    }
                ]
            }";

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(responseJson)
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new LlmOpenAiService(httpClient);

            // Act
            var result = await service.TranslateAsync("Test text", "vi");

            // Assert
            Assert.AreEqual("Bản dịch test", result.Translated);
            Assert.AreEqual("en", result.Detected); // "Test text" is english
        }

        [TestMethod]
        public async Task DeepSeekTranslateService_TranslateAsync_NoApiKey_ReturnsError()
        {
            // Arrange
            AppSettings.Current.DeepSeekApiKey = ""; // Clear API key
            var service = new DeepSeekTranslateService(new HttpClient());

            // Act
            var result = await service.TranslateAsync("Test text", "vi");

            // Assert
            Assert.IsTrue(result.Translated.Contains("Lỗi"));
        }

        [TestMethod]
        public async Task GoogleTranslateService_TranslateAsync_EmptyText_ReturnsEmpty()
        {
            // Arrange
            var service = new GoogleTranslateService(new HttpClient());

            // Act
            var result = await service.TranslateAsync("   ", "vi");

            // Assert
            Assert.AreEqual("", result.Translated);
            Assert.AreEqual("unknown", result.Detected);
        }
    }
}
