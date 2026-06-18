using Aedis.Security;
using Aedis.Security.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Aedis.Security.Tests;

/// <summary>
///     A ponte usuário-logado → contexto de auditoria: <c>CurrentActor</c> reflete o
///     <see cref="ICurrentUser" /> autenticado, e é <c>null</c> (→ ator default no provider) quando
///     anônimo. Também valida a registração de DI.
/// </summary>
public sealed class CurrentUserAuditContextTests
{
    [Fact]
    public void CurrentActor_e_o_usuario_logado() {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Id.Returns("user-123");

        new CurrentUserAuditContext(user).CurrentActor.Should().Be("user-123");
    }

    [Fact]
    public void CurrentActor_usa_Name_quando_nao_ha_Id() {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Id.Returns((string?)null);
        user.Name.Returns("Alice");

        new CurrentUserAuditContext(user).CurrentActor.Should().Be("Alice");
    }

    [Fact]
    public void CurrentActor_e_null_quando_anonimo_ou_sem_usuario() {
        var anonymous = Substitute.For<ICurrentUser>();
        anonymous.IsAuthenticated.Returns(false);

        new CurrentUserAuditContext(anonymous).CurrentActor.Should().BeNull("→ provider grava o ator default");
        new CurrentUserAuditContext().CurrentActor.Should().BeNull("sem ICurrentUser registrado");
    }

    [Fact]
    public void AddAedisAuditContext_registra_a_mesma_instancia_como_IAuditContext_e_concreto() {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.Id.Returns("u1");

        var provider = new ServiceCollection()
            .AddSingleton(user)
            .AddAedisAuditContext()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IAuditContext>();
        var concrete = scope.ServiceProvider.GetRequiredService<CurrentUserAuditContext>();

        context.Should().BeSameAs(concrete, "Reason definido no concreto é lido pelo IAuditContext");
        context.CurrentActor.Should().Be("u1");

        concrete.Reason = "motivo X";
        context.Reason.Should().Be("motivo X");
    }
}
