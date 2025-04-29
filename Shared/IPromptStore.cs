using System.Threading.Tasks;

namespace Shared
{
    public interface IPromptStore
    {
        Task GuardarAsync(Prompt prompt);
        Task<Prompt?> ObtenerSiguienteAsync();
    }
}
