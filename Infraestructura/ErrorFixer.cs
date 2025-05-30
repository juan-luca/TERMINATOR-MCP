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
        private const int MaxLinesPerFile = 50; // Max lines from error log per file

        private readonly CorrectedErrorsStore _store;

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

            var erroresLogCompleto = rawErrors.Length > MaxGlobalErrorChars ? rawErrors.Substring(0, MaxGlobalErrorChars) + "\n... (log truncado) ..." : rawErrors;

            var archivosConErrores = InferirArchivosFallados(erroresLogCompleto, rutaProyecto);
            _logger.LogInformation("📑 Archivos detectados con errores: {Count}", archivosConErrores.Count);

            if (archivosConErrores.Count == 0) { _logger.LogWarning("No se detectaron archivos específicos con errores en {LogPath}, aunque el log existe.", errorLogPath); return new List<string>(); }

            var corregidos = new List<string>();

            foreach (var filePath in archivosConErrores) // filePath is the full path to the file with errors
            {
                if (!IsPathPotentiallyValid(filePath)) { _logger.LogWarning("Omitiendo ruta de archivo inválida o sospechosa detectada: {File}", filePath); continue; }
                
                string codigoOriginal;
                try 
                {
                    codigoOriginal = await File.ReadAllTextAsync(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al leer el contenido del archivo {File}. Omitiendo corrección.", filePath);
                    continue;
                }

                var errorMessagesParaArchivo = ExtraerErroresDelArchivo(erroresLogCompleto, filePath)
                                              .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                              .ToList();

                if (!errorMessagesParaArchivo.Any())
                {
                    _logger.LogWarning("No se extrajeron mensajes de error específicos para el archivo {File}, aunque fue listado. Omitiendo.", filePath);
                    continue;
                }
                
                // Using the combined snippet of errors for this file as the "CorrectedErrorStore" key
                string errorSnippetKey = string.Join(Environment.NewLine, errorMessagesParaArchivo);
                if (_store.WasCorrected(filePath, errorSnippetKey)) 
                { 
                    _logger.LogInformation("⏭ Ya corregido antes (mismo snippet de errores): {File}", Path.GetFileName(filePath)); 
                    continue; 
                }

                _logger.LogInformation("🛠 Intentando corregir archivo: {File}", filePath);
                // Assuming projectContext might be null or a generic description if not available from a specific prompt.
                // For now, passing null as projectContext as this method doesn't have direct access to the initial Shared.Prompt.
                var prompt = CrearPromptParaCorregirError(codigoOriginal, Path.GetFileName(filePath), errorMessagesParaArchivo, null); 
                string codigoCorregido;
                try
                {
                    codigoCorregido = await _gemini.GenerarAsync(prompt);
                    codigoCorregido = LimpiarCodigoGemini(codigoCorregido);
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "❌ Error llamando a Gemini para corregir {File}", Path.GetFileName(filePath));
                    if (ex.Message.Contains("429") || ex.Message.Contains("RESOURCE_EXHAUSTED")) { _logger.LogError(">>> Límite de cuota de API alcanzado. Abortando correcciones para este ciclo. <<<"); break; }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(codigoCorregido)) { _logger.LogWarning("⚠️ Gemini devolvió contenido vacío al intentar corregir {File}. No se aplicará corrección.", Path.GetFileName(filePath)); continue; }

                try
                {
                    if (!IsPathWritable(filePath, rutaProyecto)) { _logger.LogError("❌ Permiso denegado o ruta inválida detectada ANTES de escribir corrección en: {File}. Omitiendo.", filePath); continue; }
                    _logger.LogDebug("Escribiendo corrección (Longitud: {Length}) en: {File}", codigoCorregido.Length, filePath);
                    await File.WriteAllTextAsync(filePath, codigoCorregido);
                    _store.MarkCorrected(filePath, errorSnippetKey); // Use the error snippet as key
                    corregidos.Add(filePath);
                    _logger.LogInformation("✅ Archivo corregido y escrito: {File}", Path.GetFileName(filePath));
                }
                catch (UnauthorizedAccessException uaEx) { _logger.LogError(uaEx, "❌ Permiso DENEGADO al escribir corrección en: {File}", filePath); }
                catch (DirectoryNotFoundException dnfEx) { _logger.LogError(dnfEx, "❌ Directorio no encontrado al intentar escribir corrección en: {File}", filePath); }
                catch (IOException ioEx) { _logger.LogError(ioEx, "❌ Error de I/O al escribir corrección en: {File}", filePath); }
                catch (Exception ex) { _logger.LogError(ex, "❌ Error inesperado al escribir corrección en: {File}", filePath); }
            }
            return corregidos;
        }

        private List<string> InferirArchivosFallados(string errorLogContent, string projectRootPath)
        {
            var failedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var projectFileName = Path.GetFileName(projectRootPath); // Assumes projectRootPath is the folder name
            var projectFilePath = Path.Combine(projectRootPath, $"{projectFileName}.csproj"); 
            if (!File.Exists(projectFilePath)) // If projectRootPath is already the .csproj file
            {
                var parentDir = Path.GetDirectoryName(projectRootPath);
                if (Directory.Exists(parentDir)) projectRootPath = parentDir; // adjust projectRootPath if needed
                 projectFilePath = Directory.GetFiles(projectRootPath, "*.csproj").FirstOrDefault() ?? projectFilePath; // Try to find any csproj
            }


            // Normalizar projectRootPath para comparaciones consistentes
            var fullProjectRootPath = Path.GetFullPath(projectRootPath);

            foreach (Match match in ErrorPathRegex.Matches(errorLogContent))
            {
                var pathPart = match.Groups["path"].Value.Trim();

                if (string.IsNullOrWhiteSpace(pathPart) || pathPart.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    _logger.LogWarning("Ignorando ruta potencialmente inválida capturada por Regex: '{PathPart}'", pathPart);
                    continue;
                }

                pathPart = pathPart.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

                string fullPath;
                try
                {
                    if (Path.IsPathRooted(pathPart) && File.Exists(pathPart))
                    {
                        fullPath = Path.GetFullPath(pathPart);
                    }
                    else
                    {
                         fullPath = Path.GetFullPath(Path.Combine(fullProjectRootPath, pathPart));
                    }

                    if (!fullPath.StartsWith(fullProjectRootPath, StringComparison.OrdinalIgnoreCase) &&
                        !fullPath.Equals(Path.GetFullPath(projectFilePath), StringComparison.OrdinalIgnoreCase)) 
                    {
                        _logger.LogDebug("Ignorando ruta resuelta fuera del proyecto: {FullPath} (Raíz: {ProjectRoot})", fullPath, fullProjectRootPath);
                        continue;
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Error al procesar/combinar la ruta del log: '{PathPart}'. Omitiendo.", pathPart); continue; }


                if (fullPath.Contains(Path.Combine("dotnet", "sdk"), StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    fullPath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    _logger.LogDebug("Ignorando ruta en SDK, obj o bin: {FullPath}", fullPath);
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    failedFiles.Add(fullPath);
                    _logger.LogTrace("Archivo de error detectado y validado: {FullPath}", fullPath);
                }
                else { _logger.LogWarning("Archivo de error referenciado en log no encontrado en disco: {FullPath}", fullPath); }
            }

            if (errorLogContent.Contains("error NETSDK", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(projectFilePath)) { _logger.LogWarning("Error del SDK (NETSDKxxxx) detectado. Asociando error al archivo .csproj: {ProjectFile}", projectFilePath); failedFiles.Add(projectFilePath); }
                else { _logger.LogError("Se detectó un error NETSDK pero no se encontró el archivo .csproj esperado en {ProjectFile}", projectFilePath); }
            }

            if (!failedFiles.Any() && !string.IsNullOrWhiteSpace(errorLogContent) && !(errorLogContent.Contains("Build Succeeded") || errorLogContent.Contains("Build succeeded.")))
            { _logger.LogWarning("No se pudo extraer ninguna ruta de archivo específica del log de errores, aunque contiene errores."); }

            return failedFiles.ToList();
        }

        private string ExtraerErroresDelArchivo(string fullErrorLog, string targetFilePath) 
        { 
            string searchKey;
            string targetFileNameOnly = Path.GetFileName(targetFilePath);

            // For .csproj files, the errors might not contain the full path, just the filename.
            if (targetFilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) 
            { 
                searchKey = targetFileNameOnly; 
            } 
            else 
            { 
                // For other files, try to make the search key more specific by using relative path if possible,
                // but fall back to filename if path manipulation is complex.
                // This helps distinguish files with the same name in different folders.
                try 
                { 
                    string? projectRootAttempt = Path.GetDirectoryName(targetFilePath);
                    if (projectRootAttempt != null) projectRootAttempt = Path.GetDirectoryName(projectRootAttempt); // Go up one more level for typical project structures like Project/Models/File.cs
                    
                    searchKey = Path.GetRelativePath(projectRootAttempt ?? targetFilePath, targetFilePath); 
                    if (string.IsNullOrWhiteSpace(searchKey) || searchKey == "." || searchKey.Contains("..")) 
                    { 
                        searchKey = targetFileNameOnly; 
                    }
                } 
                catch 
                { 
                    searchKey = targetFileNameOnly; 
                } 
            }
            
            var relevantLines = fullErrorLog.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(line => line.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase) >= 0 || // Line contains the file path/name
                                              (Regex.IsMatch(line, @"^\s*(error|warning)\s+\w+:") && // Line is a general error/warning
                                               !Regex.IsMatch(line, ErrorPathRegex.ToString() ) // But not an error pointing to a *different* specific file
                                              )
                                      )
                                .Take(MaxLinesPerFile)
                                .ToList(); 
            
            if (relevantLines.Count == 0) 
            { 
                _logger.LogDebug("No se encontraron líneas específicas para '{SearchKey}' en el log. Tomando las primeras 10 líneas generales.", searchKey); 
                relevantLines = fullErrorLog.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(10).ToList(); 
            } 
            return string.Join(Environment.NewLine, relevantLines); 
        }

        private string CrearPromptParaCorregirError(string codigoOriginal, string nombreArchivo, List<string> errorMessages, string? projectContext)
        {
            string errorBlock = string.Join(Environment.NewLine, errorMessages.Select(e => $"- {e}"));
            string fileType = Path.GetExtension(nombreArchivo).ToLowerInvariant() switch
            {
                ".cs" => "C#",
                ".razor" => "Blazor Razor",
                ".csproj" => "C# Project (.csproj XML)",
                ".cshtml" => "CSHTML",
                _ => "desconocido"
            };

            return @$"Eres un experto desarrollador C# y Blazor .NET. Tu tarea es corregir errores de compilación en el siguiente archivo.

Nombre del Archivo: {nombreArchivo}
Tipo de Archivo: {fileType}

Contexto General del Proyecto (si está disponible):
{projectContext ?? "No hay contexto adicional del proyecto."}

Código Original con Errores:
```{fileType.ToLowerInvariant()}
{codigoOriginal}
```

Errores de Compilación Reportados (de 'dotnet build'):
{errorBlock}

Instrucciones PRECISAS para la Corrección:
1.  Analiza CUIDADOSAMENTE los 'Errores de Compilación Reportados'.
2.  Modifica el 'Código Original con Errores' ÚNICAMENTE para solucionar estos errores específicos.
3.  NO realices cambios no solicitados, NO agregues nuevas funcionalidades, y NO refactorices código que no esté directamente relacionado con los errores.
4.  Presta atención a números de línea o detalles en los mensajes de error si están disponibles.
5.  Asegúrate de que la sintaxis sea correcta para C# y Blazor (.NET 8).
6.  Devuelve el CÓDIGO FUENTE COMPLETO y CORREGIDO del archivo '{nombreArchivo}'.
7.  NO incluyas explicaciones, introducciones, resúmenes de cambios, ni el código original sin modificar si no fue necesario.
8.  NO uses bloques de markdown (```) adicionales alrededor del código final que devuelves. Solo el contenido puro del archivo corregido.
9.  Si los errores indican un 'using' faltante, añádelo. Si indican un tipo o miembro no encontrado, verifica si es un error tipográfico o si realmente falta una definición que deberías poder inferir y añadir (de forma simple, no compleja).
10. Si un error es ambiguo y no puedes corregirlo con alta confianza, intenta la corrección más probable o deja un comentario breve en el código (`// No se pudo corregir automáticamente: [descripción del problema]`) y devuelve el código con esa anotación. No dejes el archivo sin cambios si hay errores claros que sí puedes arreglar.

IMPORTANTE: El objetivo es que el archivo resultante compile correctamente.
";
        }
        
        private string LimpiarCodigoGemini(string codigo) 
        { 
            if (string.IsNullOrWhiteSpace(codigo)) return ""; 
            var lines = codigo.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList(); 
            if (lines.Any() && lines[0].Trim().StartsWith("```")) { lines.RemoveAt(0); } 
            if (lines.Any() && lines.Last().Trim() == "```") { lines.RemoveAt(lines.Count - 1); } 
            
            string[] commonPhrases = {
                "here's the code", "here is the code", "okay, here is the", "sure, here is the", "certainly, here is the",
                "this is the code", "the code is as follows", "find the code below", "below is the code",
                "this code should work", "let me know if you have questions", "hope this helps", "hope this is helpful",
                "this is just an example", "you might need to adjust this", "this implements", "this file contains",
                "the generated code:", "generated code:", "code:", "c# code:", "razor code:", "html code:",
                "```csharp", "```c#", "```razor", "```html", "```xml", "```json",
                "here is the updated code", "here's the updated code", "this is the modified code",
                "i've made the requested changes", "the changes are as follows"
            };

            for (int i = 0; i < 3 && lines.Any(); i++)
            {
                var trimmedLowerLine = lines[0].Trim().ToLowerInvariant();
                bool removed = false;
                foreach (var phrase in commonPhrases)
                {
                    if (trimmedLowerLine.StartsWith(phrase)) 
                    {
                        _logger.LogTrace("LimpiarCodigoGemini (ErrorFixer): Removiendo línea introductoria: '{Line}'", lines[0]);
                        lines.RemoveAt(0);
                        removed = true;
                        break; 
                    }
                }
                if (!removed) break;
            }

            for (int i = 0; i < 3 && lines.Any(); i++)
            {
                var trimmedLowerLine = lines.Last().Trim().ToLowerInvariant();
                 bool removed = false;
                foreach (var phrase in commonPhrases)
                {
                    if (trimmedLowerLine.EndsWith(phrase) || trimmedLowerLine == phrase) 
                    {
                        _logger.LogTrace("LimpiarCodigoGemini (ErrorFixer): Removiendo línea conclusiva: '{Line}'", lines.Last());
                        lines.RemoveAt(lines.Count - 1);
                        removed = true;
                        break;
                    }
                }
                 if (!removed) break;
            }
            
            var processedLines = lines.Select(l => l.TrimEnd()).ToList();
            while (processedLines.Any() && string.IsNullOrWhiteSpace(processedLines[0])) 
            { 
                processedLines.RemoveAt(0); 
            }
            while (processedLines.Any() && string.IsNullOrWhiteSpace(processedLines.Last())) 
            { 
                processedLines.RemoveAt(processedLines.Count - 1); 
            }

            return string.Join(Environment.NewLine, processedLines).Trim();
        }
        private bool IsPathPotentiallyValid(string? filePath) { return !string.IsNullOrWhiteSpace(filePath); }
        private bool IsPathWritable(string filePath, string projectRootPath) { if (!IsPathPotentiallyValid(filePath)) return false; try { string fullPath = Path.GetFullPath(filePath); string fullProjectRootPath = Path.GetFullPath(projectRootPath); if (!fullPath.StartsWith(fullProjectRootPath, StringComparison.OrdinalIgnoreCase)) { _logger.LogWarning("Attempted to write outside project directory: {FullPath}", fullPath); return false; } string[] protectedPaths = { Path.Combine("C:", "Program Files"), Path.Combine("C:", "Program Files (x86)"), Path.Combine("C:", "Windows"), "/usr/bin", "/usr/sbin", "/bin", "/sbin", "/etc", Path.Combine("dotnet", "sdk") }; if (protectedPaths.Any(p => fullPath.Contains(p, StringComparison.OrdinalIgnoreCase))) { _logger.LogWarning("Attempted to write to a potentially protected system path: {FullPath}", fullPath); return false; } string? directory = Path.GetDirectoryName(fullPath); if (directory == null || !Directory.Exists(directory)) { _logger.LogWarning("Target directory for writing does not exist: {Directory}", directory ?? "N/A"); return false; } return true; } catch (Exception ex) { _logger.LogWarning(ex, "Exception while checking if path is writable: {FilePath}", filePath); return false; } }

        #endregion

    }
}
// --- END OF FILE ErrorFixer.cs --- AJUSTE FINAL PARSEO RUTAS