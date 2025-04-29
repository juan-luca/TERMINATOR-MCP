using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Infraestructura
{
    public class CorrectedErrorsStore
    {
        private readonly string _storePath = "corrected_errors.json";
        private Dictionary<string, string> _data;

        public CorrectedErrorsStore()
        {
            if (File.Exists(_storePath))
                _data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_storePath))
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            else
                _data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool WasCorrected(string filePath, string snippet)
        {
            var hash = ComputeHash(snippet);
            return _data.TryGetValue(filePath, out var existing) && existing == hash;
        }

        public void MarkCorrected(string filePath, string snippet)
        {
            var hash = ComputeHash(snippet);
            _data[filePath] = hash;
            Persist();
        }

        public void Remove(string filePath)
        {
            if (_data.Remove(filePath))
                Persist();
        }

        private void Persist()
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storePath, json);
        }

        private string ComputeHash(string text)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hashBytes = sha.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes);
        }
    }
}
