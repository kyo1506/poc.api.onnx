namespace POC.Api.Onnx.Middleware;

/// <summary>
/// Middleware para autenticação via ApiKey no header X-API-Key
/// </summary>
public class ApiKeyAuthMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<ApiKeyAuthMiddleware> logger
)
{
    private readonly RequestDelegate _next = next;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger = logger;
    private const string API_KEY_HEADER = "X-API-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        // Ignorar autenticação para endpoints públicos
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (
            path == "/"
            || path.StartsWith("/swagger")
            || path.StartsWith("/scalar")
            || path.StartsWith("/openapi")
            || path == "/favicon.ico"
        )
        {
            await _next(context);
            return;
        }

        // Verificar se o header X-API-Key está presente
        if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedApiKey))
        {
            _logger.LogWarning("Tentativa de acesso sem ApiKey: {Path}", context.Request.Path);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(
                new { error = "ApiKey ausente. Forneça o header X-API-Key" }
            );
            return;
        }

        // Buscar ApiKey configurada
        var validApiKey = _configuration["ApiKey"];
        if (string.IsNullOrEmpty(validApiKey))
        {
            _logger.LogError("ApiKey não configurada no appsettings.json");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(
                new { error = "Erro de configuração do servidor" }
            );
            return;
        }

        // Validar ApiKey
        if (!validApiKey.Equals(extractedApiKey))
        {
            _logger.LogWarning(
                "Tentativa de acesso com ApiKey inválida: {Path}",
                context.Request.Path
            );
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "ApiKey inválida" });
            return;
        }

        _logger.LogInformation("Acesso autorizado: {Path}", context.Request.Path);
        await _next(context);
    }
}
