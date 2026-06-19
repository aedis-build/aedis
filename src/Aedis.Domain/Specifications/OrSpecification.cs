using Aedis.Domain.Specifications.Abstractions;
namespace Aedis.Domain.Specifications;

/// <summary>
///     Especificação composta satisfeita quando ao menos uma das especificações <c>left</c> ou <c>right</c>
///     o é (OU lógico). Normalmente criada por <see cref="SpecificationBase{T}.Or" />.
/// </summary>
/// <typeparam name="T">Tipo do candidato avaliado pela regra.</typeparam>
public class OrSpecification<T>(ISpecification<T> left, ISpecification<T> right) : SpecificationBase<T>
{
    /// <inheritdoc />
    public override bool IsSatisfiedBy(T candidate) {
        return left.IsSatisfiedBy(candidate) || right.IsSatisfiedBy(candidate);
    }
}