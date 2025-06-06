// --- START OF FILE Program.cs (AgentWorker) --- MODIFIED

using AgentWorker;
using Infraestructura;
using Infraestructura.Memory;
using Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Optional: Force console logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "[HH:mm:ss] ";
    // Set minimum log level if needed (e.g., from appsettings.json)
    // options.SingleLine = true;
});
// Configure log level from appsettings
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));


// 1) Enable logging (already implicit with CreateApplicationBuilder)
// builder.Services.AddLogging(); // Not strictly necessary if AddSimpleConsole/AddConfiguration is used

// 2) Register infrastructure services
builder.Services.AddSingleton<IPromptStore, PromptStoreArchivo>();
// builder.Services.AddSingleton<PromptStoreArchivo>(); // No need to register concrete class if interface is registered
builder.Services.AddSingleton<GeminiClient>();
builder.Services.AddSingleton<CorrectedErrorsStore>();
builder.Services.AddSingleton<IErrorFixer, ErrorFixer>();
builder.Services.AddSingleton<ExecutionMemoryStore>();

// 3) Register Agents
builder.Services.AddSingleton<IPlanificadorAgent, PlanificadorAgent>();
builder.Services.AddSingleton<IDesarrolladorAgent, DesarrolladorAgent>();
// *** ADDED: Register the new agent ***
builder.Services.AddSingleton<ICodeCompletenessCheckerAgent, CodeCompletenessCheckerAgent>();

// 4) Register Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
// --- END OF FILE Program.cs (AgentWorker) --- MODIFIED