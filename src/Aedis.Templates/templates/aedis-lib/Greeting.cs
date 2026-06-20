namespace AedisLibrary1;

/// <summary>
///     Contrato de exemplo — substitua pela API pública da sua biblioteca. Seguindo a convenção do Aedis,
///     todo tipo/membro público recebe um <c>&lt;summary&gt;</c> didático (o quê, quando/como, e como funciona).
/// </summary>
public interface IGreeting
{
    /// <summary>Produz uma saudação para <paramref name="name" />.</summary>
    string Greet(string name);
}

/// <summary>Implementação default de <see cref="IGreeting" />.</summary>
public sealed class Greeting : IGreeting
{
    /// <inheritdoc />
    public string Greet(string name) => $"Olá, {name}!";
}
