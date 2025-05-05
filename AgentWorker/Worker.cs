// --- START OF FILE Worker.cs --- VERIFY THIS IS YOUR LOCAL VERSION

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared;
using Infraestructura;
using System.Text;
using System.Collections.Generic; // Needed for List<string>

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

        public Worker(
            ILogger<Worker> logger,
            IPromptStore promptStore,
            IPlanificadorAgent planificador,
            IDesarrolladorAgent desarrollador,
            IErrorFixer errorFixer,
            CorrectedErrorsStore store)
        {
            _logger = logger;
            _promptStore = promptStore;
            _planificador = planificador;
            _desarrollador = desarrollador;
            _errorFixer = errorFixer;
            _store = store;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔄 Worker iniciado y listo para procesar prompts.");
            while (!stoppingToken.IsCancellationRequested)
            {
                Prompt? prompt;
                try
                {
                    prompt = await _promptStore.ObtenerSiguienteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ No pude leer siguiente prompt.");
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                if (prompt == null)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                var projectName = SanitizeFileName(prompt.Titulo); // Use the local SanitizeFileName
                var outputBaseDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
                Directory.CreateDirectory(outputBaseDir);
                var rutaProyecto = Path.Combine(outputBaseDir, projectName);

                _logger.LogInformation("▶️ Procesando prompt '{Titulo}' → carpeta '{Ruta}'",
                                       prompt.Titulo, rutaProyecto);

                string[] backlog;
                try
                {
                    backlog = await _planificador.ConvertirPromptABacklog(prompt);
                    _logger.LogInformation("📋 Backlog generado con {Count} tareas para '{Titulo}'.", backlog.Length, prompt.Titulo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error al generar backlog para '{Titulo}'. Omitiendo prompt.", prompt.Titulo);
                    backlog = Array.Empty<string>();
                    continue;
                }

                bool seGeneroCodigo = false;
                if (backlog.Length > 0)
                {
                    foreach (var tarea in backlog)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        try
                        {
                            await _desarrollador.GenerarCodigoParaTarea(prompt, tarea);
                            seGeneroCodigo = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Error en Worker procesando la tarea '{Tarea}' para '{Titulo}'.", tarea, prompt.Titulo);
                        }
                    }

                    if (seGeneroCodigo && !stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("🏁 Fin de generación de tareas para '{Titulo}'. Iniciando compilación/corrección...", prompt.Titulo);
                        await CorregirYRecompilarAsync(rutaProyecto);
                    }
                    else if (!seGeneroCodigo)
                    {
                        _logger.LogWarning("⚠️ No se generó código para ninguna tarea de '{Titulo}'. Omitiendo build.", prompt.Titulo);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ Backlog vacío para '{Titulo}', no se generó código ni se intentó compilar.", prompt.Titulo);
                }

                _logger.LogInformation("✅ Ciclo completado para prompt '{Titulo}'", prompt.Titulo);
            }
            _logger.LogInformation("🛑 Worker detenido.");
        }

        // Helper SanitizeFileName (should ideally be in a shared utility class)
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
            if (sb.Length > 0 && sb[^1] == '-') { sb.Length--; }
            var result = sb.ToString().ToLowerInvariant();
            const int maxLength = 50;
            if (result.Length > maxLength) { result = result.Substring(0, maxLength).TrimEnd('-'); }
            return string.IsNullOrWhiteSpace(result) ? "proyecto-generado" : result;
        }


        private async Task CorregirYRecompilarAsync(string rutaProyecto)
        {
            if (!Directory.Exists(rutaProyecto))
            {
                _logger.LogWarning("📁 Carpeta '{Ruta}' no existe, no se puede compilar.", rutaProyecto);
                return;
            }

            var initialBuildLog = Path.Combine(rutaProyecto, "build_errors.log");
            var postFixBuildLog = Path.Combine(rutaProyecto, "build_errors_after_fix.log");

            _logger.LogInformation("🔨 Intentando compilación inicial en '{RutaProyecto}'…", rutaProyecto);

            if (await EjecutarBuildAsync(rutaProyecto, initialBuildLog))
            {
                _logger.LogInformation("✅ Proyecto compiló sin errores tras generación.");
                DeleteLogFile(initialBuildLog);
                return;
            }

            _logger.LogWarning("⚠️ Compilación inicial fallida. Iniciando corrección automática (revisar '{Log}')...", initialBuildLog);
            List<string> archivosCorregidos = new List<string>();
            try
            {
                archivosCorregidos = await _errorFixer.CorregirErroresDeCompilacionAsync(rutaProyecto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error crítico durante la ejecución de ErrorFixer para {RutaProyecto}.", rutaProyecto);
                return;
            }

            if (archivosCorregidos.Count == 0)
            {
                _logger.LogError("❌ ErrorFixer no aplicó correcciones. La compilación falló y no se pudo corregir automáticamente. Revisa '{Log}'", initialBuildLog);
                return;
            }

            _logger.LogInformation("🔄 Se aplicaron correcciones a {Count} archivos. Reintentando compilación...", archivosCorregidos.Count);

            if (await EjecutarBuildAsync(rutaProyecto, postFixBuildLog))
            {
                _logger.LogInformation("✅ Proyecto compiló correctamente tras la corrección automática.");
                foreach (var file in archivosCorregidos)
                {
                    _store.Remove(file);
                }
                DeleteLogFile(initialBuildLog);
                DeleteLogFile(postFixBuildLog);
            }
            else
            {
                _logger.LogError("❌ La recompilación falló incluso después de aplicar correcciones automáticas. Revisa el log final: '{Log}'", postFixBuildLog);
            }
        }

        private async Task<bool> EjecutarBuildAsync(string rutaProyecto, string logFilePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build --nologo -v q", // Use quiet verbosity (-v q) unless errors
                WorkingDirectory = rutaProyecto,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            Process? process = null; // Declare outside try
            try
            {
                process = new Process { StartInfo = psi, EnableRaisingEvents = true }; // Enable raising events

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                var processExitedTcs = new TaskCompletionSource<bool>(); // To wait for exit event

                process.OutputDataReceived += (sender, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); };
                process.ErrorDataReceived += (sender, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };
                process.Exited += (sender, args) => { processExitedTcs.TrySetResult(true); }; // Signal exit

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for exit or timeout
                var completedTask = await Task.WhenAny(processExitedTcs.Task, Task.Delay(TimeSpan.FromSeconds(60))); // 60s timeout

                if (completedTask != processExitedTcs.Task || process.ExitCode != 0) // Check if timed out or failed
                {
                    if (completedTask != processExitedTcs.Task) // Handle timeout specifically
                    {
                        _logger.LogError("⏰ Timeout esperando 'dotnet build' en {Ruta}. Forzando cierre.", rutaProyecto);
                        try { process.Kill(entireProcessTree: true); } catch { /* Ignore */ }
                        await File.WriteAllTextAsync(logFilePath, $"TIMEOUT (>60s) DURANTE LA COMPILACIÓN.{Environment.NewLine}Output parcial:{Environment.NewLine}{outputBuilder}{Environment.NewLine}Error parcial:{Environment.NewLine}{errorBuilder}");
                        return false;
                    }

                    // Handle non-zero exit code
                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();
                    string fullLog = $"--- Standard Output ---{Environment.NewLine}{output}{Environment.NewLine}--- Standard Error ---{Environment.NewLine}{error}{Environment.NewLine}Exit Code: {process.ExitCode}";
                    await File.WriteAllTextAsync(logFilePath, fullLog);
                    _logger.LogDebug("Resultado de Build (ExitCode={ExitCode}) guardado en {LogPath}", process.ExitCode, logFilePath);
                    return false;
                }
                else // Process exited normally with code 0
                {
                    string errorOutput = errorBuilder.ToString();
                    if (!string.IsNullOrWhiteSpace(errorOutput))
                    {
                        _logger.LogDebug("Build exitoso pero con mensajes en Stderr (posibles warnings) en {RutaProyecto}:{NewLine}{ErrorOutput}", rutaProyecto, Environment.NewLine, errorOutput.Length > 500 ? errorOutput.Substring(0, 500) + "..." : errorOutput);
                    }
                    _logger.LogDebug("Build exitoso (ExitCode=0) en {RutaProyecto}.", rutaProyecto);
                    DeleteLogFile(logFilePath); // Attempt to delete previous log if build is now successful
                    return true;
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
                process?.Dispose(); // Ensure process resources are released
            }
        }

        // Helper to delete log files safely
        private void DeleteLogFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    _logger.LogDebug("Log file deleted: {FilePath}", filePath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not delete log file: {FilePath}", filePath);
                }
            }
        }
    }
}
// --- END OF FILE Worker.cs --- VERIFY THIS IS YOUR LOCAL VERSION