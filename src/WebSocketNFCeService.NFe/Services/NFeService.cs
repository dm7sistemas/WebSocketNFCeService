using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
                ModeloDocumento.NFCe,
                nfe.infNFe.ide.serie,
                nfe.infNFe.ide.nNF,
                (int)nfe.infNFe.ide.tpEmis,
                int.Parse(nfe.infNFe.ide.cNF));

            var chave = dadosChave.Chave;
            nfe.infNFe.ide.cDV = dadosChave.DigitoVerificador;
            var idLote = Convert.ToInt32(nfe.infNFe.ide.nNF);

            _logger.LogInformation("Emitindo NFCe {Chave} - Ambiente: {Ambiente}",
                chave, pedido.Ambiente);

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

            var csc = ObterCsc();
            var qrCodeVersao = VersaoQrCode.QrCodeVersao2;
            nfe.infNFeSupl = new infNFeSupl();
            nfe.infNFeSupl.urlChave = nfe.infNFeSupl.ObterUrlConsulta(nfe, qrCodeVersao);
            nfe.infNFeSupl.qrCode = nfe.infNFeSupl.ObterUrlQrCode(nfe, qrCodeVersao,
                csc.IdToken, csc.Token, null);

            nfe.Valida(configuracao);

            using var servicoNFe = new ServicosNFe(configuracao);
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

                    _logger.LogInformation("NFCe autorizada: {Chave} - Protocolo: {Prot}",
                        chave, prot.infProt.nProt);

                    SalvarXmlLocal(procNfeXml, chave, configuracao.DiretorioSalvarXml);
                    var dirPdf = configuracao.DiretorioSalvarXml?.Replace("Xmls", "Pdfs");
                    _pdfService.Gerar(chave, nfe, prot.infProt.nProt, dirPdf);
                    EnviarXmlParaNuvem(procNfeXml, chave);
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

                _logger.LogWarning("NFCe rejeitada: {Chave} - Status {Status}: {Motivo}",
                    chave, cStat, xMotivo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao emitir NFCe {Id}", pedido.Id);
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

    private ConfiguracaoServico CriarConfiguracao(PedidoNFe pedido)
    {
        var emitente = pedido.Emitente!;
        var estado = PedidoToNFeMapper.ObterEstado(emitente.Uf);
        var setup = _configPersistence.Config;

        var config = new ConfiguracaoServico
        {
            cUF = estado,
            tpAmb = pedido.Ambiente.Equals("producao", StringComparison.OrdinalIgnoreCase)
                ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
            tpEmis = TipoEmissao.teNormal,
            TimeOut = setup?.Timeout ?? _fallbackConfig.Timeout,
            VersaoNFeAutorizacao = VersaoServico.Versao400,
            ModeloDocumento = ModeloDocumento.NFCe,
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
}
