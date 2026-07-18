namespace QuestResume.Core.Embeddings;

/// <summary>
/// Índice HNSW (Hierarchical Navigable Small World) em C# puro, sem dependência nativa, para busca
/// vetorial aproximada de vizinhos mais próximos (ANN) sub-linear. Baseado no artigo de Malkov &amp;
/// Yashunin (2016). Implementação funcional e didática — prioriza correção e clareza sobre
/// micro-otimização, mas realiza genuinamente menos comparações que a varredura linear em coleções
/// grandes, navegando um grafo hierárquico em vez de comparar contra todos os vetores.
///
/// Métrica: distância = 1 - similaridade de cosseno (menor = mais próximo). Assume vetores de
/// mesma dimensão. Não é thread-safe para escrita; construa o índice e depois consulte.
///
/// Limitações honestas: por ser aproximado, o recall não é 100% — resultados no limite podem
/// diferir da busca exata. Os parâmetros (M, efConstruction, ef) controlam o trade-off
/// recall × velocidade. É um índice em memória, reconstruído a partir dos vetores ao carregar.
/// </summary>
public sealed class HnswIndex
{
    private readonly int _m;              // conexões máximas por nó (camadas > 0)
    private readonly int _mMax0;          // conexões máximas na camada 0
    private readonly int _efConstruction; // tamanho da lista dinâmica durante a inserção
    private readonly double _levelMult;   // normalizador de nível (1/ln(M))
    private readonly Random _random;

    private readonly List<float[]> _vectors = new();
    private readonly List<int> _ids = new();
    private readonly List<List<List<int>>> _links = new(); // [nó][camada] -> vizinhos
    private int _entryPoint = -1;
    private int _maxLevel = -1;

    public HnswIndex(int m = 16, int efConstruction = 200, int? seed = null)
    {
        _m = Math.Max(2, m);
        _mMax0 = _m * 2;
        _efConstruction = Math.Max(_m, efConstruction);
        _levelMult = 1.0 / Math.Log(_m);
        _random = seed is null ? new Random() : new Random(seed.Value);
    }

    public int Count => _vectors.Count;

