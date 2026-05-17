using System;

namespace SnapTranslate.Services
{
    public static class LanguageDetector
    {
        public static string Detect(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "unknown";

            bool hasHiraganaKatakana = false;
            bool hasHangul = false;
            bool hasCjk = false;
            bool hasCyrillic = false;
            bool hasThai = false;
            bool hasArabic = false;
            bool hasHindi = false;
            bool hasVietnameseDiacritics = false;
            
            // Unique character markers for Latin-based languages
            bool hasGermanBeta = false;
            bool hasSpanishMarker = false;
            bool hasTurkishMarker = false;
            bool hasPortugueseMarker = false;

            foreach (char c in text)
            {
                int val = c;

                // Japanese Hiragana & Katakana
                if ((val >= 0x3040 && val <= 0x309F) || (val >= 0x30A0 && val <= 0x30FF))
                {
                    hasHiraganaKatakana = true;
                    break; // Hiragana/Katakana is uniquely Japanese
                }
                // Korean Hangul
                else if (val >= 0xAC00 && val <= 0xD7A3)
                {
                    hasHangul = true;
                    break; // Hangul is uniquely Korean
                }
                // CJK Unified Ideographs (Chinese)
                if (val >= 0x4E00 && val <= 0x9FFF)
                {
                    hasCjk = true;
                }
                // Cyrillic (Russian, Bulgarian, Ukrainian, etc.)
                if (val >= 0x0400 && val <= 0x04FF)
                {
                    hasCyrillic = true;
                }
                // Thai
                if (val >= 0x0E00 && val <= 0x0E7F)
                {
                    hasThai = true;
                }
                // Arabic
                if (val >= 0x0600 && val <= 0x06FF)
                {
                    hasArabic = true;
                }
                // Hindi (Devanagari)
                if (val >= 0x0900 && val <= 0x097F)
                {
                    hasHindi = true;
                }
                // Vietnamese specific characters (both uppercase and lowercase)
                if ("đĐàáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵ".Contains(c))
                {
                    hasVietnameseDiacritics = true;
                }
                // German unique marker
                if (c == 'ß' || c == 'ẞ')
                {
                    hasGermanBeta = true;
                }
                // Spanish unique markers
                if (c == 'ñ' || c == 'Ñ' || c == '¿' || c == '¡')
                {
                    hasSpanishMarker = true;
                }
                // Turkish unique markers
                if (c == 'ğ' || c == 'Ğ' || c == 'ı' || c == 'İ' || c == 'ş' || c == 'Ş')
                {
                    hasTurkishMarker = true;
                }
                // Portuguese unique markers
                if (c == 'ã' || c == 'õ' || c == 'Ã' || c == 'Õ')
                {
                    hasPortugueseMarker = true;
                }
            }

            if (hasHiraganaKatakana) return "ja";
            if (hasHangul) return "ko";
            if (hasCjk) return "zh-CN";
            if (hasCyrillic) return "ru";
            if (hasThai) return "th";
            if (hasArabic) return "ar";
            if (hasHindi) return "hi";
            
            // Unique Latin sub-language detection (checked before broad Vietnamese diacritics)
            if (hasGermanBeta) return "de";
            if (hasSpanishMarker) return "es";
            if (hasTurkishMarker) return "tr";
            if (hasPortugueseMarker) return "pt";

            if (hasVietnameseDiacritics) return "vi";

            // Default to English if it's standard ASCII / Latin
            return "en";
        }
    }
}
