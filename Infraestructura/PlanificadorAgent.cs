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
1.  **Nivel de Tarea Detallado:** Cada tarea DEBE describir la creación o modificación de UN archivo específico y la funcionalidad principal a implementar en él. Para aplicaciones CRUD, esto significa tareas separadas untuk el modelo, cada método del servicio (o la clase de servicio completa si es simple), cada página Razor (Index, Create, Edit, Details, Delete), y cualquier modificación a archivos de configuración o layout.
    Ej: 'Crear modelo C# Models/Producto.cs con propiedades X, Y, Z y DataAnnotations.',
        'Crear servicio Services/ProductoService.cs que implemente IProductoService con métodos CRUD para Producto usando AppDbContext.',
        'Crear página Pages/Productos/Index.razor para listar productos y ofrecer opciones CRUD.'
        'Modificar Program.cs para registrar ProductoService y AppDbContext.'
2.  **NO Descomponer Código:** NO generes tareas que sean líneas individuales de código, HTML, Razor o comentarios. NO intentes escribir el contenido de los archivos aquí.
3.  **Objetivo:** El resultado es un backlog para que otro agente desarrollador tome cada tarea y genere el archivo completo correspondiente. La descripción de la tarea debe ser lo suficientemente rica para que el desarrollador entienda qué crear.
4.  **Orden Lógico:** Intenta ordenar las tareas en un flujo lógico (ej: crear modelos antes que servicios, servicios antes que páginas que los usen, crear DbContext antes de usarlo, registrar servicios/DbContext en Program.cs después de crearlos).
5.  **Archivos Base:** NO incluyas tareas para archivos base que se asumen ya existentes o generados automáticamente (como _Imports.razor, App.razor, MainLayout.razor básico, .csproj), a menos que el requerimiento pida MODIFICARLOS específicamente (ej: 'Modificar Layout/NavMenu.razor para añadir enlaces...', 'Modificar Program.cs para registrar servicio...').
6.  **Claridad y Detalle CRUD:** Sé claro y conciso, pero con suficiente detalle. Para CRUD, especifica:
    *   **Modelo:** Nombre del archivo, propiedades principales y cualquier DataAnnotation clave (ej. `[Key]`, `[Required]`).
    *   **DbContext:** Nombre del archivo, qué `DbSet<>` añadir.
    *   **Servicio (Interfaz e Implementación):** Nombres de archivo, qué entidad maneja, qué métodos CRUD básicos (GetAll, GetById, Create, Update, Delete).
    *   **Páginas Razor CRUD:** Nombre del archivo (Index, Create, Edit, Details, Delete), propósito principal (listar, formulario de creación, etc.), y qué componentes clave debe tener (tabla, `<EditForm>`, botones de acción).
    *   **Configuración:** Qué archivo modificar (ej. `Program.cs`, `Layout/NavMenu.razor`) y qué añadir/cambiar (registro de servicio, `NavLink`).
7.  **Rutas:** Usa rutas relativas (ej: `Models/Cliente.cs`, `Pages/Clientes/Index.razor`).

Requerimiento de Usuario:
""{prompt.Descripcion}""

--- INICIO DE EJEMPLOS ---
Los siguientes son EJEMPLOS para ilustrar el FORMATO y NIVEL DE DETALLE deseado para cada tarea. NO COPIES el texto literal de estos ejemplos. Úsalos SOLAMENTE COMO GUÍA para el estilo de las tareas que TÚ generes.
Formato de Ejemplo de Tarea:
(No uses estas líneas exactas en tu salida, son solo para mostrar cómo se vería una tarea)

