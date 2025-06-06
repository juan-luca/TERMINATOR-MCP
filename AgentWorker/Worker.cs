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
using Infraestructura.Memory;
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
        private readonly ExecutionMemoryStore _memory;

        private readonly int _maxCorrectionCycles;

        public Worker(
            ILogger<Worker> logger,
            IPromptStore promptStore,
            IPlanificadorAgent planificador,
            IDesarrolladorAgent desarrollador,
            IErrorFixer errorFixer,
            CorrectedErrorsStore store,
            ICodeCompletenessCheckerAgent completenessChecker,
            ExecutionMemoryStore memory)
        {
            _logger = logger;
            _promptStore = promptStore;
            _planificador = planificador;
            _desarrollador = desarrollador;
            _errorFixer = errorFixer;
            _store = store;
            _completenessChecker = completenessChecker;
            _memory = memory;
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
                bool buildSuccess = false;
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
                        // --- Completeness Check ---
                        _logger.LogInformation("🔍 Verificando completitud del código generado para '{Titulo}'...", prompt.Titulo);
                        try
                        {
                            // EnsureCodeCompletenessAsync espera Shared.Prompt
                            var archivosRegenerados = await _completenessChecker.EnsureCodeCompletenessAsync(rutaProyecto, prompt, backlog);
                            if (archivosRegenerados.Any()) { _logger.LogInformation("🔄 {Count} archivos fueron regenerados/completados durante la verificación.", archivosRegenerados.Count); }
                            else { _logger.LogInformation("✅ Verificación completitud: No se necesitaron regeneraciones para '{Titulo}'.", prompt.Titulo); }
                        }
                        catch (Exception ex) { _logger.LogError(ex, "❌ Error durante la verificación de completitud para {RutaProyecto}.", rutaProyecto); }
                        // --- End Completeness Check ---

                        _logger.LogInformation("🏁 Fin de generación para '{Titulo}'. Iniciando compilación/corrección...", prompt.Titulo);
                        // await CorregirYRecompilarAsync(rutaProyecto); // Old call
                        buildSuccess = await CorregirYRecompilarAsync(rutaProyecto);
                        if (buildSuccess)
                        {
                            _logger.LogInformation("✅✅✅ PROYECTO FINAL COMPILADO EXITOSAMENTE para '{Titulo}' en '{RutaProyecto}'.", prompt.Titulo, rutaProyecto);
                        }
                        else
                        {
                            _logger.LogError("❌❌❌ FALLÓ LA COMPILACIÓN FINAL del proyecto para '{Titulo}' en '{RutaProyecto}' después de múltiples intentos.", prompt.Titulo, rutaProyecto);
                        }
                    }
                    else if (!seGeneroCodigo)
                    {
                        _logger.LogWarning("⚠️ No se generó código para '{Titulo}'. Omitiendo build.", prompt.Titulo);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ Backlog vacío para '{Titulo}', no se generó código.", prompt.Titulo);
                }

                _logger.LogInformation("✅ Ciclo completado para prompt '{Titulo}'", prompt.Titulo);

                _memory.Add(new ExecutionMemoryEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Prompt = prompt,
                    Backlog = backlog,
                    BuildSuccess = buildSuccess,
                    ProjectPath = rutaProyecto,
                    CommitHash = GetCurrentCommitHash()
                });
            }
            _logger.LogInformation("🛑 Worker detenido.");
        }

        #region Helper Methods
        private static string SanitizeFileName(string input)
        {
            var sb = new StringBuilder();
            bool lastWasHyphen = true;
            foreach (var c in input.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                    lastWasHyphen = false;
                }
                else if (c == '-' || char.IsWhiteSpace(c))
                {
                    if (!lastWasHyphen)
                    {
                        sb.Append('-');
                        lastWasHyphen = true;
                    }
                }
            }
            if (sb.Length > 0 && sb[^1] == '-')
            {
                sb.Length--;
            }
            var result = sb.ToString().ToLowerInvariant();
            const int maxLength = 50;
            if (result.Length > maxLength)
            {
                result = result.Substring(0, maxLength).TrimEnd('-');
            }
            return string.IsNullOrWhiteSpace(result) ? "proyecto-generado" : result;
        }

        private async Task<bool> CorregirYRecompilarAsync(string rutaProyecto)
        {
            if (!Directory.Exists(rutaProyecto))
            {
                _logger.LogWarning("📁 Carpeta '{Ruta}' no existe, no se puede compilar.", rutaProyecto);
                return false;
            }

            string initialBuildLogName = "build_errors.log"; // Base name for the first attempt
            string postFixBuildLogPrefix = "build_errors_after_fix_attempt_"; // Prefix for subsequent attempts

            for (int cycle = 1; _maxCorrectionCycles < 1 || cycle <= _maxCorrectionCycles; cycle++)
            {
                _logger.LogInformation("🔄 CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | Intentando compilación en '{RutaProyecto}'...", cycle, _maxCorrectionCycles, rutaProyecto);

                // Determine current log path: initial log for cycle 1, prefixed for others.
                string currentBuildLogPath = Path.Combine(rutaProyecto, cycle == 1 ? initialBuildLogName : $"{postFixBuildLogPrefix}{cycle -1}.log");
                string previousBuildLogPath = Path.Combine(rutaProyecto, cycle == 1 ? initialBuildLogName : $"{postFixBuildLogPrefix}{cycle - 2}.log"); // For cleanup, if cycle > 1 and previous was a fix log

                if (await EjecutarBuildAsync(rutaProyecto, currentBuildLogPath))
                {
                    _logger.LogInformation("✅ CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | Proyecto compiló sin errores.", cycle, _maxCorrectionCycles);
                    DeleteLogFile(currentBuildLogPath);
                    if (cycle > 1)
                    {
                        // Clean up the specific log from the previous failed attempt that led to this successful fix cycle
                        DeleteLogFile(previousBuildLogPath);
                        // Also clean up the very first build log if it exists and we are past cycle 1
                        string veryFirstLog = Path.Combine(rutaProyecto, initialBuildLogName);
                        if (File.Exists(veryFirstLog) && currentBuildLogPath != veryFirstLog) DeleteLogFile(veryFirstLog);
                    }
                    return true;
                }

                _logger.LogWarning("⚠️ CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | Compilación fallida. Revisar '{Log}'.", cycle, _maxCorrectionCycles, currentBuildLogPath);

                if (_maxCorrectionCycles < 1 || cycle < _maxCorrectionCycles)
                {
                        _logger.LogInformation("🛠️ CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | Iniciando corrección automática...", cycle, _maxCorrectionCycles);
                    List<string> archivosCorregidos;
                    try
                    {
                        archivosCorregidos = await _errorFixer.CorregirErroresDeCompilacionAsync(rutaProyecto);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | Error crítico durante la ejecución de ErrorFixer para {RutaProyecto}.", cycle, _maxCorrectionCycles, rutaProyecto);
                        return false;
                    }

                    if (archivosCorregidos.Count == 0)
                    {
                        _logger.LogWarning("🚫 CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | ErrorFixer no aplicó correcciones. La compilación falló y no se pudo corregir. Abortando más intentos.", cycle, _maxCorrectionCycles);
                        return false;
                    }
                    _logger.LogInformation("🔄 CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | Se aplicaron correcciones a {Count} archivos. Se reintentará la compilación.", cycle, _maxCorrectionCycles, archivosCorregidos.Count);
                    // Loop continues to next build attempt, log for that attempt will be named with postFixBuildLogPrefix{cycle}.log
                }
                else
                {
                    _logger.LogError("❌ CICLO DE CORRECCIÓN {Cycle}/{MaxCycles} | La compilación final falló después de todos los intentos. Revisar: '{Log}'", cycle, _maxCorrectionCycles, currentBuildLogPath);
                    return false;
                }
            }
            _logger.LogWarning("🏁 CICLO DE CORRECCIÓN | Se alcanzó el final inesperado del bucle CorregirYRecompilarAsync. Asumiendo fallo.");
            return false;
        }

        private async Task<bool> EjecutarBuildAsync(string rutaProyecto, string logFilePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build --nologo -v q", // -v q for quiet, shows only errors/warnings
                WorkingDirectory = rutaProyecto,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            Process? process = null;
            try
            {
                process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                var processExitedTcs = new TaskCompletionSource<bool>();

                process.OutputDataReceived += (sender, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); };
                process.ErrorDataReceived += (sender, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };
                process.Exited += (sender, args) => { processExitedTcs.TrySetResult(true); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completedTask = await Task.WhenAny(processExitedTcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));

                if (completedTask != processExitedTcs.Task || (process != null && process.ExitCode != 0))
                {
                    if (completedTask != processExitedTcs.Task)
                    {
                        _logger.LogError("⏰ Timeout esperando 'dotnet build' en {Ruta}. Forzando cierre.", rutaProyecto);
                        try { if(process !=null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { /* Ignore */ }
                        await File.WriteAllTextAsync(logFilePath, $"TIMEOUT (>60s) DURANTE LA COMPILACIÓN.{Environment.NewLine}Output parcial:{Environment.NewLine}{outputBuilder}{Environment.NewLine}Error parcial:{Environment.NewLine}{errorBuilder}");
                        return false;
                    }

                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();
                    // Combine stdout and stderr for the log file, as warnings might go to stdout with -v q
                    string fullLog = $"--- Standard Output (dotnet build -v q) ---{Environment.NewLine}{output}{Environment.NewLine}--- Standard Error (dotnet build -v q) ---{Environment.NewLine}{error}{Environment.NewLine}Exit Code: {process?.ExitCode ?? -1}";
                    await File.WriteAllTextAsync(logFilePath, fullLog);
                    _logger.LogDebug("Resultado de Build (ExitCode={ExitCode}) guardado en {LogPath}", process?.ExitCode ?? -1, logFilePath);
                    return false; // Build failed
                }
                else
                {
                    // Build succeeded, ExitCode is 0
                    string errorOutput = errorBuilder.ToString(); // Stderr might still contain warnings or other info
                    string stdOutput = outputBuilder.ToString();  // Stdout might also contain warnings with -v q
                    if (!string.IsNullOrWhiteSpace(stdOutput) || !string.IsNullOrWhiteSpace(errorOutput))
                    {
                       _logger.LogDebug("Build en {RutaProyecto} exitoso (ExitCode=0), pero con output/stderr (posibles warnings). Stdout: {OutputLength} chars, Stderr: {ErrorLength} chars. Ver log si se generó (se borra en éxito).",
                                        rutaProyecto, stdOutput.Length, errorOutput.Length);
                    }
                    else
                    {
                        _logger.LogDebug("Build exitoso (ExitCode=0) en {RutaProyecto} sin output/error adicional.", rutaProyecto);
                    }
                    DeleteLogFile(logFilePath); // Clean up log on success
                    return true; // Build succeeded
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Excepción al ejecutar 'dotnet build' en {RutaProyecto}.", rutaProyecto);
                await File.WriteAllTextAsync(logFilePath, $"EXCEPCIÓN AL EJECUTAR DOTNET BUILD:{Environment.NewLine}{ex}");
                return false;
            }
            finally
            {
                process?.Dispose();
            }
        }
        private void DeleteLogFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    _logger.LogDebug("🗑️ Archivo de log eliminado: {FilePath}", filePath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "⚠️ No se pudo eliminar el archivo de log: {FilePath}", filePath);
                }
            }
        }

        private static string GetCurrentCommitHash()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return string.Empty;
                string output = process.StandardOutput.ReadLine() ?? string.Empty;
                process.WaitForExit(3000);
                return output.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion
    }
}
// --- END OF FILE Worker.cs --- CORREGIDO Tipos Prompt