
// --- START OF FILE CodeCompletenessCheckerAgent.cs --- CORREGIDO Firma Método

using Microsoft.Extensions.Logging;
using Shared; // <--- Asegurar que Shared esté referenciado
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infraestructura
{
    // Asegurar que implementa la interfaz correcta
    public class CodeCompletenessCheckerAgent : ICodeCompletenessCheckerAgent
    {
        private readonly GeminiClient _gemini;
        private readonly ILogger<CodeCompletenessCheckerAgent> _logger;
        private const int MinLineCountThreshold = 5;
        private const int MinCharCountThreshold = 100;
        private static readonly HashSet<string> BaseFilesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_Imports.razor", "Error.cshtml", "Error.cshtml.cs", "_Host.cshtml" };

        public CodeCompletenessCheckerAgent(GeminiClient gemini, ILogger<CodeCompletenessCheckerAgent> logger)
        {
            _gemini = gemini;
            _logger = logger;
        }

        // *** CORRECCIÓN CLAVE: Usar Shared.Prompt explícitamente ***
        public async Task<List<string>> EnsureCodeCompletenessAsync(string projectPath, Shared.Prompt originalPrompt, string[] backlog)
        {
            var modifiedFiles = new List<string>();
            _logger.LogInformation("🔍 Iniciando verificación completitud código en: {ProjectPath}", projectPath);
            IEnumerable<string> potentialFiles;
            try { potentialFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)); }
            catch (DirectoryNotFoundException) { _logger.LogWarning("Directorio {ProjectPath} no encontrado.", projectPath); return modifiedFiles; }

            foreach (var filePath in potentialFiles)
            {
                var fileName = Path.GetFileName(filePath);
                if (BaseFilesToIgnore.Contains(fileName)) { _logger.LogTrace("Ignorando base: {FileName}", fileName); continue; }
                _logger.LogDebug("Verificando: {FilePath}", filePath);
                string content; int lineCount = 0;
                try { content = await File.ReadAllTextAsync(filePath); lineCount = content.Split('\n').Length; }
                catch (Exception ex) { _logger.LogError(ex, "Error leyendo {FilePath}. Omitiendo.", filePath); continue; }

                string trimmedContent = content.Trim();
                if (string.IsNullOrWhiteSpace(trimmedContent) || lineCount <= MinLineCountThreshold || trimmedContent.Length <= MinCharCountThreshold)
                {
                    _logger.LogWarning("⚠️ Archivo '{FileName}' incompleto (Líneas: {LineCount}, Chars: {CharCount}). Regenerando...", fileName, lineCount, trimmedContent.Length);
                    try
                    {
                        string inferredPurpose = $"Generar contenido completo para '{fileName}' parte de '{originalPrompt.Titulo}'.";
                        string regenerationPrompt = CreateRegenerationPrompt(originalPrompt, inferredPurpose, fileName);
                        string newContent = await _gemini.GenerarAsync(regenerationPrompt);
                        string cleanedNewContent = LimpiarCodigoGemini(newContent);
                        if (!string.IsNullOrWhiteSpace(cleanedNewContent)) { _logger.LogInformation("💾 Regenerando {FileName} (Nueva Longitud: {Length})", fileName, cleanedNewContent.Length); await File.WriteAllTextAsync(filePath, cleanedNewContent); modifiedFiles.Add(filePath); }
                        else { _logger.LogWarning("Regeneración para '{FileName}' vacía.", fileName); }
                    }
                    catch (Exception ex) { _logger.LogError(ex, "❌ Falló regeneración {FileName}.", fileName); }
                }
                else { _logger.LogDebug("Archivo '{FileName}' OK (Líneas: {LineCount}, Chars: {CharCount}).", fileName, lineCount, trimmedContent.Length); }
            }
            _logger.LogInformation("✅ Verificación completitud finalizada. {Count} archivos modificados.", modifiedFiles.Count);
            return modifiedFiles;
        }

        // *** CORRECCIÓN CLAVE: Usar Shared.Prompt explícitamente ***
        private string CreateRegenerationPrompt(Shared.Prompt originalPrompt, string inferredPurpose, string targetFileName)
        {
            string fileTypeHint = targetFileName.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ? "un archivo Blazor Razor (.razor)" : "un archivo C# (.cs)";
            return @$"Contexto General del Proyecto (Prompt Original):
{originalPrompt.Descripcion}
Tarea Específica (Inferida):
{inferredPurpose}
Instrucciones:
1. Basándote en CONTEXTO y TAREA, genera CÓDIGO FUENTE COMPLETO y funcional para '{targetFileName}' ({fileTypeHint}).
2. Debe estar completo: 'usings', namespace, clase/componente, métodos, lógica @code, etc.
3. NO asumas que partes existen. Genera TODO el contenido.
4. Sigue convenciones C#/.NET 8/Blazor.
5. Incluye comentarios XML `<summary>` (si es C#).
6. Devuelve ÚNICAMENTE el código fuente completo. Sin explicaciones, sin markdown (```).";
        }

        private string LimpiarCodigoGemini(string codigo) { if (string.IsNullOrWhiteSpace(codigo)) return ""; var lines = codigo.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Select(l => l.TrimEnd()).ToList(); if (lines.Count > 0 && lines[0].Trim().StartsWith("```")) { lines.RemoveAt(0); } if (lines.Count > 0 && lines[^1].Trim() == "```") { lines.RemoveAt(lines.Count - 1); } return string.Join(Environment.NewLine, lines).Trim(); }
    }
}
// --- END OF FILE CodeCompletenessCheckerAgent.cs --- CORREGIDO Firma Método