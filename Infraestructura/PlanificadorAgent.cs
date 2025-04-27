using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared;

namespace Infraestructura;
public class PlanificadorAgent : IPlanificadorAgent
{
    private readonly GeminiClient _gemini;

    public PlanificadorAgent(GeminiClient gemini)
    {
        _gemini = gemini;
    }

    public async Task<string[]> ConvertirPromptABacklog(Prompt prompt)
    {
        var mensaje = $"""
        Sos un ingeniero de software experto en Blazor.
        Convertí este requerimiento en una lista de tareas técnicas detalladas paso a paso para implementarlo:

        {prompt.Descripcion}
        """;

        var respuesta = await _gemini.GenerarAsync(mensaje);

        return respuesta.Split("\n")
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();
    }
}
