namespace Aedis.Domain.Specifications.Abstractions;

/// <summary>
///     Especificação do padrão Specification: encapsula uma regra de negócio booleana sobre um candidato e
///     permite compô-la com outras via <see cref="And" /> e <see cref="Or" />. Use para validar, filtrar ou
///     selecionar objetos por critérios reutilizáveis e combináveis.
/// </summary>
/// <typeparam name="T">Tipo do candidato avaliado pela regra.</typeparam>
public interface ISpecification<T>
{
    /// <summary>Avalia se o candidato satisfaz a regra desta especificação.</summary>
    bool IsSatisfiedBy(T candidate);

    /// <summary>Compõe esta especificação com outra exigindo que ambas sejam satisfeitas (E lógico).</summary>
    ISpecification<T> And(ISpecification<T> other);

    /// <summary>Compõe esta especificação com outra exigindo que ao menos uma seja satisfeita (OU lógico).</summary>
    ISpecification<T> Or(ISpecification<T> other);
}