namespace QuestResume.Core.Embeddings;

/// <summary>
/// Quantização simétrica de embeddings float32 para int8 (sbyte) com um fator de escala por
/// vetor. Reduz o armazenamento a ~25% do float32 (1 byte por dimensão + 4 bytes de escala, vs.
/// 4 bytes por dimensão).
///
/// Trade-off: a quantização introduz um erro de arredondamento por dimensão (no máximo metade de
/// um passo de quantização = escala/2), o que causa uma pequena perda de precisão na similaridade
/// de cosseno. Na prática, para embeddings de recuperação típicos (256–1024 dimensões
/// normalizadas), a diferença de ranking é pequena, mas resultados no limite podem trocar de
/// ordem. Use quando a economia de memória/disco compensa a leve perda de acurácia.
/// </summary>
public static class VectorQuantizer
{
    /// <summary>Blob quantizado: <c>"QRQ8"</c> (4 bytes mágicos) || escala (float, 4 bytes) || sbyte[] (1 por dimensão).</summary>
    private static readonly byte[] Magic = "QRQ8"u8.ToArray();

    /// <summary>
    /// Quantiza <paramref name="vector"/> para int8. A escala é <c>max(|vi|)/127</c>; cada
    /// componente vira <c>round(vi/escala)</c> saturado em [-127, 127].
    /// </summary>
    public static (sbyte[] Quantized, float Scale) Quantize(float[] vector)
    {
        var max = 0f;
        foreach (var v in vector)
        {
            var abs = MathF.Abs(v);
            if (abs > max)
            {
                max = abs;
            }
        }

        var scale = max <= 0f ? 1f : max / 127f;
        var quantized = new sbyte[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            var q = MathF.Round(vector[i] / scale);
            quantized[i] = (sbyte)Math.Clamp((int)q, -127, 127);
        }

        return (quantized, scale);
    }

    /// <summary>Reconstrói um float32 aproximado a partir do int8 e da escala.</summary>
    public static float[] Dequantize(sbyte[] quantized, float scale)
    {
        var result = new float[quantized.Length];
        for (var i = 0; i < quantized.Length; i++)
        {
            result[i] = quantized[i] * scale;
        }

        return result;
    }

    /// <summary>Serializa um vetor float32 no formato quantizado int8 (com cabeçalho mágico e escala).</summary>
    public static byte[] ToQuantizedBytes(float[] vector)
    {
        var (quantized, scale) = Quantize(vector);
        var bytes = new byte[Magic.Length + sizeof(float) + quantized.Length];
        Buffer.BlockCopy(Magic, 0, bytes, 0, Magic.Length);
        var scaleBytes = BitConverter.GetBytes(scale);
        Buffer.BlockCopy(scaleBytes, 0, bytes, Magic.Length, sizeof(float));
        Buffer.BlockCopy((Array)quantized, 0, bytes, Magic.Length + sizeof(float), quantized.Length);
        return bytes;
    }

    /// <summary>True se <paramref name="blob"/> tem o cabeçalho mágico de um vetor quantizado.</summary>
    public static bool IsQuantized(byte[] blob)
    {
        if (blob.Length < Magic.Length)
        {
            return false;
        }

        for (var i = 0; i < Magic.Length; i++)
        {
            if (blob[i] != Magic[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Desserializa um blob quantizado de volta para float32 aproximado. Assume <see cref="IsQuantized"/> = true.</summary>
    public static float[] FromQuantizedBytes(byte[] blob)
    {
        var scale = BitConverter.ToSingle(blob, Magic.Length);
        var count = blob.Length - Magic.Length - sizeof(float);
        var quantized = new sbyte[count];
        Buffer.BlockCopy(blob, Magic.Length + sizeof(float), quantized, 0, count);
        return Dequantize(quantized, scale);
    }
}
