using System.Text.Json;
using Shared;

namespace Infraestructura;

public class PromptStoreArchivo : IPromptStore
{
    private const string FilePath = "../prompt-queue.json";
    private readonly object _lock = new();

    public async Task GuardarAsync(Prompt prompt)
    {
        var prompts = await LeerTodosAsync();
        prompts.Add(prompt);
        await EscribirAsync(prompts);
    }

    public async Task<Prompt?> ObtenerSiguienteAsync()
    {
        var prompts = await LeerTodosAsync();
        if (prompts.Count == 0) return null;

        var primero = prompts[0];
        prompts.RemoveAt(0);
        await EscribirAsync(prompts);
        return primero;
    }

    private async Task<List<Prompt>> LeerTodosAsync()
    {
        if (!File.Exists(FilePath))
            return new List<Prompt>();

        var json = await File.ReadAllTextAsync(FilePath);
        return JsonSerializer.Deserialize<List<Prompt>>(json) ?? new();
    }

    private async Task EscribirAsync(List<Prompt> prompts)
    {
        var json = JsonSerializer.Serialize(prompts, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(FilePath, json);
    }
}
