using NFeNFe = global::NFe.Classes.NFe;

namespace WebSocketNFCeService.NFe.Services;

public interface INFePdfService
{
    void Gerar(string chave, NFeNFe nfe, string? protocolo, string? diretorio);
}