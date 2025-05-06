// --- START OF FILE Worker.cs --- CORREGIDO Tipos Prompt

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared; // <--- Asegurar que Shared esté referenciado
using Infraestructura;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace AgentWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IPromptStore _promptStore;
        private readonly IPlanificadorAgent _planificador;
        private readonly IDesarrolladorAgent _desarrollador;
        private readonly IErrorFixer _errorFixer;
        private readonly CorrectedErrorsStore _store;
        private readonly ICodeCompletenessCheckerAgent _completenessChecker; // Mantener inyectado

        public Worker(
            ILogger<Worker> logger,
            IPromptStore promptStore,
            IPlanificadorAgent planificador,
            IDesarrolladorAgent desarrollador,
            IErrorFixer errorFixer,
            CorrectedErrorsStore store,
            ICodeCompletenessCheckerAgent completenessChecker)
        {
            _logger = logger;
            _promptStore = promptStore;
            _planificador = planificador;
            _desarrollador = desarrollador;
            _errorFixer = errorFixer;
            _store = store;
            _completenessChecker = completenessChecker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔄 Worker iniciado y listo para procesar prompts.");
            while (!stoppingToken.IsCancellationRequested)
            {
                // *** CORRECCIÓN CLAVE: Usar Shared.Prompt explícitamente ***
                Shared.Prompt? prompt = null;
                string[] backlog = Array.Empty<string>();
                string projectName = "default-project";
                string rutaProyecto = Path.Combine(Directory.GetCurrentDirectory(), "output", projectName);

                try
                {
                    // ObtenerSiguienteAsync devuelve Shared.Prompt?
                    prompt = await _promptStore.ObtenerSiguienteAsync();

                    // *** CORRECCIÓN CLAVE: Comparación correcta con null ***
                    if (prompt == null)
                    {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    // *** CORRECCIÓN CLAVE: Acceder a propiedades de Shared.Prompt ***
                    projectName = SanitizeFileName(prompt.Titulo);
                    var outputBaseDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
                    Directory.CreateDirectory(outputBaseDir);
                    rutaProyecto = Path.Combine(outputBaseDir, projectName);

                    _logger.LogInformation("▶️ Procesando prompt '{Titulo}' → carpeta '{Ruta}'",
                                           prompt.Titulo, rutaProyecto);

                    // ConvertirPromptABacklog espera Shared.Prompt
                    backlog = await _planificador.ConvertirPromptABacklog(prompt);
                    _logger.LogInformation("📋 Backlog generado con {Count} tareas para '{Titulo}'.", backlog.Length, prompt.Titulo);

                }
                catch (Exception ex) { _logger.LogError(ex, "❌ Error obteniendo prompt o planificando para '{Titulo}'. Omitiendo.", prompt?.Titulo ?? "Prompt Desconocido"); await Task.Delay(1000, stoppingToken); continue; }


                bool seGeneroCodigo = false;
                if (backlog.Length > 0)
                {
                    foreach (var tarea in backlog)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        try
                        {
                            // GenerarCodigoParaTarea espera Shared.Prompt
                            await _desarrollador.GenerarCodigoParaTarea(prompt, tarea);
                            seGeneroCodigo = true;
                        }
                        catch (Exception ex) { _logger.LogError(ex, "❌ Error en Worker procesando la tarea '{Tarea}' para '{Titulo}'.", tarea, prompt.Titulo); }
                    }

                    if (seGeneroCodigo && !stoppingToken.IsCancellationRequested)
                    {
                        // --- Completeness Check (Sigue deshabilitado por ahora) ---
                        /*
                        _logger.LogInformation("🔍 (DISABLED) Verificando completitud del código generado para '{Titulo}'...", prompt.Titulo);
                        try
                        {
                            // EnsureCodeCompletenessAsync espera Shared.Prompt
                            var archivosRegenerados = await _completenessChecker.EnsureCodeCompletenessAsync(rutaProyecto, prompt, backlog);
                            if (archivosRegenerados.Any()) { _logger.LogInformation("🔄 {Count} archivos fueron regenerados/completados durante la verificación.", archivosRegenerados.Count); }
                            else { _logger.LogInformation("✅ Verificación completitud: No se necesitaron regeneraciones.", prompt.Titulo); }
                        }
                        catch (Exception ex) { _logger.LogError(ex, "❌ Error durante la verificación de completitud para {RutaProyecto}.", rutaProyecto); }
                        */
                        // --- End Disabled Step ---

                        _logger.LogInformation("🏁 Fin de generación para '{Titulo}'. Iniciando compilación/corrección...", prompt.Titulo);
                        await CorregirYRecompilarAsync(rutaProyecto);
                    }
                    else if (!seGeneroCodigo) { _logger.LogWarning("⚠️ No se generó código para '{Titulo}'. Omitiendo build.", prompt.Titulo); }
                }
                else { _logger.LogWarning("⚠️ Backlog vacío para '{Titulo}', no se generó código.", prompt.Titulo); }

                _logger.LogInformation("✅ Ciclo completado para prompt '{Titulo}'", prompt.Titulo);
            }
            _logger.LogInformation("🛑 Worker detenido.");
        }

        // ... (SanitizeFileName, CorregirYRecompilarAsync, EjecutarBuildAsync, DeleteLogFile methods sin cambios) ...
        #region Helper Methods (No changes needed here from previous version)
        private static string SanitizeFileName(string input) { var sb = new StringBuilder(); bool lastWasHyphen = true; foreach (var c in input.Trim()) { if (char.IsLetterOrDigit(c) || c == '_') { sb.Append(c); lastWasHyphen = false; } else if (c == '-' || char.IsWhiteSpace(c)) { if (!lastWasHyphen) { sb.Append('-'); lastWasHyphen = true; } } } if (sb.Length > 0 && sb[^1] == '-') { sb.Length--; } var result = sb.ToString().ToLowerInvariant(); const int maxLength = 50; if (result.Length > maxLength) { result = result.Substring(0, maxLength).TrimEnd('-'); } return string.IsNullOrWhiteSpace(result) ? "proyecto-generado" : result; }
        private async Task CorregirYRecompilarAsync(string rutaProyecto) { if (!Directory.Exists(rutaProyecto)) { _logger.LogWarning("📁 Carpeta '{Ruta}' no existe, no se puede compilar.", rutaProyecto); return; } var initialBuildLog = Path.Combine(rutaProyecto, "build_errors.log"); var postFixBuildLog = Path.Combine(rutaProyecto, "build_errors_after_fix.log"); _logger.LogInformation("🔨 Intentando compilación inicial en '{RutaProyecto}'…", rutaProyecto); if (await EjecutarBuildAsync(rutaProyecto, initialBuildLog)) { _logger.LogInformation("✅ Proyecto compiló sin errores tras generación/verificación."); DeleteLogFile(initialBuildLog); return; } _logger.LogWarning("⚠️ Compilación inicial fallida. Iniciando corrección automática (revisar '{Log}')...", initialBuildLog); List<string> archivosCorregidos = new List<string>(); try { archivosCorregidos = await _errorFixer.CorregirErroresDeCompilacionAsync(rutaProyecto); } catch (Exception ex) { _logger.LogError(ex, "❌ Error crítico durante la ejecución de ErrorFixer para {RutaProyecto}.", rutaProyecto); return; } if (archivosCorregidos.Count == 0) { _logger.LogError("❌ ErrorFixer no aplicó correcciones. La compilación falló y no se pudo corregir automáticamente. Revisa '{Log}'", initialBuildLog); return; } _logger.LogInformation("🔄 Se aplicaron correcciones a {Count} archivos. Reintentando compilación...", archivosCorregidos.Count); if (await EjecutarBuildAsync(rutaProyecto, postFixBuildLog)) { _logger.LogInformation("✅ Proyecto compiló correctamente tras la corrección automática."); foreach (var file in archivosCorregidos) { _store.Remove(file); } DeleteLogFile(initialBuildLog); DeleteLogFile(postFixBuildLog); } else { _logger.LogError("❌ La recompilación falló incluso después de aplicar correcciones automáticas. Revisa el log final: '{Log}'", postFixBuildLog); } }
        private async Task<bool> EjecutarBuildAsync(string rutaProyecto, string logFilePath) { var psi = new ProcessStartInfo { FileName = "dotnet", Arguments = "build --nologo -v q", WorkingDirectory = rutaProyecto, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 }; Process? process = null; try { process = new Process { StartInfo = psi, EnableRaisingEvents = true }; var outputBuilder = new StringBuilder(); var errorBuilder = new StringBuilder(); var processExitedTcs = new TaskCompletionSource<bool>(); process.OutputDataReceived += (sender, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); }; process.ErrorDataReceived += (sender, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); }; process.Exited += (sender, args) => { processExitedTcs.TrySetResult(true); }; process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); var completedTask = await Task.WhenAny(processExitedTcs.Task, Task.Delay(TimeSpan.FromSeconds(60))); if (completedTask != processExitedTcs.Task || process.ExitCode != 0) { if (completedTask != processExitedTcs.Task) { _logger.LogError("⏰ Timeout esperando 'dotnet build' en {Ruta}. Forzando cierre.", rutaProyecto); try { process.Kill(entireProcessTree: true); } catch { /* Ignore */ } await File.WriteAllTextAsync(logFilePath, $"TIMEOUT (>60s) DURANTE LA COMPILACIÓN.{Environment.NewLine}Output parcial:{Environment.NewLine}{outputBuilder}{Environment.NewLine}Error parcial:{Environment.NewLine}{errorBuilder}"); return false; } string output = outputBuilder.ToString(); string error = errorBuilder.ToString(); string fullLog = $"--- Standard Output ---{Environment.NewLine}{output}{Environment.NewLine}--- Standard Error ---{Environment.NewLine}{error}{Environment.NewLine}Exit Code: {process.ExitCode}"; await File.WriteAllTextAsync(logFilePath, fullLog); _logger.LogDebug("Resultado de Build (ExitCode={ExitCode}) guardado en {LogPath}", process.ExitCode, logFilePath); return false; } else { string errorOutput = errorBuilder.ToString(); if (!string.IsNullOrWhiteSpace(errorOutput)) { _logger.LogDebug("Build exitoso pero con mensajes en Stderr (posibles warnings) en {RutaProyecto}:{NewLine}{ErrorOutput}", rutaProyecto, Environment.NewLine, errorOutput.Length > 500 ? errorOutput.Substring(0, 500) + "..." : errorOutput); } _logger.LogDebug("Build exitoso (ExitCode=0) en {RutaProyecto}.", rutaProyecto); DeleteLogFile(logFilePath); return true; } } catch (Exception ex) { _logger.LogError(ex, "❌ Excepción al ejecutar 'dotnet build' en {RutaProyecto}.", rutaProyecto); await File.WriteAllTextAsync(logFilePath, $"EXCEPCIÓN AL EJECUTAR DOTNET BUILD:{Environment.NewLine}{ex}"); return false; } finally { process?.Dispose(); } }
        private void DeleteLogFile(string filePath) { if (File.Exists(filePath)) { try { File.Delete(filePath); _logger.LogDebug("Log file deleted: {FilePath}", filePath); } catch (IOException ex) { _logger.LogWarning(ex, "Could not delete log file: {FilePath}", filePath); } } }
        #endregion
    }
}
// --- END OF FILE Worker.cs --- CORREGIDO Tipos Prompt