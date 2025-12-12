using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using POC.Api.Onnx.Middleware;
using POC.Api.Onnx.Models;
using POC.Api.Onnx.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.WriteIndented = true;
});

builder.Services.AddSingleton<TextPreprocessingService>();

builder.Services.AddOpenApi(options =>
{
    // 1. Informações Gerais da API
    options.AddDocumentTransformer(
        (document, context, cancellationToken) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "API de Classificação de Documentos Legais",
                Version = "v1",
                Description =
                    "API para classificação automática de documentos jurídicos usando ONNX.",
            };

            // 2. Definir o Esquema de Segurança (API Key)
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??=
                new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes.Add(
                "ApiKey",
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-API-Key",
                    Description = "Insira sua chave de acesso aqui.",
                }
            );

            return Task.CompletedTask;
        }
    );

    // 3. Aplicar Segurança Globalmente (opcional, ou por endpoint)
    options.AddOperationTransformer(
        (operation, context, cancellationToken) =>
        {
            operation.Security ??= [];
            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("ApiKey", context.Document),
                        new List<string>()
                    },
                }
            );
            return Task.CompletedTask;
        }
    );
});

// Configurar caminhos dos modelos ( mantido original )
var contentRoot = builder.Environment.ContentRootPath;
var modelPaths = new
{
    TfidfN1 = Path.Combine(contentRoot, "docs", "Modelo_IA_Protocolo_N1", "tfidf_metadata.json"),
    TfidfN2 = Path.Combine(
        contentRoot,
        "docs",
        "Modelo_IA_Protocolo_Manifestacao_N2",
        "tfidf_metadata_manifestacao.json"
    ),
    ModelN1 = Path.Combine(
        contentRoot,
        "docs",
        "Modelo_IA_Protocolo_N1",
        "xgboost_ia_protocol_modelo_98.onnx"
    ),
    ModelN2 = Path.Combine(
        contentRoot,
        "docs",
        "Modelo_IA_Protocolo_Manifestacao_N2",
        "xgboost_ia_protocol_modelo_manifestacao.onnx"
    ),
};

// Registrar serviço de inferência
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<InferenceService>>();
    var loggerTfidf = sp.GetRequiredService<ILogger<TfidfService>>();
    var preprocessor = sp.GetRequiredService<TextPreprocessingService>();

    var tfidfServiceN1 = new TfidfService(loggerTfidf, modelPaths.TfidfN1);
    var tfidfServiceN2 = new TfidfService(loggerTfidf, modelPaths.TfidfN2);

    return new InferenceService(
        logger,
        preprocessor,
        tfidfServiceN1,
        tfidfServiceN2,
        modelPaths.ModelN1,
        modelPaths.ModelN2
    );
});

var app = builder.Build();

// 1. Gera o documento OpenAPI JSON (/openapi/v1.json)
app.MapOpenApi();

// 2. Interface Visual Scalar
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        options.Title = "Documentação - Classificação Legal";
        options.Theme = ScalarTheme.DeepSpace;
        options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);

        // Configurar autenticação ApiKey
        options.AddApiKeyAuthentication(
            "ApiKey",
            apiKey =>
            {
                apiKey.Value = "dev-api-key-12345";
            }
        );
    });
}

app.UseHttpsRedirection();

// Middleware de Autenticação (deve vir antes dos endpoints)
app.UseMiddleware<ApiKeyAuthMiddleware>();

// Endpoints
app.MapGet(
        "/",
        () =>
            Results.Ok(
                new
                {
                    service = "API de Classificação de Documentos Legais",
                    version = "1.0",
                    docs = "/scalar/v1",
                }
            )
    )
    .WithName("Root")
    .WithTags("Info");

app.MapPost(
        "/predict",
        async (
            PredictionRequest request,
            InferenceService inferenceService,
            TextPreprocessingService preprocessor
        ) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(new { error = "O campo 'text' é obrigatório" });
            }

            try
            {
                var sanitizedText = preprocessor.SanitizeText(request.Text);

                // Verificar se é texto corrompido (lixo)
                if (preprocessor.IsGarbageText(sanitizedText))
                {
                    return Results.Ok(
                        new CompletePredictionResponse
                        {
                            OriginalText = request.Text,
                            SanitizedText = sanitizedText,
                            IsClassifiable = false,
                            Message =
                                "Texto não classificável: formato inválido ou corrompido (excesso de caracteres isolados).",
                        }
                    );
                }

                if (sanitizedText.Length < 182)
                {
                    return Results.Ok(
                        new CompletePredictionResponse
                        {
                            OriginalText = request.Text,
                            SanitizedText = sanitizedText,
                            IsClassifiable = false,
                            Message =
                                $"Texto muito curto: {sanitizedText.Length} caracteres (mínimo 182).",
                        }
                    );
                }

                var result = inferenceService.Predict(request.Text);
                return Results.Ok(result);
            }
            catch (Exception exception)
            {
                // Logar exceção real aqui
                return Results.Problem(exception.Message, statusCode: 500);
            }
        }
    )
    .WithName("Predict")
    .WithTags("Classificação")
    .WithSummary("Classifica documento jurídico (JSON)")
    .WithDescription(
        "Envia texto via JSON. Requer header X-API-Key. Nota: quebras de linha devem ser escapadas como \\n no JSON."
    )
    .Produces<CompletePredictionResponse>(200)
    .Produces(400)
    .Produces(500);

app.MapPost(
        "/predict/text",
        async (
            HttpContext httpContext,
            InferenceService inferenceService,
            TextPreprocessingService preprocessor
        ) =>
        {
            using var reader = new StreamReader(httpContext.Request.Body);
            var text = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                return Results.BadRequest(new { error = "O texto é obrigatório" });
            }

            try
            {
                var sanitizedText = preprocessor.SanitizeText(text);

                // Verificar se é texto corrompido (lixo)
                if (preprocessor.IsGarbageText(sanitizedText))
                {
                    return Results.Ok(
                        new CompletePredictionResponse
                        {
                            OriginalText = text,
                            SanitizedText = sanitizedText,
                            IsClassifiable = false,
                            Message =
                                "Texto não classificável: formato inválido ou corrompido (excesso de caracteres isolados).",
                        }
                    );
                }

                if (sanitizedText.Length < 182)
                {
                    return Results.Ok(
                        new CompletePredictionResponse
                        {
                            OriginalText = text,
                            SanitizedText = sanitizedText,
                            IsClassifiable = false,
                            Message =
                                $"Texto muito curto: {sanitizedText.Length} caracteres (mínimo 182).",
                        }
                    );
                }

                var result = inferenceService.Predict(text);
                return Results.Ok(result);
            }
            catch (Exception exception)
            {
                return Results.Problem(exception.Message, statusCode: 500);
            }
        }
    )
    .WithName("PredictText")
    .WithTags("Classificação")
    .WithSummary("Classifica documento jurídico (Texto Puro)")
    .WithDescription(
        "Envia texto via text/plain no body (sem JSON). Ideal para textos com muitas quebras de linha. Requer header X-API-Key."
    )
    .Accepts<string>("text/plain")
    .Produces<CompletePredictionResponse>(200)
    .Produces(400)
    .Produces(500);

app.Run();
