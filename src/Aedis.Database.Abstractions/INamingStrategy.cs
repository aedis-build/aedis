using Aedis.Domain.Strategy;
using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Database.Abstractions;

/// <summary>
///     Estratégia que converte e valida nomes (tabela/coluna/índice/constraint) para uma
///     <see cref="NamingConvention" />. Cada convenção tem uma implementação; o resolver escolhe a
///     adequada via <see cref="IStrategy{T}.CanHandle" /> a partir do <see cref="NamingContext" />.
/// </summary>
public interface INamingStrategy : IStrategy<NamingContext>
{
    /// <summary>Converte o identificador descrito no contexto para a forma final da convenção.</summary>
    string Convert(NamingContext context);

    /// <summary>
    ///     Valida se o nome de entrada está conforme a convenção; retorna <c>false</c> e preenche
    ///     <paramref name="errorMessage" /> quando não está.
    /// </summary>
    bool Validate(NamingContext context, out string? errorMessage);
}