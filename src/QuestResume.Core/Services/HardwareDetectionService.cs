namespace QuestResume.Core.Services;

public sealed class HardwareInfo
{
        public double TotalRamMb { get; set; }

        public double AvailableRamMb { get; set; }

        public double? VramMb { get; set; }

        public int ProcessorCount { get; set; }

        public int SuggestedGpuLayerCount { get; set; }

        public string Notes { get; set; } = string.Empty;
}

public static class HardwareDetectionService
{
    private const double BytesPerMb = 1024d * 1024d;

    public static HardwareInfo Detect()
    {
        var memInfo = GC.GetGCMemoryInfo();
        var totalRamMb = memInfo.TotalAvailableMemoryBytes / BytesPerMb;

        
        
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

        public static int SuggestGpuLayerCount(double totalRamMb)
    {
        var totalRamGb = totalRamMb / 1024d;
        return totalRamGb switch
        {
            < 8 => 0,     
            < 16 => 10,
            < 32 => 20,
            < 64 => 35,
            _ => 50,      
        };
    }
}
