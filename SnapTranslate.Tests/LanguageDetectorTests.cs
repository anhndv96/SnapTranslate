using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SnapTranslate.Services;

namespace SnapTranslate.Tests
{
    [TestClass]
    public class LanguageDetectorTests
    {
        [TestMethod]
        [DataRow("This is English text.", "en")]
        [DataRow("こんにちは世界", "ja")]
        [DataRow("안녕하세요", "ko")]
        [DataRow("你好，世界", "zh-CN")]
        [DataRow("Привет, мир", "ru")]
        [DataRow("สวัสดีชาวโลก", "th")]
        [DataRow("مرحبا بالعالم", "ar")]
        [DataRow("नमस्ते दुनिया", "hi")]
        [DataRow("Xin chào thế giới", "vi")]
        [DataRow("Äpfel und Straße", "de")]
        [DataRow("¿Cómo estás? ¡Bien!", "es")]
        [DataRow("Görüşürüz", "tr")]
        [DataRow("Corações e ações", "pt")]
        public void Detect_ShouldReturnCorrectLanguageCode(string text, string expectedCode)
        {
            // Act
            string actualCode = LanguageDetector.Detect(text);

            // Assert
            Assert.AreEqual(expectedCode, actualCode, $"Failed for text: {text}");
        }

        [TestMethod]
        public void Detect_NullOrWhiteSpace_ShouldReturnUnknown()
        {
            Assert.AreEqual("unknown", LanguageDetector.Detect(""));
            Assert.AreEqual("unknown", LanguageDetector.Detect("   "));
            Assert.AreEqual("unknown", LanguageDetector.Detect(null!));
        }
    }
}
