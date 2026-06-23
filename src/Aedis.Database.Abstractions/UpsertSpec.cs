namespace Aedis.Database.Abstractions;

/// <summary>Valor calculado pelo servidor de banco, resolvido para a função nativa de cada provider.</summary>
public enum UpsertServerValue
{
    /// <summary>Instante UTC atual — <c>now()</c> no PostgreSQL, <c>SYSUTCDATETIME()</c> no SQL Server.</summary>
    UtcNow
}

/// <summary>Operador de frescor: como a linha que entra é comparada à linha já armazenada.</summary>
public enum FreshnessOperator
{
    /// <summary>A linha que entra é aceita se for maior ou igual à armazenada (<c>&gt;=</c>).</summary>
    GreaterOrEqual,

    /// <summary>A linha que entra é aceita apenas se for estritamente maior que a armazenada (<c>&gt;</c>).</summary>
    Greater
}

/// <summary>Comparação de frescor sobre uma propriedade (resolvida para coluna pela convenção de nomes).</summary>
/// <param name="Property">Nome da propriedade da entidade comparada entre a linha nova e a armazenada.</param>
/// <param name="Operator">Operador aplicado (<c>&gt;=</c> ou <c>&gt;</c>).</param>
public readonly record struct FreshnessTerm(string Property, FreshnessOperator Operator);

/// <summary>Predicado booleano sobre uma coluna da linha <em>já armazenada</em> (ex.: <c>is_deleted = false</c>).</summary>
/// <param name="Property">Nome da propriedade booleana avaliada na linha existente.</param>
/// <param name="ExpectedValue">Valor exigido para que a atualização ocorra.</param>
public readonly record struct BooleanTerm(string Property, bool ExpectedValue);

/// <summary>
///     Guarda condicional de um upsert: a atualização da linha existente só ocorre se o predicado for
///     verdadeiro. Os termos de <see cref="Freshness" /> são combinados por <c>OR</c> ("atualize se a linha
///     que entra for mais nova por <em>qualquer</em> um destes critérios"); os termos de
///     <see cref="Booleans" /> são combinados por <c>AND</c> ("e somente se a linha existente satisfizer
///     estes"). O predicado final é <c>(OR frescor) AND (AND booleanos)</c>. Cada provider o compila para o
///     seu dialeto (<c>EXCLUDED</c>/tabela no PostgreSQL, <c>s.</c>/<c>t.</c> no MERGE do SQL Server), com
///     tratamento de nulos e literais booleanos próprios — sem o consumidor escrever SQL.
/// </summary>
public sealed class UpsertGuard
{
    internal UpsertGuard(IReadOnlyList<FreshnessTerm> freshness, IReadOnlyList<BooleanTerm> booleans) {
        Freshness = freshness;
        Booleans = booleans;
    }

    /// <summary>Termos de frescor combinados por <c>OR</c>.</summary>
    public IReadOnlyList<FreshnessTerm> Freshness { get; }

    /// <summary>Predicados booleanos sobre a linha existente, combinados por <c>AND</c>.</summary>
    public IReadOnlyList<BooleanTerm> Booleans { get; }

    /// <summary>Indica que a guarda não impõe nenhuma condição (equivale a upsert incondicional).</summary>
    public bool IsEmpty => Freshness.Count == 0 && Booleans.Count == 0;
}

/// <summary>
///     Construtor fluente da <see cref="UpsertGuard" />. Use <see cref="Newer" />/<see cref="OrNewer" /> para
///     critérios <c>&gt;=</c>, <see cref="Greater" />/<see cref="OrGreater" /> para <c>&gt;</c> (tie-break
///     estrito) e <see cref="AndNotDeleted" />/<see cref="AndExistingFalse" /> para restringir a linhas não
///     soft-deleted.
/// </summary>
public sealed class UpsertGuardBuilder
{
    private readonly List<BooleanTerm> _booleans = [];
    private readonly List<FreshnessTerm> _freshness = [];

    /// <summary>Aceita a atualização se a propriedade que entra for maior ou igual à armazenada (<c>&gt;=</c>).</summary>
    public UpsertGuardBuilder Newer(string property) {
        _freshness.Add(new FreshnessTerm(property, FreshnessOperator.GreaterOrEqual));
        return this;
    }

    /// <summary>Alternativa <c>OR</c> de <see cref="Newer" /> — outro critério <c>&gt;=</c>.</summary>
    public UpsertGuardBuilder OrNewer(string property) => Newer(property);

    /// <summary>Aceita a atualização se a propriedade que entra for estritamente maior que a armazenada (<c>&gt;</c>).</summary>
    public UpsertGuardBuilder Greater(string property) {
        _freshness.Add(new FreshnessTerm(property, FreshnessOperator.Greater));
        return this;
    }

    /// <summary>Alternativa <c>OR</c> de <see cref="Greater" /> — outro critério <c>&gt;</c> (tie-break).</summary>
    public UpsertGuardBuilder OrGreater(string property) => Greater(property);

