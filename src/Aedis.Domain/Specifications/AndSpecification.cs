using Aedis.Domain.Specifications.Abstractions;
namespace Aedis.Domain.Specifications;

/// <summary>
///     Especificação composta que só é satisfeita quando ambas as especificações <c>left</c> e <c>right</c>
///     o são (E lógico). Normalmente criada por <see cref="SpecificationBase{T}.And" />.
/// </summary>
/// <typeparam name="T">Tipo do candidato avaliado pela regra.</typeparam>
public class AndSpecification<T>(ISpecification<T> left, ISpecification<T> right) : SpecificationBase<T>
{
    /// <inheritdoc />
    public override bool IsSatisfiedBy(T candidate) {
        return left.IsSatisfiedBy(candidate) && right.IsSatisfiedBy(candidate);
    }
}