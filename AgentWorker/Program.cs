using AgentWorker;
using Infraestructura;
using Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Opcional: Forzar logs por consola
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[HH:mm:ss] ";
});

// 1) Habilitar logging
builder.Services.AddLogging();

// 2) Registrar infraestructuras
builder.Services.AddSingleton<IPromptStore, PromptStoreArchivo>();
builder.Services.AddSingleton<PromptStoreArchivo>();
builder.Services.AddSingleton<GeminiClient>();
builder.Services.AddSingleton<CorrectedErrorsStore>();
builder.Services.AddSingleton<IErrorFixer, ErrorFixer>();

// 3) Registrar agentes (planificador + desarrollador)
builder.Services.AddSingleton<IPlanificadorAgent, PlanificadorAgent>();
builder.Services.AddSingleton<IDesarrolladorAgent, DesarrolladorAgent>();

// 4) Registrar Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
