using System.ComponentModel.DataAnnotations;

namespace Aedis.ExchangeRates.Ptax;

/// <summary>Opções do provider PTAX (seção <c>ExchangeRates:Ptax</c>). Os padrões servem à API pública do Banco Central.</summary>
public sealed class PtaxOptions
{
    /// <summary>Nome da seção de configuração (<c>ExchangeRates:Ptax</c>).</summary>
    public const string SectionName = "ExchangeRates:Ptax";

    /// <summary>Raiz OData do serviço PTAX (Olinda).</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://olinda.bcb.gov.br/olinda/servico/PTAX/versao/v1/odata/";

    /// <summary>Quantos dias antes da data pedida consultar, para atravessar fins de semana e feriados prolongados.</summary>
    [Range(1, 31)]
    public int LookbackDays { get; set; } = 10;

    /// <summary>Tempo máximo de uma consulta.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
