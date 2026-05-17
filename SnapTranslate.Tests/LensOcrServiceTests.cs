using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SnapTranslate.Services;
using Moq;
using Moq.Protected;

namespace SnapTranslate.Tests
{
    [TestClass]
    public class LensOcrServiceTests
    {
        [TestMethod]
        public async Task PerformOcrAsync_HttpError_ReturnsErrorResult()
        {
            // Arrange
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new LensOcrService(httpClient);

            // Act
            var result = await service.PerformOcrAsync(new byte[] { 1, 2, 3 }, 100, 100);

            // Assert
            Assert.IsTrue(result.HasError);
            Assert.IsNotNull(result.Error);
            Assert.IsTrue(result.Error.Contains("Lỗi"));
        }
    }
}
