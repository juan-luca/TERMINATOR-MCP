using Infraestructura;
using Microsoft.Extensions.Logging;
using Shared;
using System.Text.Json;
public class PlanificadorAgent : IPlanificadorAgent
{
    private readonly GeminiClient _gemini;
    private readonly ILogger<PlanificadorAgent> _logger;

    public PlanificadorAgent(GeminiClient gemini, ILogger<PlanificadorAgent> logger)
    {
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<string[]> ConvertirPromptABacklog(Prompt prompt)
    {
        try
        {
            var mensaje = $"""
            Sos un ingeniero de software experto en Blazor.
            Convertí este requerimiento en una lista de tareas técnicas detalladas paso a paso para implementarlo:

            {prompt.Descripcion}
            """;

            var respuesta = await _gemini.GenerarAsync(mensaje);

            return respuesta
                   .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                   .Select(x => x.Trim())
                   .Where(x => !string.IsNullOrWhiteSpace(x))
                   .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error al convertir prompt a backlog. Prompt: {Titulo}", prompt.Titulo);
            return Array.Empty<string>();
        }
    }
}
