using WebSocketNFCeService.Domain.Models;

namespace WebSocketNFCeService.NFe.Services;

public interface INFeService
{
    Task<ResultadoNFe> EmitirAsync(PedidoNFe pedido, CancellationToken ct = default);

    ResultadoEventoFiscal Cancelar(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal CartaCorrecao(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal Consultar(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal ListarAssinadas(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal GuardarArquivos(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal Distribuicao(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal Manifestar(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal Inutilizar(PedidoEventoFiscal pedido);
    ResultadoEventoFiscal StatusServico(PedidoEventoFiscal pedido);

    /// Validade do certificado A1 configurado, ou null se nao houver um legivel.
    DateTime? ValidadeDoCertificado();
}
