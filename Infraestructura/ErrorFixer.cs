// --- START OF FILE ErrorFixer.cs --- AJUSTE FINAL PARSEO RUTAS

using Shared;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.IO;

namespace Infraestructura
{
    public class ErrorFixer : IErrorFixer
    {
        private readonly GeminiClient _gemini;
        private readonly ILogger<ErrorFixer> _logger;
        private const int MaxGlobalErrorChars = 20000;
        private const int MaxLinesPerFile = 50;

        private readonly CorrectedErrorsStore _store;

        // Regex mejorado:
        // ^\s* : Inicio de línea opcionalmente con espacios.
        // (?<path>...) : Grupo nombrado "path" para la ruta.
        //    (?:[a-zA-Z]:\\|\/)? : Drive opcional (Windows) o slash inicial (Linux/Mac).
        //    (?:[\w\-\.\s\\\/()]+?) : Uno o más caracteres válidos para carpetas/archivos (incluye espacios, paréntesis). No codicioso (?).
        //    \.(cs|razor|cshtml|csproj) : Debe terminar en una extensión conocida.
        // \((?<line>\d+)[,;](?<col>\d+)\) : Captura línea y columna (separador , o ;).
        // :\s*(error|warning)\s+(?<code>\w+): : Captura tipo y código de error/warning.
        private static readonly Regex ErrorPathRegex = new Regex(
             @"^\s*(?<path>(?:[a-zA-Z]:\\|\/)?(?:[\w\-\.\s\\\/()]+?)\.(cs|razor|cshtml|csproj))\((?<line>\d+)[,;](?<col>\d+)\):\s*(error|warning)\s+(?<code>\w+):",
             RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public ErrorFixer(GeminiClient gemini, ILogger<ErrorFixer> logger, CorrectedErrorsStore store)
        {
            _gemini = gemini;
            _logger = logger;
            _store = store;
        }

        public async Task<List<string>> CorregirErroresDeCompilacionAsync(string rutaProyecto)
        {
            var errorLogPath = Path.Combine(rutaProyecto, "build_errors.log");
            _logger.LogInformation("🔍 Buscando build_errors.log en: {Path} (Existe? {Exists})",
                errorLogPath, File.Exists(errorLogPath));

            if (!File.Exists(errorLogPath)) { _logger.LogInformation("No se encontró build_errors.log."); return new List<string>(); }

            string rawErrors;
            try { rawErrors = await File.ReadAllTextAsync(errorLogPath); }
            catch (Exception ex) { _logger.LogError(ex, "Error al leer el archivo de log de errores: {LogPath}", errorLogPath); return new List<string>(); }

            var errores = rawErrors.Length > MaxGlobalErrorChars ? rawErrors.Substring(0, MaxGlobalErrorChars) + "\n... (log truncado) ..." : rawErrors;

            var archivosConErrores = InferirArchivosFallados(errores, rutaProyecto);
            _logger.LogInformation("📑 Archivos detectados con errores: {Count}", archivosConErrores.Count);

            if (archivosConErrores.Count == 0) { _logger.LogWarning("No se detectaron archivos específicos con errores en {LogPath}, aunque el log existe.", errorLogPath); return new List<string>(); }

            var corregidos = new List<string>();

            foreach (var archivo in archivosConErrores)
            {
                if (!IsPathPotentiallyValid(archivo)) { _logger.LogWarning("Omitiendo ruta de archivo inválida o sospechosa detectada: {File}", archivo); continue; }
                var snippet = ExtraerErroresDelArchivo(errores, archivo);
                if (_store.WasCorrected(archivo, snippet)) { _logger.LogInformation("⏭ Ya corregido antes (mismo snippet): {File}", Path.GetFileName(archivo)); continue; }

                _logger.LogInformation("🛠 Intentando corregir archivo: {File}", archivo);
                var prompt = CreateCorrectionPrompt(archivo, snippet);
                string codigoCorregido;
                try
                {
                    codigoCorregido = await _gemini.GenerarAsync(prompt);
                    codigoCorregido = LimpiarCodigoGemini(codigoCorregido);
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "❌ Error llamando a Gemini para corregir {File}", Path.GetFileName(archivo));
                    if (ex.Message.Contains("429") || ex.Message.Contains("RESOURCE_EXHAUSTED")) { _logger.LogError(">>> Límite de cuota de API alcanzado. Abortando correcciones para este ciclo. <<<"); break; }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(codigoCorregido)) { _logger.LogWarning("⚠️ Gemini devolvió contenido vacío al intentar corregir {File}. No se aplicará corrección.", Path.GetFileName(archivo)); continue; }

                try
                {
                    if (!IsPathWritable(archivo, rutaProyecto)) { _logger.LogError("❌ Permiso denegado o ruta inválida detectada ANTES de escribir corrección en: {File}. Omitiendo.", archivo); continue; }
                    _logger.LogDebug("Escribiendo corrección (Longitud: {Length}) en: {File}", codigoCorregido.Length, archivo);
                    await File.WriteAllTextAsync(archivo, codigoCorregido);
                    _store.MarkCorrected(archivo, snippet);
                    corregidos.Add(archivo);
                    _logger.LogInformation("✅ Archivo corregido y escrito: {File}", Path.GetFileName(archivo));
                }
                catch (UnauthorizedAccessException uaEx) { _logger.LogError(uaEx, "❌ Permiso DENEGADO al escribir corrección en: {File}", archivo); }
                catch (DirectoryNotFoundException dnfEx) { _logger.LogError(dnfEx, "❌ Directorio no encontrado al intentar escribir corrección en: {File}", archivo); }
                catch (IOException ioEx) { _logger.LogError(ioEx, "❌ Error de I/O al escribir corrección en: {File}", archivo); }
                catch (Exception ex) { _logger.LogError(ex, "❌ Error inesperado al escribir corrección en: {File}", archivo); }
            }
            return corregidos;
        }

