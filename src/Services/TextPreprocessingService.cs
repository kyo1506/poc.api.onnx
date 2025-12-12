using System.Text;
using System.Text.RegularExpressions;

namespace POC.Api.Onnx.Services;

/// <summary>
/// Serviço de pré-processamento de texto para documentos legais
/// Aplica normalização, remoção de stopwords e limpeza de padrões
/// </summary>
public class TextPreprocessingService
{
    private readonly HashSet<string> _stopWords;
    private readonly ILogger<TextPreprocessingService> _logger;

    public TextPreprocessingService(ILogger<TextPreprocessingService> logger)
    {
        _logger = logger;
        _stopWords = LoadPortugueseStopWords();
    }

    /// <summary>
    /// Sanitiza o texto removendo tabs, quebras de linha e espaços múltiplos
    /// </summary>
    public string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Remove tabs
        text = text.Replace("\t", "");

        // Remove quebras de linha (flatten)
        text = text.Replace("\n", "").Replace("\r", "");

        // Remove espaços múltiplos usando Regex
        text = Regex.Replace(text, @" +", " ");

        // Remove espaços no início e fim
        return text.Trim();
    }

    /// <summary>
    /// Verifica se o texto parece ser corrompido/lixo (caracteres isolados por espaços)
    /// Exemplo: "y   >  e d 1 ^ ^ / d k" retorna true
    /// </summary>
    public bool IsGarbageText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        // Remove espaços múltiplos para análise
        var normalizedText = Regex.Replace(text, @"\s+", " ").Trim();

        // 1. Verificar proporção de espaços (mais de 35% é suspeito)
        var totalChars = normalizedText.Length;
        var spaceCount = normalizedText.Count(c => c == ' ');
        var spaceRatio = (double)spaceCount / totalChars;

        if (spaceRatio > 0.35)
        {
            _logger.LogWarning(
                "Texto rejeitado: {SpaceRatio:P} de espaços (limite 35%)",
                spaceRatio
            );
            return true;
        }

        // 2. Detectar padrão "char espaço char espaço char" (palavras de 1-2 caracteres isoladas)
        // Se mais de 50% das "palavras" têm 1-2 caracteres, é texto corrompido
        var words = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 10)
            return false; // Textos muito curtos não são analisados por esse critério

        var shortWords = words.Count(w => w.Length <= 2);
        var shortWordRatio = (double)shortWords / words.Length;

        if (shortWordRatio > 0.50)
        {
            _logger.LogWarning(
                "Texto rejeitado: {Ratio:P} de palavras com 1-2 caracteres (limite 50%)",
                shortWordRatio
            );
            return true;
        }

        return false;
    }

    public string PreprocessText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Converter para minúsculas
        text = text.ToLowerInvariant();

        // Remover acentos
        text = RemoveAccents(text);

        // Remover pontuações
        text = Regex.Replace(text, @"[!""#$%&'()*+,\-./:;<=>?@\[\]^_`{|}~—–]", " ");

        // Remover URLs
        text = Regex.Replace(text, @"http\S+|www\S+|bit\.ly\S+", " ");

        // Remover endereços
        string[] addressWords =
        [
            "rua",
            "av",
            "avenida",
            "travessa",
            "rodovia",
            "estrada",
            "bairro",
            "distrito",
            "quadra",
            "lote",
            "bloco",
            "predio",
            "sala",
            "condominio",
            "logradouro",
            "alameda",
            "vila",
        ];
        string addressPattern = @"\b(" + string.Join("|", addressWords) + @")\b\s+\S+";
        text = Regex.Replace(text, addressPattern, " ");

        // Remover expressões jurídicas
        string[] legalPhrases =
        [
            "vem a presenca de",
            "vem perante",
            "douto juizo",
            "douto tribunal",
            "excelentissimo senhor doutor",
            "processo n",
            "neste",
            "exa",
            "doutor",
            "cep",
            "vossa excelencia",
        ];
        foreach (var phrase in legalPhrases)
        {
            text = Regex.Replace(text, phrase, " ", RegexOptions.IgnoreCase);
        }

        // Remover palavras com 1 caractere
        text = Regex.Replace(text, @"\b\w{1}\b", " ");

        // Remover stop words
        text = RemoveStopWords(text);

        // Normalizar espaços
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    private string RemoveStopWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filteredWords = words.Where(w => !_stopWords.Contains(w.ToLowerInvariant()));
        return string.Join(" ", filteredWords);
    }

    private string RemoveAccents(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    private HashSet<string> LoadPortugueseStopWords()
    {
        return
        [
            "a",
            "ao",
            "aos",
            "aquela",
            "aquelas",
            "aquele",
            "aqueles",
            "aquilo",
            "as",
            "até",
            "à",
            "às",
            "com",
            "como",
            "da",
            "das",
            "de",
            "dela",
            "delas",
            "dele",
            "deles",
            "depois",
            "do",
            "dos",
            "e",
            "ela",
            "elas",
            "ele",
            "eles",
            "em",
            "entre",
            "era",
            "eram",
            "éramos",
            "essa",
            "essas",
            "esse",
            "esses",
            "esta",
            "estas",
            "este",
            "estes",
            "esteve",
            "estive",
            "estivemos",
            "estiver",
            "estivera",
            "estiveram",
            "estivermos",
            "estivesse",
            "estivessem",
            "estivéramos",
            "estivéssemos",
            "estou",
            "está",
            "estávamos",
            "estão",
            "eu",
            "foi",
            "fomos",
            "for",
            "fora",
            "foram",
            "forem",
            "formos",
            "fosse",
            "fossem",
            "fui",
            "fôramos",
            "fôssemos",
            "haja",
            "hajam",
            "hajamos",
            "havemos",
            "havia",
            "haver",
            "hei",
            "houve",
            "houvemos",
            "houvera",
            "houveram",
            "houveremos",
            "houverei",
            "houverem",
            "houveremos",
            "houveria",
            "houveriam",
            "houveríamos",
            "houverá",
            "houverão",
            "houvesse",
            "houvessem",
            "houvéramos",
            "houvéssemos",
            "hão",
            "há",
            "isso",
            "isto",
            "já",
            "lhe",
            "lhes",
            "mais",
            "mas",
            "me",
            "mesmo",
            "meu",
            "meus",
            "minha",
            "minhas",
            "muito",
            "na",
            "nas",
            "nem",
            "no",
            "nos",
            "nossa",
            "nossas",
            "nosso",
            "nossos",
            "não",
            "nós",
            "num",
            "numa",
            "o",
            "os",
            "ou",
            "pela",
            "pelas",
            "pelo",
            "pelos",
            "por",
            "qual",
            "quando",
            "que",
            "quem",
            "se",
            "seja",
            "sejam",
            "sejamos",
            "sem",
            "ser",
            "sera",
            "serei",
            "seremos",
            "seria",
            "seriam",
            "seríamos",
            "serão",
            "será",
            "seu",
            "seus",
            "só",
            "somos",
            "sou",
            "são",
            "sua",
            "suas",
            "te",
            "tem",
            "temos",
            "tenha",
            "tenham",
            "tenhamos",
            "tenho",
            "ter",
            "terei",
            "teremos",
            "teria",
            "teriam",
            "teríamos",
            "terá",
            "terão",
            "teu",
            "teus",
            "teve",
            "tinha",
            "tinham",
            "tínhamos",
            "tive",
            "tivemos",
            "tiver",
            "tivera",
            "tiveram",
            "tiverem",
            "tivermos",
            "tivesse",
            "tivessem",
            "tivéramos",
            "tivéssemos",
            "tu",
            "tua",
            "tuas",
            "tém",
            "um",
            "uma",
            "você",
            "vocês",
            "vos",
            "à",
            "às",
            "é",
        ];
    }
}
