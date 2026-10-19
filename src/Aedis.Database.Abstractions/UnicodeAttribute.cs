namespace Aedis.Database.Abstractions;

/// <summary>
///     Marca uma propriedade <c>string</c> cuja coluna é Unicode (<c>nvarchar</c> no SQL Server). Por padrão o
///     provider SQL Server envia strings como <c>varchar</c>, para a comparação não converter a coluna e perder o
///     índice; com este atributo o parâmetro vai como <c>nvarchar</c>. Providers sem essa distinção (PostgreSQL,
///     MySQL) ignoram o atributo.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class UnicodeAttribute : Attribute;
