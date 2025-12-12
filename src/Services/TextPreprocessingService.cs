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
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0",
            "teriam",
            "nas",
            "foram",
            "houvesse",
            "quem",
            "lhe",
            "seja",
            "também",
            "entre",
            "houve",
            "do",
            "estiveram",
            "pelos",
            "estivéramos",
            "estivermos",
            "estou",
            "não",
            "qual",
            "essas",
            "hajamos",
            "tenham",
            "pelo",
            "o",
            "estes",
            "à",
            "fomos",
            "tive",
            "ou",
            "estavam",
            "tua",
            "houvessem",
            "nossos",
            "estiver",
            "houverei",
            "foi",
            "suas",
            "estivesse",
            "pelas",
            "eram",
            "ser",
            "tivermos",
            "houvermos",
            "aqueles",
            "forem",
            "tu",
            "sejam",
            "sem",
            "esteve",
            "houvemos",
            "fui",
            "seríamos",
            "como",
            "estava",
            "estamos",
            "muito",
            "serão",
            "haver",
            "se",
            "em",
            "teríamos",
            "tivessem",
            "nosso",
            "houveria",
            "houverão",
            "depois",
            "sou",
            "te",
            "terão",
            "estive",
            "está",
            "aquele",
            "ao",
            "hei",
            "tuas",
            "tínhamos",
            "estas",
            "essa",
            "ela",
            "num",
            "tivemos",
            "e",
            "tenha",
            "esses",
            "terei",
            "sua",
            "há",
            "é",
            "deles",
            "mais",
            "tiverem",
            "éramos",
            "tem",
            "às",
            "houverá",
            "uma",
            "as",
            "esse",
            "meus",
            "aquela",
            "temos",
            "tenhamos",
            "tivéssemos",
            "fosse",
            "elas",
            "da",
            "teremos",
            "numa",
            "houvera",
            "dos",
            "tenho",
            "tivéramos",
            "havemos",
            "por",
            "para",
            "estiverem",
            "seus",
            "fossem",
            "aquelas",
            "nos",
            "formos",
            "dela",
            "me",
            "fôramos",
            "estejamos",
            "estão",
            "houveram",
            "seria",
            "seremos",
            "tivesse",
            "fora",
            "sejamos",
            "estivéssemos",
            "tinha",
            "hão",
            "isto",
            "haja",
            "mesmo",
            "nem",
            "serei",
            "aquilo",
            "das",
            "estivemos",
            "será",
            "na",
            "já",
            "houvéssemos",
            "eles",
            "houvéramos",
            "com",
            "nós",
            "estivessem",
            "quando",
            "estar",
            "no",
            "que",
            "dele",
            "estivera",
            "houveremos",
            "são",
            "ele",
            "era",
            "delas",
            "nossa",
            "tivera",
            "nossas",
            "meu",
            "até",
            "seriam",
            "houver",
            "só",
            "fôssemos",
            "tiveram",
            "de",
            "for",
            "você",
            "tém",
            "eu",
            "estejam",
            "a",
            "esteja",
            "lhes",
            "pela",
            "mas",
            "somos",
            "tiver",
            "terá",
            "minha",
            "aos",
            "seu",
            "estávamos",
            "hajam",
            "houveriam",
            "os",
            "isso",
            "teu",
            "houverem",
            "esta",
            "houveríamos",
            "minhas",
            "este",
            "teus",
            "teria",
            "teve",
            "um",
            "tinham",
            "vocês",
            "vos",
        };
    }
}