- Crear archivo de modelo C# para la entidad Cliente en la ruta Models/Cliente.cs. Este modelo debe incluir las siguientes propiedades: Id (entero, actuará como clave primaria), Nombre (cadena, obligatorio, longitud máxima de 100 caracteres), Email (cadena, obligatorio, debe ser una dirección de correo válida), FechaRegistro (fecha y hora), Saldo (numérico decimal). Incluir las DataAnnotations o equivalentes necesarias para validaciones (ej. Required, StringLength, EmailAddress).
- Modificar la clase DbContext en Data/AppDbContext.cs para asegurar que incluya una propiedad DbSet para la entidad Cliente (ejemplo: public DbSet<Cliente> Clientes get set).
- Modificar el archivo Program.cs para registrar el servicio AppDbContext. Configurar para que use una base de datos en memoria (ejemplo de configuración: builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(""AppDb""))).
- Crear interfaz de servicio en Services/IClienteService.cs para la entidad Cliente, definiendo métodos asíncronos para las operaciones CRUD básicas: obtener todos, obtener por Id, crear, actualizar y eliminar.
- Crear clase de servicio en Services/ClienteService.cs que implemente la interfaz IClienteService, utilizando el AppDbContext para realizar las operaciones CRUD para la entidad Cliente.
- Modificar el archivo Program.cs para registrar el servicio de Cliente (ejemplo de configuración: builder.Services.AddScoped<IClienteService, ClienteService>()).
- Crear página Razor en Pages/Clientes/Index.razor para mostrar una tabla de todos los clientes. Incluir enlaces o botones para 'Crear Nuevo', y para cada cliente en la tabla: 'Editar', 'Detalles', 'Eliminar'.
- Crear página Razor en Pages/Clientes/Create.razor con un formulario EditForm para crear un nuevo Cliente. Incluir campos de entrada para todas las propiedades editables del modelo Cliente, validaciones usando DataAnnotationsValidator y ValidationSummary, y un botón de 'Guardar'.
- Crear página Razor en Pages/Clientes/Edit.razor con un formulario EditForm para modificar un Cliente existente (cargado por su Id). Incluir campos de entrada para todas las propiedades editables, validaciones, y un botón de 'Guardar'.
- Crear página Razor en Pages/Clientes/Details.razor para mostrar todas las propiedades de un Cliente (cargado por su Id) en modo de solo lectura.
- Crear página Razor en Pages/Clientes/Delete.razor para mostrar los detalles de un Cliente (cargado por su Id) y pedir confirmación al usuario antes de proceder con la eliminación.
- Modificar el archivo Layout/NavMenu.razor para añadir enlaces de navegación a la página de listado de Clientes (Index.razor).

--- FIN DE EJEMPLOS ---

