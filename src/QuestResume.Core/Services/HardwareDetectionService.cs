namespace QuestResume.Core.Services;

/// <summary>
/// Informação de hardware detectada (item 6), usada para sugerir um <c>GpuLayerCount</c>
/// conservador ao usuário.
/// </summary>
public sealed class HardwareInfo
{
    /// <summary>RAM física total do sistema, em megabytes. 0 quando não foi possível detectar.</summary>
    public double TotalRamMb { get; set; }

    /// <summary>
    /// RAM disponível estimada, em megabytes. Baseada em <see cref="GC.GetGCMemoryInfo"/>
    /// (<c>TotalAvailableMemoryBytes</c>), que reflete o limite de memória visível ao processo.
    /// </summary>
    public double AvailableRamMb { get; set; }

    /// <summary>
    /// VRAM detectada, em megabytes, ou <c>null</c> quando não detectável de forma confiável.
    /// Ver <see cref="HardwareDetectionService"/> para a limitação (sem NVML/driver específico
    /// não há detecção confiável de VRAM sem dependência nativa pesada).
    /// </summary>
    public double? VramMb { get; set; }

    /// <summary>Número de processadores lógicos.</summary>
    public int ProcessorCount { get; set; }

    /// <summary>
    /// <c>GpuLayerCount</c> sugerido por heurística grosseira baseada apenas na RAM total (NÃO em
    /// medição real de VRAM). Ver <see cref="HardwareDetectionService.SuggestGpuLayerCount"/>.
    /// </summary>
    public int SuggestedGpuLayerCount { get; set; }

    /// <summary>Explicação em PT-BR da sugestão e de suas limitações, para exibir ao usuário.</summary>
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Detecta memória disponível e sugere um valor conservador de <c>GpuLayerCount</c> (item 6).
///
/// LIMITAÇÃO HONESTA: só a RAM é detectada de forma portável (via <see cref="GC.GetGCMemoryInfo"/>).
/// A VRAM da GPU NÃO é detectável de forma confiável sem uma dependência nativa pesada e específica
/// de fornecedor (ex.: NVML da NVIDIA, ADL da AMD) ou consultas WMI frágeis; por isso
/// <see cref="HardwareInfo.VramMb"/> é sempre <c>null</c> aqui. A sugestão de camadas de GPU é,
/// portanto, uma HEURÍSTICA GROSSEIRA baseada na RAM total, não uma medição real de VRAM — serve
/// apenas como ponto de partida que o usuário deve ajustar conforme a GPU real.
/// </summary>
public static class HardwareDetectionService
{
    private const double BytesPerMb = 1024d * 1024d;

    public static HardwareInfo Detect()
    {
        var memInfo = GC.GetGCMemoryInfo();
        var totalRamMb = memInfo.TotalAvailableMemoryBytes / BytesPerMb;

        // TotalAvailableMemoryBytes é o teto de memória visível ao processo; usamos como proxy de
        // RAM total. A memória "disponível" é aproximada subtraindo o working set atual.
        var workingSetMb = Environment.WorkingSet / BytesPerMb;
        var availableRamMb = Math.Max(0, totalRamMb - workingSetMb);

        var suggested = SuggestGpuLayerCount(totalRamMb);

        return new HardwareInfo
        {
            TotalRamMb = Math.Round(totalRamMb, 1),
            AvailableRamMb = Math.Round(availableRamMb, 1),
            VramMb = null,
            ProcessorCount = Environment.ProcessorCount,
            SuggestedGpuLayerCount = suggested,
            Notes =
                "Sugestão baseada apenas na RAM total detectada (heurística grosseira, NÃO uma " +
                "medição real de VRAM). A VRAM da GPU não é detectável de forma confiável sem " +
                "drivers/bibliotecas nativas específicas do fornecedor. Ajuste o valor conforme a " +
                "GPU realmente disponível: comece baixo e aumente enquanto a inferência couber na VRAM."
        };
    }

    /// <summary>
    /// Heurística grosseira que mapeia RAM total (MB) para um <c>GpuLayerCount</c> conservador.
    /// Explicitamente NÃO é uma medição de VRAM — é um ponto de partida seguro.
    /// </summary>
    public static int SuggestGpuLayerCount(double totalRamMb)
    {
        var totalRamGb = totalRamMb / 1024d;
        return totalRamGb switch
        {
            < 8 => 0,     // pouca memória: rode 100% na CPU
            < 16 => 10,
            < 32 => 20,
            < 64 => 35,
            _ => 50,      // muita memória: descarregue a maior parte, ajuste conforme a GPU
        };
    }
}
