using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Carrega e pré-processa uma imagem para entrada de um modelo CLIP ONNX: redimensiona para
/// <c>size x size</c> e normaliza os canais RGB com média/desvio-padrão do ImageNet, no layout
/// NCHW (channels-first) esperado pela maioria dos exports padrão do CLIP.
/// </summary>
internal static class ImagePreprocessor
{
    private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
    private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

    public static float[] LoadAndPreprocess(string imagePath, int size)
    {
        using var image = Image.Load<Rgb24>(imagePath);
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(size, size),
            Mode = ResizeMode.Stretch
        }));

        var data = new float[3 * size * size];
        var planeSize = size * size;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var pixel = image[x, y];
                var idx = y * size + x;
                data[idx] = (pixel.R / 255f - Mean[0]) / Std[0];
                data[planeSize + idx] = (pixel.G / 255f - Mean[1]) / Std[1];
                data[2 * planeSize + idx] = (pixel.B / 255f - Mean[2]) / Std[2];
            }
        }

        return data;
    }
}