    /// <summary>Exige que a propriedade booleana da linha existente seja <c>false</c> para atualizar.</summary>
    public UpsertGuardBuilder AndExistingFalse(string property) {
        _booleans.Add(new BooleanTerm(property, false));
        return this;
    }

    /// <summary>Exige que a propriedade booleana da linha existente seja <c>true</c> para atualizar.</summary>
    public UpsertGuardBuilder AndExistingTrue(string property) {
        _booleans.Add(new BooleanTerm(property, true));
        return this;
    }

    /// <summary>Atalho para <see cref="AndExistingFalse" /> sobre a propriedade de soft-delete (padrão <c>IsDeleted</c>).</summary>
    public UpsertGuardBuilder AndNotDeleted(string property = "IsDeleted") => AndExistingFalse(property);

    internal UpsertGuard Build() => new(_freshness, _booleans);
}

/// <summary>
///     Especificação <strong>agnóstica de provider</strong> de um upsert: declara as chaves de conflito, se
///     a linha em colisão é atualizada, quais colunas recebem valor do servidor (ex.: <c>UpdatedAt</c> =
///     agora) e a <see cref="UpsertGuard">guarda</see> condicional. Cada provider Aedis a compila para o seu
///     dialeto — <c>INSERT … ON CONFLICT … DO UPDATE … WHERE</c> no PostgreSQL e
///     <c>MERGE … WHEN MATCHED AND … THEN UPDATE</c> no SQL Server — de modo que o mesmo
///     <c>GetUpsertSpec()</c> vale nos dois sem refatoração ao trocar o provider.
///     <example>
///         <code>
///         protected override UpsertSpec? GetUpsertSpec() => UpsertSpec.OnKey("Id")
///             .SetServerUtcNow("UpdatedAt")
///             .When(g => g.Newer("ObservedAt").OrGreater("SourceSeq").AndNotDeleted());
///         </code>
///     </example>
/// </summary>
public sealed class UpsertSpec
{
    private readonly HashSet<string> _preserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UpsertServerValue> _serverValues = new(StringComparer.OrdinalIgnoreCase);

    private UpsertSpec(IReadOnlyList<string> keyProperties) => KeyProperties = keyProperties;

    /// <summary>Propriedades que formam a chave de conflito (resolvidas para colunas pela convenção de nomes).</summary>
    public IReadOnlyList<string> KeyProperties { get; }

    /// <summary>Quando <c>false</c>, a linha em colisão é preservada (DO NOTHING / sem WHEN MATCHED de update).</summary>
    public bool UpdateMatched { get; private set; } = true;

    /// <summary>Colunas cujo valor, na atualização, vem do servidor (ex.: <c>UpdatedAt</c> = agora) e não da linha que entra.</summary>
    public IReadOnlyDictionary<string, UpsertServerValue> ServerValues => _serverValues;

    /// <summary>Propriedades inseridas mas <strong>nunca sobrescritas</strong> na atualização (ex.: <c>CreatedAt</c>).</summary>
    public IReadOnlyCollection<string> PreservedProperties => _preserved;

    /// <summary>Guarda condicional; <c>null</c> significa atualização incondicional.</summary>
    public UpsertGuard? Guard { get; private set; }

    /// <summary>Inicia uma especificação com as propriedades-chave informadas (sem argumentos, assume <c>Id</c>).</summary>
    public static UpsertSpec OnKey(params string[] keyProperties) =>
        new(keyProperties is null or { Length: 0 } ? ["Id"] : keyProperties);

    /// <summary>Em colisão, preserva a linha existente sem atualizar (insere apenas as novas).</summary>
    public UpsertSpec DoNothingOnConflict() {
        UpdateMatched = false;
        Guard = null;
        _serverValues.Clear();
        return this;
    }

    /// <summary>Na atualização, grava o instante UTC do servidor na propriedade informada (em vez do valor que entra).</summary>
    public UpsertSpec SetServerUtcNow(string property) {
        _serverValues[property] = UpsertServerValue.UtcNow;
        return this;
    }

    /// <summary>
    ///     Marca propriedades que entram apenas no INSERT e <strong>nunca</strong> são sobrescritas na
    ///     atualização (ex.: <c>CreatedAt</c>) — ficam fora do <c>SET</c> tanto no ON CONFLICT quanto no MERGE.
    /// </summary>
    public UpsertSpec Preserve(params string[] properties) {
        foreach (var property in properties)
            _preserved.Add(property);
        return this;
    }

    /// <summary>Define a guarda condicional: a atualização só ocorre se o predicado construído for verdadeiro.</summary>
    public UpsertSpec When(Action<UpsertGuardBuilder> build) {
        var builder = new UpsertGuardBuilder();
        build(builder);
        Guard = builder.Build();
        return this;
    }
}
