using System.Globalization;
using System.Text.RegularExpressions;

namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Converte unidades comuns (item 11): distância (km↔milhas, m↔pés), massa (kg↔libras) e
/// temperatura (°C↔°F). Aceita entradas em linguagem natural simples como "10 km em milhas" ou
/// "100 f para c". Parser deliberadamente simples e sem execução dinâmica.
/// </summary>
public sealed class UnitConverterTool : ITool
{
    public string Name => "unit_converter";

    public string Description =>
        "Converte unidades comuns: km↔milhas, m↔pés, kg↔libras, °C↔°F. Informe a entrada no " +
        "formato '<número> <unidade_origem> em <unidade_destino>', ex.: '10 km em milhas'.";

    private static readonly Regex InputRegex = new(
        @"^\s*(-?\d+(?:[.,]\d+)?)\s*([a-zA-Z°ºçÇ]+)\s*(?:em|para|to|in|->)\s*([a-zA-Z°ºçÇ]+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default)
    {
        var result = Convert(input);
        return Task.FromResult(result);
    }

    /// <summary>Converte a expressão e devolve um texto com o resultado.</summary>
    /// <exception cref="UnitConverterException">Quando a expressão ou as unidades são inválidas.</exception>
    public static string Convert(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new UnitConverterException("Entrada vazia.");
        }

        var match = InputRegex.Match(input);
        if (!match.Success)
        {
            throw new UnitConverterException(
                "Formato inválido. Use '<número> <unidade_origem> em <unidade_destino>', ex.: '10 km em milhas'.");
        }

        var value = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var from = NormalizeUnit(match.Groups[2].Value);
        var to = NormalizeUnit(match.Groups[3].Value);

        var converted = ConvertValue(value, from, to);
        var formatted = Math.Round(converted, 4).ToString(CultureInfo.InvariantCulture);
        return $"{value.ToString(CultureInfo.InvariantCulture)} {from} = {formatted} {to}";
    }

    private static double ConvertValue(double value, string from, string to)
    {
        // Temperatura (não é conversão por fator linear simples).
        if (IsTemperature(from) && IsTemperature(to))
        {
            if (from == to) return value;
            return from == "c" ? value * 9d / 5d + 32d : (value - 32d) * 5d / 9d;
        }

        // Distância: converte para metros e depois para o destino.
        var distanceToMeters = new Dictionary<string, double>
        {
            ["km"] = 1000, ["m"] = 1, ["mi"] = 1609.344, ["ft"] = 0.3048,
        };
        if (distanceToMeters.ContainsKey(from) && distanceToMeters.ContainsKey(to))
        {
            return value * distanceToMeters[from] / distanceToMeters[to];
        }

        // Massa: converte para gramas.
        var massToGrams = new Dictionary<string, double>
        {
            ["kg"] = 1000, ["g"] = 1, ["lb"] = 453.59237,
        };
        if (massToGrams.ContainsKey(from) && massToGrams.ContainsKey(to))
        {
            return value * massToGrams[from] / massToGrams[to];
        }

        throw new UnitConverterException($"Não sei converter de '{from}' para '{to}' (unidades incompatíveis ou desconhecidas).");
    }

    private static bool IsTemperature(string unit) => unit is "c" or "f";

    private static string NormalizeUnit(string raw)
    {
        var u = raw.Trim().ToLowerInvariant().Replace("°", "").Replace("º", "");
        return u switch
        {
            "km" or "quilometro" or "quilometros" or "quilômetro" or "quilômetros" => "km",
            "m" or "metro" or "metros" => "m",
            "mi" or "milha" or "milhas" or "mile" or "miles" => "mi",
            "ft" or "pe" or "pes" or "pé" or "pés" or "feet" or "foot" => "ft",
            "kg" or "quilo" or "quilos" or "quilograma" or "quilogramas" => "kg",
            "g" or "grama" or "gramas" => "g",
            "lb" or "lbs" or "libra" or "libras" or "pound" or "pounds" => "lb",
            "c" or "celsius" or "centigrado" or "centígrado" => "c",
            "f" or "fahrenheit" => "f",
            _ => throw new UnitConverterException($"Unidade desconhecida: '{raw}'."),
        };
    }
}

/// <summary>Lançada quando <see cref="UnitConverterTool"/> não consegue interpretar/converter a entrada.</summary>
public sealed class UnitConverterException : Exception
{
    public UnitConverterException(string message) : base(message)
    {
    }
}