        // *** MÉTODO CORREGIDO ***
        private List<string> InferirArchivosFallados(string errorLogContent, string projectRootPath)
        {
            var failedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var projectFileName = Path.GetFileName(projectRootPath);
            var projectFilePath = Path.Combine(projectRootPath, $"{projectFileName}.csproj");

            // Normalizar projectRootPath para comparaciones consistentes
            var fullProjectRootPath = Path.GetFullPath(projectRootPath);

            foreach (Match match in ErrorPathRegex.Matches(errorLogContent))
            {
                var pathPart = match.Groups["path"].Value.Trim();

                // Validar caracteres inválidos básicos (además de los permitidos por el Regex)
                if (string.IsNullOrWhiteSpace(pathPart) || pathPart.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    _logger.LogWarning("Ignorando ruta potencialmente inválida capturada por Regex: '{PathPart}'", pathPart);
                    continue;
                }

                // Normalizar separadores
                pathPart = pathPart.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

                string fullPath;
                try
                {
                    // Intentar resolver la ruta combinándola con la raíz del proyecto
                    fullPath = Path.GetFullPath(Path.Combine(projectRootPath, pathPart));

                    // *** VALIDACIÓN MÁS ESTRICTA: La ruta resuelta DEBE estar dentro de la carpeta del proyecto ***
                    if (!fullPath.StartsWith(fullProjectRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                        !fullPath.Equals(projectFilePath, StringComparison.OrdinalIgnoreCase)) // Permitir el propio archivo csproj
                    {
                        _logger.LogDebug("Ignorando ruta resuelta fuera del proyecto: {FullPath} (Raíz: {ProjectRoot})", fullPath, fullProjectRootPath);
                        continue;
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Error al procesar/combinar la ruta del log: '{PathPart}'. Omitiendo.", pathPart); continue; }


                // Validaciones adicionales
                if (fullPath.Contains(Path.Combine("dotnet", "sdk"), StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    fullPath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    _logger.LogDebug("Ignorando ruta en SDK, obj o bin: {FullPath}", fullPath);
                    continue;
                }

                // Verificar si el archivo existe físicamente
                if (File.Exists(fullPath))
                {
                    failedFiles.Add(fullPath);
                    _logger.LogTrace("Archivo de error detectado y validado: {FullPath}", fullPath);
                }
                else { _logger.LogWarning("Archivo de error referenciado en log no encontrado en disco: {FullPath}", fullPath); }
            }

            // Manejo de errores NETSDK (sin cambios, parece correcto)
            if (errorLogContent.Contains("error NETSDK", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(projectFilePath)) { _logger.LogWarning("Error del SDK (NETSDKxxxx) detectado. Asociando error al archivo .csproj: {ProjectFile}", projectFilePath); failedFiles.Add(projectFilePath); }
                else { _logger.LogError("Se detectó un error NETSDK pero no se encontró el archivo .csproj esperado en {ProjectFile}", projectFilePath); }
            }

            if (!failedFiles.Any() && !string.IsNullOrWhiteSpace(errorLogContent) && !(errorLogContent.Contains("Build Succeeded") || errorLogContent.Contains("Build succeeded.")))
            { _logger.LogWarning("No se pudo extraer ninguna ruta de archivo específica del log de errores, aunque contiene errores."); }

            return failedFiles.ToList();
        }

        // ... (El resto de los métodos Helper permanecen igual que en la versión anterior) ...
        #region Helper Methods (No changes needed from previous version)

        private string ExtraerErroresDelArchivo(string fullErrorLog, string targetFilePath) { string searchKey; if (targetFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) { searchKey = Path.GetFileName(targetFilePath); } else { try { string? projectRootParent = Path.GetDirectoryName(targetFilePath.StartsWith(Path.GetPathRoot(targetFilePath) ?? "") ? Path.GetDirectoryName(targetFilePath) : targetFilePath); searchKey = Path.GetRelativePath(projectRootParent ?? targetFilePath, targetFilePath); if (string.IsNullOrWhiteSpace(searchKey) || searchKey == ".") { searchKey = Path.GetFileName(targetFilePath); } } catch { searchKey = Path.GetFileName(targetFilePath); } } var relevantLines = fullErrorLog.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(line => line.Contains(searchKey, StringComparison.OrdinalIgnoreCase) || (Regex.IsMatch(line, @"^\s*(error|warning)\s+\w+:") && !Regex.IsMatch(line, @"\.(cs|razor|cshtml)\("))).Take(MaxLinesPerFile).ToList(); if (relevantLines.Count == 0) { _logger.LogDebug("No se encontraron líneas específicas para '{SearchKey}' en el log. Tomando las primeras 10 líneas generales.", searchKey); relevantLines = fullErrorLog.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(10).ToList(); } return string.Join(Environment.NewLine, relevantLines); }
        private string CreateCorrectionPrompt(string filePath, string errorSnippet) { string fileExtension = Path.GetExtension(filePath).ToLowerInvariant(); string fileType = fileExtension switch { ".cs" => "C#", ".razor" => "Blazor Razor", ".csproj" => "C# Project (.csproj XML)", ".cshtml" => "CSHTML", _ => "desconocido" }; string fileName = Path.GetFileName(filePath); string projectRootPath = Directory.GetParent(filePath)?.Parent?.FullName ?? Path.GetDirectoryName(filePath) ?? filePath; string relativePath = Path.GetRelativePath(projectRootPath, filePath); string specificInstructions = fileType switch { "C# Project (.csproj XML)" => "Analiza el siguiente XML del archivo .csproj y corrige los errores reportados. Presta atención a la estructura XML, PackageReferences duplicadas o con versiones incorrectas, ItemGroups mal formados, o propiedades inválidas en PropertyGroup según los errores.", "C#" or "Blazor Razor" or "CSHTML" => $"Analiza el siguiente código {fileType} del archivo '{fileName}' y corrige los errores de compilación reportados. Corrige únicamente la sintaxis, referencias inválidas, tipos faltantes, etc. Mantén la lógica original intacta si es posible.", _ => "Intenta corregir los errores reportados en el siguiente archivo." }; return @$"Se encontraron errores de compilación. Por favor, corrige el archivo.**Archivo:** {fileName} **Ruta Relativa:** {relativePath} **Tipo:** {fileType} **Errores Reportados (Extracto):** ```log {errorSnippet} ``` **Instrucciones Específicas:** 1. {specificInstructions} 2. Considera los números de línea si se mencionan en los errores. 3. Devuelve **ÚNICAMENTE el contenido COMPLETO y CORREGIDO** del archivo '{fileName}'. 4. NO incluyas explicaciones, introducciones, resúmenes de cambios, ni el bloque de errores original. 5. NO uses bloques de markdown como ```csharp, ```xml, o ```razor alrededor del código corregido. Solo el contenido puro del archivo."; }
        private string LimpiarCodigoGemini(string codigo) { if (string.IsNullOrWhiteSpace(codigo)) return ""; var lines = codigo.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Select(l => l.TrimEnd()).ToList(); if (lines.Count > 0 && lines[0].Trim().StartsWith("```")) { lines.RemoveAt(0); } if (lines.Count > 0 && lines[^1].Trim() == "```") { lines.RemoveAt(lines.Count - 1); } return string.Join(Environment.NewLine, lines).Trim(); }
        private bool IsPathPotentiallyValid(string? filePath) { return !string.IsNullOrWhiteSpace(filePath); }
        private bool IsPathWritable(string filePath, string projectRootPath) { if (!IsPathPotentiallyValid(filePath)) return false; try { string fullPath = Path.GetFullPath(filePath); string fullProjectRootPath = Path.GetFullPath(projectRootPath); if (!fullPath.StartsWith(fullProjectRootPath, StringComparison.OrdinalIgnoreCase)) { _logger.LogWarning("Attempted to write outside project directory: {FullPath}", fullPath); return false; } string[] protectedPaths = { Path.Combine("C:", "Program Files"), Path.Combine("C:", "Program Files (x86)"), Path.Combine("C:", "Windows"), "/usr/bin", "/usr/sbin", "/bin", "/sbin", "/etc", Path.Combine("dotnet", "sdk") }; if (protectedPaths.Any(p => fullPath.Contains(p, StringComparison.OrdinalIgnoreCase))) { _logger.LogWarning("Attempted to write to a potentially protected system path: {FullPath}", fullPath); return false; } string? directory = Path.GetDirectoryName(fullPath); if (directory == null || !Directory.Exists(directory)) { _logger.LogWarning("Target directory for writing does not exist: {Directory}", directory ?? "N/A"); return false; } return true; } catch (Exception ex) { _logger.LogWarning(ex, "Exception while checking if path is writable: {FilePath}", filePath); return false; } }

        #endregion

    }
}
// --- END OF FILE ErrorFixer.cs --- AJUSTE FINAL PARSEO RUTAS