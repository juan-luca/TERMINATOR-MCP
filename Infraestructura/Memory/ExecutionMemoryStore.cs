using System.Text.Json;

namespace Infraestructura.Memory
{
    public class ExecutionMemoryStore
    {
        private const string MemoryFile = "execution_memory.json";
        private readonly object _lock = new();
        private readonly List<ExecutionMemoryEntry> _entries;

        public ExecutionMemoryStore()
        {
            if (File.Exists(MemoryFile))
            {
                try
                {
                    var json = File.ReadAllText(MemoryFile);
                    _entries = JsonSerializer.Deserialize<List<ExecutionMemoryEntry>>(json) ?? new();
                }
                catch
                {
                    _entries = new();
                }
            }
            else
            {
                _entries = new();
            }
        }

        public void Add(ExecutionMemoryEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
                Persist();
            }
        }

        public IReadOnlyList<ExecutionMemoryEntry> GetAll()
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }

        private void Persist()
        {
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MemoryFile, json);
        }
    }
}
