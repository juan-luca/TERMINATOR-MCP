using Shared;

namespace Infraestructura.Memory
{
    public class ExecutionMemoryEntry
    {
        public DateTime TimestampUtc { get; set; }
        public Prompt Prompt { get; set; } = new(""," ");
        public string[] Backlog { get; set; } = Array.Empty<string>();
        public bool BuildSuccess { get; set; }
        public string ProjectPath { get; set; } = string.Empty;
        public string CommitHash { get; set; } = string.Empty;
    }
}
