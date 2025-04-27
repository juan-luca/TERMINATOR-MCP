using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Infraestructura;
using Shared;

var builder = Host.CreateApplicationBuilder(args);

// Registros de servicios
builder.Services.AddSingleton<IPromptStore, PromptStoreArchivo>();

builder.Services.AddSingleton<GeminiClient>();
builder.Services.AddSingleton<IPlanificadorAgent, PlanificadorAgent>();
builder.Services.AddSingleton<IDesarrolladorAgent, DesarrolladorAgent>();

// Worker principal (ciclo autónomo)
builder.Services.AddHostedService<Worker>();

// Ejecutar la app
var host = builder.Build();
host.Run();