    /// <summary>Adiciona um vetor com seu identificador externo (ex.: índice na lista de chunks).</summary>
    public void Add(int id, float[] vector)
    {
        var node = _vectors.Count;
        _vectors.Add(vector);
        _ids.Add(id);

        var level = RandomLevel();
        var nodeLinks = new List<List<int>>();
        for (var l = 0; l <= level; l++)
        {
            nodeLinks.Add(new List<int>());
        }
        _links.Add(nodeLinks);

        if (_entryPoint == -1)
        {
            _entryPoint = node;
            _maxLevel = level;
            return;
        }

        var current = _entryPoint;
        var currentDist = Distance(vector, _vectors[current]);

        // Desce das camadas superiores até level+1 fazendo greedy search (1 vizinho).
        for (var l = _maxLevel; l > level; l--)
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var neighbor in NeighborsAt(current, l))
                {
                    var d = Distance(vector, _vectors[neighbor]);
                    if (d < currentDist)
                    {
                        currentDist = d;
                        current = neighbor;
                        changed = true;
                    }
                }
            }
        }

        // Nas camadas <= level, faz busca ef e conecta.
        for (var l = Math.Min(level, _maxLevel); l >= 0; l--)
        {
            var candidates = SearchLayer(vector, current, _efConstruction, l);
            var mMax = l == 0 ? _mMax0 : _m;
            var selected = SelectNeighbors(vector, candidates, _m);

            _links[node][l].AddRange(selected);
            foreach (var neighbor in selected)
            {
                _links[neighbor][l].Add(node);
                // Poda vizinhos que excederam o limite, mantendo os mais próximos.
                if (_links[neighbor][l].Count > mMax)
                {
                    var pruned = SelectNeighbors(_vectors[neighbor], _links[neighbor][l], mMax);
                    _links[neighbor][l] = pruned;
                }
            }

            if (candidates.Count > 0)
            {
                current = candidates[0];
            }
        }

        if (level > _maxLevel)
        {
            _maxLevel = level;
            _entryPoint = node;
        }
    }

    /// <summary>Retorna os <paramref name="k"/> ids mais próximos de <paramref name="query"/>, do mais próximo ao mais distante.</summary>
    public IReadOnlyList<int> Search(float[] query, int k, int? ef = null)
    {
        if (_entryPoint == -1)
        {
            return Array.Empty<int>();
        }

        var efSearch = Math.Max(k, ef ?? Math.Max(_efConstruction / 4, k));
        var current = _entryPoint;
        var currentDist = Distance(query, _vectors[current]);

        for (var l = _maxLevel; l > 0; l--)
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var neighbor in NeighborsAt(current, l))
                {
                    var d = Distance(query, _vectors[neighbor]);
                    if (d < currentDist)
                    {
                        currentDist = d;
                        current = neighbor;
                        changed = true;
                    }
                }
            }
        }

        var candidates = SearchLayer(query, current, efSearch, 0);
        return candidates
            .Take(k)
            .Select(node => _ids[node])
            .ToList();
    }

    private IEnumerable<int> NeighborsAt(int node, int layer)
        => layer < _links[node].Count ? _links[node][layer] : Enumerable.Empty<int>();

    /// <summary>Busca ef-greedy na camada, retornando nós ordenados por proximidade (mais próximo primeiro).</summary>
    private List<int> SearchLayer(float[] query, int entry, int ef, int layer)
    {
        var visited = new HashSet<int> { entry };
        var entryDist = Distance(query, _vectors[entry]);

        // candidatos: min-heap por distância (mais próximo primeiro) — usamos lista ordenada simples.
        var candidates = new List<(double Dist, int Node)> { (entryDist, entry) };
        var results = new List<(double Dist, int Node)> { (entryDist, entry) };

        while (candidates.Count > 0)
        {
            // pega o candidato mais próximo
            var bestIdx = 0;
            for (var i = 1; i < candidates.Count; i++)
            {
                if (candidates[i].Dist < candidates[bestIdx].Dist)
                {
                    bestIdx = i;
                }
            }
            var (cDist, cNode) = candidates[bestIdx];
            candidates.RemoveAt(bestIdx);

            // pior resultado atual
            var worst = results.Max(r => r.Dist);
            if (cDist > worst && results.Count >= ef)
            {
                break;
            }

            foreach (var neighbor in NeighborsAt(cNode, layer))
            {
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                var d = Distance(query, _vectors[neighbor]);
                var worstResult = results.Count > 0 ? results.Max(r => r.Dist) : double.MaxValue;
                if (results.Count < ef || d < worstResult)
                {
                    candidates.Add((d, neighbor));
                    results.Add((d, neighbor));
                    if (results.Count > ef)
                    {
                        // remove o pior
                        var worstIdx = 0;
                        for (var i = 1; i < results.Count; i++)
                        {
                            if (results[i].Dist > results[worstIdx].Dist)
                            {
                                worstIdx = i;
                            }
                        }
                        results.RemoveAt(worstIdx);
                    }
                }
            }
        }

        return results.OrderBy(r => r.Dist).Select(r => r.Node).ToList();
    }

    private List<int> SelectNeighbors(float[] baseVector, IReadOnlyList<int> candidates, int m)
        => candidates
            .Distinct()
            .Select(c => (Dist: Distance(baseVector, _vectors[c]), Node: c))
            .OrderBy(x => x.Dist)
            .Take(m)
            .Select(x => x.Node)
            .ToList();

    private int RandomLevel()
    {
        var r = _random.NextDouble();
        return (int)(-Math.Log(r <= 0 ? double.Epsilon : r) * _levelMult);
    }

    private static double Distance(float[] a, float[] b)
    {
        // 1 - cosseno. Menor = mais próximo.
        float dot = 0f, na = 0f, nb = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        if (na == 0f || nb == 0f)
        {
            return 1.0;
        }

        var cos = dot / (MathF.Sqrt(na) * MathF.Sqrt(nb));
        return 1.0 - cos;
    }
}
