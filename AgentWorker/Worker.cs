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

                var projectName = SanitizeFileName(prompt.Titulo);
                var rutaProyecto = Path.Combine(Directory.GetCurrentDirectory(), "output", projectName);

                _logger.LogInformation("▶️ Procesando prompt '{Titulo}' → carpeta '{Ruta}'",
                                       prompt.Titulo, rutaProyecto);

                string[] backlog;
                try
                {
                    backlog = await _planificador.ConvertirPromptABacklog(prompt);
                }
                catch
                {
                    backlog = Array.Empty<string>();
                }

                if (backlog.Length > 0)
                {
                    foreach (var tarea in backlog)
                    {
                        try
                        {
                            await _desarrollador.GenerarCodigoParaTarea(prompt, tarea);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Error generando tarea '{Tarea}'", tarea);
                        }
                    }

                    // Solo intento corregir/compilar si realmente generé código
                    await CorregirYRecompilarAsync(rutaProyecto);
                }
                else
                {
                    _logger.LogWarning("⚠️ No se generaron tareas para '{Titulo}', omito build.", prompt.Titulo);
                }

                _logger.LogInformation("✅ Ciclo completado para prompt '{Titulo}'", prompt.Titulo);
            }
            _logger.LogInformation("🛑 Worker detenido.");
        }
        // -----------------------------------------------------------
        // Helper para sanear nombres de carpeta/proyecto
        // -----------------------------------------------------------
        private static string SanitizeFileName(string input)
        {
            var sb = new StringBuilder();
            foreach (var c in input)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
            }
            var result = sb.ToString().ToLowerInvariant();
            return result.Length > 40 ? result.Substring(0, 40) : result;
        }

        private async Task CorregirYRecompilarAsync(string rutaProyecto)
        {
            if (!Directory.Exists(rutaProyecto))
            {
                _logger.LogWarning("📁 Carpeta '{Ruta}' no existe, no se puede compilar.", rutaProyecto);
                return;
            }

            var buildLog = Path.Combine(rutaProyecto, "build_errors.log");
            _logger.LogInformation("🔨 Intentando compilación en '{RutaProyecto}'…", rutaProyecto);

            if (await EjecutarBuildAsync(rutaProyecto))
            {
                _logger.LogInformation("✅ Proyecto compiló sin errores tras generación.");
                return;
            }

            _logger.LogWarning("⚠️ Compilación fallida. Iniciando corrección automática…");
            var archivosCorregidos = await _errorFixer.CorregirErroresDeCompilacionAsync(rutaProyecto);

            if (archivosCorregidos.Count == 0)
            {
                _logger.LogError("❌ No se corrigieron archivos. Revisa '{Log}'", buildLog);
                return;
            }

            _logger.LogInformation("🔄 Correcciones aplicadas a {Count} archivos. Reintentando compilación…", archivosCorregidos.Count);
            if (await EjecutarBuildAsync(rutaProyecto))
            {
                _logger.LogInformation("✅ Proyecto compiló correctamente tras corrección.");
                foreach (var file in archivosCorregidos)
                    _store.Remove(file);
            }
            else
            {
                _logger.LogError("❌ Recompilación fallida de nuevo. Revisa build_errors_after_fix.log");
            }
        }


        private async Task<bool> EjecutarBuildAsync(string rutaProyecto)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build",
                WorkingDirectory = rutaProyecto,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi)!;
            var outp = await p.StandardOutput.ReadToEndAsync();
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            if (p.ExitCode != 0)
            {
                var postLog = Path.Combine(rutaProyecto, "build_errors_after_fix.log");
                await File.WriteAllTextAsync(postLog, outp + Environment.NewLine + err);
                return false;
            }

            return true;
        }
    }
}
