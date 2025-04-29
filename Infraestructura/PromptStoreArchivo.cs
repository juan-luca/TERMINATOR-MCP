// Infraestructura/PromptStoreArchivo.cs

using System.Text.Json;
using Shared;

namespace Infraestructura
{
    public class PromptStoreArchivo : IPromptStore
    {
        // Usamos AppContext.BaseDirectory para que sea absoluto y compartido
        private static readonly string FilePath =
           Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "prompt-queue.json"));


        private readonly object _lock = new();

        public async Task GuardarAsync(Prompt prompt)
        {
            lock (_lock)
            {
                var prompts = ReadAll();
                prompts.Add(prompt);
                WriteAll(prompts);
            }
        }

        public Task<Prompt?> ObtenerSiguienteAsync()
        {
            lock (_lock)
            {
                var prompts = ReadAll();
                if (prompts.Count == 0)
                    return Task.FromResult<Prompt?>(null);

                var primero = prompts[0];
                prompts.RemoveAt(0);
                WriteAll(prompts);
                return Task.FromResult<Prompt?>(primero);
            }
        }

        // Lee el archivo de disco y devuelve siempre una lista (nunca null)
        private List<Prompt> ReadAll()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<Prompt>();

                var json = File.ReadAllText(FilePath).Trim();
                if (string.IsNullOrEmpty(json))
                    return new List<Prompt>();

                // Intentamos deserializar como lista
                return JsonSerializer.Deserialize<List<Prompt>>(json)
                       ?? new List<Prompt>();
            }
            catch (JsonException)
            {
                // Si el JSON está corrupto o es un objeto simple, lo limpiamos a []
                File.WriteAllText(FilePath, "[]");
                return new List<Prompt>();
            }
        }

        // Serializa y guarda en disco
        private void WriteAll(List<Prompt> prompts)
        {
            var json = JsonSerializer.Serialize(prompts, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(FilePath, json);
        }
    }
}
