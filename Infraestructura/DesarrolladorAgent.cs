using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Shared; // Asegurar que Shared esté referenciado
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Infraestructura
{
    // Asegurar que implementa la interfaz correcta
    public class DesarrolladorAgent : IDesarrolladorAgent // Assuming IDesarrolladorAgent interface exists
    {
        // NOTE: This version uses a concrete GeminiClient, not IGeminiClient like the previous one.
        private readonly GeminiClient _gemini; // Replace GeminiClient with your actual concrete class if different
        private readonly ILogger<DesarrolladorAgent> _logger;

        // Regex mejorado para extraer rutas de archivos de las descripciones de tareas.
        // Prioriza rutas explícitas mencionadas con palabras clave y/o comillas/backticks.
        private static readonly Regex PathExtractionRegex = new Regex(
            // Opción 1: Keywords como "ruta", "archivo", "fichero", "modificar", etc., seguido de una ruta entre comillas simples, dobles o backticks.
            // Ejemplo: "Crear archivo en la ruta 'Models/MiModelo.cs'"
            // Ejemplo: "Modificar `Pages/Index.razor` para..."
            @"(?:en\s+(?:la\s+)?ruta|archivo|fichero|para\s+(?:el\s+)?archivo|componente|pagina|modelo|servicio|contexto|modificar)\s+(?:`|['""])\s*(?<path>(?:(?:[\w\-\.]+[\/\\])+)?[\w\-\.]+\.(?:cs|razor|csproj))\s*(?:`|['""])" +

            // Opción 2: Keywords como las anteriores, seguido de una ruta SIN comillas pero que parezca una ruta (contiene separadores de directorio o empieza con carpeta conocida).
            // Ejemplo: "Tarea para el archivo Services/MiServicio.cs"
            // Ejemplo: "Crear modelo Models/OtroModelo.cs"
            @"|(?:en\s+(?:la\s+)?ruta|archivo|fichero|para\s+(?:el\s+)?archivo|componente|pagina|modelo|servicio|contexto|modificar)\s+(?<path>(?:(?:Models|Data|Services|Pages|Components|Layout|Shared)(?:[\/\\][\w\-\.]+)*|[\w\-\.]*[\/\\][\w\-\.]+)\.(?:cs|razor|csproj))" +

            // Opción 3: Mención directa de archivos específicos en la raíz del proyecto (Program.cs, App.razor, etc.), opcionalmente precedidos por "en" o "modificar".
            // Ejemplo: "Modificar Program.cs para..."
            // Ejemplo: "En App.razor hacer..."
            @"|(?:en|modificar)\s+['""]?\s*(?<path>Program\.cs|App\.razor|_Imports\.razor|Routes\.razor)\s*['""]?" +

            // Opción 4: Rutas que comienzan con una carpeta conocida (Models, Data, Pages, etc.) sin keyword explícita antes.
            // Ejemplo: "Pages/MiPagina.razor debe ser actualizada."
            @"|['""]?\s*(?<path>(?:Models|Data|Services|Pages|Components|Layout|Shared)(?:[\/\\][\w\-\.]+)+\.(?:cs|razor|csproj))\s*['""]?" +

            // Opción 5: (Fallback menos específico) Cualquier palabra que termine con .cs, .razor, .csproj y esté precedida por alguna keyword general.
            // Este es el más propenso a errores si la descripción es ambigua, por eso va al final.
            @"|(?:archivo|modelo|página|componente|servicio|DbContext|fichero|en|a|para|modificar)\s+['""]?\s*(?<path>[\w\-\.\s\\\/]+\.(cs|razor|csproj))\s*['""]?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex FileNameExtractionRegex = new Regex( @"(?<filename>[\w\-\.]+?\.(cs|razor|csproj))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Constructor using concrete GeminiClient
        public DesarrolladorAgent( GeminiClient gemini, ILogger<DesarrolladorAgent> logger)
        {
            _gemini = gemini ?? throw new ArgumentNullException(nameof(gemini));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Método principal usando Shared.Prompt
        public async Task GenerarCodigoParaTarea(Shared.Prompt prompt, string tarea)
        {
            var nombreProyecto = SanitizeFileName(prompt.Titulo);
            var rutaProyecto = Path.Combine("output", nombreProyecto);
            _logger.LogInformation("➡️ Procesando Tarea: '{Tarea}' para Proyecto: '{Proyecto}'", tarea, nombreProyecto);
            string? targetRelativePath = null;
            string rutaCompletaArchivo = "";

            try
            {
                Directory.CreateDirectory(rutaProyecto);
                await GenerarCsprojAsync(nombreProyecto, rutaProyecto);
                await GenerarArchivosBaseAsync(nombreProyecto, rutaProyecto, prompt);

                targetRelativePath = ExtractPathFromTask(tarea);
                bool rutaExplicita = false;
                if (targetRelativePath != null)
                {
                    _logger.LogInformation("Ruta EXPLICITA extraída por Regex: '{RelativePath}'", targetRelativePath);
                    targetRelativePath = targetRelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Replace("Razor Pages", "Pages").Trim();
                    if (targetRelativePath.StartsWith(Path.DirectorySeparatorChar)) { targetRelativePath = targetRelativePath.Length > 1 ? targetRelativePath.Substring(1) : ""; }

                    if (string.IsNullOrWhiteSpace(targetRelativePath) || targetRelativePath.Any(c => Path.GetInvalidPathChars().Contains(c) && c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar) ) {
                         _logger.LogWarning("Ruta extraída '{OriginalPath}' contiene caracteres inválidos o es vacía después de la normalización. Se inferirá.", targetRelativePath);
                         targetRelativePath = null;
                    }
                    else
                    {
                         try
                         {
                              rutaCompletaArchivo = Path.GetFullPath(Path.Combine(rutaProyecto, targetRelativePath));
                              if (!IsPathWithinProject(rutaCompletaArchivo, rutaProyecto))
                              {
                                   _logger.LogError("RUTA PELIGROSA: '{RelativePath}' -> '{FullPath}' fuera de '{ProjectRoot}'. Tarea abortada.", targetRelativePath, rutaCompletaArchivo, rutaProyecto);
                                   return;
                              }
                              _logger.LogInformation("Ruta EXPLICITA determinada y validada: '{FullPath}'", rutaCompletaArchivo);
                              rutaExplicita = true;
                         }
                         catch (Exception ex)
                         {
                              _logger.LogError(ex, "Error al validar ruta '{RelativePath}'. Se inferirá.", targetRelativePath);
                              targetRelativePath = null;
                         }
                    }
                } else { _logger.LogWarning("No se pudo extraer ruta explícita de la tarea: '{Tarea}'. Se inferirá la ubicación.", tarea); }

                string normalizedTargetPathForCheck = targetRelativePath ?? "";
                bool esModificacionBase = rutaExplicita &&
                                          (normalizedTargetPathForCheck.Equals("Layout" + Path.DirectorySeparatorChar + "NavMenu.razor", StringComparison.OrdinalIgnoreCase) ||
                                           normalizedTargetPathForCheck.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) &&
                                          (tarea.ToLowerInvariant().Contains("registrar ") || tarea.ToLowerInvariant().Contains("modificar ") || tarea.ToLowerInvariant().Contains("añadir enlace"));

                if (esModificacionBase)
                {
                     await ModificarArchivoBaseAsync(rutaCompletaArchivo, tarea, prompt);
                }
                else
                {
                     await CrearNuevoArchivoAsync(rutaProyecto, tarea, targetRelativePath, rutaExplicita, prompt);
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "❌ Error crítico procesando la tarea '{Tarea}'.", tarea); }
        }

        private async Task CrearNuevoArchivoAsync(string rutaProyecto, string tarea, string? targetRelativePath, bool rutaExplicita, Shared.Prompt prompt)
        {
            string codigoGenerado = "";
            string rawCodigoGenerado = "";
            string rutaCompletaArchivo = "";
            string nombreArchivo = "";
            string tipoCodigo = "C#";
            try
            {
                tipoCodigo = InferirTipoCodigo(tarea, targetRelativePath);
                if (!rutaExplicita || targetRelativePath == null)
                {
                    _logger.LogDebug("Ejecutando inferencia ruta/nombre CREACIÓN...");
                    if (tipoCodigo == "Razor") nombreArchivo = ExtractRazorFilename("", tarea); else nombreArchivo = ExtractCSharpFilename("", tarea);
                    string subcarpetaInferida = InferirSubcarpeta(tarea, "");
                    rutaCompletaArchivo = Path.Combine(rutaProyecto, subcarpetaInferida, nombreArchivo);
                    _logger.LogInformation("Ruta INFERIDA nuevo archivo: {FullPath}", rutaCompletaArchivo);
                    targetRelativePath = Path.GetRelativePath(rutaProyecto, rutaCompletaArchivo);
                } else {
                    rutaCompletaArchivo = Path.GetFullPath(Path.Combine(rutaProyecto, targetRelativePath));
                    nombreArchivo = Path.GetFileName(rutaCompletaArchivo);
                }

                string promptParaGemini = CrearPromptParaTarea(prompt, tarea, tipoCodigo, rutaProyecto, targetRelativePath);
                _logger.LogDebug("🔄 Llamando Gemini CREACIÓN...");
                try
                {
                     rawCodigoGenerado = await _gemini.GenerarAsync(promptParaGemini);
                     codigoGenerado = LimpiarCodigoGemini(rawCodigoGenerado);
                }
                catch (Exception ex) when (ex.Message.Contains("503"))
                {
                     _logger.LogWarning(ex, "⚠️ Error 503 Gemini CREACIÓN '{File}'. Omitido.", nombreArchivo);
                     return;
                }
                catch (Exception ex)
                {
                     _logger.LogError(ex, "❌ Error Gemini CREACIÓN '{File}'.", nombreArchivo);
                     return;
                }

                if (!EsCodigoPlausible(codigoGenerado, nombreArchivo, tipoCodigo))
                {
                    _logger.LogWarning("⚠️ Código generado CREACIÓN '{File}' NO PLAUSIBLE o vacío. Omitido. Primeras 500 chars del contenido problemático:\n{CodigoProblematico}", nombreArchivo, string.IsNullOrEmpty(codigoGenerado) ? "[VACIO]" : codigoGenerado.Substring(0, Math.Min(500, codigoGenerado.Length)));
                    return;
                }

                string? directoryPath = Path.GetDirectoryName(rutaCompletaArchivo);
                if (string.IsNullOrEmpty(directoryPath))
                {
                     _logger.LogError("Directorio inválido CREACIÓN: {FilePath}.", rutaCompletaArchivo);
                     return;
                }
                Directory.CreateDirectory(directoryPath);
                _logger.LogInformation("💾 Escribiendo NUEVO (Longitud: {Length}): {FilePath}", codigoGenerado.Length, rutaCompletaArchivo);
                await File.WriteAllTextAsync(rutaCompletaArchivo, codigoGenerado);
                _logger.LogInformation("✅ NUEVO generado '{Tarea}': '{RutaArchivo}'", tarea, rutaCompletaArchivo);
            } catch (Exception ex) {
                 _logger.LogError(ex, "❌ Error crítico CREACIÓN archivo tarea '{Tarea}'.", tarea);
            }
        }

        private async Task ModificarArchivoBaseAsync(string filePath, string taskDescription, Shared.Prompt promptContext)
        {
             _logger.LogInformation("🔧 Modificando base: {FilePath} Tarea: '{Task}'", filePath, taskDescription);
             if (!File.Exists(filePath)) { _logger.LogError("Archivo base no encontrado: {FilePath}", filePath); return; }

             string originalContent;
             try { originalContent = await File.ReadAllTextAsync(filePath); }
             catch (Exception ex) { _logger.LogError(ex, "Error leyendo original: {FilePath}", filePath); return; }

             string modificationPrompt = CreateModificationPrompt(filePath, taskDescription, originalContent, promptContext);
             string modifiedContentRaw;
             string modifiedContentClean;

             _logger.LogDebug("🔄 Llamando Gemini MODIFICACIÓN...");
             try
             {
                  modifiedContentRaw = await _gemini.GenerarAsync(modificationPrompt);
                  modifiedContentClean = LimpiarCodigoGemini(modifiedContentRaw);
             }
             catch (Exception ex) when (ex.Message.Contains("503"))
             {
                  _logger.LogWarning(ex, "⚠️ Error 503 Gemini MODIFICACIÓN '{File}'. Omitido.", Path.GetFileName(filePath));
                  return;
             }
             catch (Exception ex)
             {
                  _logger.LogError(ex, "❌ Error Gemini MODIFICACIÓN '{File}'.", Path.GetFileName(filePath));
                  return;
             }

            string fileTypeForPlausibility = Path.GetExtension(filePath).ToLowerInvariant() == ".cs" ? "C#" : "Razor";
            if (!EsCodigoPlausible(modifiedContentClean, Path.GetFileName(filePath), fileTypeForPlausibility))
            {
                _logger.LogWarning("⚠️ Código modificado '{File}' NO PLAUSIBLE o vacío. No se sobrescribe. Primeras 500 chars del contenido problemático:\n{CodigoProblematico}", Path.GetFileName(filePath), string.IsNullOrEmpty(modifiedContentClean) ? "[VACIO]" : modifiedContentClean.Substring(0, Math.Min(500, modifiedContentClean.Length)));
                return;
            }

             if (originalContent.Length > 50 && Math.Abs(originalContent.Length - modifiedContentClean.Length) > originalContent.Length * 0.75)
             {
                  _logger.LogWarning("⚠️ Código modificado {File} difiere mucho en longitud (Original: {OrigLen}, Nuevo: {NewLen}). NO SE SOBREESCRIBIRÁ.", Path.GetFileName(filePath), originalContent.Length, modifiedContentClean.Length);
                  return;
             }
              if (string.IsNullOrWhiteSpace(originalContent) && modifiedContentClean.Length > 10000)
             {
                  _logger.LogWarning("⚠️ Código modificado {File} es muy grande ({NewLen} chars) partiendo de un original vacío/pequeño. NO SE SOBREESCRIBIRÁ.", Path.GetFileName(filePath), modifiedContentClean.Length);
                  return;
             }

             try
             {
                  _logger.LogInformation("💾 Escribiendo MODIFICADO (Longitud: {Length}): {FilePath}", modifiedContentClean.Length, filePath);
                  await File.WriteAllTextAsync(filePath, modifiedContentClean);
                  _logger.LogInformation("✅ MODIFICADO '{Task}': '{FilePath}'", taskDescription, filePath);
             }
             catch (Exception writeEx)
             {
                  _logger.LogError(writeEx, "❌ Error escribiendo MODIFICADO: {FilePath}", filePath);
             }
        }

        private string CreateModificationPrompt(string targetFilePath, string taskDescription, string originalCode, Shared.Prompt promptContext)
        {
            string fileName = Path.GetFileName(targetFilePath);
            string fileType = Path.GetExtension(fileName).ToLowerInvariant() == ".cs" ? "C#" : "Blazor Razor";
            string langHint = fileType == "C#" ? "csharp" : "html";
            return @$"Contexto General del Proyecto:
{promptContext.Descripcion}
Tarea Específica de Modificación:
{taskDescription}
Archivo a Modificar: '{fileName}' ({fileType})
Código Original del Archivo:
```{langHint}
{originalCode}
```
Instrucciones PRECISAS:
1.  Aplica SOLAMENTE el cambio descrito en la TAREA ESPECÍFICA al CÓDIGO ORIGINAL.
2.  **Para Program.cs:**
    *   Si la tarea es registrar un servicio, localiza la sección de registro de servicios (ej. `// Add services to the container.`) y añade la línea `builder.Services.AddScoped<INombreInterfaz, NombreClase>();` o `builder.Services.AddSingleton<...>();` etc., según corresponda.
    *   Si la tarea es registrar un DbContext, añade `builder.Services.AddDbContext<NombreDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString(""DefaultConnection"")));` (o el proveedor que aplique). Asegúrate que el `using Microsoft.EntityFrameworkCore;` esté presente.
    *   Coloca el nuevo registro de servicio de forma lógica con otros registros similares.
3.  **Para NavMenu.razor (o cualquier archivo .razor de menú):**
    *   Si la tarea es añadir un enlace de navegación, localiza el elemento `<nav class=""flex-column"">` o una lista similar de `NavLink`.
    *   Añade un nuevo `<div class=""nav-item px-3""><NavLink class=""nav-link"" href=""nueva-ruta""><span class=""oi oi-nombre-icono"" aria-hidden=""true""></span> TextoEnlace</NavLink></div>`. Adapta el icono y el texto según la tarea.
4.  **Para cualquier otro archivo:**
    *   Identifica cuidadosamente la sección de código que necesita ser modificada según la TAREA ESPECÍFICA.
    *   Realiza únicamente los cambios solicitados.
5.  **MUY IMPORTANTE:** Devuelve ÚNICAMENTE el código fuente COMPLETO y MODIFICADO del archivo '{fileName}'.
6.  ASEGÚRATE de que TODO el código original que NO necesita cambiarse se mantenga EXACTAMENTE IGUAL.
7.  Incluye TODOS los 'using' necesarios si la modificación los introduce.
8.  El código modificado debe ser COMPILABLE y seguir las mejores prácticas de .NET 8 y Blazor.
9.  NO incluyas explicaciones, introducciones, resúmenes de cambios, notas, advertencias, ni el código original sin modificar como referencia.
10. NO uses bloques de markdown (como ```csharp, ```html o ```razor) alrededor del código final. Solo el contenido puro del archivo.";
        }

        private async Task GenerarArchivosBaseAsync(string nombreProyecto, string rutaProyecto, Shared.Prompt prompt)
        {
            string projectNamespace = SanitizeNamespace(nombreProyecto);
            string promptTitulo = prompt.Titulo;

            var programPath = Path.Combine(rutaProyecto, "Program.cs");
            if (!File.Exists(programPath))
            {
                _logger.LogDebug("Generando Program.cs");
                var programContent = @$"using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// using {projectNamespace}.Data;
// using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// builder.Services.AddDbContext<ApplicationDbContext>(options =>
//     options.UseInMemoryDatabase(""AppDb""));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{{
    app.UseExceptionHandler(""/Error"", createScopeForErrors: true);
    // app.UseHsts();
}}

// app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// app.MapFallbackToFile(""/index.html"");

app.Run();
";
                await File.WriteAllTextAsync(programPath, programContent);
            }

            var appRazorPath = Path.Combine(rutaProyecto, "App.razor");
            if (!File.Exists(appRazorPath))
            {
                _logger.LogDebug("Generando App.razor");
                var appContent = @$"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <base href=""/"" />
    <title>{promptTitulo}</title>
    <link rel=""stylesheet"" href=""css/bootstrap/bootstrap.min.css"" />
    <link rel=""stylesheet"" href=""css/app.css"" />
    <link rel=""icon"" type=""image/png"" href=""favicon.png""/>
    <link href=""{nombreProyecto}.styles.css"" rel=""stylesheet"" />
    <HeadOutlet @rendermode=""InteractiveServer"" />
</head>
<body>
    <Routes @rendermode=""InteractiveServer"" />
    <script src=""_framework/blazor.web.js""></script>
</body>
</html>
";
                await File.WriteAllTextAsync(appRazorPath, appContent);
            }

            var importsPath = Path.Combine(rutaProyecto, "_Imports.razor");
            if (!File.Exists(importsPath))
            {
                _logger.LogDebug("Generando _Imports.razor con usings para Models, Data, Services en: {Path}", importsPath);
                var importsContent = @$"@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using {projectNamespace}
@using {projectNamespace}.Models
@using {projectNamespace}.Data
@using {projectNamespace}.Services
@using {projectNamespace}.Components
@using {projectNamespace}.Layout
";
                await File.WriteAllTextAsync(importsPath, importsContent);
            }

            var routesPath = Path.Combine(rutaProyecto, "Routes.razor");
             if (!File.Exists(routesPath))
            {
                _logger.LogDebug("Generando Routes.razor base...");
                var routesContent = @$"@using {projectNamespace}.Layout

<Router AppAssembly=""@typeof(Program).Assembly"">
    <Found Context=""routeData"">
        <RouteView RouteData=""@routeData"" DefaultLayout=""@typeof(MainLayout)"" />
        <FocusOnNavigate RouteData=""@routeData"" Selector=""h1"" />
    </Found>
    <NotFound>
        <PageTitle>Not found</PageTitle>
        <LayoutView Layout=""@typeof(MainLayout)"">
            <p role=""alert"">Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>
";
                 await File.WriteAllTextAsync(routesPath, routesContent);
            }

            var layoutFolder = Path.Combine(rutaProyecto, "Layout");
            Directory.CreateDirectory(layoutFolder);
            _logger.LogDebug("Generando carpeta Layout en: {Path}", layoutFolder);


            var componentsFolder = Path.Combine(rutaProyecto, "Components");
            Directory.CreateDirectory(componentsFolder);
            _logger.LogDebug("Generando carpeta Components en: {Path}", componentsFolder);

            var placeholderComponentPath = Path.Combine(componentsFolder, "_ComponentsPlaceholder.cs");
            if (!File.Exists(placeholderComponentPath))
            {
                var placeholderContent = @$"// This file is intentionally left almost empty.
// It's needed to ensure the {projectNamespace}.Components namespace is recognized by the compiler.
namespace {projectNamespace}.Components
{{
    internal class _ComponentsPlaceholder
    {{
        // This class serves as a placeholder to define the namespace.
        // Common application components can be added here or in separate files within this folder.
    }}
}}
";
                await File.WriteAllTextAsync(placeholderComponentPath, placeholderContent);
                _logger.LogDebug("Generando placeholder _ComponentsPlaceholder.cs en: {Path} para definir el namespace {ProjectNamespace}.Components", placeholderComponentPath, projectNamespace);
            }


            var pagesFolder = Path.Combine(rutaProyecto, "Pages");
            Directory.CreateDirectory(pagesFolder);
            _logger.LogDebug("Generando carpeta Pages en: {Path}", pagesFolder);


            var layoutPath = Path.Combine(layoutFolder, "MainLayout.razor");
            if (!File.Exists(layoutPath))
            {
                _logger.LogDebug("Generando MainLayout.razor");
                var layoutContent = @$"@inherits LayoutComponentBase

<div class=""page"">
    <div class=""sidebar"">
        <NavMenu />
    </div>

    <main>
        <div class=""top-row px-4"">
            <a href=""https://learn.microsoft.com/aspnet/core/"" target=""_blank"">About</a>
        </div>

        <article class=""content px-4"">
            @Body
        </article>
    </main>
</div>

<div id=""blazor-error-ui"">
    An unhandled error has occurred.
    <a href="""" class=""reload"">Reload</a>
    <a class=""dismiss"">🗙</a>
</div>
";
                await File.WriteAllTextAsync(layoutPath, layoutContent);
            }

            var navMenuPath = Path.Combine(layoutFolder, "NavMenu.razor");
            if (!File.Exists(navMenuPath))
            {
                _logger.LogDebug("Generando NavMenu.razor");
                var navMenuContent = @$"
<div class=""top-row ps-3 navbar navbar-dark"">
    <div class=""container-fluid"">
        <a class=""navbar-brand"" href="""">{projectNamespace}</a>
    </div>
</div>

<input type=""checkbox"" title=""Navigation menu"" class=""navbar-toggler"" />

<div class=""nav-scrollable"" onclick=""document.querySelector('.navbar-toggler').click()"">
    <nav class=""flex-column"">
        <div class=""nav-item px-3"">
            <NavLink class=""nav-link"" href="""" Match=""NavLinkMatch.All"">
                <span class=""bi bi-house-door-fill-nav-menu"" aria-hidden=""true""></span> Home
            </NavLink>
        </div>

    </nav>
</div>
";
                await File.WriteAllTextAsync(navMenuPath, navMenuContent);

                var navMenuCssPath = Path.ChangeExtension(navMenuPath, ".razor.css");
                if (!File.Exists(navMenuCssPath))
                {
                    var cssContent = @"/* Basic NavMenu Styles - Can be expanded */
.navbar-toggler { /* Styles for the mobile menu button */ }
/* Add other NavMenu specific styles here */
";
                    await File.WriteAllTextAsync(navMenuCssPath, cssContent);
                }
            }

            var errorPagePath = Path.Combine(pagesFolder, "Error.razor");
            if (!File.Exists(errorPagePath))
            {
                _logger.LogDebug("Generando Error.razor en Pages");
                var errorPageContent = @$"@page ""/Error""
@using Microsoft.AspNetCore.Components.Web
@inject ILogger<Error> Logger

<PageTitle>Error</PageTitle>

<h1 class=""text-danger"">Error.</h1>
<h2 class=""text-danger"">An error occurred while processing your request.</h2>

@if (ShowDetailedErrors)
{{
    <p>
        <strong>Development environment error details:</strong>
        <code>@Exception?.Message</code>
        <br />
        <a href=""javascript:location.reload()"">Reload</a>
    </p>
}}
else
{{
     <p>Sorry, something went wrong. Please try again later.</p>
}}

@code {{
    [CascadingParameter]
    private HttpContext? HttpContext {{ get; set; }}

    [Parameter]
    public Exception? Exception {{ get; set; }}

    private bool ShowDetailedErrors => !string.IsNullOrEmpty(Exception?.Message);

    protected override void OnInitialized()
    {{
        Logger.LogError(Exception, ""An unhandled error occurred."");
    }}
}}";
                await File.WriteAllTextAsync(errorPagePath, errorPageContent);
            }

            var indexPagePath = Path.Combine(pagesFolder, "Index.razor");
            if (!File.Exists(indexPagePath))
            {
                _logger.LogDebug("Generando Index.razor");
                var indexPageContent = @$"@page ""/""

<PageTitle>Index - {promptTitulo}</PageTitle>

<h1>Hello, world!</h1>

Welcome to your new app '{promptTitulo}'.
";
                await File.WriteAllTextAsync(indexPagePath, indexPageContent);
            }

            var wwwrootFolder = Path.Combine(rutaProyecto, "wwwroot");
            Directory.CreateDirectory(wwwrootFolder);
            var cssFolder = Path.Combine(wwwrootFolder, "css");
            Directory.CreateDirectory(cssFolder);
            var bootstrapFolder = Path.Combine(cssFolder, "bootstrap");
            Directory.CreateDirectory(bootstrapFolder);

            var bootstrapCssPath = Path.Combine(bootstrapFolder, "bootstrap.min.css");
            if (!File.Exists(bootstrapCssPath))
            {
                _logger.LogDebug("Generando placeholder bootstrap.min.css");
                await File.WriteAllTextAsync(bootstrapCssPath, @"/* Download Bootstrap 5+ and place bootstrap.min.css here */");
            }

            var appCssPath = Path.Combine(cssFolder, "app.css");
            if (!File.Exists(appCssPath))
            {
                _logger.LogDebug("Generando app.css");
                var appCssContent = @"/* Basic app styles - Can be expanded */
html, body { font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif; }
/* Add other global styles here */
";
                await File.WriteAllTextAsync(appCssPath, appCssContent);
            }

            var faviconPath = Path.Combine(wwwrootFolder, "favicon.png");
            if (!File.Exists(faviconPath))
            {
                 _logger.LogDebug("Generando placeholder favicon.png");
                 byte[] pngPlaceholder = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
                 await File.WriteAllBytesAsync(faviconPath, pngPlaceholder);
            }
        }

        private string CrearPromptParaTarea(Shared.Prompt promptOriginal, string tareaEspecifica, string tipoCodigo, string rutaProyecto, string? targetRelativePath)
        {
            string formatoCodigo = tipoCodigo == "Razor" ? "un componente Blazor (.razor)" : "una clase C# (.cs)";
            string nombreArchivo = targetRelativePath != null ? Path.GetFileName(targetRelativePath) : "(Nombre a inferir)";
            string projectNamespace = SanitizeNamespace(promptOriginal.Titulo);
            string instruccionesAdicionales = "";

            if (tipoCodigo == "Razor")
            {
                 instruccionesAdicionales += @$"
            **Importante para Páginas/Componentes Razor - Inclusión de Namespaces:**
            - **Verifica si los siguientes namespaces comunes ya están en `_Imports.razor`. Si no lo están, considera añadirlos allí para disponibilidad global, o directamente en este archivo si es más específico:**
                - `@using {projectNamespace}.Models;` (MUY COMÚNMENTE NECESARIO para cualquier página que maneje datos de la aplicación)
                - `@using {projectNamespace}.Data;` (COMÚNMENTE NECESARIO si se interactúa con DbContext o entidades directamente en algunas páginas, aunque los servicios son preferibles)
                - `@using {projectNamespace}.Services;` (COMÚNMENTE NECESARIO si la página va a invocar lógica de negocio o acceso a datos a través de servicios)
                - `@using {projectNamespace}.Layout;` (Si usas componentes de Layout específicos)
                - `@using {projectNamespace}.Components;` (Si usas componentes compartidos de UI)
            - **Para este archivo específico (`{nombreArchivo}`), asegúrate ABSOLUTAMENTE que todos los namespaces para los tipos que utilices (modelos, DbContext, servicios, etc.) estén referenciados, ya sea vía `_Imports.razor` o con un `@using` directo en este archivo.**
            - Incluye cualquier otro `using` estándar necesario (ej. `System.Collections.Generic`, `Microsoft.AspNetCore.Components`).";

                // Conditionally add anti-duplication note for Index or list-like pages
                string lowerTarea = tareaEspecifica.ToLowerInvariant();
                string lowerNombreArchivo = nombreArchivo.ToLowerInvariant();
                if (lowerNombreArchivo.Contains("index.razor") ||
                    lowerTarea.Contains("index.razor") ||
                    (lowerTarea.Contains("listar") && (lowerTarea.Contains("tabla") || lowerTarea.Contains("grid"))) ||
                    lowerTarea.Contains("página de listado"))
                {
                    instruccionesAdicionales += "\n            **Nota Adicional de Codificación:** Al escribir el bloque `@code`, presta mucha atención para NO definir la misma variable, campo (field), o propiedad más de una vez. Cada nombre de variable debe ser único dentro de su scope para evitar errores de compilación.";
                }
            }

            if (EsTareaCrud(tareaEspecifica))
            {
                instruccionesAdicionales += @$"
            **Instrucciones Detalladas para Archivo: '{nombreArchivo}' ({tipoCodigo})**
            La TAREA ESPECÍFICA es: '{tareaEspecifica}'.
            Basado en esto, genera el código COMPLETO y FUNCIONAL.

            **Si es un Modelo C# (.cs):**
            - Define una clase pública con el nombre apropiado (ej. 'public class NombreEntidad').
            - Incluye propiedades públicas con tipos de datos C# correctos (ej. 'public int Id {{ get; set; }}', 'public string Nombre {{ get; set; }}').
            - Añade DataAnnotations necesarias (ej. '[Key]', '[Required]', '[StringLength(100)]') de 'System.ComponentModel.DataAnnotations'.
            - Incluye un constructor vacío si es necesario para EF Core.
            - Asegúrate de tener todos los 'using' necesarios (ej. 'using System.ComponentModel.DataAnnotations;').

            **Si es un DbContext C# (.cs) (ej. 'Data/ApplicationDbContext.cs'):**
            - Hereda de 'Microsoft.EntityFrameworkCore.DbContext'.
            - Incluye un constructor que acepte 'DbContextOptions<ApplicationDbContext>'.
            - Define propiedades 'DbSet<NombreEntidad> NombreEntidades {{ get; set; }}' para cada entidad.
            - Si es necesario, sobreescribe 'OnModelCreating(ModelBuilder modelBuilder)' para configuraciones adicionales (ej. relaciones, claves compuestas).
            - Asegúrate de tener todos los 'using' necesarios (ej. 'using Microsoft.EntityFrameworkCore;', 'using {projectNamespace}.Models;').

            **Si es una Interfaz de Servicio C# (.cs) (ej. 'Services/IClienteService.cs'):**
            - Define una interfaz pública (ej. 'public interface IClienteService').
            - Declara métodos para operaciones CRUD (ej. 'Task<List<Cliente>> GetAllClientesAsync();', 'Task<Cliente> GetClienteByIdAsync(int id);', 'Task CreateClienteAsync(Cliente cliente);', 'Task UpdateClienteAsync(Cliente cliente);', 'Task DeleteClienteAsync(int id);').
            - Utiliza los modelos del proyecto en las firmas de los métodos.
            - Asegúrate de tener todos los 'using' necesarios (ej. 'using {projectNamespace}.Models;').

            **Si es una Clase de Servicio C# (.cs) (ej. 'Services/ClienteService.cs'):**
            - Implementa la interfaz de servicio correspondiente (ej. 'public class ClienteService : IClienteService').
            - Inyecta el DbContext (ej. 'private readonly ApplicationDbContext _context;') a través del constructor.
            - Implementa todos los métodos de la interfaz, usando el DbContext para interactuar con la base de datos.
            - Incluye manejo básico de errores (try-catch) y logging si es posible.
            - Asegúrate de tener todos los 'using' necesarios (ej. 'using Microsoft.EntityFrameworkCore;', 'using {projectNamespace}.Models;', 'using {projectNamespace}.Data;').";

                if (tipoCodigo == "Razor")
                {
                    instruccionesAdicionales += @$"
            **Si es un Componente/Página Razor (.razor) para CRUD:**
            - **General:**
                - Usa '@page ""/ruta-correcta""' para páginas.
                - Inyecta servicios necesarios (ej. '@inject IClienteService ClienteService').
            - **Para Listas (ej. 'Pages/Clientes/Index.razor'):**
                - Muestra los datos en una tabla HTML.
                - Incluye botones/enlaces para 'Crear Nuevo', 'Editar', 'Detalles', 'Eliminar' para cada item.
                - En '@code':
                    - Define 'private List<Cliente> clientes;'
                    - En 'OnInitializedAsync()', llama al servicio para obtener todos los clientes (ej. 'clientes = await ClienteService.GetAllClientesAsync();').
                    - Métodos para navegar a crear/editar/eliminar páginas.
            - **Para Formularios (Crear/Editar) (ej. 'Pages/Clientes/Create.razor', 'Pages/Clientes/Edit.razor'):**
                - Usa '<EditForm Model=""@cliente"" OnValidSubmit=""HandleSubmit"">'
                - Incluye '<DataAnnotationsValidator />' y '<ValidationSummary />'.
                - Usa componentes de entrada como '<InputText @bind-Value=""cliente.Nombre"" />', etc., para cada propiedad del modelo.
                - Botón de submit.
                - En '@code':
                    - Define '[Parameter] public int Id {{ get; set; }}' (para Editar/Detalles).
                    - Define 'private Cliente cliente = new Cliente();'
                    - En 'OnInitializedAsync()' (para Editar), carga el cliente por Id ('cliente = await ClienteService.GetClienteByIdAsync(Id);').
                    - Método 'HandleSubmit()': Llama al servicio correspondiente (CreateAsync o UpdateAsync).
                    - Navegación después de la operación ('NavigationManager.NavigateTo(""/clientes"");').
            - **Para Páginas de Confirmación (Eliminar) (ej. 'Pages/Clientes/Delete.razor'):**
                - Muestra detalles del item a eliminar.
                - Botón de confirmación y cancelación.
                - En '@code': Llama al servicio 'DeleteAsync' y navega.
            - **Para Páginas de Detalles (ej. 'Pages/Clientes/Details.razor'):**
                - Muestra todas las propiedades del modelo.
                - Enlace para volver a la lista o editar.
";
                }
            }

            string rutaInfo = targetRelativePath != null ? $"para el archivo '{targetRelativePath}'" : $"que se guardará como '{nombreArchivo}' (aproximadamente)";
            return @$"Contexto General del Proyecto (Prompt Original):
{promptOriginal.Descripcion}

Tarea Específica a Implementar:
{tareaEspecifica}

Instrucciones Generales para la Generación de Código:
1.  Genera el código fuente COMPLETO y FUNCIONAL para {formatoCodigo} {rutaInfo} que cumpla con la TAREA ESPECÍFICA.
2.  El código debe ser COMPILABLE y estar listo para usarse en un proyecto .NET 8 Blazor Server.
3.  Incluye TODOS los 'using' necesarios al principio del archivo (para C#) o donde corresponda (para Razor).
4.  Añade comentarios XML (<summary>...</summary>) para todas las clases y métodos públicos (si es C#). Para código Razor, añade comentarios C# (// o /* */) para explicar lógica compleja en el bloque @code.
5.  Sigue las mejores prácticas de codificación de .NET 8 y Blazor.
6.  Implementa manejo básico de errores (try-catch) donde sea crítico (ej. llamadas a base de datos, servicios externos).
{instruccionesAdicionales}
RESTRICCIÓN ABSOLUTA: Devuelve ÚNICAMENTE el código fuente completo y correcto para el archivo solicitado. NO incluyas NINGUNA explicación, introducción, resumen, notas, advertencias, ni texto adicional antes o después del bloque de código. NO uses bloques de markdown (como ```csharp, ```html o ```razor) alrededor del código final. Solo el contenido puro del archivo.";
        }

        #region Helper Methods

        private string? ExtractPathFromTask(string task)
        {
            Match match = PathExtractionRegex.Match(task);
            if (match.Success && match.Groups["path"].Success && !string.IsNullOrWhiteSpace(match.Groups["path"].Value))
            {
                string extractedPath = match.Groups["path"].Value.Trim();
                 _logger.LogDebug("PathExtractionRegex (v2) encontró path inicial: '{ExtractedPath}' en tarea: '{Task}'", extractedPath, task);

                // Remover comillas/backticks si están presentes alrededor del path capturado
                if ((extractedPath.StartsWith("'") && extractedPath.EndsWith("'")) ||
                    (extractedPath.StartsWith("\"") && extractedPath.EndsWith("\"")) ||
                    (extractedPath.StartsWith("`") && extractedPath.EndsWith("`")))
                {
                    extractedPath = extractedPath.Substring(1, extractedPath.Length - 2).Trim();
                     _logger.LogDebug("Path después de remover comillas/backticks: '{ExtractedPath}'", extractedPath);
                }

                // Basic sanity check on extracted path
                if (!string.IsNullOrWhiteSpace(extractedPath) &&
                    (extractedPath.EndsWith(".cs") || extractedPath.EndsWith(".razor") || extractedPath.EndsWith(".csproj")) &&
                     extractedPath.Length > 3 &&
                     !extractedPath.Contains(" ")) // Paths con espacios son problemáticos si no están bien manejados/citados consistentemente
                {
                    _logger.LogInformation("PathExtractionRegex (v2) extrajo path válido: '{ExtractedPath}'", extractedPath);
                    return extractedPath;
                }
                else
                {
                    _logger.LogWarning("PathExtractionRegex (v2) extrajo un path, pero fue invalidado por sanity checks: '{ExtractedPath}'", extractedPath);
                }
            }

            _logger.LogDebug("PathExtractionRegex (v2) no encontró un path explícito válido en: '{Task}'. Intentando FileNameExtractionRegex.", task);
            match = FileNameExtractionRegex.Match(task);
            if (match.Success && match.Groups["filename"].Success)
            {
                string filename = match.Groups["filename"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(filename) && filename.Length > 3)
                {
                    _logger.LogDebug("FileNameExtractionRegex encontró filename: '{Filename}'. Se usará para inferencia de carpeta.", filename);
                    return null; // Indica que se necesita inferir la carpeta, pero tenemos un nombre de archivo.
                }
            }

            _logger.LogWarning("Ningún Regex pudo extraer una ruta/nombre de archivo válido de la tarea: {Task}", task);
            return null;
        }

        private bool IsPathWithinProject(string fullPath, string projectRootPath)
        {
             try
             {
                  string normalizedFullPath = Path.GetFullPath(fullPath);
                  string normalizedProjectRootPath = Path.GetFullPath(projectRootPath);
                  if (!normalizedProjectRootPath.EndsWith(Path.DirectorySeparatorChar))
                  {
                       normalizedProjectRootPath += Path.DirectorySeparatorChar;
                  }
                  if ((Path.GetDirectoryName(normalizedFullPath) ?? "").Equals(Path.GetFullPath(projectRootPath), StringComparison.OrdinalIgnoreCase))
                  {
                       string fileNameLower = Path.GetFileName(normalizedFullPath).ToLowerInvariant();
                       if(fileNameLower == "program.cs" || fileNameLower == "app.razor" || fileNameLower == "_imports.razor" || fileNameLower.EndsWith(".csproj") || fileNameLower == "routes.razor")
                       {
                            return true;
                       }
                       _logger.LogWarning("Archivo '{FileName}' detectado en la raíz del proyecto, pero no es un archivo base esperado.", Path.GetFileName(normalizedFullPath));
                  }
                  return normalizedFullPath.StartsWith(normalizedProjectRootPath, StringComparison.OrdinalIgnoreCase);
             }
             catch (Exception ex)
             {
                  _logger.LogWarning(ex, "Error al validar ruta '{FullPath}' dentro de '{ProjectRootPath}'", fullPath, projectRootPath);
                  return false;
             }
        }

        private string LimpiarCodigoGemini(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return "";

            var lines = codigo.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();

            if (lines.Any() && lines[0].Trim().StartsWith("```")) { lines.RemoveAt(0); }
            if (lines.Any() && lines.Last().Trim() == "```") { lines.RemoveAt(lines.Count - 1); }

            string[] commonPhrases = {
                "here's the code", "here is the code", "okay, here is the", "sure, here is the", "certainly, here is the",
                "this is the code", "the code is as follows", "find the code below", "below is the code",
                "this code should work", "let me know if you have questions", "hope this helps", "hope this is helpful",
                "this is just an example", "you might need to adjust this", "this implements", "this file contains",
                "the generated code:", "generated code:", "code:", "c# code:", "razor code:", "html code:",
                "```csharp", "```c#", "```razor", "```html", "```xml", "```json",
                "here is the updated code", "here's the updated code", "this is the modified code",
                "i've made the requested changes", "the changes are as follows"
            };

            for (int i = 0; i < 3 && lines.Any(); i++)
            {
                var trimmedLowerLine = lines[0].Trim().ToLowerInvariant();
                bool removed = false;
                foreach (var phrase in commonPhrases)
                {
                    if (trimmedLowerLine.StartsWith(phrase) || trimmedLowerLine.EndsWith(phrase))
                    {
                        _logger.LogTrace("LimpiarCodigoGemini: Removiendo línea introductoria: '{Line}'", lines[0]);
                        lines.RemoveAt(0);
                        removed = true;
                        break;
                    }
                }
                if (!removed) break;
            }

            for (int i = 0; i < 3 && lines.Any(); i++)
            {
                var trimmedLowerLine = lines.Last().Trim().ToLowerInvariant();
                 bool removed = false;
                foreach (var phrase in commonPhrases)
                {
                    if (trimmedLowerLine.StartsWith(phrase) || trimmedLowerLine.EndsWith(phrase) || trimmedLowerLine == phrase)
                    {
                        _logger.LogTrace("LimpiarCodigoGemini: Removiendo línea conclusiva: '{Line}'", lines.Last());
                        lines.RemoveAt(lines.Count - 1);
                        removed = true;
                        break;
                    }
                }
                 if (!removed) break;
            }

            var processedLines = lines.Select(l => l.TrimEnd()).ToList();
            while (processedLines.Any() && string.IsNullOrWhiteSpace(processedLines[0]))
            {
                processedLines.RemoveAt(0);
            }
            while (processedLines.Any() && string.IsNullOrWhiteSpace(processedLines.Last()))
            {
                processedLines.RemoveAt(processedLines.Count - 1);
            }

            return string.Join(Environment.NewLine, processedLines).Trim();
        }

        private bool EsCodigoPlausible(string codigo, string fileName, string tipoCodigo)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                _logger.LogWarning("⚠️ Plausibility check failed for '{FileName}': Código está vacío o es solo espacio en blanco.", fileName);
                return false;
            }

            var lines = codigo.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var nonCommentLines = lines.Where(l =>
                !l.TrimStart().StartsWith("//") &&
                !l.TrimStart().StartsWith("/*") &&
                !(l.TrimStart().StartsWith("*") && !l.TrimStart().StartsWith("*/") && lines.Any(prevL => prevL.TrimStart().StartsWith("/*") && !prevL.TrimEnd().EndsWith("*/"))) &&
                !l.TrimStart().StartsWith("@*") &&
                !(l.TrimStart().StartsWith("*") && !l.TrimStart().StartsWith("*@") && lines.Any(prevL => prevL.TrimStart().StartsWith("@*") && !prevL.TrimEnd().EndsWith("*@")))
            ).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();


            if (nonCommentLines.Count == 0)
            {
                _logger.LogWarning("⚠️ Plausibility check failed for '{FileName}': No hay líneas de código que no sean comentarios o vacías. Total lines: {TotalLines}", fileName, lines.Length);
                return false;
            }

            if (tipoCodigo == "C#")
            {
                bool hasNamespace = nonCommentLines.Any(l => Regex.IsMatch(l, @"^\s*namespace\s+[\w\.]+"));
                bool hasTypeDefinition = nonCommentLines.Any(l => Regex.IsMatch(l, @"\b(class|interface|enum|struct|record)\s+[\w_]+"));
                bool hasBraces = codigo.Contains("{") && codigo.Contains("}");

                if (!hasNamespace) _logger.LogWarning("⚠️ Plausibility check C# ('{FileName}'): Parece faltar 'namespace'.", fileName);
                if (!hasTypeDefinition) _logger.LogWarning("⚠️ Plausibility check C# ('{FileName}'): Parece faltar definición de tipo (class, interface, etc.).", fileName);
                if (!hasBraces) _logger.LogWarning("⚠️ Plausibility check C# ('{FileName}'): Parece faltar llaves '{{' o '}}'.", fileName);

                if (!hasNamespace && !hasTypeDefinition && nonCommentLines.Count < 5) {
                     _logger.LogWarning("⚠️ Plausibility check C# ('{FileName}') FAILED: Muy pocas líneas y faltan namespace y definición de tipo.", fileName);
                     return false;
                }
                if (!hasTypeDefinition && nonCommentLines.Count < 3) {
                     _logger.LogWarning("⚠️ Plausibility check C# ('{FileName}') FAILED: Muy pocas líneas y falta definición de tipo.", fileName);
                     return false;
                }
                 if (!hasBraces && nonCommentLines.Count < 2) {
                     _logger.LogWarning("⚠️ Plausibility check C# ('{FileName}') FAILED: Muy pocas líneas y faltan llaves.", fileName);
                     return false;
                }
            }
            else if (tipoCodigo == "Razor")
            {
                bool hasHtml = nonCommentLines.Any(l => l.Contains("<") && l.Contains(">") && !l.StartsWith("@"));
                bool hasAtDirectives = nonCommentLines.Any(l => l.StartsWith("@") && !l.StartsWith("@@"));
                bool hasCodeBlockContent = false;
                var codeBlockIndex = nonCommentLines.FindIndex(l => l.Trim() == "@code");
                if (codeBlockIndex != -1 && codeBlockIndex < nonCommentLines.Count -1)
                {
                    hasCodeBlockContent = nonCommentLines.Skip(codeBlockIndex + 1).Any(l => l.Trim() != "{" && l.Trim() != "}" && !string.IsNullOrWhiteSpace(l));
                }

                if (!hasHtml && !hasAtDirectives && !hasCodeBlockContent)
                {
                    _logger.LogWarning("⚠️ Plausibility check Razor ('{FileName}') FAILED: No se encontraron tags HTML, ni directivas '@' significativas, ni contenido en bloque '@code'.", fileName);
                    return false;
                }

                bool isLikelyPage = fileName.Contains("Page", StringComparison.OrdinalIgnoreCase) ||
                                    Regex.IsMatch(fileName, @"(Index|Create|Edit|Details|Delete|List)\.razor", RegexOptions.IgnoreCase);
                if (isLikelyPage && !nonCommentLines.Any(l => l.StartsWith("@page")))
                {
                     _logger.LogWarning("⚠️ Plausibility check Razor ('{FileName}'): Parece una página pero no tiene directiva '@page'. Podría ser un error.", fileName);
                }
                 if (nonCommentLines.Count < 1 && !hasHtml && !hasCodeBlockContent)
                {
                     _logger.LogWarning("⚠️ Plausibility check Razor ('{FileName}') FAILED: Muy pocas líneas ({Count}) sin HTML claro o contenido en bloque de código.", fileName, nonCommentLines.Count);
                    return false;
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Plausibility check para '{FileName}': Tipo de código '{TipoCodigo}' no reconocido para validación específica. Se omite validación detallada.", fileName, tipoCodigo);
            }

            _logger.LogInformation("✅ Plausibility check PASSED for '{FileName}'. Non-comment lines: {Count}", fileName, nonCommentLines.Count);
            return true;
        }

        private string InferirTipoCodigo(string tarea, string? targetRelativePath)
        {
             if (targetRelativePath != null)
             {
                  if (targetRelativePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) return "Razor";
                  if (targetRelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return "C#";
             }
             var tLower = tarea.ToLowerInvariant();
             if (tLower.Contains(".razor") || tLower.Contains("página") || tLower.Contains("componente") || tLower.Contains(" vista ") || tLower.Contains(" ui ") || (tLower.Contains("interfaz") && !tLower.Contains("servicio")))
             {
                  return "Razor";
             }
             return "C#";
        }

        private bool EsTareaCrud(string tarea)
        {
             var tLower = tarea.ToLowerInvariant();
             return tLower.Contains("crud") || tLower.Contains("abm") ||
                    (tLower.Contains("crear") && tLower.Contains("leer") && tLower.Contains("actualizar") && tLower.Contains("eliminar")) ||
                    tLower.Contains("gestionar") || tLower.Contains("administrar");
        }

        public string SanitizeFileName(string input)
        {
             var sb = new StringBuilder();
             bool lastWasInvalid = true;
             foreach (var c in input.Trim())
             {
                  if (char.IsLetterOrDigit(c))
                  {
                       sb.Append(c);
                       lastWasInvalid = false;
                  }
                  else if (c == '_' || c == '-')
                  {
                      if (!lastWasInvalid)
                      {
                         sb.Append(c);
                         lastWasInvalid = true;
                      }
                  }
                  else if (char.IsWhiteSpace(c))
                  {
                       if (!lastWasInvalid)
                       {
                            sb.Append('-');
                            lastWasInvalid = true;
                       }
                  }
             }
             if (sb.Length > 0 && (sb[^1] == '-' || sb[^1] == '_'))
             {
                  sb.Length--;
             }
             var result = sb.ToString();
             const int maxLength = 50;
             if (result.Length > maxLength)
             {
                  result = result.Substring(0, maxLength).TrimEnd('-', '_');
             }
             return string.IsNullOrWhiteSpace(result) ? "proyecto-generado" : result;
        }

        private string ExtractCSharpFilename(string code, string tareaFallback)
        {
            var match = Regex.Match(code, @"\b(?:public\s+|internal\s+)?(?:sealed\s+|abstract\s+)?(?:partial\s+)?(class|interface|record|enum|struct)\s+([A-Za-z_][\w]*)");
            if (match.Success)
            {
                return match.Groups[2].Value + ".cs";
            }
            _logger.LogWarning("No se pudo extraer nombre C# del código. Fallback de tarea: {Tarea}", tareaFallback);
            Match nameMatch = FileNameExtractionRegex.Match(tareaFallback);
            if (nameMatch.Success && nameMatch.Groups["filename"].Success && nameMatch.Groups["filename"].Value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                 return SanitizeFileName(Path.GetFileNameWithoutExtension(nameMatch.Groups["filename"].Value)) + ".cs";
            }
            string nombreDeTarea = Regex.Match(tareaFallback, @"\b(?:crear|implementar|generar)\s+(?:la\s+)?(?:clase|interfaz|servicio|modelo|contexto|enum|componente|record|struct)?\s*'?([A-Za-z_]\w+)'?").Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(nombreDeTarea))
            {
                return SanitizeFileName(nombreDeTarea) + ".cs";
            }
            _logger.LogWarning("Fallback GUID nombre archivo C#.");
            return "Clase_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".cs";
        }

        private string ExtractRazorFilename(string code, string tareaFallback)
        {
            var pageMatch = Regex.Match(code, @"@page\s+""\/?([\w\/-]+)(?:/{.*?})?/??""");
            if (pageMatch.Success)
            {
                var parts = pageMatch.Groups[1].Value.Split('/');
                var lastPart = parts.LastOrDefault(p => !string.IsNullOrWhiteSpace(p) && !p.Contains('{'));
                if (!string.IsNullOrWhiteSpace(lastPart))
                {
                    return UppercaseFirst(SanitizeFileName(lastPart)) + ".razor";
                }
            }
            var codeClassMatch = Regex.Match(code, @"@code\s*\{?\s*(?:public\s+)?(?:partial\s+)?class\s+([A-Z][A-Za-z_]\w*)\b", RegexOptions.Singleline);
            if (codeClassMatch.Success)
            {
                return codeClassMatch.Groups[1].Value + ".razor";
            }
            _logger.LogWarning("No se pudo extraer nombre Razor del código. Fallback de tarea: {Tarea}", tareaFallback);
            Match nameMatch = FileNameExtractionRegex.Match(tareaFallback);
             if (nameMatch.Success && nameMatch.Groups["filename"].Success && nameMatch.Groups["filename"].Value.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            {
                 return UppercaseFirst(SanitizeFileName(Path.GetFileNameWithoutExtension(nameMatch.Groups["filename"].Value))) + ".razor";
            }
            string nombreDeTarea = Regex.Match(tareaFallback, @"\b(?:crear|implementar|generar)\s+(?:la\s+)?(?:página|componente|vista)?\s*'?([A-Za-z_]\w+)'?").Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(nombreDeTarea))
            {
                return UppercaseFirst(SanitizeFileName(nombreDeTarea)) + ".razor";
            }
            _logger.LogWarning("Fallback GUID nombre archivo Razor.");
            return "Componente_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".razor";
        }

        private string UppercaseFirst(string s)
        {
             if (string.IsNullOrEmpty(s)) return s;
             if (s.Length == 1) return s.ToUpperInvariant();
             return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private string InferirSubcarpeta(string tarea, string codigo)
        {
            var t = tarea.ToLowerInvariant();
            var c = codigo.ToLowerInvariant();
            if (t.Contains("controlador") || t.Contains("controller") || t.Contains(" api ") || c.Contains("[apicontroller]") || c.Contains("controllerbase")) return "Controllers";
            if (t.Contains("contexto") || t.Contains("dbcontext") || t.Contains("base de datos") || t.Contains("database") || t.Contains("repositorio") || t.Contains("repository") || t.Contains("unit of work") || c.Contains(" entityframeworkcore") || c.Contains(": dbcontext")) return "Data";
            if (t.Contains("servicio") || t.Contains("service") || t.Contains("email") || t.Contains("exportar") || t.Contains("login") || t.Contains("fachada") || t.Contains("manager") || t.Contains("helper") || t.Contains("client") || t.Contains("logic") || t.Contains("business")) return "Services";
            if (c.Contains("@page ") || (t.Contains("página") && t.Contains(".razor"))) return "Pages";
            if (t.Contains("layout") || (t.Contains(".razor") && (t.Contains("layout/") || t.Contains("layout\\")))) return "Layout";
            if (t.Contains("navmenu") || (t.Contains(".razor") && (t.Contains("shared/") || t.Contains("shared\\")))) return "Layout";
            if (t.Contains(".razor") || t.Contains("componente") || (t.Contains(" ui ") && !t.Contains("servicio")) || (t.Contains("interfaz") && !c.Contains(" public interface ") && !t.Contains("servicio"))) return "Components";
            if (t.Contains("interfaz") || t.Contains("interface") || c.Contains(" public interface ")) return "Interfaces";
            if (t.Contains("modelo") || t.Contains("model") || t.Contains("entidad") || t.Contains("entity") || t.Contains("dto") || t.Contains("viewmodel") || c.Contains(" public class ") || c.Contains(" public record ") || c.Contains(" public struct ")) return "Models";
            if (t.Contains("configuración") || t.Contains("appsettings") || t.Contains("startup") || t.Contains("program.cs")) return "";

            _logger.LogDebug("No se pudo inferir subcarpeta para tarea '{Tarea}'. Usando raíz.", tarea);
            return "";
        }

        private async Task GenerarCsprojAsync(string nombreProyecto, string rutaProyecto)
        {
             var csprojPath = Path.Combine(rutaProyecto, $"{nombreProyecto}.csproj");
             if (File.Exists(csprojPath)) return;
             _logger.LogDebug("Generando .csproj: {Path}", csprojPath);
             var csprojContent = @$"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>{SanitizeNamespace(nombreProyecto)}</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.AspNetCore.Components.Web"" Version=""8.0.0"" />
    <PackageReference Include=""Microsoft.EntityFrameworkCore.InMemory"" Version=""8.0.0"" />
  </ItemGroup>
</Project>";
             await File.WriteAllTextAsync(csprojPath, csprojContent);
        }

        private string SanitizeNamespace(string projectName)
        {
             var sanitized = Regex.Replace(projectName, @"[^\w\.]", "_");
             if (string.IsNullOrWhiteSpace(sanitized) || (!char.IsLetter(sanitized[0]) && sanitized[0] != '_'))
             {
                  sanitized = "_" + sanitized;
             }
             sanitized = Regex.Replace(sanitized, @"\.{2,}", ".");
             sanitized = sanitized.Trim('.');
             sanitized = string.Join(".", sanitized.Split('.').Select(p => ToPascalCase(p)));
             return string.IsNullOrWhiteSpace(sanitized) ? "_GeneratedProject" : sanitized;
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return "_";
            var parts = input.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!parts.Any())
            {
                var fallback = new string(input.Where(char.IsLetterOrDigit).ToArray());
                if (string.IsNullOrEmpty(fallback)) return "_";
                parts = new[] { fallback };
            }
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length > 0)
                {
                    sb.Append(char.ToUpperInvariant(part[0]));
                    if (part.Length > 1)
                    {
                        sb.Append(part.Substring(1).ToLowerInvariant());
                    }
                }
            }
            var result = sb.ToString();
            if (string.IsNullOrEmpty(result)) return "_";
             if (char.IsDigit(result[0])) result = "_" + result;
            return result;
        }
        #endregion
    }
}

[end of Infraestructura/DesarrolladorAgent.cs]

[end of Infraestructura/DesarrolladorAgent.cs]
