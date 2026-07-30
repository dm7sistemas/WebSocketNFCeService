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

app.Map(wsConfig.Path, async (HttpContext context) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var handler = context.RequestServices
            .GetRequiredService<NFeWebSocketHandler>();
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleAsync(webSocket, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket required");
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "running",
    service = "AZFoodWebSocketNFCe",
    wsEndpoint = $"ws://localhost:{wsConfig.Porta}{wsConfig.Path}"
}));

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
