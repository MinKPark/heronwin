using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using Xunit;

namespace HeronWin.HerFace.Tests;

public sealed class ImageOptimizationTests
{
    [Fact]
    public void OptimizeToolImageForVision_DownsizesOversizedRasterImage()
    {
        var original = new ToolImage("image/png", Convert.ToBase64String(CreatePngBytes(1920, 1080)));

        var optimized = McpClientManager.OptimizeToolImageForVision(original);

        Assert.Equal("image/jpeg", optimized.MimeType);
        Assert.NotEqual(original.Base64Data, optimized.Base64Data);

        using var stream = new MemoryStream(Convert.FromBase64String(optimized.Base64Data));
        using var image = new Bitmap(stream);
        Assert.True(Math.Max(image.Width, image.Height) <= 1280);
    }

    [Fact]
    public void OptimizeToolImageForVision_KeepsSmallImageAsIs()
    {
        var original = new ToolImage("image/png", Convert.ToBase64String(CreatePngBytes(512, 512)));

        var optimized = McpClientManager.OptimizeToolImageForVision(original);

        Assert.Equal(original, optimized);
    }

    [Fact]
    public void ToOpenAiMessages_UsesLowDetailForVisualImages()
    {
        var messages = OpenAiApiClient.ToOpenAiMessages(
            [
                new AgentMessage.VisualContext(
                    "Screenshot evidence",
                    [new ToolImage("image/png", "AA==")])
            ],
            systemPrompt: string.Empty);

        var content = messages[0]!.AsObject()["content"]!.AsArray();
        var imageItem = content[1]!.AsObject();
        var imageUrl = imageItem["image_url"]!.AsObject();

        Assert.Equal("low", imageUrl["detail"]!.GetValue<string>());
    }

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            graphics.FillRectangle(Brushes.Black, 0, 0, width / 2, height / 2);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
