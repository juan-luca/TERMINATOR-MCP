using Shared;
using Infraestructura;

var builder = WebApplication.CreateBuilder(args);

// REGISTRÁ TODOS LOS SERVICIOS ANTES DE builder.Build()
builder.Services.AddSingleton<GeminiClient>();
builder.Services.AddSingleton<IPlanificadorAgent, PlanificadorAgent>();
builder.Services.AddSingleton<IPromptStore, PromptStoreArchivo>();


var app = builder.Build();

// ENDPOINT DE TEST
app.MapPost("/prompt", async (Prompt prompt, IPromptStore store) =>
{
    await store.GuardarAsync(prompt);
    return Results.Ok("Prompt recibido correctamente.");
});

app.Run();
