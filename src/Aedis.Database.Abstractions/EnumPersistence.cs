namespace Aedis.Database.Abstractions;

/// <summary>
///     Regra única de persistência de enums em texto: o nome do membro exatamente como declarado em C#
///     (PascalCase), em INSERT/UPDATE, bulk e critérios de consulta. Gravar em UPPERCASE escondia linhas de
///     SQL escrito à mão e de outros produtores que usam o nome declarado; manter uma só regra em todos os
///     caminhos garante que a mesma condição encontre o que foi gravado por qualquer um deles.
/// </summary>
public static class EnumPersistence
{
    /// <summary>Nome persistido de um enum: o nome do membro declarado (PascalCase).</summary>
    /// <param name="value">Valor do enum.</param>
    /// <returns>O nome do membro, ou os nomes combinados para enums <c>[Flags]</c>.</returns>
    public static string ToStoredName(Enum value) {
        ArgumentNullException.ThrowIfNull(value);
        return value.ToString();
    }

    /// <summary>Aplica a regra quando o valor é um enum; qualquer outro valor passa inalterado.</summary>
    /// <param name="value">Valor possivelmente enum.</param>
    /// <returns>O nome persistido se for enum; caso contrário o próprio valor.</returns>
    public static object? ToStored(object? value) => value is Enum enumValue ? ToStoredName(enumValue) : value;
}
