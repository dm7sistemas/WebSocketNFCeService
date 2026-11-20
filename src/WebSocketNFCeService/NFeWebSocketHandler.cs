using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WebSocketNFCeService.Domain.Models;
using WebSocketNFCeService.Infra.Services;
using WebSocketNFCeService.NFe.Services;

namespace WebSocketNFCeService;

public class NFeWebSocketHandler
{
    private readonly INFeService _nfeService;
    private readonly ConfigPersistenceService _configPersistence;
    private readonly ILogger<NFeWebSocketHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public NFeWebSocketHandler(
        INFeService nfeService,
        ConfigPersistenceService configPersistence,
        ILogger<NFeWebSocketHandler> logger)
    {
        _nfeService = nfeService;
        _configPersistence = configPersistence;
        _logger = logger;
    }

    // `servicoParando` corta SO A ESPERA pela proxima mensagem.
    //
    // Por que existe: o electron-print deixa duas conexoes abertas o tempo todo,
    // e este loop ficava parado no ReceiveAsync ouvindo apenas `ct`
    // (RequestAborted), que so dispara quando o CLIENTE sai. Na parada do servico
    // ninguem avisava o loop, e o Windows ficava "Parando..." os 30s inteiros do
    // ShutdownTimeout ate o .NET derrubar as conexoes a forca.
    //
    // Por que NAO vai no processamento: a resposta de uma emissao volta por
    // EnviarRespostaAsync(..., ct). Cancelar ali no meio de uma nota corta o envio
    // da chave -- nota AUTORIZADA na SEFAZ e o AZFood sem saber, e a proxima
    // emissao bate em duplicidade de numero. Por isso a mensagem ja recebida
    // segue com o `ct` original ate o fim, e so depois o loop encerra.
    public async Task HandleAsync(WebSocket webSocket, CancellationToken ct, CancellationToken servicoParando = default)
    {
        var buffer = new byte[1024 * 1024];
        var messageBuffer = new StringBuilder();
        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct, servicoParando);

