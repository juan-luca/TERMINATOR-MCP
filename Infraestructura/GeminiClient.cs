using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infraestructura;

public class GeminiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(IConfiguration config, ILogger<GeminiClient> logger)
    {
        _http = new HttpClient();
        _apiKey = config["Gemini:ApiKey"];
        _model = config["Gemini:Model"];
        _logger = logger;  
    }


    public async Task<string> GenerarAsync(string prompt)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        var requestBody = new
        {
            contents = new[]
            {
                    new { parts = new[] { new { text = prompt } } }
                }
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, requestBody);
        }
        catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "⏰ Timeout al llamar a Gemini API.");
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API error ({(int)response.StatusCode}): {errorText}");
        }

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        // 1) Obtener primer candidato
        if (!root.TryGetProperty("candidates", out var candsElem) ||
            !candsElem.EnumerateArray().Any())
            throw new Exception("Gemini API: no se encontraron candidatos en la respuesta.");

        var firstCand = candsElem.EnumerateArray().First();

        // 2) Obtener content.parts
        if (!firstCand.TryGetProperty("content", out var contentElem) ||
            !contentElem.TryGetProperty("parts", out var partsElem) ||
            !partsElem.EnumerateArray().Any())
            throw new Exception("Gemini API: estructura inesperada, falta content.parts.");

        var firstPart = partsElem.EnumerateArray().First();

        string result;
        // 3) Si es string puro
        if (firstPart.ValueKind == JsonValueKind.String)
        {
            result = firstPart.GetString()!;
        }
        // 4) Si es objeto, extraer la propiedad "text"
        else if (firstPart.ValueKind == JsonValueKind.Object &&
                 firstPart.TryGetProperty("text", out var textElem) &&
                 textElem.ValueKind == JsonValueKind.String)
        {
            result = textElem.GetString()!;
        }
        else
        {
            throw new Exception($"Gemini API: no pude extraer texto de parts[0] (ValueKind={firstPart.ValueKind}).");
        }

        return result;
    }


}
