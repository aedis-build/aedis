namespace Aedis.Database.Abstractions;

/// <summary>
///     Mapeamento de erros do banco em exceções da aplicação, registrado no contêiner e consultado pelos
///     repositórios após o hook <c>OnDatabaseError</c> e antes do mapeamento padrão do framework. Útil para
///     traduzir restrições com nome conhecido (ex.: <c>ux_contratos_chave_idempotencia</c>) em regras de
///     negócio com código próprio, sem herdar do repositório.
/// </summary>
public interface IDatabaseErrorMapper
{
    /// <summary>Devolve a exceção a lançar para o erro, ou <c>null</c> para seguir ao mapeamento padrão.</summary>
    /// <param name="error">Erro normalizado pelo provider.</param>
    Exception? Map(DatabaseError error);
}
