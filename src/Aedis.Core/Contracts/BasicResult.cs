namespace Aedis.Core;

/// <summary>
///     Resultado simples de uma operação: indica sucesso/falha, carrega um <see cref="CorrelationId" />
///     para rastreabilidade e uma lista de mensagens. Use quando não há payload de retorno; para retornar
///     dados, prefira <see cref="BasicResult{T}" />. Construa pelos fábricas <see cref="Ok" /> e
///     <see cref="Error" /> em vez de instanciar diretamente.
/// </summary>
public class BasicResult
{
    /// <summary>Indica se a operação foi bem-sucedida.</summary>
    public bool Success { get; init; }

    /// <summary>Identificador de correlação para rastreamento end-to-end; gerado automaticamente se não informado.</summary>
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    /// <summary>Mensagens associadas ao resultado (tipicamente descrições de erro quando <see cref="Success" /> é falso).</summary>
    public List<string> Messages { get; init; } = [];

    /// <summary>Cria um resultado de sucesso, opcionalmente com um <paramref name="correlationId" /> existente.</summary>
    public static BasicResult Ok(Guid? correlationId = null) {
        var ok = new BasicResult {
            Success = true,
            CorrelationId = correlationId ?? Guid.NewGuid()
        };
        return ok;
    }

    /// <summary>Cria um resultado de falha com as <paramref name="mensagens" /> de erro, opcionalmente com um <paramref name="correlationId" /> existente.</summary>
    public static BasicResult Error(Guid? correlationId = null, params string[] mensagens) {
        var error = new BasicResult {
            Success = false,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Messages = mensagens is { Length: > 0 } ? [..mensagens] : []
        };
        return error;
    }
}

/// <summary>
///     Variante de <see cref="BasicResult" /> que carrega um payload de retorno em <see cref="Result" />
///     quando a operação é bem-sucedida. Use os fábricas <see cref="Ok" /> e <see cref="Error" />.
/// </summary>
/// <typeparam name="T">Tipo do dado retornado em caso de sucesso.</typeparam>
public class BasicResult<T> : BasicResult
{
    /// <summary>Payload retornado quando <see cref="BasicResult.Success" /> é verdadeiro; nulo em caso de erro.</summary>
    public T? Result { get; init; }

    /// <summary>Cria um resultado de sucesso contendo <paramref name="result" />, opcionalmente com um <paramref name="correlationId" /> existente.</summary>
    public static BasicResult<T> Ok(T result, Guid? correlationId = null) {
        var ok = new BasicResult<T> {
            Success = true,
            Result = result,
            CorrelationId = correlationId ?? Guid.NewGuid()
        };
        return ok;
    }

    /// <summary>Cria um resultado de falha com as <paramref name="mensagens" /> de erro, opcionalmente com um <paramref name="correlationId" /> existente.</summary>
    public new static BasicResult<T> Error(Guid? correlationId = null, params string[] mensagens) {
        var error = new BasicResult<T> {
            Success = false,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Messages = mensagens is { Length: > 0 } ? [..mensagens] : []
        };
        return error;
    }
}