        try
        {
            while (webSocket.State == WebSocketState.Open && !espera.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), espera.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Cliente desconectou");
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Encerrado",
                        CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = messageBuffer.ToString();
                        messageBuffer.Clear();

                        await ProcessarMensagemAsync(webSocket, json, ct);
                    }
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "Erro de WebSocket");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Operação cancelada");
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open ||
                webSocket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Finalizado",
                        CancellationToken.None);
                }
                catch { }
            }
        }
    }

    private async Task ProcessarMensagemAsync(
        WebSocket webSocket, string json, CancellationToken ct)
    {
        MensagemWebSocket? msgTipo;

        try
        {
            msgTipo = JsonSerializer.Deserialize<MensagemWebSocket>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON inválido recebido");
            await EnviarRespostaAsync(webSocket, new ResultadoNFe
            {
                Sucesso = false,
                Motivo = $"JSON inválido: {ex.Message}",
                Erros = { new ErroNFe { Codigo = -1, Mensagem = ex.Message } }
            }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(msgTipo?.Tipo))
        {
            await EnviarRespostaAsync(webSocket, new ResultadoNFe
            {
                Sucesso = false,
                Motivo = "Campo 'tipo' é obrigatório. Use \"setup\", \"nfe\" ou \"nfce\".",
                Erros = { new ErroNFe { Codigo = -1, Mensagem = "tipo não informado" } }
            }, ct);
            return;
        }

        switch (msgTipo.Tipo.ToLowerInvariant())
        {
            case "setup":
                await ProcessarSetupAsync(webSocket, json, ct);
                break;

            case "nfe":
            case "nfce":
                _logger.LogInformation("JSON RECEBIDO: {Json}", json);
                await ProcessarNfceAsync(webSocket, json, ct);
                break;

            case "cancelar":
            case "cartacorrecao":
            case "consultar":
            case "inutilizar":
            case "statusservico":
            case "assinadas":
            case "guardar":
            case "distribuicao":
            case "manifestar":
                _logger.LogInformation("EVENTO RECEBIDO: {Json}", json);
                await ProcessarEventoAsync(webSocket, json, ct);
                break;

            default:
                await EnviarRespostaAsync(webSocket, new ResultadoNFe
                {
                    Sucesso = false,
                    Motivo = $"Tipo de mensagem desconhecido: {msgTipo.Tipo}",
                    Erros = { new ErroNFe { Codigo = -1, Mensagem = $"Tipo desconhecido: {msgTipo.Tipo}" } }
                }, ct);
                break;
        }
    }

    private async Task ProcessarSetupAsync(
        WebSocket webSocket, string json, CancellationToken ct)
    {
        MensagemSetup? setup;

        try
        {
            setup = JsonSerializer.Deserialize<MensagemSetup>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON de setup inválido");
            await EnviarRespostaSetupAsync(webSocket, new ResultadoSetup
            {
                Status = "erro",
                Mensagem = $"JSON inválido: {ex.Message}"
            }, ct);
            return;
        }

        if (setup == null ||
            (string.IsNullOrEmpty(setup.Certificado?.Arquivo) && string.IsNullOrEmpty(setup.Certificado?.Serial)))
        {
            await EnviarRespostaSetupAsync(webSocket, new ResultadoSetup
            {
                Status = "erro",
                Mensagem = "Certificado é obrigatório (informe arquivo ou serial)"
            }, ct);
            return;
        }

        _configPersistence.Salvar(setup);
        _logger.LogInformation("Config atualizada via WebSocket setup");

        await EnviarRespostaSetupAsync(webSocket, new ResultadoSetup
        {
            Status = "ok",
            Mensagem = "Config salva com sucesso"
        }, ct);
    }

    private async Task ProcessarNfceAsync(
        WebSocket webSocket, string json, CancellationToken ct)
    {
        PedidoNFe? pedido;

        try
        {
            pedido = JsonSerializer.Deserialize<PedidoNFe>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON inválido recebido");
            await EnviarRespostaAsync(webSocket, new ResultadoNFe
            {
                Sucesso = false,
                Motivo = $"JSON inválido: {ex.Message}",
                Erros = { new ErroNFe { Codigo = -1, Mensagem = ex.Message } }
            }, ct);
            return;
        }

        if (pedido?.NFe?.ValorGorjeta > 0)
        {
            _logger.LogInformation("GORJETA: valor {Valor} adicionado como produto 99999", pedido.NFe.ValorGorjeta);
            pedido.Produtos.Add(new ProdutoNFe
            {
                Codigo = "99999",
                Descricao = "GORJETA CONCEDIDA",
                Ncm = "00000000",
                Cfop = "5102",
                Unidade = "UN",
                Quantidade = 1,
                ValorUnitario = pedido.NFe.ValorGorjeta,
                ValorTotal = pedido.NFe.ValorGorjeta,
                CstIcms = "400",
                CstPis = "99",
                CstCofins = "49"
            });
        }

        var totalProdutos = pedido?.Produtos?.Sum(p => p.ValorTotal) ?? 0;
        var totalPagamentos = pedido?.NFe?.Parcelas?.Sum(p => p.Valor) ?? pedido?.NFe?.ValorPagamento ?? 0;
        _logger.LogInformation("PEDIDO: {Id} | Ambiente: {Amb}", pedido?.Id, pedido?.Ambiente);
        _logger.LogInformation("PRODUTOS: {Qtd} itens | Total: {Total:C}", pedido?.Produtos?.Count, totalProdutos);
        _logger.LogInformation("PAGAMENTOS: {Total:C} | Troco informado: {Troco:C}",
            totalPagamentos, pedido?.NFe?.ValorTroco);
        _logger.LogInformation("NFE: Numero={Num} Serie={Ser} Gorjeta={Gorjeta:C}",
            pedido?.NFe?.Numero, pedido?.NFe?.Serie, pedido?.NFe?.ValorGorjeta);

        var erros = Validar(pedido);
        if (erros.Count > 0)
        {
            await EnviarRespostaAsync(webSocket, new ResultadoNFe
            {
                Id = pedido?.Id ?? string.Empty,
                Sucesso = false,
                Motivo = erros.First().Mensagem,
                Erros = erros
            }, ct);
            return;
        }

        var resultado = await _nfeService.EmitirAsync(pedido!, ct);
        await EnviarRespostaAsync(webSocket, resultado, ct);
    }

    private async Task ProcessarEventoAsync(
        WebSocket webSocket, string json, CancellationToken ct)
    {
        PedidoEventoFiscal? pedido;
        try
        {
            pedido = JsonSerializer.Deserialize<PedidoEventoFiscal>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            pedido = null;
            _logger.LogWarning(ex, "JSON de evento inválido");
        }

        var erros = ValidarEvento(pedido);
        if (erros.Count > 0)
        {
            await EnviarJsonAsync(webSocket, new ResultadoEventoFiscal
            {
                Tipo = pedido?.Tipo ?? string.Empty,
                Id = pedido?.Id ?? string.Empty,
                Sucesso = false,
                CodigoStatus = -1,
                Motivo = erros.First().Mensagem,
                Erros = erros
            }, ct);
            return;
        }

        var resultado = pedido!.Tipo.ToLowerInvariant() switch
        {
            "cancelar" => _nfeService.Cancelar(pedido),
            "cartacorrecao" => _nfeService.CartaCorrecao(pedido),
            "consultar" => _nfeService.Consultar(pedido),
            "inutilizar" => _nfeService.Inutilizar(pedido),
            "assinadas" => _nfeService.ListarAssinadas(pedido),
            "guardar" => _nfeService.GuardarArquivos(pedido),
            "distribuicao" => _nfeService.Distribuicao(pedido),
            "manifestar" => _nfeService.Manifestar(pedido),
            _ => _nfeService.StatusServico(pedido),
        };
        await EnviarJsonAsync(webSocket, resultado, ct);
    }

    // Barra aqui o que a SEFAZ recusaria: cada recusa conta para o bloqueio de
    // consumo indevido (656), e o texto que a SEFAZ devolve nao diz o que fazer.
    // Normaliza o texto livre ANTES de medir -- quebra de linha e espaco duplo
    // sao recusados pelo schema, e o operador digita os dois sem ver.
    internal static List<ErroNFe> ValidarEvento(PedidoEventoFiscal? pedido)
    {
        var erros = new List<ErroNFe>();
        if (pedido is null)
        {
            erros.Add(new ErroNFe { Codigo = -1, Mensagem = "Pedido de evento inválido" });
            return erros;
        }

        static string UmaLinha(string? t) => string.Join(" ", (t ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        static string Digitos(string? t) => new((t ?? "").Where(char.IsDigit).ToArray());
        pedido.Justificativa = UmaLinha(pedido.Justificativa);
        pedido.Correcao = UmaLinha(pedido.Correcao);

        var tipo = pedido.Tipo.ToLowerInvariant();
        var chave = Digitos(pedido.Chave);

        if (tipo is "distribuicao" or "manifestar")
        {
            if (string.IsNullOrWhiteSpace(pedido.Uf))
                erros.Add(new ErroNFe { Codigo = 36, Mensagem = "UF é obrigatória" });
            if (Digitos(pedido.Cnpj).Length != 14)
                erros.Add(new ErroNFe { Codigo = 38, Mensagem = "CNPJ da loja deve ter 14 dígitos" });
            if (tipo == "manifestar" && chave.Length != 44)
                erros.Add(new ErroNFe { Codigo = 30, Mensagem = "Chave de acesso deve ter 44 dígitos" });
            // Consulta por chave: ou vem a chave inteira, ou nao vem chave.
            if (tipo == "distribuicao" && chave.Length is not (0 or 44))
                erros.Add(new ErroNFe { Codigo = 30, Mensagem = "Chave de acesso deve ter 44 dígitos" });
        }

        if (tipo == "guardar" && string.IsNullOrWhiteSpace(pedido.Xml))
            erros.Add(new ErroNFe { Codigo = 42, Mensagem = "O XML da nota é obrigatório" });

        if (tipo is "cancelar" or "cartacorrecao" or "consultar" or "guardar" && chave.Length != 44)
            erros.Add(new ErroNFe { Codigo = 30, Mensagem = "Chave de acesso deve ter 44 dígitos" });

        if (tipo is "cancelar" or "inutilizar" && pedido.Justificativa.Length is < 15 or > 255)
            erros.Add(new ErroNFe { Codigo = 31, Mensagem = "A justificativa deve ter entre 15 e 255 caracteres" });

        // 15 ou 17 (schema TProt): a SEFAZ-SP devolve 17 na NFC-e (13526000014872171).
        if (tipo == "cancelar" && Digitos(pedido.Protocolo).Length is not (15 or 17))
            erros.Add(new ErroNFe { Codigo = 32, Mensagem = "Protocolo de autorização da nota deve ter 15 ou 17 dígitos" });

        if (tipo == "cartacorrecao")
        {
            // A CC-e e so da NF-e. A NFC-e nao tem carta: o conserto dela e cancelar.
            if (chave.Length == 44 && chave.Substring(20, 2) != "55")
                erros.Add(new ErroNFe { Codigo = 33, Mensagem = "Carta de correção só existe para NF-e (modelo 55)" });
            if (pedido.Correcao.Length is < 15 or > 1000)
                erros.Add(new ErroNFe { Codigo = 34, Mensagem = "A correção deve ter entre 15 e 1000 caracteres" });
            if (pedido.Sequencia is < 1 or > 20)
                erros.Add(new ErroNFe { Codigo = 35, Mensagem = "Sequência da carta de correção deve ser de 1 a 20" });
        }

        if (tipo == "assinadas")
        {
            if (Digitos(pedido.Mes).Length != 4)
                erros.Add(new ErroNFe { Codigo = 41, Mensagem = "Mês deve vir como AAMM (ex.: 2610)" });
            if (pedido.Modelo is not (55 or 65))
                erros.Add(new ErroNFe { Codigo = 37, Mensagem = "Modelo deve ser 55 ou 65" });
        }

        if (tipo is "inutilizar" or "statusservico")
        {
            if (string.IsNullOrWhiteSpace(pedido.Uf))
                erros.Add(new ErroNFe { Codigo = 36, Mensagem = "UF é obrigatória" });
            if (pedido.Modelo is not (55 or 65))
                erros.Add(new ErroNFe { Codigo = 37, Mensagem = "Modelo deve ser 55 ou 65" });
        }

        if (tipo == "inutilizar")
        {
            if (Digitos(pedido.Cnpj).Length != 14)
                erros.Add(new ErroNFe { Codigo = 38, Mensagem = "CNPJ do emitente deve ter 14 dígitos" });
            if (pedido.Serie is < 0 or > 999)
                erros.Add(new ErroNFe { Codigo = 39, Mensagem = "Série deve ser de 0 a 999" });
            if (pedido.NumeroInicial < 1 || pedido.NumeroFinal < pedido.NumeroInicial)
                erros.Add(new ErroNFe { Codigo = 40, Mensagem = "Faixa de numeração inválida" });
        }

        return erros;
    }

    private static async Task EnviarJsonAsync(WebSocket webSocket, object resposta, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(resposta, resposta.GetType(), JsonOptions));
        if (webSocket.State == WebSocketState.Open)
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static List<ErroNFe> Validar(PedidoNFe? pedido)
    {
        var erros = new List<ErroNFe>();

        if (pedido is null)
        {
            erros.Add(new ErroNFe { Codigo = -1, Mensagem = "Pedido não pode ser nulo" });
            return erros;
        }

        if (pedido.Emitente is null)
        {
            erros.Add(new ErroNFe { Codigo = 1, Mensagem = "Emitente é obrigatório" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(pedido.Emitente.Cnpj))
                erros.Add(new ErroNFe { Codigo = 2, Mensagem = "CNPJ do emitente é obrigatório" });
            if (string.IsNullOrWhiteSpace(pedido.Emitente.RazaoSocial))
                erros.Add(new ErroNFe { Codigo = 3, Mensagem = "Razão social do emitente é obrigatória" });
            if (string.IsNullOrWhiteSpace(pedido.Emitente.Uf))
                erros.Add(new ErroNFe { Codigo = 4, Mensagem = "UF do emitente é obrigatória" });
        }

        if (pedido.NFe is null)
        {
            erros.Add(new ErroNFe { Codigo = 5, Mensagem = "Dados da NFe (nfe) são obrigatórios" });
        }
        else
        {
            if (pedido.NFe.Numero <= 0)
                erros.Add(new ErroNFe { Codigo = 6, Mensagem = "Número da NFe é obrigatório" });
            if (pedido.NFe.Serie <= 0)
                erros.Add(new ErroNFe { Codigo = 7, Mensagem = "Série da NFe é obrigatória" });

            if (pedido.NFe.Modelo == 55)
            {
                if (pedido.Destinatario is null)
                {
                    erros.Add(new ErroNFe { Codigo = 15, Mensagem = "Para NFe (Modelo 55), o destinatário é obrigatório" });
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(pedido.Destinatario.Cpf) && string.IsNullOrWhiteSpace(pedido.Destinatario.Cnpj))
                        erros.Add(new ErroNFe { Codigo = 16, Mensagem = "Para NFe, CPF ou CNPJ do destinatário é obrigatório" });
                    
                    if (string.IsNullOrWhiteSpace(pedido.Destinatario.Logradouro) || 
                        string.IsNullOrWhiteSpace(pedido.Destinatario.Municipio) ||
                        pedido.Destinatario.CodigoMunicipio <= 0 ||
                        string.IsNullOrWhiteSpace(pedido.Destinatario.Uf))
                    {
                        erros.Add(new ErroNFe { Codigo = 17, Mensagem = "Para NFe, o endereço completo do destinatário (Logradouro, Municipio, CodigoMunicipio, UF) é obrigatório" });
                    }

                    // Os dois abaixo a SEFAZ tambem recusa -- mas cada recusa conta
                    // para o bloqueio de "consumo indevido" (Rejeicao 656), que no
                    // GRAVETOS em 11/09/2026 travou a emissao por mais de uma hora
                    // com o cliente esperando. Barrar aqui nao custa tentativa.
                    if (pedido.Destinatario.IndicadorIE == 1 && string.IsNullOrWhiteSpace(pedido.Destinatario.InscricaoEstadual))
                        erros.Add(new ErroNFe { Codigo = 18, Mensagem = "Destinatário marcado como contribuinte (indicadorIE 1) sem Inscrição Estadual" });

                    // Na NF-e o nome e obrigatorio; na NFC-e nao. O mapper omite a tag
                    // em branco para a NFC-e passar, e e aqui que a NF-e e barrada.
                    if (string.IsNullOrWhiteSpace(pedido.Destinatario.Nome))
                        erros.Add(new ErroNFe { Codigo = 19, Mensagem = "Para NFe, o nome do destinatário é obrigatório" });
                }
            }
        }

        // COBRANCA: opcional. Mas se vier, a soma das duplicatas tem que fechar
        // com o valor liquido da fatura -- a SEFAZ confere, e rejeicao dela
        // QUEIMA o numero da serie. Barrar aqui custa nada; barrar la custa uma
        // inutilizacao.
        if (pedido.Cobranca?.Duplicatas is { Count: > 0 } dups)
        {
            var soma = dups.Sum(d => d.Valor);
            var liquido = pedido.Cobranca.Fatura?.ValorLiquido;
            if (liquido.HasValue && Math.Abs(liquido.Value - soma) > 0.01m)
            {
                erros.Add(new ErroNFe
                {
                    Codigo = 18,
                    Mensagem = $"A soma das duplicatas ({soma:N2}) nao bate com o valor liquido da fatura ({liquido.Value:N2})"
                });
            }
            for (int i = 0; i < dups.Count; i++)
            {
                if (dups[i].Valor <= 0)
                    erros.Add(new ErroNFe { Codigo = 19, Mensagem = $"Duplicata [{i}]: valor deve ser maior que zero" });
                if (string.IsNullOrWhiteSpace(dups[i].Vencimento))
                    erros.Add(new ErroNFe { Codigo = 20, Mensagem = $"Duplicata [{i}]: vencimento e obrigatorio" });
            }
        }

        if (pedido.Produtos is null || pedido.Produtos.Count == 0)
        {
            erros.Add(new ErroNFe { Codigo = 8, Mensagem = "Pelo menos um produto é obrigatório" });
        }
        else
        {
            for (int i = 0; i < pedido.Produtos.Count; i++)
            {
                var p = pedido.Produtos[i];
                if (string.IsNullOrWhiteSpace(p.Codigo))
                    erros.Add(new ErroNFe { Codigo = 9, Mensagem = $"Produto [{i}]: código é obrigatório" });
                if (string.IsNullOrWhiteSpace(p.Descricao))
                    erros.Add(new ErroNFe { Codigo = 10, Mensagem = $"Produto [{i}]: descrição é obrigatória" });
                if (p.Quantidade <= 0)
                    erros.Add(new ErroNFe { Codigo = 11, Mensagem = $"Produto [{i}]: quantidade deve ser maior que zero" });
                if (p.ValorUnitario <= 0)
                    erros.Add(new ErroNFe { Codigo = 12, Mensagem = $"Produto [{i}]: valor unitário deve ser maior que zero" });

                // LOTE: opcional, mas INDIVISIVEL. O schema exige nLote, qLote,
                // dFab e dVal juntos; mandar so o numero do lote faz a SEFAZ
                // rejeitar a nota INTEIRA. Recusar antes e a diferenca entre
                // corrigir um campo e inutilizar um numero.
                foreach (var (lote, j) in (p.Lotes ?? new List<LoteProduto>()).Select((l, j) => (l, j)))
                {
                    if (string.IsNullOrWhiteSpace(lote.Numero))
                        erros.Add(new ErroNFe { Codigo = 21, Mensagem = $"Produto [{i}] lote [{j}]: numero do lote e obrigatorio" });
                    if (lote.Quantidade <= 0)
                        erros.Add(new ErroNFe { Codigo = 22, Mensagem = $"Produto [{i}] lote [{j}]: quantidade do lote deve ser maior que zero" });
                    if (string.IsNullOrWhiteSpace(lote.Fabricacao) || string.IsNullOrWhiteSpace(lote.Validade))
                        erros.Add(new ErroNFe { Codigo = 23, Mensagem = $"Produto [{i}] lote [{j}]: fabricacao e validade sao obrigatorias quando o lote e informado" });
                }

                var crt = pedido.Emitente?.Crt ?? 3;
                var ehSimplesNacional = crt is 1 or 2 or 4;
                if (ehSimplesNacional)
                {
                    var csosnValidos = new[] { "101", "102", "103", "201", "202", "203", "300", "400", "500", "900" };
                    if (!csosnValidos.Contains(p.CstIcms))
                        erros.Add(new ErroNFe { Codigo = 14, Mensagem = $"Produto [{i}]: CSOSN inválido para Simples Nacional (CRT {crt}). Valores: {string.Join(", ", csosnValidos)}" });
                }
            }
        }

        return erros;
    }

    private static async Task EnviarRespostaAsync(
        WebSocket webSocket, ResultadoNFe resultado, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(resultado, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                ct);
        }
    }

    private static async Task EnviarRespostaSetupAsync(
        WebSocket webSocket, ResultadoSetup resultado, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(resultado, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (webSocket.State == WebSocketState.Open)
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                ct);
        }
    }
}
