// --- START OF FILE PlanificadorAgent.cs --- CORREGIDO Firma Método

using Infraestructura;
using Microsoft.Extensions.Logging;
using Shared; // <--- Asegurar que Shared esté referenciado
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Infraestructura
{
    // Asegurar que implementa la interfaz correcta
    public class PlanificadorAgent : IPlanificadorAgent
    {
        private readonly GeminiClient _gemini;
        private readonly ILogger<PlanificadorAgent> _logger;

        private enum TaskCategory { Model = 1, Data = 2, Configuration = 3, Service = 4, Component = 5, Page = 6, Layout = 7, Other = 100 }

        public PlanificadorAgent(GeminiClient gemini, ILogger<PlanificadorAgent> logger)
        {
            _gemini = gemini;
            _logger = logger;
        }

        // *** CORRECCIÓN CLAVE: Usar Shared.Prompt explícitamente ***
        public async Task<string[]> ConvertirPromptABacklog(Shared.Prompt prompt)
        {
            _logger.LogInformation("📑 Iniciando conversión de Prompt a Backlog para: '{Titulo}'", prompt.Titulo);

            var mensaje = @$"Sos un ingeniero de software senior experto en Blazor Server y arquitectura de aplicaciones web. Basado en el siguiente requerimiento de usuario, genera una lista concisa de TAREAS TÉCNICAS de alto nivel, paso a paso, para implementar la funcionalidad completa.

Consideraciones Importantes:
1.  **Nivel de Tarea:** Cada tarea DEBE describir la creación o modificación de UN archivo específico (ej: 'Crear modelo Models/Producto.cs', 'Modificar página Pages/Index.razor', 'Registrar servicio en Program.cs') o una acción de configuración clara.
2.  **NO Descomponer Código:** NO generes tareas que sean líneas individuales de código, HTML, Razor o comentarios. NO intentes escribir el contenido de los archivos aquí.
3.  **Objetivo:** El resultado es un backlog para que otro agente desarrollador tome cada tarea y genere el archivo completo correspondiente.
4.  **Orden Lógico:** Intenta ordenar las tareas en un flujo lógico (ej: crear modelos antes que páginas que los usen, crear DbContext antes de usarlo).
5.  **Archivos Base:** NO incluyas tareas para archivos base que se asumen ya existentes o generados automáticamente (como _Imports.razor, App.razor, MainLayout.razor, Program.cs básico, .csproj), a menos que el requerimiento pida MODIFICARLOS específicamente (ej: 'Añadir enlace a NavMenu.razor', 'Registrar servicio en Program.cs').
6.  **Claridad:** Sé claro y conciso en la descripción de cada tarea. Usa rutas relativas (ej: Models/Cliente.cs, Pages/Clientes/Listado.razor).

Requerimiento de Usuario:
""{prompt.Descripcion}""

Ejemplos de TAREAS TÉCNICAS VÁLIDAS (Formato deseado):
- Crear modelo C# Models/Cliente.cs con propiedades Id, Nombre, Email, FechaRegistro.
- Crear DbContext Data/AppDbContext.cs con DbSet<Cliente>.
- Registrar AppDbContext en Program.cs usando base de datos en memoria.
- Crear página Razor Pages/Clientes/Listado.razor para mostrar tabla de clientes.
- Crear página Razor Pages/Clientes/Crear.razor con formulario para añadir cliente.
- Crear página Razor Pages/Clientes/Editar.razor para modificar cliente por Id.
- Crear página Razor Pages/Clientes/Detalles.razor para ver detalles de cliente por Id.
- Crear página Razor Pages/Clientes/Eliminar.razor para confirmar borrado de cliente por Id.
- Añadir enlaces CRUD para Clientes en Shared/NavMenu.razor.

Ejemplos de TAREAS INVÁLIDAS (Formato INCORRECTO - NO HACER ESTO):
- public class Cliente
- {{
- public string Nombre {{ get; set; }}
- <InputText @bind-Value=""cliente.Nombre"" />
- await dbContext.Clientes.AddAsync(nuevoCliente);
- }}
- @page ""/clientes/crear""
- ```csharp

Genera la lista de tareas técnicas necesarias para cumplir el requerimiento, una tarea por línea, comenzando cada tarea con '-' o un número:
";

            try
            {
                _logger.LogDebug("Enviando prompt al Planificador Gemini...");
                _logger.LogTrace("Prompt Planificador:\n{Prompt}", mensaje);
                var respuesta = await _gemini.GenerarAsync(mensaje);
                _logger.LogDebug("Respuesta recibida del Planificador Gemini.");
                _logger.LogTrace("Respuesta Planificador (Raw):\n{Respuesta}", respuesta);

                if (string.IsNullOrWhiteSpace(respuesta)) { _logger.LogWarning("El planificador Gemini devolvió una respuesta vacía para el prompt '{Titulo}'.", prompt.Titulo); return Array.Empty<string>(); }

                var initialLines = respuesta.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim(' ', '-', '*', '.')).Select(line => line.Trim()).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
                _logger.LogDebug("Líneas iniciales después de split y trim básico: {Count}", initialLines.Count);

                var validTasks = new List<string>();
                var discardedLines = new List<string>();
                foreach (var line in initialLines) { if (IsValidTask(line)) validTasks.Add(line); else discardedLines.Add(line); }
                if (discardedLines.Any()) { _logger.LogWarning("Se descartaron {Count} líneas por no parecer tareas válidas: {Discarded}", discardedLines.Count, string.Join(" | ", discardedLines.Take(5)) + (discardedLines.Count > 5 ? "..." : "")); }
                _logger.LogDebug("Tareas válidas después del filtrado: {Count}", validTasks.Count);

                var sortedTasks = validTasks.Select(task => new { Task = task, Category = GetTaskCategory(task) }).OrderBy(item => item.Category).ThenBy(item => item.Task).Select(item => item.Task).ToList();
                _logger.LogDebug("Tareas después de ordenar por categoría: {Count}", sortedTasks.Count);
                if (_logger.IsEnabled(LogLevel.Debug) && sortedTasks.Any()) { _logger.LogDebug("Orden de tareas final (primeras 5): {Tareas}", string.Join(" -> ", sortedTasks.Take(5))); }

                const int minRequiredTasks = 1;
                if (sortedTasks.Count < minRequiredTasks) { _logger.LogError("¡FALLO DE PLANIFICACIÓN! No se generaron tareas válidas para '{Titulo}'. Backlog final vacío/insuficiente. Respuesta original:\n{Respuesta}", prompt.Titulo, respuesta); return Array.Empty<string>(); }

                _logger.LogInformation("✅ Backlog validado y ordenado generado con {Count} tareas para '{Titulo}'.", sortedTasks.Count, prompt.Titulo);
                return sortedTasks.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error crítico al convertir prompt a backlog para '{Titulo}'.", prompt.Titulo);
                return Array.Empty<string>();
            }
        }

        private bool IsValidTask(string line) { if (string.IsNullOrWhiteSpace(line) || line.Length < 10) return false; if (line.StartsWith("{") || line.StartsWith("}") || line.StartsWith("(") || line.StartsWith(")") || line.StartsWith("<") || line.StartsWith("/") || line.StartsWith("@") || line.StartsWith("using ") || line.StartsWith("public ") || line.StartsWith("private ") || line.StartsWith("protected ") || line.StartsWith("internal ") || line.StartsWith("namespace ") || line.StartsWith("var ") || line.StartsWith("await ") || line.StartsWith("return ") || line.StartsWith("if ") || line.StartsWith("else") || line.StartsWith("foreach") || line.StartsWith("while ") || line.StartsWith("Console.") || line.StartsWith("builder.") || line.StartsWith("context.") || line.StartsWith("services.") || line.StartsWith("app.") || line.Trim() == "```") return false; var actionVerbs = new[] { "crear", "modificar", "añadir", "agregar", "registrar", "configurar", "actualizar", "eliminar", "generar", "implementar", "asegurar", "refactorizar", "mover", "renombrar" }; if (!actionVerbs.Any(verb => line.ToLowerInvariant().Contains(verb))) return false; var artifacts = new[] { ".cs", ".razor", "modelo", "model", "página", "page", "componente", "component", "servicio", "service", "context", "dbcontext", "controlador", "controller", "api", "dto", "viewmodel", "entidad", "entity", "repositorio", "repository", "interfaz", "interface", "program", "startup", "config", "setting", "navmenu", "layout", "clase", "class" }; if (!artifacts.Any(art => line.ToLowerInvariant().Contains(art))) return false; return true; }
        private TaskCategory GetTaskCategory(string task) { var tLower = task.ToLowerInvariant(); if (tLower.Contains("modelo") || tLower.Contains("model") || tLower.Contains("entidad") || tLower.Contains("entity") || tLower.Contains("dto") || tLower.Contains("enum") || (tLower.Contains(".cs") && (tLower.Contains("/models/") || tLower.Contains("\\models\\")))) return TaskCategory.Model; if (tLower.Contains("dbcontext") || tLower.Contains("contexto") || tLower.Contains("repositorio") || tLower.Contains("repository") || (tLower.Contains(".cs") && (tLower.Contains("/data/") || tLower.Contains("\\data\\")))) return TaskCategory.Data; if (tLower.Contains("program.cs") || tLower.Contains("startup") || tLower.Contains("configurar") || tLower.Contains("registrar servicio") || tLower.Contains("appsettings")) return TaskCategory.Configuration; if (tLower.Contains("servicio") || tLower.Contains("service") || tLower.Contains("cliente") || tLower.Contains("client") || tLower.Contains("helper") || tLower.Contains("manager") || (tLower.Contains(".cs") && (tLower.Contains("/services/") || tLower.Contains("\\services\\") || tLower.Contains("/clients/") || tLower.Contains("\\clients\\") || tLower.Contains("/helpers/") || tLower.Contains("\\helpers\\")))) return TaskCategory.Service; if ((tLower.Contains("página") || tLower.Contains("page")) && tLower.Contains(".razor") || (tLower.Contains(".razor") && (tLower.Contains("/pages/") || tLower.Contains("\\pages\\")))) return TaskCategory.Page; if (tLower.Contains("componente") || tLower.Contains("component") || (tLower.Contains(".razor") && (tLower.Contains("/components/") || tLower.Contains("\\components\\")))) return TaskCategory.Component; if (tLower.Contains("navmenu") || tLower.Contains("layout") || (tLower.Contains(".razor") && (tLower.Contains("/shared/") || tLower.Contains("\\shared\\")))) return TaskCategory.Layout; if (tLower.Contains("interfaz") || tLower.Contains("interface") || (tLower.Contains(".cs") && (tLower.Contains("/interfaces/") || tLower.Contains("\\interfaces\\")))) return TaskCategory.Service; if (tLower.EndsWith(".cs")) return TaskCategory.Other; if (tLower.EndsWith(".razor")) return TaskCategory.Component; return TaskCategory.Other; }

    }
}
// --- END OF FILE PlanificadorAgent.cs --- CORREGIDO Firma Método