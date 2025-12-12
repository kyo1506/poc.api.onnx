using System.Text.Json;
using System.Text.Json.Serialization;

namespace POC.Api.Onnx.Services;

/// <summary>
/// Metadados do vetorizador TF-IDF carregados do JSON
/// </summary>
public class TfidfMetadata
{
    public Dictionary<string, int> Vocabulary { get; set; } = new();

    [JsonPropertyName("idf_values")]
    public List<double> IdfValues { get; set; } = new();
}

/// <summary>
/// Serviço de vetorização TF-IDF para transformar texto em features numéricas
/// </summary>
public class TfidfService
{
    private readonly TfidfMetadata _metadata;
    private readonly ILogger<TfidfService> _logger;

    public TfidfService(ILogger<TfidfService> logger, string metadataPath)
    {
        _logger = logger;

        _logger.LogInformation("Carregando metadados TF-IDF: {Path}", metadataPath);

        var json = File.ReadAllText(metadataPath);
        _metadata =
            JsonSerializer.Deserialize<TfidfMetadata>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? throw new InvalidOperationException("Falha ao carregar metadados TF-IDF");

        _logger.LogInformation(
            "TF-IDF carregado: {VocabSize} termos, {IdfSize} valores IDF",
            _metadata.Vocabulary.Count,
            _metadata.IdfValues.Count
        );

        if (_metadata.Vocabulary.Count != _metadata.IdfValues.Count)
        {
            throw new InvalidOperationException(
                $"Inconsistência nos metadados: {_metadata.Vocabulary.Count} termos no vocabulário vs {_metadata.IdfValues.Count} valores IDF"
            );
        }
    }

    public float[] Transform(string preprocessedText)
    {
        var words = preprocessedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var termFrequency = new Dictionary<string, int>();

        // Calcular frequência dos termos
        foreach (var word in words)
        {
            if (_metadata.Vocabulary.ContainsKey(word))
            {
                termFrequency[word] = termFrequency.GetValueOrDefault(word, 0) + 1;
            }
        }

        // Criar vetor TF-IDF
        var tfidfVector = new float[_metadata.Vocabulary.Count];

        foreach (var (term, index) in _metadata.Vocabulary)
        {
            if (termFrequency.ContainsKey(term))
            {
                if (index < 0 || index >= _metadata.IdfValues.Count)
                {
                    _logger.LogWarning(
                        "Índice fora do range para termo '{Term}': {Index} (IDF size: {Size})",
                        term,
                        index,
                        _metadata.IdfValues.Count
                    );
                    continue;
                }

                var tf = (double)termFrequency[term] / words.Length;
                var idf = _metadata.IdfValues[index];
                tfidfVector[index] = (float)(tf * idf);
            }
        }

        return tfidfVector;
    }
}
