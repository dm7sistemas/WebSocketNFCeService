using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using DFe.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NFe.Classes;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Servicos.Tipos;
using NFe.Servicos;
using NFe.Utils;
using NFe.Utils.Consulta;
using NFe.Utils.NFe;
using NFe.Utils.InformacoesSuplementares;
using WebSocketNFCeService.Domain.Models;
using WebSocketNFCeService.Infra.Configuration;
using WebSocketNFCeService.Infra.Services;
using WebSocketNFCeService.NFe.Mappers;
using NFeNFe = global::NFe.Classes.NFe;

namespace WebSocketNFCeService.NFe.Services;

public class NFeService : INFeService
{
    private static readonly HttpClient _httpClient = new();

    private readonly ILogger<NFeService> _logger;
    private readonly ConfigPersistenceService _configPersistence;
    private readonly NFeConfig _fallbackConfig;
    private readonly INFePdfService _pdfService;

    public NFeService(
        ConfigPersistenceService configPersistence,
        IOptions<NFeConfig> fallbackConfig,
        ILogger<NFeService> logger,
        INFePdfService pdfService)
    {
        _configPersistence = configPersistence;
        _fallbackConfig = fallbackConfig.Value;
        _logger = logger;
        _pdfService = pdfService;
    }

    public Task<ResultadoNFe> EmitirAsync(PedidoNFe pedido, CancellationToken ct = default)
    {
        var resultado = new ResultadoNFe { Id = pedido.Id };

        try
        {
            var configuracao = CriarConfiguracao(pedido);
            var nfe = PedidoToNFeMapper.Mapear(pedido);

            foreach (var pag in nfe.infNFe.pag)
            {
                foreach (var dp in pag.detPag)
                {
                    _logger.LogInformation("PAG: tPag={Tipo} vPag={Valor} card={Card}",
                        (int)dp.tPag, dp.vPag, dp.card != null ? $"CNPJ={dp.card.CNPJ} tBand={dp.card.tBand}" : "null");
                }
                _logger.LogInformation("TROCO: vTroco={Troco}", pag.vTroco);
            }

            const string homologacaoNome = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
            if (configuracao.tpAmb == TipoAmbiente.Homologacao && nfe.infNFe.dest != null)
                nfe.infNFe.dest.xNome = homologacaoNome;

            var dadosChave = ChaveFiscal.ObterChave(
                configuracao.cUF,
                nfe.infNFe.ide.dhEmi,
                pedido.Emitente!.Cnpj,
                pedido.NFe!.Modelo == 55 ? ModeloDocumento.NFe : ModeloDocumento.NFCe,
                nfe.infNFe.ide.serie,
                nfe.infNFe.ide.nNF,
                (int)nfe.infNFe.ide.tpEmis,
                int.Parse(nfe.infNFe.ide.cNF));

            var chave = dadosChave.Chave;
            nfe.infNFe.ide.cDV = dadosChave.DigitoVerificador;
            var idLote = Convert.ToInt32(nfe.infNFe.ide.nNF);

            var docName = pedido.NFe!.Modelo == 55 ? "NFe" : "NFCe";
            _logger.LogInformation("Emitindo {DocName} {Chave} - Ambiente: {Ambiente}",
                docName, chave, pedido.Ambiente);

            var certificado = CarregarCertificado();
            if (certificado != null)
            {
                nfe.Assina(configuracao, certificado);
            }
            else if (configuracao.Certificado != null)
            {
                nfe.Assina(configuracao);
            }
            else
            {
                throw new InvalidOperationException("Nenhum certificado configurado. Envie setup com certificado ou configure no appsettings.");
            }

            // QR Code / infNFeSupl é exclusivo da NFC-e (modelo 65). Na NF-e
            // (modelo 55) esse grupo é inválido e provoca rejeição/erro de schema.
            if (pedido.NFe!.Modelo != 55)
            {
                var csc = ObterCsc();
                var qrCodeVersao = VersaoQrCode.QrCodeVersao2;
                nfe.infNFeSupl = new infNFeSupl();
                nfe.infNFeSupl.urlChave = nfe.infNFeSupl.ObterUrlConsulta(nfe, qrCodeVersao);
                nfe.infNFeSupl.qrCode = nfe.infNFeSupl.ObterUrlQrCode(nfe, qrCodeVersao,
                    csc.IdToken, csc.Token, null);
            }

            nfe.Valida(configuracao);

            // A nota assinada fica guardada ANTES de ir para a SEFAZ. Se a
            // resposta se perder no caminho, a SEFAZ autorizou e ninguem ficou
            // sabendo; a proxima tentativa cai em duplicidade, e e com este
            // arquivo que ela recupera a nota em vez de travar o numero.
            SalvarAssinada(nfe, chave, pedido.Id, configuracao.DiretorioSalvarXml);

            using var servicoNFe = new ServicosNFe(configuracao, certificado!);
            var retorno = servicoNFe.NFeAutorizacao(
                idLote,
                IndicadorSincronizacao.Sincrono,
                new List<NFeNFe> { nfe },
                compactarMensagem: false);

            var cStat = retorno.Retorno.cStat;
            var xMotivo = retorno.Retorno.xMotivo;

            if (retorno.Retorno.protNFe != null)
            {
                var prot = retorno.Retorno.protNFe;
                cStat = prot.infProt.cStat;
                xMotivo = prot.infProt.xMotivo;

                resultado.NumeroProtocolo = prot.infProt.nProt;

                if (NfeSituacao.Autorizada(cStat))
                {
                    var procNfeXml = GerarProcNFeXml(nfe, chave, prot.infProt);

                    resultado.Sucesso = true;
                    resultado.ChaveAcesso = chave;
                    resultado.NumeroNFe = (int?)nfe.infNFe.ide.nNF;
                    resultado.Serie = nfe.infNFe.ide.serie;
                    resultado.XmlAutorizado = procNfeXml;

                    _logger.LogInformation("{DocName} autorizada: {Chave} - Protocolo: {Prot}",
                        docName, chave, prot.infProt.nProt);

                    SalvarXmlLocal(procNfeXml, chave, configuracao.DiretorioSalvarXml);
                    var dirPdf = configuracao.DiretorioSalvarXml?.Replace("Xmls", "Pdfs");
                    _pdfService.Gerar(chave, nfe, prot.infProt.nProt, dirPdf);
                    EnviarXmlParaNuvem(procNfeXml, chave);
                }
            }

            // 204 = esta mesma chave ja foi autorizada; 539 = o NUMERO ja foi
            // autorizado com outra chave (o cNF e sorteado a cada tentativa).
            // As duas sao o retrato da resposta perdida: a tentativa anterior
            // deu certo. Se a nota anterior e DESTE pedido, ela e a resposta.
            if (!resultado.Sucesso && cStat is 204 or 539)
            {
                var chaveAnterior = ChaveNoMotivo(xMotivo) ?? chave;
                var recuperada = RecuperarAutorizada(chaveAnterior, pedido.Id, servicoNFe);
                if (recuperada != null)
                {
                    var (nfeAnterior, infProt) = recuperada.Value;
                    var procNfeXml = GerarProcNFeXml(nfeAnterior, chaveAnterior, infProt);
                    resultado.Sucesso = true;
                    resultado.ChaveAcesso = chaveAnterior;
                    resultado.NumeroProtocolo = infProt.nProt;
                    resultado.NumeroNFe = (int?)nfeAnterior.infNFe.ide.nNF;
                    resultado.Serie = nfeAnterior.infNFe.ide.serie;
                    resultado.XmlAutorizado = procNfeXml;
                    cStat = infProt.cStat;
                    xMotivo = infProt.xMotivo;

                    _logger.LogWarning("{DocName} {Chave} recuperada: ja estava autorizada (resposta anterior perdida)",
                        docName, chaveAnterior);

                    SalvarXmlLocal(procNfeXml, chaveAnterior, configuracao.DiretorioSalvarXml);
                    _pdfService.Gerar(chaveAnterior, nfeAnterior, infProt.nProt, configuracao.DiretorioSalvarXml?.Replace("Xmls", "Pdfs"));
                    EnviarXmlParaNuvem(procNfeXml, chaveAnterior);
                }
                else
                {
                    // Nota de OUTRO pedido (ou sem o arquivo assinado): assumir
                    // seria dar a este pedido os itens de outra venda.
                    xMotivo += $" -- o numero ja pertence a nota {chaveAnterior}, que nao foi emitida para este pedido. Consulte-a pela chave antes de emitir de novo.";
                }
            }

            resultado.CodigoStatus = cStat;
            resultado.Motivo = xMotivo;

            if (!resultado.Sucesso)
            {
                resultado.Erros.Add(new ErroNFe
                {
                    Codigo = cStat,
                    Mensagem = xMotivo
                });

                _logger.LogWarning("{DocName} rejeitada: {Chave} - Status {Status}: {Motivo}",
                    docName, chave, cStat, xMotivo);
            }
        }
        catch (Exception ex)
        {
            var docNameCatch = pedido.NFe?.Modelo == 55 ? "NFe" : "NFCe";
            _logger.LogError(ex, "Erro ao emitir {DocName} {Id}", docNameCatch, pedido.Id);
            resultado.Sucesso = false;
            resultado.Motivo = ex.Message;
            resultado.Erros.Add(new ErroNFe
            {
                Codigo = -1,
                Mensagem = ex.Message
            });
        }

        return Task.FromResult(resultado);
    }

