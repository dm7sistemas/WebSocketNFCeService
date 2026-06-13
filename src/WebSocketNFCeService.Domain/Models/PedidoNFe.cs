using System.Text.Json.Serialization;

namespace WebSocketNFCeService.Domain.Models;

public class PedidoNFe
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("ambiente")]
    public string Ambiente { get; set; } = "homologacao";

    [JsonPropertyName("emitente")]
    public Emitente? Emitente { get; set; }

    [JsonPropertyName("destinatario")]
    public Destinatario? Destinatario { get; set; }

    [JsonPropertyName("produtos")]
    public List<ProdutoNFe> Produtos { get; set; } = new();

    [JsonPropertyName("nfe")]
    public DadosNFe? NFe { get; set; }
}

public class DadosNFe
{
    [JsonPropertyName("numero")]
    public int Numero { get; set; }

    [JsonPropertyName("serie")]
    public int Serie { get; set; }

    [JsonPropertyName("naturezaOperacao")]
    public string NaturezaOperacao { get; set; } = "VENDA";

    [JsonPropertyName("finalidade")]
    public int Finalidade { get; set; } = 1;

    [JsonPropertyName("consumidorFinal")]
    public int ConsumidorFinal { get; set; } = 1;

    [JsonPropertyName("presencaComprador")]
    public int PresencaComprador { get; set; } = 1;

    [JsonPropertyName("formaPagamento")]
    public string FormaPagamento { get; set; } = "dinheiro";

    [JsonPropertyName("valorPagamento")]
    public decimal ValorPagamento { get; set; }

    [JsonPropertyName("tipoPagamento")]
    public int TipoPagamento { get; set; } = 1;

    [JsonPropertyName("valorTroco")]
    public decimal ValorTroco { get; set; }

    [JsonPropertyName("valorGorjeta")]
    public decimal ValorGorjeta { get; set; }

    [JsonPropertyName("valorDesconto")]
    public decimal ValorDesconto { get; set; }

    [JsonPropertyName("parcelas")]
    public List<Parcela>? Parcelas { get; set; }

    [JsonPropertyName("cnpjCredenciadora")]
    public string? CnpjCredenciadora { get; set; }

    [JsonPropertyName("codigoAutorizacao")]
    public string? CodigoAutorizacao { get; set; }
}

public class Parcela
{
    [JsonPropertyName("formaPagamento")]
    public string FormaPagamento { get; set; } = "dinheiro";

    [JsonPropertyName("valor")]
    public decimal Valor { get; set; }

    [JsonPropertyName("tipoPagamento")]
    public int TipoPagamento { get; set; } = 1;

    [JsonPropertyName("cnpjCredenciadora")]
    public string? CnpjCredenciadora { get; set; }

    [JsonPropertyName("bandeiraCartao")]
    public string? BandeiraCartao { get; set; }

    [JsonPropertyName("codigoAutorizacao")]
    public string? CodigoAutorizacao { get; set; }

    [JsonPropertyName("cnpjRecebedor")]
    public string? CnpjRecebedor { get; set; }

    [JsonPropertyName("terminalPagamento")]
    public string? TerminalPagamento { get; set; }
}
