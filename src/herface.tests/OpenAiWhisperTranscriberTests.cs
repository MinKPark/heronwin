using System.Net;
using System.Net.Http;
using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class OpenAiWhisperTranscriberTests
{
    [Fact]
    public async Task TranscribeAudioAsync_IncludesPromptAndSingleLanguageHint_WhenOnlyOneLanguageIsConfigured()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"text":"hello"}""")
            };
        });
        using var client = new HttpClient(handler);
        var transcriber = new OpenAiWhisperTranscriber(
            client,
            "test-key",
            "whisper-1",
            VoiceLanguagePreferences.Parse("Korean"));
        var recordingPath = CreateTempRecording();

        try
        {
            var actual = await transcriber.TranscribeAudioAsync(recordingPath, CancellationToken.None);

            Assert.Equal("hello", actual);
            Assert.NotNull(requestBody);
            Assert.Contains("name=prompt", requestBody, StringComparison.Ordinal);
            Assert.Contains("Korean", requestBody, StringComparison.Ordinal);
            Assert.Contains("name=language", requestBody, StringComparison.Ordinal);
            Assert.Contains("\r\nko\r\n", requestBody, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(recordingPath);
        }
    }

    [Fact]
    public async Task TranscribeAudioAsync_IncludesPromptButLeavesLanguageAutoDetect_WhenMultipleLanguagesAreConfigured()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"text":"hello"}""")
            };
        });
        using var client = new HttpClient(handler);
        var transcriber = new OpenAiWhisperTranscriber(
            client,
            "test-key",
            "whisper-1",
            VoiceLanguagePreferences.Parse("American English, Korean"));
        var recordingPath = CreateTempRecording();

        try
        {
            await transcriber.TranscribeAudioAsync(recordingPath, CancellationToken.None);

            Assert.NotNull(requestBody);
            Assert.Contains("name=prompt", requestBody, StringComparison.Ordinal);
            Assert.Contains("American English", requestBody, StringComparison.Ordinal);
            Assert.Contains("Korean", requestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("name=language", requestBody, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(recordingPath);
        }
    }

    private static string CreateTempRecording()
    {
        var path = Path.Combine(Path.GetTempPath(), $"herface-transcriber-test-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00]);
        return path;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }
}