    private ConfiguracaoServico CriarConfiguracao(PedidoNFe pedido) =>
        CriarConfiguracao(PedidoToNFeMapper.ObterEstado(pedido.Emitente!.Uf), pedido.Ambiente, pedido.NFe!.Modelo);

    // Sem PedidoNFe: cancelar, consultar e inutilizar nao tem nota montada na mao,
    // so UF, ambiente e modelo -- que e tudo o que a configuracao usa.
    private ConfiguracaoServico CriarConfiguracao(Estado estado, string ambiente, int modelo)
    {
        var setup = _configPersistence.Config;

        var config = new ConfiguracaoServico
        {
            cUF = estado,
            tpAmb = ambiente.Equals("producao", StringComparison.OrdinalIgnoreCase)
                ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            tpEmis = TipoEmissao.teNormal,
            TimeOut = setup?.Timeout ?? _fallbackConfig.Timeout,
            VersaoNFeAutorizacao = VersaoServico.Versao400,
            // Os eventos e a inutilizacao tem versao propria no Zeus; sem dizer,
            // ele nao acha a URL do servico.
            VersaoRecepcaoEventoCceCancelamento = VersaoServico.Versao400,
            VersaoNfeConsultaProtocolo = VersaoServico.Versao400,
            VersaoNfeInutilizacao = VersaoServico.Versao400,
            VersaoNfeStatusServico = VersaoServico.Versao400,
            ModeloDocumento = modelo == 55 ? ModeloDocumento.NFe : ModeloDocumento.NFCe,
            DiretorioSchemas = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Schemas"),
            DiretorioSalvarXml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Xmls", DateTime.Now.ToString("yyyyMM")),
            SalvarXmlServicos = true,
        };

        var arquivo = setup?.Certificado?.Arquivo ?? _fallbackConfig.Certificado.Arquivo;
        if (!string.IsNullOrEmpty(arquivo))
        {
            config.Certificado = new ConfiguracaoCertificado
            {
                TipoCertificado = TipoCertificado.A1Arquivo,
                Arquivo = arquivo,
                Senha = setup?.Certificado?.Senha ?? _fallbackConfig.Certificado.Senha
            };
        }

        var dirXml = config.DiretorioSalvarXml;
        if (!string.IsNullOrEmpty(dirXml) && !Directory.Exists(dirXml))
            Directory.CreateDirectory(dirXml);

        return config;
    }

