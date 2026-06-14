using Aedis.Domain.Strategy;
using Aedis.Domain.Strategy.Abstractions;

namespace Aedis.Database.Abstractions;

public interface INamingStrategy : IStrategy<NamingContext>
{
    string Convert(NamingContext context);

    bool Validate(NamingContext context, out string? errorMessage);
}