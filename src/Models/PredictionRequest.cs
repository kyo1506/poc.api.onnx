namespace POC.Api.Onnx.Models;

/// <summary>
/// Request para classificação de documento legal
/// </summary>
public class PredictionRequest
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Resultado da classificação N1 (classificação geral)
/// </summary>
public class N1PredictionOutput
{
    public long ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public float[] Probabilities { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Resultado da classificação N2 (sub-classificação de Manifestação)
/// </summary>
public class N2PredictionOutput
{
    public long ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public float[] Probabilities { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Response completo com resultados de N1 e opcionalmente N2
/// </summary>
public class CompletePredictionResponse
{
    public string OriginalText { get; set; } = string.Empty;
    public string SanitizedText { get; set; } = string.Empty;
    public string ProcessedText { get; set; } = string.Empty;
    public bool IsClassifiable { get; set; } = true;
    public string? Message { get; set; }
    public N1PredictionOutput? N1Result { get; set; }
    public N2PredictionOutput? N2Result { get; set; }
}