    private X509Certificate2? CarregarCertificado()
    {
        var setup = _configPersistence.Config;

        var caminhoCert = setup?.Certificado?.Arquivo ?? _fallbackConfig.Certificado.Arquivo;
        var senhaCert = setup?.Certificado?.Senha ?? _fallbackConfig.Certificado.Senha;
        var serial = setup?.Certificado?.Serial ?? _fallbackConfig.Certificado.Serial;

        if (!string.IsNullOrEmpty(caminhoCert) && File.Exists(caminhoCert))
        {
            _logger.LogInformation("Carregando certificado do arquivo: {Arquivo}", caminhoCert);
            return new X509Certificate2(caminhoCert, senhaCert,
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.PersistKeySet |
                X509KeyStorageFlags.Exportable);
        }

        if (!string.IsNullOrEmpty(serial))
        {
            _logger.LogInformation("Buscando certificado pelo serial: {Serial}", serial);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var certs = store.Certificates.Find(X509FindType.FindBySerialNumber, serial, false);
            if (certs.Count > 0)
            {
                _logger.LogInformation("Certificado encontrado na store: {Subject}", certs[0].Subject);
                return certs[0];
            }
            _logger.LogWarning("Certificado com serial {Serial} não encontrado na store", serial);
        }

        return null;
    }

