using System.Globalization;

namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Avalia expressões aritméticas simples (+ - * / e parênteses, números decimais) usando um
/// parser recursivo-descendente escrito à mão — deliberadamente SEM <c>eval</c>,
/// <c>DataTable.Compute</c> ou reflection dinâmica, para evitar execução de código arbitrário.
/// </summary>
public sealed class CalculatorTool : ITool
{
    public string Name => "calculator";

    public string Description =>
        "Avalia expressões aritméticas (soma, subtração, multiplicação, divisão, parênteses, " +
        "números decimais). Use para perguntas que envolvam cálculos matemáticos explícitos.";

    public Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default)
    {
        var result = Evaluate(input);
        return Task.FromResult(result.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Avalia uma expressão aritmética e retorna o resultado numérico.</summary>
    /// <exception cref="CalculatorException">Quando a expressão é inválida (sintaxe, parêntese não fechado, divisão por zero etc.).</exception>
    public static double Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new CalculatorException("Expressão vazia.");
        }

        var parser = new Parser(expression);
        var value = parser.ParseExpression();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
        {
            throw new CalculatorException($"Caractere inesperado na posição {parser.Position}: '{parser.Current}'.");
        }

        return value;
    }

    /// <summary>
    /// Parser recursivo-descendente clássico para gramática de expressões aritméticas:
    /// expression := term (('+' | '-') term)*
    /// term       := factor (('*' | '/') factor)*
    /// factor     := number | '(' expression ')' | ('+' | '-') factor
    /// </summary>
    private sealed class Parser
    {
        private readonly string _text;
        public int Position { get; private set; }

        public Parser(string text)
        {
            _text = text;
        }

        public bool AtEnd => Position >= _text.Length;
        public char Current => AtEnd ? '\0' : _text[Position];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current)) Position++;
        }

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (!AtEnd && Current == '+')
                {
                    Position++;
                    value += ParseTerm();
                }
                else if (!AtEnd && Current == '-')
                {
                    Position++;
                    value -= ParseTerm();
                }
                else
                {
                    break;
                }
            }

            return value;
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (!AtEnd && Current == '*')
                {
                    Position++;
                    value *= ParseFactor();
                }
                else if (!AtEnd && Current == '/')
                {
                    Position++;
                    var divisor = ParseFactor();
                    if (divisor == 0)
                    {
                        throw new CalculatorException("Divisão por zero.");
                    }
                    value /= divisor;
                }
                else
                {
                    break;
                }
            }

            return value;
        }

        private double ParseFactor()
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw new CalculatorException("Fim inesperado da expressão.");
            }

            if (Current == '+')
            {
                Position++;
                return ParseFactor();
            }

            if (Current == '-')
            {
                Position++;
                return -ParseFactor();
            }

            if (Current == '(')
            {
                Position++;
                var value = ParseExpression();
                SkipWhitespace();
                if (AtEnd || Current != ')')
                {
                    throw new CalculatorException("Parêntese não fechado.");
                }
                Position++;
                return value;
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            var start = Position;
            var hasDigits = false;

            while (!AtEnd && char.IsDigit(Current))
            {
                hasDigits = true;
                Position++;
            }

            if (!AtEnd && Current == '.')
            {
                Position++;
                while (!AtEnd && char.IsDigit(Current))
                {
                    hasDigits = true;
                    Position++;
                }
            }

            if (!hasDigits)
            {
                throw new CalculatorException($"Número inválido na posição {start}.");
            }

            var token = _text.Substring(start, Position - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new CalculatorException($"Número inválido: '{token}'.");
            }

            return value;
        }
    }
}

/// <summary>Lançada quando <see cref="CalculatorTool"/> não consegue avaliar a expressão informada.</summary>
public sealed class CalculatorException : Exception
{
    public CalculatorException(string message) : base(message)
    {
    }
}
