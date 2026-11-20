using WebSocketNFCeService;
using WebSocketNFCeService.Infra.Configuration;
using WebSocketNFCeService.Infra.Services;
using WebSocketNFCeService.NFe.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "AZFoodWebSocketNFCe";
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventLog(options =>
{
    options.SourceName = "AZFoodWebSocketNFCe";
    options.LogName = "Application";
});

builder.Services.Configure<NFeConfig>(
    builder.Configuration.GetSection(NFeConfig.SectionName));
builder.Services.Configure<WebSocketConfig>(
    builder.Configuration.GetSection(WebSocketConfig.SectionName));

builder.Services.AddSingleton<ConfigPersistenceService>();
builder.Services.AddSingleton<INFeService, NFeService>();
builder.Services.AddSingleton<INFePdfService, NFePdfService>();
builder.Services.AddSingleton<NFeWebSocketHandler>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

var wsConfig = app.Configuration
    .GetSection(WebSocketConfig.SectionName)
    .Get<WebSocketConfig>() ?? new WebSocketConfig();

// NFC-e e NF-e são multiplexadas pelo campo "tipo" da mensagem, mas o
// electron-print abre duas conexões distintas: /ws/nfce e /ws/nfe. Por isso
// registramos o MESMO handler nos dois caminhos — quem decide entre modelo 55
// e 65 é a mensagem recebida (tipo "nfe"/"nfce").
Func<HttpContext, Task> handleWebSocket = async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var handler = context.RequestServices
            .GetRequiredService<NFeWebSocketHandler>();
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        // A parada do servico entra SEPARADA do RequestAborted: ela corta so a
        // espera por mensagem, nunca uma nota em processamento. Ver HandleAsync.
        await handler.HandleAsync(webSocket, context.RequestAborted, app.Lifetime.ApplicationStopping);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket required");
    }
};

app.Map(wsConfig.Path, handleWebSocket);   // /ws/nfce (NFC-e — modelo 65)
app.Map("/ws/nfe", handleWebSocket);       // /ws/nfe  (NF-e  — modelo 55)

// A versao sai do assembly, e nao de uma constante escrita a mao: constante em
// codigo mente na primeira vez que alguem esquece de atualizar.
var versaoServico = System.Reflection.Assembly.GetExecutingAssembly()
    .GetName().Version?.ToString() ?? "0.0.0";

// RECURSOS, e nao so a versao. O electron-print reporta os dele; este reporta os
// deste servico -- os dois sao instalados juntos, mas nada garante que estejam
// na mesma versao, e quem emite a nota e este.
string[] recursosDoServico =
[
    "nfce",       // modelo 65
    "nfe",        // modelo 55
    "lote",       // grupo rastro por item
    "transporte", // transp: modalidade, transportadora, veiculo, volumes
    "cobranca",   // cobr: fatura + duplicatas
    "infAdic",
    "danfeA4",
    "ibscbs",     // Reforma Tributaria
    "eventos",    // cancelar, cartaCorrecao, consultar, inutilizar, statusServico
    "assinadas",  // lista as notas assinadas do mes (recuperar resposta perdida)
    "guardar",    // grava aqui o XML autorizado e o PDF de uma nota que o AZFood tem
    "codigoNumerico", // usa o cNF que o AZFood manda (a chave fica conhecida antes do envio)
    "assinadaNoAzfood", // manda a nota assinada ao AZFood antes do envio e a aceita de volta na consulta
    "pdfDoAzfood", // grava na pasta o PDF que o AZFood desenha (identico ao da opcao DANFE)
    "notasRecebidas", // distribuicao de DF-e (notas emitidas contra o CNPJ) e ciencia da operacao
];

// A VALIDADE do certificado vai no health porque o painel ja pergunta por ele a
// cada 30s: o aviso de vencimento sai de graca, sem chamada nova. So a data --
// nada que identifique ou abra o certificado, porque este endereco e publico.
object HealthPayload(string wsPath)
{
    var validade = app.Services.GetRequiredService<INFeService>().ValidadeDoCertificado();
    return new
    {
        status = "running",
        service = "AZFoodWebSocketNFCe",
        versao = versaoServico,
        recursos = recursosDoServico,
        wsEndpoint = $"ws://localhost:{wsConfig.Porta}{wsPath}",
        certificado = validade is null ? null : new
        {
            validade = validade.Value.ToString("yyyy-MM-dd"),
            diasRestantes = (int)Math.Floor((validade.Value - DateTime.Now).TotalDays),
        },
    };
}

app.MapGet("/health", () => Results.Ok(HealthPayload(wsConfig.Path)));
app.MapGet("/health-nfe", () => Results.Ok(HealthPayload("/ws/nfe")));

// LOOPBACK, não 0.0.0.0.
//
// Em 0.0.0.0 o serviço atendia em TODAS as interfaces da máquina — Ethernet,
// Wi-Fi, Hyper-V, VPN. Qualquer aparelho na rede do salão abria
// http://<ip-da-maquina>:5000/ws/nfce e emitia NFC-e no CNPJ do restaurante:
// sem token, sem passar pelo electron-print, sem passar pela Cloudflare. Era o
// último caminho que escapava da autenticação.
//
// Quem consome este serviço é o electron-print, que conecta em
// ws://127.0.0.1:5000/ws/nfce (endereço fixo no main.js dele). Os dois sempre
// moram na mesma máquina, então nada legítimo vem de fora e nada quebra.
//
// Vale mais que regra de firewall: com o socket fora da interface, o sistema
// recusa a conexão na origem. Não é filtro que alguém desabilita sem querer.
app.Urls.Add($"http://127.0.0.1:{wsConfig.Porta}");

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var loggerProgram = loggerFactory.CreateLogger<Program>();

try
{
    app.Run();
}
catch (Exception ex)
{
    loggerProgram.LogCritical(ex, "Falha ao iniciar serviço");
    throw;
}