    private CscSetup ObterCsc()
    {
        var setup = _configPersistence.Config;
        if (setup?.Csc != null && !string.IsNullOrEmpty(setup.Csc.IdToken))
            return setup.Csc;

        return new CscSetup
        {
            IdToken = _fallbackConfig.CSC.IdToken,
            Token = _fallbackConfig.CSC.Token
        };
    }

    private void EnviarXmlParaNuvem(string xml, string chave)
    {
        var apiUrl = _fallbackConfig.CloudApi.Url;
        var apiKey = _fallbackConfig.CloudApi.ApiKey;

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("Cloud API não configurada. XML não enviado para nuvem.");
            return;
        }

        try
        {
            using var content = new StringContent(xml, Encoding.UTF8, MediaTypeNames.Text.Xml);
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = content
            };
            request.Headers.Add("X-API-Key", apiKey);

            var response = _httpClient.Send(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("XML enviado para nuvem com sucesso: {Chave}", chave);
            }
            else
            {
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                _logger.LogWarning("Falha ao enviar XML para nuvem: {Chave} - Status: {Status} - {Body}",
                    chave, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar XML para nuvem: {Chave}", chave);
        }
    }

    private static string GerarProcNFeXml(NFeNFe nfe, string chave, global::NFe.Classes.Protocolo.infProt infProt)
    {
        var nfeXml = nfe.ObterXmlString();
        var nfeDoc = XDocument.Parse(nfeXml);

        var protDoc = new XDocument(
            new XElement(XName.Get("procNFe", "http://www.portalfiscal.inf.br/nfe"),
                new XAttribute("versao", "4.00"),
                new XElement(XName.Get("NFe", "http://www.portalfiscal.inf.br/nfe"),
                    nfeDoc.Root!.Elements()
                ),
                new XElement(XName.Get("protNFe", "http://www.portalfiscal.inf.br/nfe"),
                    new XAttribute("versao", "4.00"),
                    new XElement(XName.Get("infProt", "http://www.portalfiscal.inf.br/nfe"),
                        new XAttribute("Id", $"ID{infProt.nProt}"),
                        new XAttribute("tpAmb", (int)infProt.tpAmb),
                        new XAttribute("verAplic", infProt.verAplic ?? ""),
                        new XAttribute("chNFe", chave),
                        new XAttribute("dhRecbto", infProt.dhRecbto.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                        new XAttribute("nProt", infProt.nProt ?? ""),
                        new XAttribute("digVal", infProt.digVal ?? ""),
                        new XAttribute("cStat", infProt.cStat),
                        new XAttribute("xMotivo", infProt.xMotivo ?? "")
                    )
                )
            )
        );

        return protDoc.Declaration + Environment.NewLine + protDoc;
    }

    private void SalvarXmlLocal(string xml, string chave, string? diretorio)
    {
        if (string.IsNullOrWhiteSpace(diretorio))
        {
            _logger.LogWarning("Diretório de salvamento não configurado. XML não salvo.");
            return;
        }

        try
        {
            if (!Directory.Exists(diretorio))
                Directory.CreateDirectory(diretorio);

            var caminho = Path.Combine(diretorio, $"{chave}-nfe.xml");
            File.WriteAllText(caminho, xml);
            _logger.LogInformation("XML salvo em: {Caminho}", caminho);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar XML local: {Chave}", chave);
        }
    }

    // =======================================================================
    // Depois da emissao: cancelar, corrigir, consultar, inutilizar, status.
    // =======================================================================

    // 135 = evento registrado; 136 = registrado, aguardando vinculacao;
    // 155 = cancelamento homologado fora de prazo. Os tres valem.
    private static readonly int[] EventoRegistrado = { 135, 136, 155 };

    public ResultadoEventoFiscal Cancelar(PedidoEventoFiscal pedido) =>
        Executar(pedido, (servico, config, chave) =>
        {
            var retorno = servico.RecepcaoEventoCancelamento(
                IdLote(), 1, SoDigitos(pedido.Protocolo), chave, pedido.Justificativa!, CnpjDaChave(chave));
            var resultado = ResultadoDoEvento(retorno, chave, "cancelamento", 1, config);

            // 573 = duplicidade de evento: a nota JA estava cancelada -- a resposta
            // do cancelamento anterior se perdeu. Sem isto o pedido ficaria para
            // sempre "emitida" no AZFood, e cada tentativa nova daria o mesmo 573.
            if (!resultado.Sucesso && resultado.CodigoStatus == 573)
            {
                var consulta = ConsultarCom(servico, chave);
                if (consulta.Situacao == "cancelada")
                {
                    resultado.Sucesso = true;
                    resultado.Motivo = "A nota já estava cancelada na SEFAZ.";
                    resultado.Xml = consulta.Eventos.FirstOrDefault(x => x.Contains("110111"));
                    resultado.Erros.Clear();
                }
            }
            return resultado;
        });

    public ResultadoEventoFiscal CartaCorrecao(PedidoEventoFiscal pedido) =>
        Executar(pedido, (servico, config, chave) =>
        {
            var retorno = servico.RecepcaoEventoCartaCorrecao(
                IdLote(), pedido.Sequencia, chave, pedido.Correcao!, CnpjDaChave(chave));
            return ResultadoDoEvento(retorno, chave, "cce", pedido.Sequencia, config);
        });

    public ResultadoEventoFiscal Consultar(PedidoEventoFiscal pedido) =>
        Executar(pedido, (servico, _, chave) => ConsultarCom(servico, chave));

    public ResultadoEventoFiscal Inutilizar(PedidoEventoFiscal pedido) =>
        Executar(pedido, (servico, config, _) =>
        {
            var cnpj = SoDigitos(pedido.Cnpj);
            // O ano vai com DOIS digitos: e assim que ele entra no Id da inutilizacao.
            var retorno = servico.NfeInutilizacao(cnpj, DateTime.Now.Year % 100, config.ModeloDocumento,
                pedido.Serie, pedido.NumeroInicial, pedido.NumeroFinal, pedido.Justificativa!);
            var inf = retorno.Retorno.infInut;
            var resultado = new ResultadoEventoFiscal
            {
                CodigoStatus = inf.cStat,
                Motivo = inf.xMotivo,
                Protocolo = inf.nProt,
                Sucesso = inf.cStat == 102,
            };
            if (resultado.Sucesso)
            {
                // procInutNFe = o pedido assinado + a resposta. As duas strings ja
                // vem prontas do Zeus; reserializar mexeria na assinatura.
                var ns = XNamespace.Get("http://www.portalfiscal.inf.br/nfe");
                resultado.Xml = new XElement(ns + "procInutNFe", new XAttribute("versao", "4.00"),
                        XDocument.Parse(retorno.EnvioStr, LoadOptions.PreserveWhitespace).Root,
                        XDocument.Parse(retorno.RetornoStr, LoadOptions.PreserveWhitespace).Root)
                    .ToString(SaveOptions.DisableFormatting);
                SalvarArquivo(config.DiretorioSalvarXml,
                    $"inut-{cnpj}-{(int)config.ModeloDocumento}-{pedido.Serie}-{pedido.NumeroInicial}-{pedido.NumeroFinal}.xml",
                    resultado.Xml);
            }
            return resultado;
        });

    public ResultadoEventoFiscal StatusServico(PedidoEventoFiscal pedido) =>
        Executar(pedido, (servico, _, _) =>
        {
            var ret = servico.NfeStatusServico().Retorno;
            return new ResultadoEventoFiscal
            {
                CodigoStatus = ret.cStat,
                Motivo = ret.xMotivo,
                Sucesso = ret.cStat == 107, // 107 = servico em operacao
                TempoMedio = ret.tMed,
            };
        });

    // O painel do AZFood pergunta a cada 30s pelo /health. Abrir o PFX a cada
    // pergunta e caro -- e com PersistKeySet deixaria um arquivo de chave no
    // disco por leitura. Por isso EphemeralKeySet e uma hora de cache, que
    // recomeca se o arquivo do certificado mudar.
    private (DateTime quando, string origem, DateTime? validade)? _validade;

    public DateTime? ValidadeDoCertificado()
    {
        var setup = _configPersistence.Config;
        var arquivo = setup?.Certificado?.Arquivo ?? _fallbackConfig.Certificado.Arquivo;
        var senha = setup?.Certificado?.Senha ?? _fallbackConfig.Certificado.Senha;
        var serial = setup?.Certificado?.Serial ?? _fallbackConfig.Certificado.Serial;
        var origem = $"{arquivo}|{serial}";

        if (_validade is { } c && c.origem == origem && DateTime.UtcNow - c.quando < TimeSpan.FromHours(1))
            return c.validade;

        DateTime? validade = null;
        try
        {
            if (!string.IsNullOrEmpty(arquivo) && File.Exists(arquivo))
            {
                using var cert = new X509Certificate2(arquivo, senha, X509KeyStorageFlags.EphemeralKeySet);
                validade = cert.NotAfter;
            }
            else if (!string.IsNullOrEmpty(serial))
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindBySerialNumber, serial, false);
                if (certs.Count > 0) validade = certs[0].NotAfter;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel ler a validade do certificado");
        }

        _validade = (DateTime.UtcNow, origem, validade);
        return validade;
    }

