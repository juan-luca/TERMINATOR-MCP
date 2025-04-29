using System.Threading.Tasks;

namespace Shared
{
    public interface IErrorFixer
    {
        Task<List<string>> CorregirErroresDeCompilacionAsync(string rutaProyecto);
    }
}
