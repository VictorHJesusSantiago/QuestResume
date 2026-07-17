using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Histórico de versões anteriores de documentos. Quando um documento é reindexado com conteúdo
/// diferente do hash anterior, o chamador (indexador) chama <see cref="SaveVersion"/> para guardar
/// o texto antigo antes de sobrescrever. Persistido num sidecar JSON
/// <c>document-versions.json</c>. Limita a <paramref name="maxVersionsPerDocument"/> versões por
/// documento (as mais antigas são descartadas).
/// </summary>
public sealed class DocumentVersionStore
{
    public const string FileName = "document-versions.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly int _maxVersionsPerDocument;
    private Dictionary<string, List<DocumentVersion>> _versions;

    public DocumentVersionStore(string indexPath, int maxVersionsPerDocument = 5)
    {
        _filePath = Path.Combine(indexPath, FileName);
        _maxVersionsPerDocument = Math.Max(1, maxVersionsPerDocument);
        _versions = Load();
    }

    private Dictionary<string, List<DocumentVersion>> Load()
    {
        if (!File.Exists(_filePath)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<DocumentVersion>>>(
                       File.ReadAllText(_filePath), SerializerOptions)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public void Save() => File.WriteAllText(_filePath, JsonSerializer.Serialize(_versions, SerializerOptions));

    public static string ComputeHash(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Guarda uma versão do texto de um documento. Ignora (retorna <c>false</c>) se o hash do texto
    /// coincidir com a versão mais recente já guardada (nada mudou). Aplica a retenção
    /// (<see cref="_maxVersionsPerDocument"/>), descartando as mais antigas.
    /// </summary>
    public bool SaveVersion(string sourcePath, string fullText)
    {
        var hash = ComputeHash(fullText);
        if (!_versions.TryGetValue(sourcePath, out var list))
        {
            list = new List<DocumentVersion>();
            _versions[sourcePath] = list;
        }

        if (list.Count > 0 && list[^1].ContentHash == hash)
            return false;

        list.Add(new DocumentVersion
        {
            SourcePath = sourcePath,
            ContentHash = hash,
            FullTextSnapshot = fullText,
            SnapshotUtc = DateTime.UtcNow
        });

        // Retenção: mantém só as N mais recentes e renumera 1..N.
        if (list.Count > _maxVersionsPerDocument)
            list.RemoveRange(0, list.Count - _maxVersionsPerDocument);
        for (var i = 0; i < list.Count; i++)
            list[i].VersionNumber = i + 1;

        Save();
        return true;
    }

    public IReadOnlyList<DocumentVersion> GetVersions(string sourcePath) =>
        _versions.TryGetValue(sourcePath, out var list) ? list.ToList() : Array.Empty<DocumentVersion>();

    /// <summary>
    /// Diff simples linha a linha entre a versão <paramref name="versionNumber"/> e o
    /// <paramref name="currentText"/> atual. Cada linha resultante é prefixada com <c>'+'</c>
    /// (presente só no atual), <c>'-'</c> (presente só na versão antiga) ou <c>' '</c> (igual).
    /// </summary>
    public IReadOnlyList<string> DiffAgainstCurrent(string sourcePath, int versionNumber, string currentText)
    {
        var versions = GetVersions(sourcePath);
        var version = versions.FirstOrDefault(v => v.VersionNumber == versionNumber)
                      ?? throw new KeyNotFoundException($"Versão {versionNumber} não encontrada para '{sourcePath}'.");

        return DiffLines(version.FullTextSnapshot, currentText);
    }

    /// <summary>Diff LCS simples linha a linha entre dois textos.</summary>
    public static IReadOnlyList<string> DiffLines(string oldText, string newText)
    {
        var a = oldText.Replace("\r\n", "\n").Split('\n');
        var b = newText.Replace("\r\n", "\n").Split('\n');
        var n = a.Length;
        var m = b.Length;

        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<string>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { result.Add("  " + a[x]); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { result.Add("- " + a[x]); x++; }
            else { result.Add("+ " + b[y]); y++; }
        }
        while (x < n) { result.Add("- " + a[x]); x++; }
        while (y < m) { result.Add("+ " + b[y]); y++; }
        return result;
    }
}
