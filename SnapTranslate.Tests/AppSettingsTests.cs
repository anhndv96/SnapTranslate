using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SnapTranslate.Models;

namespace SnapTranslate.Tests
{
    [TestClass]
    public class AppSettingsTests
    {
        [TestMethod]
        public void AppSettingsData_DefaultValues_ShouldBeCorrect()
        {
            // Arrange
            var data = new AppSettingsData();

            // Assert
            Assert.AreEqual("", data.DeepSeekApiKey);
            Assert.AreEqual(500, data.WindowWidth);
            Assert.AreEqual(0, data.WindowHeight);
            Assert.AreEqual(0, data.SelectedEngineIndex);
            Assert.AreEqual("http://localhost:1234/v1/chat/completions", data.LocalLlmUrl);
            Assert.IsTrue(data.StartWithWindows);
        }

        [TestMethod]
        public void AppSettingsData_Serialization_ShouldRetainValues()
        {
            // Arrange
            var original = new AppSettingsData
            {
                DeepSeekApiKey = "test-key",
                WindowWidth = 800,
                WindowHeight = 600,
                SelectedEngineIndex = 2,
                LocalLlmUrl = "http://test:1111",
                StartWithWindows = false
            };

            // Act
            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<AppSettingsData>(json);

            // Assert
            Assert.IsNotNull(deserialized);
            Assert.AreEqual("test-key", deserialized.DeepSeekApiKey);
            Assert.AreEqual(800, deserialized.WindowWidth);
            Assert.AreEqual(600, deserialized.WindowHeight);
            Assert.AreEqual(2, deserialized.SelectedEngineIndex);
            Assert.AreEqual("http://test:1111", deserialized.LocalLlmUrl);
            Assert.IsFalse(deserialized.StartWithWindows);
        }
    }
}
