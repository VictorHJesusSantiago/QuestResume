namespace QuestResume.Core.Embeddings;

public static class VectorQuantizer
{
        private static readonly byte[] Magic = "QRQ8"u8.ToArray();

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

        public static float[] Dequantize(sbyte[] quantized, float scale)
    {
        var result = new float[quantized.Length];
        for (var i = 0; i < quantized.Length; i++)
        {
            result[i] = quantized[i] * scale;
        }

        return result;
    }

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

        public static float[] FromQuantizedBytes(byte[] blob)
    {
        var scale = BitConverter.ToSingle(blob, Magic.Length);
        var count = blob.Length - Magic.Length - sizeof(float);
        var quantized = new sbyte[count];
        Buffer.BlockCopy(blob, Magic.Length + sizeof(float), quantized, 0, count);
        return Dequantize(quantized, scale);
    }
}
