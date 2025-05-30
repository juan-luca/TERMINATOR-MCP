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
1.  **Nivel de Tarea Detallado:** Cada tarea DEBE describir la creación o modificación de UN archivo específico y la funcionalidad principal a implementar en él. Para aplicaciones CRUD, esto significa tareas separadas para el modelo, cada método del servicio (o la clase de servicio completa si es simple), cada página Razor (Index, Create, Edit, Details, Delete), y cualquier modificación a archivos de configuración o layout.
    Ej: 'Crear modelo C# Models/Producto.cs con propiedades X, Y, Z y DataAnnotations.',
        'Crear servicio Services/ProductoService.cs que implemente IProductoService con métodos CRUD para Producto usando AppDbContext.',
        'Crear página Pages/Productos/Index.razor para listar productos y ofrecer opciones CRUD.'
        'Modificar Program.cs para registrar ProductoService y AppDbContext.'
2.  **NO Descomponer Código:** NO generes tareas que sean líneas individuales de código, HTML, Razor o comentarios. NO intentes escribir el contenido de los archivos aquí.
3.  **Objetivo:** El resultado es un backlog para que otro agente desarrollador tome cada tarea y genere el archivo completo correspondiente. La descripción de la tarea debe ser lo suficientemente rica para que el desarrollador entienda qué crear.
4.  **Orden Lógico:** Intenta ordenar las tareas en un flujo lógico (ej: crear modelos antes que servicios, servicios antes que páginas que los usen, crear DbContext antes de usarlo, registrar servicios/DbContext en Program.cs después de crearlos).
5.  **Archivos Base:** NO incluyas tareas para archivos base que se asumen ya existentes o generados automáticamente (como _Imports.razor, App.razor, MainLayout.razor básico, .csproj), a menos que el requerimiento pida MODIFICARLOS específicamente (ej: 'Modificar Shared/NavMenu.razor para añadir enlaces...', 'Modificar Program.cs para registrar servicio...').
6.  **Claridad y Detalle CRUD:** Sé claro y conciso, pero con suficiente detalle. Para CRUD, especifica:
    *   **Modelo:** Nombre del archivo, propiedades principales y cualquier DataAnnotation clave (ej. `[Key]`, `[Required]`).
    *   **DbContext:** Nombre del archivo, qué `DbSet<>` añadir.
    *   **Servicio (Interfaz e Implementación):** Nombres de archivo, qué entidad maneja, qué métodos CRUD básicos (GetAll, GetById, Create, Update, Delete).
    *   **Páginas Razor CRUD:** Nombre del archivo (Index, Create, Edit, Details, Delete), propósito principal (listar, formulario de creación, etc.), y qué componentes clave debe tener (tabla, `<EditForm>`, botones de acción).
    *   **Configuración:** Qué archivo modificar (ej. `Program.cs`, `Shared/NavMenu.razor`) y qué añadir/cambiar (registro de servicio, `NavLink`).
7.  **Rutas:** Usa rutas relativas (ej: `Models/Cliente.cs`, `Pages/Clientes/Index.razor`).

Requerimiento de Usuario:
""{prompt.Descripcion}""

Ejemplos de TAREAS TÉCNICAS VÁLIDAS (Formato deseado):
- Crear modelo C# `Models/Cliente.cs` con propiedades Id (Key), Nombre (Required, MaxLength 100), Email (Required, EmailAddress), FechaRegistro (DateTime), Saldo (decimal), y DataAnnotations apropiadas.
- Crear (o modificar) DbContext en `Data/AppDbContext.cs` para incluir `public DbSet<Cliente> Clientes { get; set; }`.
- Modificar `Program.cs` para registrar `AppDbContext` (ej. `builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("AppDb"));`).
- Crear interfaz `Services/IClienteService.cs` definiendo métodos asíncronos para GetAll, GetById, Create, Update, Delete para la entidad Cliente.
- Crear clase `Services/ClienteService.cs` que implemente `IClienteService` utilizando `AppDbContext` para realizar las operaciones CRUD para Cliente.
- Modificar `Program.cs` para registrar el servicio Cliente (ej. `builder.Services.AddScoped<IClienteService, ClienteService>();`).
- Crear página Razor `Pages/Clientes/Index.razor` para mostrar una tabla de todos los clientes. Incluir enlaces/botones para 'Crear Nuevo', y para cada cliente: 'Editar', 'Detalles', 'Eliminar'.
- Crear página Razor `Pages/Clientes/Create.razor` con un `<EditForm>` para crear un nuevo Cliente. Incluir campos para todas las propiedades editables del modelo Cliente, validaciones (`<DataAnnotationsValidator />`), y un botón de 'Guardar'.
- Crear página Razor `Pages/Clientes/Edit.razor` con un `<EditForm>` para modificar un Cliente existente (cargado por Id). Incluir campos para todas las propiedades editables, validaciones, y un botón de 'Guardar'.
- Crear página Razor `Pages/Clientes/Details.razor` para mostrar todas las propiedades de un Cliente (cargado por Id) en modo de solo lectura.
- Crear página Razor `Pages/Clientes/Delete.razor` para mostrar los detalles de un Cliente (cargado por Id) y pedir confirmación antes de eliminarlo.
- Modificar el archivo `Shared/NavMenu.razor` para añadir enlaces de navegación a la página de listado de Clientes (Index.razor).

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