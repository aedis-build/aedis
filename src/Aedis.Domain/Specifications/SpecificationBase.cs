using Aedis.Domain.Specifications.Abstractions;
namespace Aedis.Domain.Specifications;

public abstract class SpecificationBase<T> : ISpecification<T>
{
    public abstract bool IsSatisfiedBy(T candidate);

    public ISpecification<T> And(ISpecification<T> other) {
        return new AndSpecification<T>(this, other);
    }

    public ISpecification<T> Or(ISpecification<T> other) {
        return new OrSpecification<T>(this, other);
    }
}