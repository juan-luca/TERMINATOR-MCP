using Shared;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
namespace Infraestructura
{
    public class ErrorFixer : IErrorFixer
    {
        private readonly GeminiClient _gemini;
        private readonly ILogger<ErrorFixer> _logger;
        private const int MaxGlobalErrorChars = 20000;
        private const int MaxLinesPerFile = 50;

        private readonly CorrectedErrorsStore _store;
        public ErrorFixer(GeminiClient gemini, ILogger<ErrorFixer> logger, CorrectedErrorsStore store)
        {
            _gemini = gemini;
            _logger = logger;
            _store = store;
        }


        public async Task<List<string>> CorregirErroresDeCompilacionAsync(string rutaProyecto)
        {
            var errorPath = Path.Combine(rutaProyecto, "build_errors.log");
            _logger.LogInformation("🔍 Buscando build_errors.log en: {Path} (Existe? {Exists})",
                errorPath, File.Exists(errorPath));
            if (!File.Exists(errorPath))
                return new List<string>();

            // Leer y truncar si es muy grande
            var raw = await File.ReadAllTextAsync(errorPath);
            var errores = raw.Length > 20000
                ? raw.Substring(0, 20000) + "\n... (log truncado) ..."
                : raw;

            // Detectar archivos con error
            var archivosConErrores = InferirArchivosFallados(errores, rutaProyecto);
            _logger.LogInformation("📑 Archivos detectados con errores: {Count}", archivosConErrores.Count);
            if (archivosConErrores.Count == 0)
                return new List<string>();

            var corregidos = new List<string>();
            foreach (var archivo in archivosConErrores)
            {
                // Extraer snippet y saltar si ya corregido igual
                var snippet = ExtraerErroresDelArchivo(errores, archivo);
                if (_store.WasCorrected(archivo, snippet))
                {
                    _logger.LogInformation("⏭ Ya corregido antes: {File}", archivo);
                    continue;
                }

                _logger.LogInformation("🛠 Corrigiendo archivo: {File}", archivo);
                var prompt = $"""
Corrige únicamente el contenido de este archivo C#, basándote en estos errores:

{snippet}

Ruta: {Path.GetFileName(archivo)}

Devuelve solo el código completo corregido sin explicaciones.
""";
                string codigo;
                try
                {
                    codigo = await _gemini.GenerarAsync(prompt);
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "❌ Error llamando a Gemini para {File}", archivo);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(codigo))
                {
                    _logger.LogWarning("⚠️ Gemini devolvió vacío para {File}", archivo);
                    continue;
                }

                // Sobrescribir y marcar
                await File.WriteAllTextAsync(archivo, codigo);
                _store.MarkCorrected(archivo, snippet);
                corregidos.Add(archivo);
                _logger.LogInformation("✅ Archivo corregido: {File}", archivo);
            }

            return corregidos;
        }

        private List<string> InferirArchivosFallados(string errores, string rutaProyecto)
        {
            var archivos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var linea in errores.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!linea.Contains(".cs", System.StringComparison.OrdinalIgnoreCase) &&
                    !linea.Contains(".razor", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                int idx = linea.IndexOf('(');
                if (idx < 0) idx = linea.IndexOf(':');
                if (idx < 0) continue;

                var rutaParte = linea.Substring(0, idx).Trim()
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);

                var fullPath = Path.IsPathRooted(rutaParte)
                    ? rutaParte
                    : Path.Combine(rutaProyecto, rutaParte);

                fullPath = fullPath.Split('(')[0].Trim();

                var nombreSinExt = Path.GetFileNameWithoutExtension(fullPath);
                if (string.IsNullOrWhiteSpace(nombreSinExt))
                    continue;

                if (File.Exists(fullPath))
                    archivos.Add(fullPath);
            }
            return archivos.ToList();
        }



        private string ExtraerErroresDelArchivo(string errores, string archivo)
        {
            var nombre = Path.GetFileName(archivo);
            var lineas = errores
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains(nombre))
                .Take(50)
                .ToList();

            if (lineas.Count == 0)
                lineas = errores.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Take(10)
                    .ToList();

            return string.Join(System.Environment.NewLine, lineas);
        }
    }
}
