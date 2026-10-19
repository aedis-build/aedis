using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aedis.Core.Utils;
using Aedis.Hosting.Abstractions;
using Aedis.Hypermedia.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aedis.Hypermedia.AspNetCore;

/// <summary>
///     Gera os links de navegação de uma coleção paginada (<c>self</c>/<c>first</c>/<c>prev</c>/<c>next</c>/
///     <c>last</c>) e, opcionalmente, os de exportação — em dois contratos. Em <c>GET</c>, cada link é uma URL
///     autossuficiente: a query string da requisição é preservada (filtros, ordenação) e só <c>page</c>/
///     <c>pageSize</c> (e <c>export.*</c>) são reescritos. Em <c>QUERY</c>, a URL fica limpa, o link sai com
///     <c>method: "QUERY"</c> e <c>_templates[rel].body</c> traz o request ecoado com a página (ou o descritor de
///     export) do <c>rel</c> — as chaves respeitam a serialização real da API via <see cref="IPageable" />/
///     <see cref="IExportable" />.
/// </summary>
internal static class PaginationLinks {
    private static readonly string[] NavigationRels = ["self", "first", "prev", "next", "last"];
    private static readonly JsonSerializerOptions FallbackJson = SystemJsonOptionsFactory.Create();

    public static void Apply<T>(ResourceCollection<T> collection, HttpContext httpContext, int? page, int? pageSize,
        int? totalCount, ExportLinkOptions? export, object? requestEcho) {
        requestEcho ??= httpContext.GetCapturedActionRequest();
        var isQuery = httpContext.IsQueryMethod();
        var cleanUrl = $"{httpContext.GetBaseUrl()}{httpContext.Request.Path}";
        var json = isQuery ? ResolveJsonOptions(httpContext) : null;

        foreach (var (rel, targetPage) in Navigation(page, pageSize, totalCount, collection.Items.Count)) {
            if (!isQuery) {
                collection.AddLink(rel, BuildGetUrl(httpContext, cleanUrl, targetPage, pageSize, null));
                continue;
            }

            collection.AddLink(rel, cleanUrl, HalFormsTemplate.QueryMethod);
            collection.AddTemplate(rel, new HalFormsTemplate {
                Target = cleanUrl,
                Body = BuildBody(requestEcho, json!, targetPage, pageSize, null)
            });
        }

        if (export is null) {
            return;
        }

        foreach (var format in export.Formats) {
            var formatName = format.ToString().ToLowerInvariant();

            if (export.IncludeAllExport) {
                AddExport(collection, httpContext, cleanUrl, isQuery, json, requestEcho, $"export-all-{formatName}",
                    new ExportRequest { Type = format, Mode = CollectionExportMode.All }, page, pageSize);
            }

            if (export.IncludePageExport && page is > 0 && pageSize is > 0) {
                AddExport(collection, httpContext, cleanUrl, isQuery, json, requestEcho, $"export-page-{formatName}",
                    new ExportRequest { Type = format, Mode = CollectionExportMode.Page }, page, pageSize);
            }
        }
    }

    private static IEnumerable<(string Rel, int? Page)> Navigation(int? page, int? pageSize, int? totalCount, int itemCount) {
        yield return ("self", page);

        if (page is not > 0 || pageSize is not > 0) {
            yield break;
        }

        yield return ("first", 1);

        if (page > 1) {
            yield return ("prev", page - 1);
        }

        if (totalCount is not null) {
            var lastPage = (int)Math.Ceiling((double)totalCount.Value / pageSize.Value);
            if (page < lastPage) {
                yield return ("next", page + 1);
            }

            if (lastPage > 1) {
                yield return ("last", lastPage);
            }
        }
        else if (itemCount == pageSize.Value) {
            yield return ("next", page + 1);
        }
    }

    private static void AddExport<T>(ResourceCollection<T> collection, HttpContext httpContext, string cleanUrl, bool isQuery,
        JsonSerializerOptions? json, object? requestEcho, string rel, ExportRequest descriptor, int? page, int? pageSize) {
        if (!isQuery) {
            collection.AddLink(rel, BuildGetUrl(httpContext, cleanUrl, page, pageSize, descriptor));
            return;
        }

        collection.AddLink(rel, cleanUrl, HalFormsTemplate.QueryMethod);
        collection.AddTemplate(rel, new HalFormsTemplate {
            Target = cleanUrl,
            Body = BuildBody(requestEcho, json!, page, pageSize, descriptor)
        });
    }

    private static string BuildGetUrl(HttpContext httpContext, string cleanUrl, int? page, int? pageSize, ExportRequest? export) {
        var query = new QueryBuilder();

        foreach (var parameter in httpContext.Request.Query) {
            if (IsReservedKey(parameter.Key)) {
                continue;
            }

            foreach (var value in parameter.Value) {
                if (value is not null) {
                    query.Add(parameter.Key, value);
                }
            }
        }

        if (page is not null) {
            query.Add("page", page.Value.ToString());
        }

        if (pageSize is > 0) {
            query.Add("pageSize", pageSize.Value.ToString());
        }

        if (export is not null) {
            query.Add("export.type", export.Type.ToString().ToLowerInvariant());
            query.Add("export.mode", export.Mode.ToString().ToLowerInvariant());
        }

        return $"{cleanUrl}{query.ToQueryString()}";
    }

    private static bool IsReservedKey(string key) =>
        key.Equals("page", StringComparison.OrdinalIgnoreCase)
        || key.Equals("pageSize", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("export.", StringComparison.OrdinalIgnoreCase)
        || key.Equals("export", StringComparison.OrdinalIgnoreCase);

    private static JsonObject BuildBody(object? requestEcho, JsonSerializerOptions json, int? page, int? pageSize, ExportRequest? export) {
        var body = requestEcho is null
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(requestEcho, requestEcho.GetType(), json)?.AsObject() ?? new JsonObject();

        var pageKey = ResolveJsonName(requestEcho, json, typeof(IPageable), nameof(IPageable.Page)) ?? "page";
        var pageSizeKey = ResolveJsonName(requestEcho, json, typeof(IPageable), nameof(IPageable.PageSize)) ?? "pageSize";
        var exportKey = ResolveJsonName(requestEcho, json, typeof(IExportable), nameof(IExportable.Export)) ?? "export";

        if (page is not null) {
            body[pageKey] = page.Value;
        }

        if (pageSize is > 0) {
            body[pageSizeKey] = pageSize.Value;
        }

        if (export is not null) {
            body[exportKey] = JsonSerializer.SerializeToNode(export, json);
        }
        else {
            body.Remove(exportKey);
        }

        return body;
    }

    private static string? ResolveJsonName(object? requestEcho, JsonSerializerOptions json, Type contract, string clrPropertyName) {
        if (requestEcho is null || !contract.IsInstanceOfType(requestEcho)) {
            return null;
        }

        foreach (var property in json.GetTypeInfo(requestEcho.GetType()).Properties) {
            if (property.AttributeProvider is PropertyInfo info && string.Equals(info.Name, clrPropertyName, StringComparison.Ordinal)) {
                return property.Name;
            }
        }

        return null;
    }

    private static JsonSerializerOptions ResolveJsonOptions(HttpContext httpContext) =>
        httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.JsonSerializerOptions ?? FallbackJson;

    internal static bool IsNavigationRel(string rel) => NavigationRels.Contains(rel, StringComparer.Ordinal);
}
