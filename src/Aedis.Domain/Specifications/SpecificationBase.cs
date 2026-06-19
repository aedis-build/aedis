using Aedis.Domain.Specifications.Abstractions;
namespace Aedis.Domain.Specifications;

/// <summary>
///     Base para especificações concretas: implemente apenas <see cref="IsSatisfiedBy" /> e herde de graça a
///     composição via <see cref="And" /> (<see cref="AndSpecification{T}" />) e <see cref="Or" />
///     (<see cref="OrSpecification{T}" />).
/// </summary>
/// <typeparam name="T">Tipo do candidato avaliado pela regra.</typeparam>
public abstract class SpecificationBase<T> : ISpecification<T>
{
    /// <inheritdoc />
    public abstract bool IsSatisfiedBy(T candidate);

    /// <inheritdoc />
    public ISpecification<T> And(ISpecification<T> other) {
        return new AndSpecification<T>(this, other);
    }

    /// <inheritdoc />
    public ISpecification<T> Or(ISpecification<T> other) {
        return new OrSpecification<T>(this, other);
    }
}