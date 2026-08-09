using System.Runtime.CompilerServices;
using System.Text;

namespace QuestResume.Core.Rag;

public sealed class RoutingLlmProvider : ILlmProvider
{
    private readonly IReadOnlyList<ILlmProvider> _providers;

    public RoutingLlmProvider(IReadOnlyList<ILlmProvider> providers)
    {
        if (providers is null || providers.Count == 0)
        {
            throw new ArgumentException("É necessário informar ao menos um provedor de LLM para roteamento.", nameof(providers));
        }

        _providers = providers;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var exceptions = new List<Exception>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await provider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        throw BuildAggregateException(exceptions);
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var exceptions = new List<Exception>();

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var emittedAny = false;
            var enumerator = provider.CompleteStreamAsync(prompt, cancellationToken).GetAsyncEnumerator(cancellationToken);
            var isLastProvider = i == _providers.Count - 1;

            while (true)
            {
                string? token = null;
                var hasNext = false;
                Exception? failure = null;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    if (hasNext) token = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                if (failure is not null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);

                    if (emittedAny || isLastProvider)
                    {
                        
                        
                        exceptions.Add(failure);
                        throw BuildAggregateException(exceptions);
                    }

                    exceptions.Add(failure);
                    break; 
                }

                if (!hasNext)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                    yield break; 
                }

                emittedAny = true;
                yield return token!;
            }
        }

        throw BuildAggregateException(exceptions);
    }

    private static Exception BuildAggregateException(List<Exception> exceptions)
    {
        if (exceptions.Count == 1)
        {
            return exceptions[0];
        }

        var sb = new StringBuilder();
        sb.Append("Todos os provedores de LLM configurados falharam. ");
        for (var i = 0; i < exceptions.Count; i++)
        {
            sb.Append($"[{i + 1}] {exceptions[i].Message} ");
        }

        return new AggregateException(sb.ToString().Trim(), exceptions);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }
}
