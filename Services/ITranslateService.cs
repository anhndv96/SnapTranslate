using System;
using System.Threading.Tasks;

namespace SnapTranslate.Services
{
    public interface ITranslateService
    {
        Task<TranslateResult> TranslateAsync(string text, string target = "vi", Action<string>? onChunkReceived = null);
    }
}
