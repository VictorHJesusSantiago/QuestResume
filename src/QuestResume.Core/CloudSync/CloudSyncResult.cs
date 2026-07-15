namespace QuestResume.Core.CloudSync;

/// <summary>Resultado de uma sincronização de pasta remota para local via <see cref="CloudSyncService"/>.</summary>
public sealed class CloudSyncResult
{
    /// <summary>Pasta local (subpasta <c>_cloud_&lt;provider&gt;</c>) onde os arquivos foram salvos.</summary>
    public string LocalFolder { get; set; } = string.Empty;

    /// <summary>Quantidade de arquivos baixados com sucesso.</summary>
    public int FilesDownloaded { get; set; }

    /// <summary>Quantidade de itens ignorados por serem subpastas (sincronização não-recursiva).</summary>
    public int FoldersSkipped { get; set; }

    /// <summary>Erros (nome do arquivo -> mensagem) ocorridos durante o download; não interrompem os demais arquivos.</summary>
    public List<string> Errors { get; set; } = new();
}