    // UF e modelo saem da CHAVE quando ha chave -- e o que a SEFAZ confere.
    private ResultadoEventoFiscal Executar(
        PedidoEventoFiscal pedido,
        Func<ServicosNFe, ConfiguracaoServico, string, ResultadoEventoFiscal> acao)
    {
        var chave = SoDigitos(pedido.Chave);
        try
        {
            var temChave = chave.Length == 44;
            var estado = temChave ? (Estado)int.Parse(chave[..2]) : PedidoToNFeMapper.ObterEstado(pedido.Uf ?? "");
            var modelo = temChave ? int.Parse(chave.Substring(20, 2)) : pedido.Modelo;

            var configuracao = CriarConfiguracao(estado, pedido.Ambiente, modelo);
            var certificado = CarregarCertificado();
            if (certificado == null && configuracao.Certificado == null)
                throw new InvalidOperationException("Nenhum certificado configurado.");

            using var servico = new ServicosNFe(configuracao, certificado!);
            var resultado = acao(servico, configuracao, chave);

            resultado.Tipo = pedido.Tipo;
            resultado.Id = pedido.Id;
            if (temChave) resultado.Chave ??= chave;
            if (!resultado.Sucesso && resultado.Erros.Count == 0)
                resultado.Erros.Add(new ErroNFe { Codigo = resultado.CodigoStatus, Mensagem = resultado.Motivo });

            _logger.LogInformation("{Tipo} {Chave}: {Status} {Motivo}",
                pedido.Tipo, temChave ? chave : $"modelo {modelo}", resultado.CodigoStatus, resultado.Motivo);
            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em {Tipo} {Id}", pedido.Tipo, pedido.Id);
            return new ResultadoEventoFiscal
            {
                Tipo = pedido.Tipo,
                Id = pedido.Id,
                Chave = chave.Length == 44 ? chave : null,
                CodigoStatus = -1,
                Motivo = ex.Message,
                Erros = { new ErroNFe { Codigo = -1, Mensagem = ex.Message } },
            };
        }
    }

