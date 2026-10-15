using Aedis.Hosting.AspNetCore.Exports;
using Aedis.Hosting.AspNetCore.Requests;
using FluentAssertions;
using Xunit;

namespace Aedis.Hosting.AspNetCore.Tests;

/// <summary>Leitura paginada para export: o escopo <c>page</c> busca uma vez; o <c>all</c> percorre em blocos até o total.</summary>
public sealed class CollectionExportPageLoaderTests {
    private static Func<int, int, CancellationToken, Task<(IReadOnlyList<int> Items, int Total)>> Source(int total, List<(int Page, int Size)> calls) =>
        (page, size, _) => {
            calls.Add((page, size));
            IReadOnlyList<int> items = Enumerable.Range(1, total).Skip((page - 1) * size).Take(size).ToList();
            return Task.FromResult((items, total));
        };

    [Fact]
    public async Task Escopo_page_busca_apenas_a_pagina_pedida() {
        var calls = new List<(int, int)>();

        var items = await CollectionExportPageLoader.LoadAsync(CollectionExportMode.Page, 2, 3, 100, Source(10, calls));

        items.Should().Equal(4, 5, 6);
        calls.Should().Equal((2, 3));
    }

    [Fact]
    public async Task Escopo_all_streama_em_blocos_ate_o_total() {
        var calls = new List<(int, int)>();
        var streamed = new List<int>();

        await foreach (var item in CollectionExportPageLoader.StreamAsync(4, Source(10, calls)))
            streamed.Add(item);

        streamed.Should().Equal(Enumerable.Range(1, 10));
        calls.Should().Equal((1, 4), (2, 4), (3, 4));
    }

    [Fact]
    public async Task Escopo_all_vazio_nao_insiste() {
        var calls = new List<(int, int)>();

        var items = await CollectionExportPageLoader.LoadAsync(CollectionExportMode.All, 1, 10, 5, Source(0, calls));

        items.Should().BeEmpty();
        calls.Should().HaveCount(1);
    }
}
