// AgentApi/Program.cs
using System.IO;
using Infraestructura;
using Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// 1) Logging por consola
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(opts =>
{
    opts.TimestampFormat = "[HH:mm:ss] ";
});

// 2) Registrar la cola de prompts
builder.Services.AddSingleton<IPromptStore, PromptStoreArchivo>();

var app = builder.Build();

// 3) Endpoint POST /prompt
app.MapPost("/prompt", async (Prompt prompt, IPromptStore store, ILogger<Program> log) =>
{
    log.LogInformation("📥 Recibido prompt: {Titulo}", prompt.Titulo);

    // Guardamos
    await store.GuardarAsync(prompt);

    // Loggeamos en qué archivo quedó
    var path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "prompt-queue.json"));
    log.LogInformation("💾 Prompt guardado en: {Path}", path);

    return Results.Ok(new { mensaje = "Prompt recibido correctamente." });
});

// 4) Forzar URL de escucha en localhost:5297
app.Run("http://localhost:5297");