    private ResultadoEventoFiscal ResultadoDoEvento(
        global::NFe.Servicos.Retorno.RetornoRecepcaoEvento retorno, string chave, string rotulo, int sequencia,
        ConfiguracaoServico config)
    {
        var ev = retorno.Retorno.retEvento?.FirstOrDefault()?.infEvento;
        var resultado = new ResultadoEventoFiscal
        {
            // Sem retEvento o lote inteiro foi recusado, e o motivo esta nele.
            CodigoStatus = ev?.cStat ?? retorno.Retorno.cStat,
            Motivo = ev?.xMotivo ?? retorno.Retorno.xMotivo,
            Protocolo = ev?.nProt,
            Sucesso = ev != null && EventoRegistrado.Contains(ev.cStat),
        };
        if (resultado.Sucesso)
        {
            resultado.Xml = retorno.ProcEventosNFe?.FirstOrDefault()?.ObterXmlString();
            if (resultado.Xml != null)
                SalvarArquivo(config.DiretorioSalvarXml, $"{chave}-{rotulo}-{sequencia}.xml", resultado.Xml);
        }
        return resultado;
    }

    private ResultadoEventoFiscal ConsultarCom(ServicosNFe servico, string chave)
    {
        var ret = servico.NfeConsultaProtocolo(chave).Retorno;
        var resultado = new ResultadoEventoFiscal
        {
            CodigoStatus = ret.cStat,
            Motivo = ret.xMotivo,
            Situacao = ret.cStat switch
            {
                100 or 150 => "autorizada",
                101 or 151 or 155 => "cancelada",
                110 or 205 or 301 or 302 or 303 => "denegada",
                217 => "inexistente",
                _ => "outra",
            },
            Protocolo = ret.protNFe?.infProt?.nProt,
            Eventos = ret.procEventoNFe?.Select(p => p.ObterXmlString()).ToList() ?? new List<string>(),
        };
        resultado.Sucesso = resultado.Situacao != "outra";

        // O procNFe, se este servico ainda tem a nota: primeiro o ja montado na
        // autorizacao, depois a assinada guardada antes do envio.
        if (ret.protNFe?.infProt != null && NfeSituacao.Autorizada(ret.protNFe.infProt.cStat))
        {
            var pronto = ProcurarXml($"{chave}-nfe.xml");
            if (pronto != null)
            {
                resultado.XmlAutorizado = File.ReadAllText(pronto);
            }
            else if (ProcurarXml($"{chave}-assinada-*.xml") is { } assinada)
            {
                var nfe = new NFeNFe().CarregarDeXmlString(File.ReadAllText(assinada));
                resultado.XmlAutorizado = GerarProcNFeXml(nfe, chave, ret.protNFe.infProt);
            }
        }
        return resultado;
    }

