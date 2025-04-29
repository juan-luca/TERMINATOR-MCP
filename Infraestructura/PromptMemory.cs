using Shared;
using System.Text.Json;

namespace Infraestructura
{
    public class PromptMemory : IPromptStore
    {
        private readonly string _promptQueuePath = "prompt-queue.json";
        private readonly List<Prompt> _queue = new();

        public PromptMemory()
        {
            if (File.Exists(_promptQueuePath))
            {
                var json = File.ReadAllText(_promptQueuePath);
                var prompts = JsonSerializer.Deserialize<List<Prompt>>(json);
                if (prompts != null)
                {
                    _queue.AddRange(prompts);
                }
            }
        }

        public async Task GuardarAsync(Prompt prompt)
        {
            _queue.Add(prompt);
            var nuevoJson = JsonSerializer.Serialize(_queue, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_promptQueuePath, nuevoJson);
        }

        public Task<Prompt?> ObtenerSiguienteAsync()
        {
            if (_queue.Count == 0)
                return Task.FromResult<Prompt?>(null);

            var prompt = _queue[0];
            _queue.RemoveAt(0);

            // Actualizar el archivo
            var nuevoJson = JsonSerializer.Serialize(_queue, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_promptQueuePath, nuevoJson);

            return Task.FromResult<Prompt?>(prompt);
        }
    }
}
