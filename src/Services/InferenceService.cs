using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using POC.Api.Onnx.Models;

namespace POC.Api.Onnx.Services;

/// <summary>
/// Serviço de inferência ONNX para classificação de documentos legais em dois níveis.
/// N1: Classificação geral (16 tipos de documentos)
/// N2: Sub-classificação de Manifestações (4 tipos)
/// </summary>
public class InferenceService
{
    private readonly TextPreprocessingService _preprocessor;
    private readonly TfidfService _tfidfServiceN1;
    private readonly TfidfService _tfidfServiceN2;
    private readonly InferenceSession _n1Session;
    private readonly InferenceSession _n2Session;
    private readonly ILogger<InferenceService> _logger;

    // Mapeamento N1 - baseado no encoder (16 classes)
    private readonly Dictionary<long, string> _n1ClassMapping = new()
    {
        { 14, "Manifestação" },
        { 148, "Acordo" },
        { 149, "Alegações finais" },
        { 150, "Apelação" },
        { 151, "Contraminuta" },
        { 152, "Contrarrazões" },
        { 153, "Contestação" },
        { 154, "Embargos de declaração" },
        { 155, "Embargos" },
        { 156, "Impugnação" },
        { 157, "Memoriais" },
        { 158, "Recurso especial" },
        { 159, "Recurso inominado" },
        { 160, "Recurso extraordinário" },
        { 532, "Especificação de provas" },
        { 533, "Laudo pericial" },
        { 1020, "Contrarrazões de Apelação" },
        { 157492, "Recurso Ordinário" },
        { 157493, "Agravo de Petição" },
        { 157494, "Recurso Adesivo" },
        { 157496, "Razões Finais" },
    };

    // Mapeamento N2 - sub-classificação de Manifestação
    private readonly Dictionary<long, string> _n2ClassMapping = new()
    {
        { 0, "Manifestação" },
        { 1, "Especificação de provas" },
        { 2, "Memoriais" },
        { 3, "Alegações finais" },
    };

    public InferenceService(
        ILogger<InferenceService> logger,
        TextPreprocessingService preprocessor,
        TfidfService tfidfServiceN1,
        TfidfService tfidfServiceN2,
        string n1ModelPath,
        string n2ModelPath
    )
    {
        _logger = logger;
        _preprocessor = preprocessor;
        _tfidfServiceN1 = tfidfServiceN1;
        _tfidfServiceN2 = tfidfServiceN2;

        _logger.LogInformation("Carregando modelo N1 ONNX: {Path}", n1ModelPath);
        _n1Session = new InferenceSession(n1ModelPath);
        LogModelInfo(_n1Session, "N1");

        _logger.LogInformation("Carregando modelo N2 ONNX: {Path}", n2ModelPath);
        _n2Session = new InferenceSession(n2ModelPath);
        LogModelInfo(_n2Session, "N2");

        _logger.LogInformation("Modelos ONNX carregados com sucesso");
    }

    private void LogModelInfo(InferenceSession session, string modelName)
    {
        _logger.LogInformation("Modelo {Name} - Inputs:", modelName);
        foreach (var input in session.InputMetadata)
        {
            var dims =
                input.Value.Dimensions.Length > 0
                    ? string.Join(
                        ",",
                        input.Value.Dimensions.Select(d => d == -1 ? "dynamic" : d.ToString())
                    )
                    : "scalar";
            _logger.LogInformation(
                "  {Name}: [{Dims}] Type={Type}",
                input.Key,
                dims,
                input.Value.ElementDataType
            );
        }
        _logger.LogInformation("Modelo {Name} - Outputs:", modelName);
        foreach (var output in session.OutputMetadata)
        {
            var dims =
                output.Value.Dimensions.Length > 0
                    ? string.Join(
                        ",",
                        output.Value.Dimensions.Select(d => d == -1 ? "dynamic" : d.ToString())
                    )
                    : "scalar";
            _logger.LogInformation(
                "  {Name}: [{Dims}] Type={Type}",
                output.Key,
                dims,
                output.Value.ElementDataType
            );
        }
    }

    private (long predictedClass, float[] probabilities) RunInference(
        InferenceSession session,
        float[] features,
        string modelName
    )
    {
        // Criar tensor de entrada com shape [1, num_features]
        var inputTensor = new DenseTensor<float>(features, new[] { 1, features.Length });

        // Criar inputs para o modelo
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
        };

        // Executar inferência
        using var results = session.Run(inputs);

        // Extrair resultados
        var labelTensor = results.First(r => r.Name == "label").AsTensor<long>();
        var probTensor = results.First(r => r.Name == "probabilities").AsTensor<float>();

        var predictedLabel = labelTensor.GetValue(0);
        var probabilities = probTensor.ToArray();

        _logger.LogInformation(
            "{Model} - Predicted: {Class}, Prob shape: {Shape}",
            modelName,
            predictedLabel,
            string.Join(",", probTensor.Dimensions.ToArray())
        );

        return (predictedLabel, probabilities);
    }

    public CompletePredictionResponse Predict(string inputText)
    {
        try
        {
            // 1. Pré-processar texto
            var preprocessedText = _preprocessor.PreprocessText(inputText);
            _logger.LogInformation(
                "Texto pré-processado: {Length} caracteres",
                preprocessedText.Length
            );

            // 2. Aplicar TF-IDF N1
            var tfidfFeaturesN1 = _tfidfServiceN1.Transform(preprocessedText);
            _logger.LogInformation(
                "Vetor TF-IDF N1 criado: {Size} features",
                tfidfFeaturesN1.Length
            );

            // 3. Predição N1
            var (predictedClassId, probabilities) = RunInference(_n1Session, tfidfFeaturesN1, "N1");

            var n1Result = new N1PredictionOutput
            {
                ClassId = predictedClassId,
                ClassName = _n1ClassMapping.GetValueOrDefault(predictedClassId, "Desconhecido"),
                Probabilities = probabilities,
            };

            _logger.LogInformation(
                "N1 Predição: {Class} (ID: {Id})",
                n1Result.ClassName,
                n1Result.ClassId
            );

            var response = new CompletePredictionResponse
            {
                ProcessedText = preprocessedText,
                N1Result = n1Result,
            };

            // 4. Se for "Manifestação", aplicar N2
            if (n1Result.ClassName == "Manifestação")
            {
                _logger.LogInformation("Aplicando classificação N2 (Manifestação detectada)");

                // Aplicar TF-IDF N2 (usando o vetorizador específico do N2)
                var tfidfFeaturesN2 = _tfidfServiceN2.Transform(preprocessedText);
                _logger.LogInformation(
                    "Vetor TF-IDF N2 criado: {Size} features",
                    tfidfFeaturesN2.Length
                );

                var (n2PredictedClassId, n2Probabilities) = RunInference(
                    _n2Session,
                    tfidfFeaturesN2,
                    "N2"
                );

                response.N2Result = new N2PredictionOutput
                {
                    ClassId = n2PredictedClassId,
                    ClassName = _n2ClassMapping.GetValueOrDefault(
                        n2PredictedClassId,
                        "Desconhecido"
                    ),
                    Probabilities = n2Probabilities,
                };

                _logger.LogInformation(
                    "N2 Predição: {Class} (ID: {Id})",
                    response.N2Result.ClassName,
                    response.N2Result.ClassId
                );
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao executar inferência completa");
            throw;
        }
    }
}
