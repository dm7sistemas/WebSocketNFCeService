using WebSocketNFCeService.Domain.Models;

namespace WebSocketNFCeService.NFe.Services;

public interface INFeService
{
    Task<ResultadoNFe> EmitirAsync(PedidoNFe pedido, CancellationToken ct = default);
}
