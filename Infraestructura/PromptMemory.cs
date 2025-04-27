using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;

namespace Infraestructura;

public class PromptMemory : IPromptStore
{
    private readonly Queue<Prompt> _prompts = new();

    public Task GuardarAsync(Prompt prompt)
    {
        _prompts.Enqueue(prompt);
        return Task.CompletedTask;
    }

    public Task<Prompt?> ObtenerSiguienteAsync()
    {
        return Task.FromResult(_prompts.Count > 0 ? _prompts.Dequeue() : null);
    }
}
