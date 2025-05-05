// --- START OF FILE ICodeCompletenessCheckerAgent.cs (in Shared project or equivalent) ---
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Shared
{
    /// <summary>
    /// Agente responsable de verificar que los archivos de código generados
    /// tengan contenido y, opcionalmente, intentar completarlos si están vacíos.
    /// </summary>
    public interface ICodeCompletenessCheckerAgent
    {
        /// <summary>
        /// Revisa los archivos .cs y .razor en la ruta del proyecto, identifica
        /// aquellos que están vacíos o incompletos, e intenta regenerar su contenido
        /// usando la IA y el contexto del prompt/backlog original.
        /// </summary>
        /// <param name="projectPath">Ruta a la carpeta raíz del proyecto generado.</param>
        /// <param name="originalPrompt">El prompt original que inició la generación.</param>
        /// <param name="backlog">El backlog de tareas que se intentaron implementar.</param>
        /// <returns>Una lista de rutas de archivos que fueron modificados/regenerados.</returns>
        Task<List<string>> EnsureCodeCompletenessAsync(string projectPath, Prompt originalPrompt, string[] backlog);
    }
}
// --- END OF FILE ICodeCompletenessCheckerAgent.cs ---