namespace Aedis.Commands;

/// <summary>
///     Representa um comando que pode ser executado e retorna um resultado.
///     Baseado no padrão Command (https://refactoring.guru/design-patterns/command).
///     Usado para encapsular operações que precisam de retry, observability, auditoria, etc.
/// </summary>
/// <typeparam name="TResult">Tipo do resultado da execução do comando.</typeparam>
public interface ICommand<TResult> { }