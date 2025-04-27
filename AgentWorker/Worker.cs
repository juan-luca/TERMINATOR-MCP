using Shared;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IPromptStore _promptStore;
    private readonly IPlanificadorAgent _planificador;
    private readonly IDesarrolladorAgent _desarrollador;

    public Worker(
        ILogger<Worker> logger,
        IPromptStore promptStore,
        IPlanificadorAgent planificador,
        IDesarrolladorAgent desarrollador)
    {
        _logger = logger;
        _promptStore = promptStore;
        _planificador = planificador;
        _desarrollador = desarrollador;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Agente desarrollador en ejecución...");

        while (!stoppingToken.IsCancellationRequested)
        {
            var prompt = await _promptStore.ObtenerSiguienteAsync();
            if (prompt != null)
            {
                _logger.LogInformation("📦 Procesando requerimiento: {0}", prompt.Titulo);
                var tareas = await _planificador.ConvertirPromptABacklog(prompt);

                foreach (var tarea in tareas)
                {
                    _logger.LogInformation("🛠 Generando código para: {0}", tarea);
                    await _desarrollador.GenerarCodigoParaTarea(prompt, tarea);

                }

                _logger.LogInformation("✅ Requerimiento completado.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