    // So recupera a nota que foi assinada PARA ESTE PEDIDO -- o id do pedido
    // esta no nome do arquivo. Nota de outra venda com o mesmo numero nao e
    // resposta deste pedido, por mais que a SEFAZ diga "duplicidade".
    private (NFeNFe nfe, global::NFe.Classes.Protocolo.infProt infProt)? RecuperarAutorizada(
        string chave, string pedidoId, ServicosNFe servico)
    {
        if (string.IsNullOrWhiteSpace(pedidoId)) return null;
        try
        {
            var arquivo = ProcurarXml($"{chave}-assinada-{NomeSeguro(pedidoId)}.xml");
            if (arquivo == null) return null;

            var ret = servico.NfeConsultaProtocolo(chave).Retorno;
            if (ret.cStat != 100 || ret.protNFe?.infProt == null) return null;

            return (new NFeNFe().CarregarDeXmlString(File.ReadAllText(arquivo)), ret.protNFe.infProt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao recuperar a nota {Chave}", chave);
            return null;
        }
    }

    private void SalvarAssinada(NFeNFe nfe, string chave, string pedidoId, string? diretorio) =>
        SalvarArquivo(diretorio, $"{chave}-assinada-{NomeSeguro(pedidoId)}.xml", nfe.ObterXmlString());

    private void SalvarArquivo(string? diretorio, string nome, string conteudo)
    {
        if (string.IsNullOrWhiteSpace(diretorio)) return;
        try
        {
            Directory.CreateDirectory(diretorio);
            File.WriteAllText(Path.Combine(diretorio, nome), conteudo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao salvar {Nome}", nome);
        }
    }

    private static string? ProcurarXml(string padrao)
    {
        var raiz = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Xmls");
        return Directory.Exists(raiz)
            ? Directory.EnumerateFiles(raiz, padrao, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    internal static string? ChaveNoMotivo(string? motivo)
    {
        var m = Regex.Match(motivo ?? "", @"chNFe:\s*(\d{44})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string SoDigitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    private static string CnpjDaChave(string chave) => chave.Substring(6, 14);

    private static string NomeSeguro(string s) =>
        new((s ?? "").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    // idLote so precisa ser unico por envio; o numero da nota (usado na
    // emissao) nao existe aqui.
    private static int IdLote() => (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue);
}
