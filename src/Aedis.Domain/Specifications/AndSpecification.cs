namespace Aedis.Domain.Specifications;

public class AndSpecification<T>(ISpecification<T> left, ISpecification<T> right) : SpecificationBase<T>
{
    public override bool IsSatisfiedBy(T candidate) {
        return left.IsSatisfiedBy(candidate) && right.IsSatisfiedBy(candidate);
    }
}