using System.Text;
using Shared;

namespace Infraestructura;

public class DesarrolladorAgent : IDesarrolladorAgent
{
    private readonly GeminiClient _gemini;

    public DesarrolladorAgent(GeminiClient gemini)
    {
        _gemini = gemini;
    }

    public async Task GenerarCodigoParaTarea(Prompt prompt, string tarea)
    {
        var nombreProyecto = SanitizeFileName(prompt.Titulo);
        var rutaProyecto = Path.Combine("output", nombreProyecto);

        var promptFinal = $"""
    Generá un archivo de código completo para esta tarea dentro de un proyecto Blazor profesional.
    La tarea es:
    {tarea}

    El archivo debe incluir su contenido tal como iría en el proyecto:
    - Si es una página: .razor
    - Si es configuración: .json
    - Si es backend: .cs
    - No agregues explicaciones ni encabezados.

    Solo devolvé el código exacto.
    """;

        var codigo = await _gemini.GenerarAsync(promptFinal);

        // Inferencia de tipo de archivo
        string extension = tarea.ToLower().Contains("razor") ? ".razor" :
                           tarea.ToLower().Contains("json") ? ".json" :
                           ".cs";

        var nombreArchivo = SanitizeFileName(tarea) + extension;
        var rutaArchivo = Path.Combine(rutaProyecto, InferirSubcarpeta(extension), nombreArchivo);

        Directory.CreateDirectory(Path.GetDirectoryName(rutaArchivo)!);
        await File.WriteAllTextAsync(rutaArchivo, codigo);
    }

    private string InferirSubcarpeta(string extension)
    {
        return extension switch
        {
            ".razor" => "Pages",
            ".json" => "",
            ".cs" => "Services", // por ahora todo .cs lo mandamos ahí
            _ => ""
        };
    }



    private string SanitizeFileName(string input)
    {
        var sb = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
        }
        return sb.ToString().ToLowerInvariant();
    }
}