Recuerda: Genera la lista de tareas técnicas necesarias para CUMPLIR EL REQUERIMIENTO DEL USUARIO. Las tareas deben estar ordenadas lógicamente. Usa los ejemplos ANTERIORES ÚNICAMENTE COMO GUÍA para el formato, el nivel de detalle y el tipo de archivo a crear o modificar por tarea. NO COPIES EL TEXTO DE LOS EJEMPLOS.

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

                var initialLines = respuesta.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(line => line.Trim())
                                   .Where(line => !string.IsNullOrWhiteSpace(line))
                                   .ToList();
                _logger.LogDebug("Líneas iniciales después de split y trim básico: {Count}", initialLines.Count);

                // NEW: Sanitize first
                var sanitizedTasks = SanitizarBacklog(initialLines, _logger);
                _logger.LogDebug("Tareas después de la sanitización: {Count}", sanitizedTasks.Count);

                var validTasks = new List<string>();
                var discardedLines = new List<string>(); // Tasks discarded by IsValidTask AFTER sanitization
                foreach (var line in sanitizedTasks) // Iterate sanitized tasks
                {
                    if (IsValidTask(line)) // IsValidTask checks if the line *looks* like a task
                    {
                        validTasks.Add(line);
                    }
                    else
                    {
                        discardedLines.Add(line);
                    }
                }

                if (discardedLines.Any()) { _logger.LogWarning("Se descartaron {Count} líneas por IsValidTask DESPUÉS de la sanitización: {Discarded}", discardedLines.Count, string.Join(" | ", discardedLines.Take(5)) + (discardedLines.Count > 5 ? "..." : "")); }
                _logger.LogDebug("Tareas válidas después del filtrado final: {Count}", validTasks.Count);

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

        private List<string> SanitizarBacklog(List<string> rawTasks, ILogger<PlanificadorAgent> logger)
        {
            var cleanedTasks = new List<string>();
            var commonPromptPhrases = new[] {
                "ejemplos de tareas técnicas válidas", "formato deseado", "no copies el texto",
                "requerimiento de usuario:", "paso a paso", "nivel de tarea:", "no descomponer código",
                "objetivo:", "resultado es un backlog", "orden lógico:", "archivos base:",
                "claridad:", "sé claro y conciso", "genera la lista de tareas", "--- inicio de ejemplos ---",
                "--- fin de ejemplos ---", "recuerda:", "solo como guía", "no copies los ejemplos",
                "sos un ingeniero de software", "basado en el siguiente requerimiento",
                "public class", "get; set;", "return ", "async Task", "<EditForm", "@page", "builder.Services",
                "options =>", "await ", "_context.", "Console.WriteLine", "string[]", "List<string>", "=>",
                "ej:", "ejemplo de configuración:", "ejemplo:", "este modelo debe incluir", "este servicio debe", "esta página debe",
                "actuará como clave primaria", "obligatorio", "longitud máxima", "debe ser una dirección de correo válida",
                "fecha y hora", "numérico decimal", "propiedad DbSet para la entidad", "métodos asíncronos para las operaciones CRUD",
                "utilizando el AppDbContext", "mostrar una tabla de todos", "incluir enlaces o botones para",
                "formulario EditForm para crear", "campos de entrada para todas las propiedades", "validaciones usando DataAnnotationsValidator",
                "cargar por su Id", "pedir confirmación al usuario antes de proceder", "añadir enlaces de navegación a la página"
            }.Select(p => p.ToLowerInvariant()).ToArray();

            var actionVerbs = new[] {
                "crear", "modificar", "añadir", "agregar", "registrar", "configurar", "actualizar", "eliminar", "generar",
                "implementar", "asegurar", "refactorizar", "mover", "renombrar", "definir", "establecer",
                "integrar", "mostrar", "permitir", "validar", "usar", "inyectar", "heredar", "llamar", "navegar"
            }.Select(v => v.ToLowerInvariant()).ToArray();

            foreach (var task in rawTasks)
            {
                string currentTask = task.Trim(' ', '-', '*', '.', '`', '"'); // Aggressive initial trim

                // Check if the task seems to be a leftover from the prompt's own text or examples
                bool isPromptRemnant = commonPromptPhrases.Any(phrase =>
                    currentTask.ToLowerInvariant().Contains(phrase));

                if (isPromptRemnant)
                {
                    // Allow if it looks like a genuine task despite containing some common words from examples (e.g. "Crear modelo...")
                    bool looksLikeTask = actionVerbs.Any(verb => currentTask.ToLowerInvariant().StartsWith(verb)) &&
                                         (currentTask.Contains(".cs") || currentTask.Contains(".razor"));
                    if (!looksLikeTask)
                    {
                        logger.LogDebug("Sanitizando: Tarea removida por parecer remanente del prompt/ejemplo: '{Task}'", task);
                        continue;
                    }
                }

                // Remove tasks that are too short to be meaningful
                // Exception for common short tasks that are usually valid (e.g. "Modificar Program.cs para...")
                bool isCommonShortTask = (currentTask.ToLowerInvariant().Contains("program.cs") ||
                                         currentTask.ToLowerInvariant().Contains("navmenu.razor") ||
                                         currentTask.ToLowerInvariant().Contains("appdbcontext"))
                                         && currentTask.Length < 30; // If it's short but refers to these, it might be okay

                if (currentTask.Length < 15 && !isCommonShortTask)
                {
                    logger.LogDebug("Sanitizando: Tarea removida por ser demasiado corta: '{Task}'", task);
                    continue;
                }

                // Check for lines that are clearly code and not task descriptions
                bool containsActionVerb = actionVerbs.Any(verb => currentTask.ToLowerInvariant().Contains(verb));

                if (!containsActionVerb)
                {
                    // If no action verb, it's highly suspect if it also contains code-like syntax
                    if (currentTask.EndsWith(";") || currentTask.EndsWith("{") || currentTask.EndsWith("}") || currentTask.EndsWith("/>") || currentTask.Contains("=>") || currentTask.Contains(" get ") || currentTask.Contains(" set ") || currentTask.StartsWith("<") || currentTask.StartsWith("@"))
                    {
                         logger.LogDebug("Sanitizando: Tarea removida por parecer código (sin verbo de acción y con sintaxis de código): '{Task}'", task);
                         continue;
                    }
                }
                // If it starts with a non-action verb and contains code-like syntax, it might be a comment or description of code.
                if (!actionVerbs.Any(verb => currentTask.ToLowerInvariant().TrimStart().StartsWith(verb)) && (currentTask.Contains("=>") || currentTask.Contains("{ get; set; }")))
                {
                    logger.LogDebug("Sanitizando: Tarea removida por no iniciar con verbo de acción y contener sintaxis de código: '{Task}'", task);
                    continue;
                }


                // Remove tasks that are just instructions to the AI
                if (currentTask.ToLowerInvariant().StartsWith("genera la lista") ||
                    currentTask.ToLowerInvariant().StartsWith("no incluyas") ||
                    currentTask.ToLowerInvariant().StartsWith("recuerda:") ||
                    currentTask.ToLowerInvariant().StartsWith("considera:"))
                {
                    logger.LogDebug("Sanitizando: Tarea removida por ser instrucción para el AI: '{Task}'", task);
                    continue;
                }

                cleanedTasks.Add(currentTask);
            }
            return cleanedTasks;
        }

        private bool IsValidTask(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 10) return false;

            string lowerLine = line.ToLowerInvariant();

            var actionVerbs = new[] {
                "crear", "modificar", "añadir", "agregar", "registrar", "configurar", "actualizar", "eliminar",
                "generar", "implementar", "asegurar", "refactorizar", "mover", "renombrar", "definir",
                "establecer", "integrar", "mostrar", "permitir", "validar", "usar", "inyectar", "heredar",
                "llamar", "navegar"
            };
            bool hasActionVerb = actionVerbs.Any(verb => lowerLine.Contains(verb));

            var artifacts = new[] {
                ".cs", ".razor", "modelo", "model", "página", "page", "componente", "component",
                "servicio", "service", "context", "dbcontext", "controlador", "controller", "api",
                "dto", "viewmodel", "entidad", "entity", "repositorio", "repository", "interfaz", "interface",
                "program", "startup", "config", "setting", "navmenu", "layout", "clase", "class", "archivo", ".csproj"
            };
            bool hasArtifact = artifacts.Any(art => lowerLine.Contains(art));

            // Core requirement: must have an action and refer to an artifact.
            if (!hasActionVerb)
            {
                _logger.LogDebug("IsValidTask: Rejected line '{Line}' due to missing action verb.", line);
                return false;
            }
            if (!hasArtifact)
            {
                _logger.LogDebug("IsValidTask: Rejected line '{Line}' due to missing artifact type.", line);
                return false;
            }

            // Stricter checks for lines that might be code despite having an action verb and artifact.
            // These are more about the *start* of the line.
            string[] codeLikeStarters = {
                "{", "}", "(", ")", "<", "@", "using ", "public ", "private ", "protected ",
                "internal ", "namespace ", "var ", "await ", "return ", "if ", "else", "foreach",
                "while ", "Console.", "builder.", "context.", "services.", "app."
            };
            if (codeLikeStarters.Any(s => line.TrimStart().StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                // Special allowance for config file modifications, as they often include code-like instructions.
                bool isConfigFileTask = lowerLine.Contains("program.cs") || lowerLine.Contains(".csproj");
                if (isConfigFileTask)
                {
                    // If it's a config file task, allow if it also has substantial descriptive text beyond the code-like part.
                    // Heuristic: check if the line is much longer than the code-like starter, or if it contains common descriptive patterns.
                    if (line.Length > 30 || lowerLine.Contains("para") || lowerLine.Contains("con") || lowerLine.Contains("usando") || lowerLine.Contains("asegurar que")) {
                        _logger.LogDebug("IsValidTask: Allowed config file task '{Line}' despite code-like start due to descriptive text.", line);
                    } else {
                        _logger.LogDebug("IsValidTask: Rejected config file task '{Line}' due to code-like start without enough descriptive text.", line);
                        return false;
                    }
                }
                else // Not a config file task, reject if it starts with code-like patterns.
                {
                    _logger.LogDebug("IsValidTask: Rejected line '{Line}' due to code-like start for a non-config file task.", line);
                    return false;
                }
            }

            // Reject if it's just "```"
            if (line.Trim() == "```") {
                 _logger.LogDebug("IsValidTask: Rejected line '{Line}' because it is just backticks.", line);
                return false;
            }

            // Allow tasks like "Modificar Program.cs para registrar ApplicationDbContext usando builder.Services.AddDbContext(...);"
            // The general checks for "=>" or "{ get; set; }" are removed if hasActionVerb and hasArtifact are true,
            // as these might be part of a detailed description for the developer agent.
            // SanitizarBacklog should catch more obvious code-only lines.

            return true;
        }
        private TaskCategory GetTaskCategory(string task) { var tLower = task.ToLowerInvariant(); if (tLower.Contains("modelo") || tLower.Contains("model") || tLower.Contains("entidad") || tLower.Contains("entity") || tLower.Contains("dto") || tLower.Contains("enum") || (tLower.Contains(".cs") && (tLower.Contains("/models/") || tLower.Contains("\\models\\")))) return TaskCategory.Model; if (tLower.Contains("dbcontext") || tLower.Contains("contexto") || tLower.Contains("repositorio") || tLower.Contains("repository") || (tLower.Contains(".cs") && (tLower.Contains("/data/") || tLower.Contains("\\data\\")))) return TaskCategory.Data; if (tLower.Contains("program.cs") || tLower.Contains("startup") || tLower.Contains("configurar") || tLower.Contains("registrar servicio") || tLower.Contains("appsettings")) return TaskCategory.Configuration; if (tLower.Contains("servicio") || tLower.Contains("service") || tLower.Contains("cliente") || tLower.Contains("client") || tLower.Contains("helper") || tLower.Contains("manager") || (tLower.Contains(".cs") && (tLower.Contains("/services/") || tLower.Contains("\\services\\") || tLower.Contains("/clients/") || tLower.Contains("\\clients\\") || tLower.Contains("/helpers/") || tLower.Contains("\\helpers\\")))) return TaskCategory.Service; if ((tLower.Contains("página") || tLower.Contains("page")) && tLower.Contains(".razor") || (tLower.Contains(".razor") && (tLower.Contains("/pages/") || tLower.Contains("\\pages\\")))) return TaskCategory.Page; if (tLower.Contains("componente") || tLower.Contains("component") || (tLower.Contains(".razor") && (tLower.Contains("/components/") || tLower.Contains("\\components\\")))) return TaskCategory.Component; if (tLower.Contains("navmenu") || tLower.Contains("layout") || (tLower.Contains(".razor") && (tLower.Contains("/shared/") || tLower.Contains("\\shared\\") || tLower.Contains("/layout/") || tLower.Contains("\\layout\\")))) return TaskCategory.Layout; if (tLower.Contains("interfaz") || tLower.Contains("interface") || (tLower.Contains(".cs") && (tLower.Contains("/interfaces/") || tLower.Contains("\\interfaces\\")))) return TaskCategory.Service; if (tLower.EndsWith(".cs")) return TaskCategory.Other; if (tLower.EndsWith(".razor")) return TaskCategory.Component; return TaskCategory.Other; }

    }
}
// --- END OF FILE PlanificadorAgent.cs --- CORREGIDO Firma Método