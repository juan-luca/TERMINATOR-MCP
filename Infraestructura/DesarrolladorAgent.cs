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

        // Regex (sin cambios respecto a la versión anterior)
        private static readonly Regex PathExtractionRegex = new Regex( @"(?:archivo|modelo|página|componente|servicio|DbContext|fichero|en|a|para|modificar)\s+['""]?\s*(?<path>(?:[\w\-\.\s]+\/|[\w\-\.\s]+\\)+[\w\-\.\s]+\.(cs|razor|csproj))\s*['""]?" + @"|(?:en|modificar)\s+['""]?\s*(?<path>Program\.cs|App\.razor|_Imports\.razor)\s*['""]?" + @"['""]?\s*(?<path>(?:Models|Data|Services|Pages|Components|Shared)(?:\/|\\)[\w\-\.\s\\\/]+\.(cs|razor))\s*['""]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
                // Pasar Shared.Prompt a GenerarArchivosBaseAsync
                await GenerarArchivosBaseAsync(nombreProyecto, rutaProyecto, prompt); // Pasar el prompt completo

                targetRelativePath = ExtractPathFromTask(tarea);
                bool rutaExplicita = false;
                if (targetRelativePath != null)
                {
                    _logger.LogDebug("Ruta potencial extraída: '{RelativePath}'", targetRelativePath);
                    targetRelativePath = targetRelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Replace("Razor Pages", "Pages").Trim();
                    if (targetRelativePath.StartsWith(Path.DirectorySeparatorChar)) { targetRelativePath = targetRelativePath.Length > 1 ? targetRelativePath.Substring(1) : ""; }
                    if (string.IsNullOrWhiteSpace(targetRelativePath) || targetRelativePath.Any(Path.GetInvalidPathChars().Contains)) { _logger.LogWarning("Ruta extraída inválida '{OriginalPath}'. Se inferirá.", targetRelativePath); targetRelativePath = null; }
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
                              _logger.LogInformation("Ruta EXPLICITA determinada: '{FullPath}'", rutaCompletaArchivo);
                              rutaExplicita = true;
                         }
                         catch (Exception ex)
                         {
                              _logger.LogError(ex, "Error al validar ruta '{RelativePath}'. Se inferirá.", targetRelativePath);
                              targetRelativePath = null;
                         }
                    }
                } else { _logger.LogWarning("No se pudo extraer ruta explícita: '{Tarea}'. Se inferirá.", tarea); }

                string normalizedTargetPathForCheck = targetRelativePath ?? "";
                bool esModificacionBase = rutaExplicita &&
                                          (normalizedTargetPathForCheck.Equals("Shared" + Path.DirectorySeparatorChar + "NavMenu.razor", StringComparison.OrdinalIgnoreCase) ||
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

        // Método para crear archivo usando Shared.Prompt
        private async Task CrearNuevoArchivoAsync(string rutaProyecto, string tarea, string? targetRelativePath, bool rutaExplicita, Shared.Prompt prompt)
        {
            string codigoGenerado = "";
            string rawCodigoGenerado = "";
            string rutaCompletaArchivo = "";
            string nombreArchivo = "";
            string tipoCodigo = "C#"; // Default
            try
            {
                tipoCodigo = InferirTipoCodigo(tarea, targetRelativePath);
                if (!rutaExplicita)
                {
                    _logger.LogDebug("Ejecutando inferencia ruta/nombre CREACIÓN...");
                    if (tipoCodigo == "Razor") nombreArchivo = ExtractRazorFilename("", tarea); else nombreArchivo = ExtractCSharpFilename("", tarea);
                    string subcarpetaInferida = InferirSubcarpeta(tarea, "");
                    rutaCompletaArchivo = Path.Combine(rutaProyecto, subcarpetaInferida, nombreArchivo);
                    _logger.LogInformation("Ruta INFERIDA nuevo archivo: {FullPath}", rutaCompletaArchivo);
                    targetRelativePath = Path.GetRelativePath(rutaProyecto, rutaCompletaArchivo);
                } else {
                    rutaCompletaArchivo = Path.GetFullPath(Path.Combine(rutaProyecto, targetRelativePath!));
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

                if (!EsCodigoPlausible(codigoGenerado, nombreArchivo, tipoCodigo)) // Check includes null/whitespace
                {
                    _logger.LogWarning("⚠️ Código generado CREACIÓN '{File}' NO PLAUSIBLE o vacío. Omitido. Primeras 500 chars del contenido problemático:\n{CodigoProblematico}", nombreArchivo, string.IsNullOrEmpty(codigoGenerado) ? "[VACIO]" : codigoGenerado.Substring(0, Math.Min(500, codigoGenerado.Length)));
                    return; // Skip writing
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

        // Método para modificar archivo usando Shared.Prompt
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
            if (!EsCodigoPlausible(modifiedContentClean, Path.GetFileName(filePath), fileTypeForPlausibility)) // Check includes null/whitespace
            {
                _logger.LogWarning("⚠️ Código modificado '{File}' NO PLAUSIBLE o vacío. No se sobrescribe. Primeras 500 chars del contenido problemático:\n{CodigoProblematico}", Path.GetFileName(filePath), string.IsNullOrEmpty(modifiedContentClean) ? "[VACIO]" : modifiedContentClean.Substring(0, Math.Min(500, modifiedContentClean.Length)));
                return; // Skip writing
            }

             // Optional validation: Check if the modified code significantly differs in length
             // Make sure originalContent is not empty before calculating percentage difference to avoid division by zero or skewed results.
             if (originalContent.Length > 50 && Math.Abs(originalContent.Length - modifiedContentClean.Length) > originalContent.Length * 0.75)
             {
                  _logger.LogWarning("⚠️ Código modificado {File} difiere mucho en longitud (Original: {OrigLen}, Nuevo: {NewLen}). NO SE SOBREESCRIBIRÁ.", Path.GetFileName(filePath), originalContent.Length, modifiedContentClean.Length);
                  return;
             }
              if (string.IsNullOrWhiteSpace(originalContent) && modifiedContentClean.Length > 10000) // Heuristic: If original was empty/small and new is huge
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

        // Método para crear prompt de modificación usando Shared.Prompt
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

        // Método para generar archivos base usando Shared.Prompt
        private async Task GenerarArchivosBaseAsync(string nombreProyecto, string rutaProyecto, Shared.Prompt prompt) // Cambiado a Shared.Prompt
        {
            string projectNamespace = SanitizeNamespace(nombreProyecto);
            string promptTitulo = prompt.Titulo; // Extraer título aquí

            // Program.cs
            var programPath = Path.Combine(rutaProyecto, "Program.cs");
            if (!File.Exists(programPath))
            {
                _logger.LogDebug("Generando Program.cs");
                // Basic .NET 8 Blazor Web App Program.cs structure
                var programContent = @$"using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// using {projectNamespace}.Data; // Example for DbContext
// using Microsoft.EntityFrameworkCore; // Example for EF Core

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(); // Enable server-side interactivity

// Example DbContext registration (replace with your actual context and provider)
// builder.Services.AddDbContext<ApplicationDbContext>(options =>
//     options.UseInMemoryDatabase(""AppDb""));

// Register other services (e.g., from {projectNamespace}.Services)

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{{
    app.UseExceptionHandler(""/Error"", createScopeForErrors: true);
    // app.UseHsts(); // Consider enabling HSTS in production
}}

// app.UseHttpsRedirection(); // Consider enabling HTTPS redirection

app.UseStaticFiles();
app.UseAntiforgery(); // Add antiforgery middleware

app.MapRazorComponents<App>() // Map the root component (App.razor)
    .AddInteractiveServerRenderMode();

// Map fallback page (optional, depends on routing strategy)
// app.MapFallbackToFile(""/index.html""); // Example for SPA fallback

app.Run();
";
                await File.WriteAllTextAsync(programPath, programContent);
            }

            // App.razor (Root component for Blazor Web App)
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

            // _Imports.razor
            var importsPath = Path.Combine(rutaProyecto, "_Imports.razor");
            if (!File.Exists(importsPath))
            {
                _logger.LogDebug("Generando _Imports.razor");
                var importsContent = @$"@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using {projectNamespace}
@using {projectNamespace}.Components // Assuming a Components folder
@using {projectNamespace}.Layout // Assuming a Layout folder
";
                await File.WriteAllTextAsync(importsPath, importsContent);
            }

            // Routes.razor (New in .NET 8 Blazor Web App)
            var routesPath = Path.Combine(rutaProyecto, "Routes.razor");
             if (!File.Exists(routesPath))
            {
                _logger.LogDebug("Generando Routes.razor base...");
                var routesContent = @$"@using {projectNamespace}.Components // Make sure Components namespace is available if needed

<Router AppAssembly=""@typeof(Program).Assembly"">
    <Found Context=""routeData"">
        <RouteView RouteData=""@routeData"" DefaultLayout=""@typeof(Layout.MainLayout)"" />
        <FocusOnNavigate RouteData=""@routeData"" Selector=""h1"" />
    </Found>
    <NotFound>
        <PageTitle>Not found</PageTitle>
        <LayoutView Layout=""@typeof(Layout.MainLayout)"">
            <p role=""alert"">Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>
";
                 await File.WriteAllTextAsync(routesPath, routesContent);
            }

            // Layout folder and files
            var layoutFolder = Path.Combine(rutaProyecto, "Layout"); // Changed from "Shared" to "Layout" for .NET 8 convention
            Directory.CreateDirectory(layoutFolder);

            var layoutPath = Path.Combine(layoutFolder, "MainLayout.razor");
            if (!File.Exists(layoutPath))
            {
                _logger.LogDebug("Generando MainLayout.razor");
                var layoutContent = @$"@inherits LayoutComponentBase
@using {projectNamespace}.Layout // Ensure using for NavMenu

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
                // Basic NavMenu for .NET 8 Blazor Web App
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

        @* Add other navigation links here *@

    </nav>
</div>
";
                await File.WriteAllTextAsync(navMenuPath, navMenuContent);

                // Add basic CSS for NavMenu if it doesn't exist
                var navMenuCssPath = Path.ChangeExtension(navMenuPath, ".razor.css");
                if (!File.Exists(navMenuCssPath))
                {
                    var cssContent = @$"/* Basic NavMenu Styles */
.navbar-toggler {{
    appearance: none;
    cursor: pointer;
    width: 3.5rem;
    height: 2.5rem;
    color: white;
    position: absolute;
    top: 0.5rem;
    right: 1rem;
    border: 1px solid rgba(255, 255, 255, 0.1);
    background: url(""data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 30 30'%3e%3cpath stroke='rgba%28255, 255, 255, 0.55%29' stroke-linecap='round' stroke-miterlimit='10' stroke-width='2' d='M4 7h22M4 15h22M4 23h22'/%3e%3c/svg%3e"") no-repeat center/1.75rem rgba(255, 255, 255, 0.1);
}}

.navbar-toggler:checked {{
    background-color: rgba(255, 255, 255, 0.5);
}}

.top-row {{
    height: 3.5rem;
    background-color: rgba(0,0,0,0.4);
}}

.navbar-brand {{
    font-size: 1.1rem;
}}

.bi {{
    width: 2rem;
    font-size: 1.1rem;
    vertical-align: text-top;
    top: -2px;
}}

.nav-item {{
    font-size: 0.9rem;
    padding-bottom: 0.5rem;
}}

    .nav-item:first-of-type {{
        padding-top: 1rem;
    }}

    .nav-item:last-of-type {{
        padding-bottom: 1rem;
    }}

    .nav-item ::deep .nav-link {{
        color: #d7d7d7;
        background: none;
        border: none;
        border-radius: 4px;
        height: 3rem;
        display: flex;
        align-items: center;
        line-height: 3rem;
        width: 100%;
    }}

.nav-item ::deep .nav-link.active {{
    background-color: rgba(255,255,255,0.37);
    color: white;
}}

.nav-item ::deep .nav-link:hover {{
    background-color: rgba(255,255,255,0.1);
    color: white;
}}

.nav-scrollable {{
    display: none;
}}

.navbar-toggler:checked ~ .nav-scrollable {{
    display: block;
}}

@media (min-width: 641px) {{
    .navbar-toggler {{
        display: none;
    }}

    .nav-scrollable {{
        /* Never collapse the sidebar for wide screens */
        display: block;

        /* Allow sidebar to scroll for tall menus */
        height: calc(100vh - 3.5rem);
        overflow-y: auto;
    }}
}}
";
                    await File.WriteAllTextAsync(navMenuCssPath, cssContent);
                }
            }

            // Pages folder and files
            var pagesFolder = Path.Combine(rutaProyecto, "Pages"); // Default folder for routable components
            Directory.CreateDirectory(pagesFolder);

            // Error component (Error.razor) for Blazor Web App
            var errorPath = Path.Combine(pagesFolder, "Error.razor");
            if (!File.Exists(errorPath))
            {
                _logger.LogDebug("Generando Error.razor");
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

    private bool ShowDetailedErrors => !string.IsNullOrEmpty(Exception?.Message); // Basic check

    protected override void OnInitialized()
    {{
        // Log the error
        Logger.LogError(Exception, ""An unhandled error occurred."");

        // You might want to add more sophisticated error handling/display logic here
    }}
}}";
                await File.WriteAllTextAsync(errorPath, errorPageContent);
            }

            // Index page (Index.razor)
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

            // wwwroot folder and basic CSS/favicon
            var wwwrootFolder = Path.Combine(rutaProyecto, "wwwroot");
            Directory.CreateDirectory(wwwrootFolder);
            var cssFolder = Path.Combine(wwwrootFolder, "css");
            Directory.CreateDirectory(cssFolder);
            var bootstrapFolder = Path.Combine(cssFolder, "bootstrap");
            Directory.CreateDirectory(bootstrapFolder);

            // Placeholder bootstrap.min.css (expecting user to add the actual file)
            var bootstrapCssPath = Path.Combine(bootstrapFolder, "bootstrap.min.css");
            if (!File.Exists(bootstrapCssPath))
            {
                _logger.LogDebug("Generando placeholder bootstrap.min.css");
                await File.WriteAllTextAsync(bootstrapCssPath, @"/* Download Bootstrap 5+ and place bootstrap.min.css here */");
            }

            // Basic app.css
            var appCssPath = Path.Combine(cssFolder, "app.css"); // Changed from site.css
            if (!File.Exists(appCssPath))
            {
                _logger.LogDebug("Generando app.css");
                var appCssContent = @$"html, body {{
    font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif;
}}

h1:focus {{
    outline: none;
}}

a, .btn-link {{
    color: #0071c1;
}}

.btn-primary {{
    color: #fff;
    background-color: #1b6ec2;
    border-color: #1861ac;
}}

.valid.modified:not([type=checkbox]) {{
    outline: 1px solid #26b050;
}}

.invalid {{
    outline: 1px solid red;
}}

.validation-message {{
    color: red;
}}

#blazor-error-ui {{
    background: lightyellow;
    bottom: 0;
    box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
    display: none;
    left: 0;
    padding: 0.6rem 1.25rem 0.7rem 1.25rem;
    position: fixed;
    width: 100%;
    z-index: 1000;
}}

    #blazor-error-ui .dismiss {{
        cursor: pointer;
        position: absolute;
        right: 0.75rem;
        top: 0.5rem;
    }}

.blazor-error-boundary {{
    background: url(data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNTYiIGhlaWdodD0iNDkiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyIgeG1sbnM6eGxpbms9Imh0dHA6Ly93d3cudzMub3JnLzE5OTkveGxpbmsiIG92ZXJmbG93PSJoaWRkZW4iPjxkZWZzPjxjbGlwUGF0aCBpZD0iY2xpcDAiPjxyZWN0IHg9IjAiIHk9IjAiIHdpZHRoPSI1NiIgaGVpZ2h0PSI0OSIvPjwvY2xpcFBhdGg+PC9kZWZzPjxnIGNsaXAtcGF0aD0idXJsKCNjbGlwMCkiIHRyYW5zZm9ybT0idHJhbnNsYXRlKC0xNjIuMjUyIC0zOS45ODYpIj48cGF0aCBkPSJNMTY5LjE5OCA0OC4xMDJjLTUuNTk0IDAtMTAuMjk2IDQuNTAyLTEwLjI5NiAxMC4yOTYgMCA1LjU5NCA0LjUwMiAxMC4yOTYgMTAuMjk2IDEwLjI5NiA1LjU5NCAwIDEwLjI5Ni00LjUwMiAxMC4yOTYtMTAuMjk2IDAtNS41OTQtNC41MDItMTAuMjk2LTEwLjI5Ni0xMC4yOTZ6TTIxNC45NjEgNDguMTAyYy01LjU5NCAwLTEwLjI5NiA0LjUwMi0xMC4yOTYgMTAuMjk2IDAgNS41OTQgNC41MDIgMTAuMjk2IDEwLjI5NiAxMC4yOTYgNS41OTQgMCAxMC4yOTYtNC41MDIgMTAuMjk2LTEwLjI5NiAwLTUuNTk0LTQuNTAyLTEwLjI5Ni0xMC4yOTYtMTAuMjk2ek0xOTEuNzk0IDQzLjA5NGMtNS41OTQgMC0xMC4yOTYgNC41MDItMTAuMjk2IDEwLjI5NiAwIDUuNTk0IDQuNTAyIDEwLjI5NiAxMC4yOTYgMTAuMjk2IDUuNTk0IDAgMTAuMjk2LTQuNTAyIDEwLjI5Ni0xMC4yOTYgMC01LjU5NC00LjUwMi0xMC4yOTYtMTAuMjk2LTEwLjI5NnpNMTY5LjE5OCA2Ny4xMDJjLTUuNTk0IDAtMTAuMjk2IDQuNTAyLTEwLjI5NiAxMC4yOTYgMCA1LjU5NCA0LjUwMiAxMC4yOTYgMTAuMjk2IDEwLjI5NiA1LjU5NCAwIDEwLjI5Ni00LjUwMiAxMC4yOTYtMTAuMjk2IDAtNS41OTQtNC41MDItMTAuMjk2LTEwLjI5Ni0xMC4yOTZ6TTIxNC45NjEgNjcuMTAyYy01LjU5NCAwLTEwLjI5NiA0LjUwMi0xMC4yOTYgMTAuMjk2IDAgNS41OTQgNC41MDIgMTAuMjk2IDEwLjI5NiAxMC4yOTYgNS41OTQgMCAxMC4yOTYtNC41MDIgMTAuMjk2LTEwLjI5NiAwLTUuNTk0LTQuNTAyLTEwLjI5Ni0xMC4yOTYtMTAuMjk2ek0xOTEuNzk0IDYyLjA5NGMtNS41OTQgMC0xMC4yOTYgNC41MDItMTAuMjk2IDEwLjI5NiAwIDUuNTk0IDQuNTAyIDEwLjI5NiAxMC4yOTYgMTAuMjk2IDUuNTk0IDAgMTAuMjk2LTQuNTAyIDEwLjI5Ni0xMC4yOTYgMC01LjU5NC00LjUwMi0xMC4yOTYtMTAuMjk2LTEwLjI5NnpNMjA0LjgwNiA1Mi4xMDJjMC01LjU5NC00LjUwMi0xMC4yOTYtMTAuMjk2LTEwLjI5Ni01LjU5NCAwLTEwLjI5NiA0LjUwMi0xMC4yOTYgMTAuMjk2IDAgNS41OTQgNC41MDIgMTAuMjk2IDEwLjI5NiAxMC4yOTYgNS41OTQgMCAxMC4yOTYtNC41MDIgMTAuMjk2LTEwLjI5NnpNMTgyLjcxNyA1Mi4xMDJjMC01LjU5NC00LjUwMi0xMC4yOTYtMTAuMjk2LTEwLjI5Ni01LjU5NCAwLTEwLjI5NiA0LjUwMi0xMC4yOTYgMTAuMjk2IDAgNS41OTQgNC41MDIgMTAuMjk2IDEwLjI5NiAxMC4yOTYgNS41OTQgMCAxMC4yOTYtNC41MDIgMTAuMjk2LTEwLjI5NnpNMjA0LjgwNiA3Mi4xMDJjMC01LjU5NC00LjUwMi0xMC4yOTYtMTAuMjk2LTEwLjI5Ni01LjU5NCAwLTEwLjI5NiA0LjUwMi0xMC4yOTYgMTAuMjk2IDAgNS41OTQgNC41MDIgMTAuMjk2IDEwLjI5NiAxMC4yOTYgNS41OTQgMCAxMC4yOTYtNC41MDIgMTAuMjk2LTEwLjI5NnpNMTgyLjcxNyA3Mi4xMDJjMC01LjU5NC00LjUwMi0xMC4yOTYtMTAuMjk2LTEwLjI5Ni01LjU5NCAwLTEwLjI5NiA0LjUwMi0xMC4yOTYgMTAuMjk2IDAgNS41OTQgNC41MDIgMTAuMjk2IDEwLjI5NiAxMC4yOTYgNS41OTQgMCAxMC4yOTYtNC41MDIgMTAuMjk2LTEwLjI5NnoiIGZpbGw9IiNGRkU1MDAiIGZpbGwtcnVsZT0iZXZlbm9kZCIvPjwvZz48L3N2Zz4=) no-repeat 1rem/1.8rem, #b32121;
    padding: 1rem 1rem 1rem 3.7rem;
    color: white;
}}

    .blazor-error-boundary::after {{
        content: ""An error has occurred."";
    }}

.loading-progress {{
    position: relative;
    display: block;
    width: 8rem;
    height: 8rem;
    margin: 20vh auto 1rem auto;
}}

    .loading-progress circle {{
        fill: none;
        stroke: #e0e0e0;
        stroke-width: 0.6rem;
        transform-origin: 50% 50%;
        transform: rotate(-90deg);
    }}

        .loading-progress circle:last-child {{
            stroke: #1b6ec2;
            stroke-dasharray: calc(3.141 * var(--blazor-load-percentage, 0%) * 0.8), 500%;
            transition: stroke-dasharray 0.05s ease-in-out;
        }}

.loading-progress-text {{
    position: absolute;
    text-align: center;
    font-weight: bold;
    inset: calc(20vh + 3.25rem) 0 auto 0.2rem;
}}

    .loading-progress-text:after {{
        content: var(--blazor-load-percentage-text, ""Loading..."");
    }}

/* Add custom styles here */
";
                await File.WriteAllTextAsync(appCssPath, appCssContent);
            }

            // Placeholder favicon.png (expecting user to add the actual file)
            var faviconPath = Path.Combine(wwwrootFolder, "favicon.png");
            if (!File.Exists(faviconPath))
            {
                 _logger.LogDebug("Generando placeholder favicon.png");
                 // Create a tiny transparent png placeholder if needed
                 byte[] pngPlaceholder = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");
                 await File.WriteAllBytesAsync(faviconPath, pngPlaceholder);
            }
        }

        // Método para crear prompt de tarea usando Shared.Prompt
        private string CrearPromptParaTarea(Shared.Prompt promptOriginal, string tareaEspecifica, string tipoCodigo, string rutaProyecto, string? targetRelativePath)
        {
            string formatoCodigo = tipoCodigo == "Razor" ? "un componente Blazor (.razor)" : "una clase C# (.cs)";
            string nombreArchivo = targetRelativePath != null ? Path.GetFileName(targetRelativePath) : "(Nombre a inferir)";
            string instruccionesAdicionales = "";

            if (EsTareaCrud(tareaEspecifica))
            {
                instruccionesAdicionales = @$"
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
            - Asegúrate de tener todos los 'using' necesarios (ej. 'using Microsoft.EntityFrameworkCore;', 'using NombreProyecto.Models;').

            **Si es una Interfaz de Servicio C# (.cs) (ej. 'Services/IClienteService.cs'):**
            - Define una interfaz pública (ej. 'public interface IClienteService').
            - Declara métodos para operaciones CRUD (ej. 'Task<List<Cliente>> GetAllClientesAsync();', 'Task<Cliente> GetClienteByIdAsync(int id);', 'Task CreateClienteAsync(Cliente cliente);', 'Task UpdateClienteAsync(Cliente cliente);', 'Task DeleteClienteAsync(int id);').
            - Utiliza los modelos del proyecto en las firmas de los métodos.
            - Asegúrate de tener todos los 'using' necesarios (ej. 'using NombreProyecto.Models;').

            **Si es una Clase de Servicio C# (.cs) (ej. 'Services/ClienteService.cs'):**
            - Implementa la interfaz de servicio correspondiente (ej. 'public class ClienteService : IClienteService').
            - Inyecta el DbContext (ej. 'private readonly ApplicationDbContext _context;') a través del constructor.
            - Implementa todos los métodos de la interfaz, usando el DbContext para interactuar con la base de datos.
            - Incluye manejo básico de errores (try-catch) y logging si es posible.
            - Asegúrate de tener todos los 'using' necesarios (ej. 'using Microsoft.EntityFrameworkCore;', 'using NombreProyecto.Models;', 'using NombreProyecto.Data;').

            **Si es un Componente/Página Razor (.razor) para CRUD:**
            - **General:**
                - Usa '@page ""/ruta-correcta""' para páginas.
                - Incluye '@using NombreProyecto.Models', '@using NombreProyecto.Services'.
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

            string rutaInfo = targetRelativePath != null ? $"para el archivo '{targetRelativePath}'" : $"que se guardará como '{nombreArchivo}' (aproximadamente)";
            return @$"Contexto General del Proyecto (Prompt Original):
{promptOriginal.Descripcion}

Tarea Específica a Implementar:
{tareaEspecifica}

Instrucciones Generales para la Generación de Código:
1.  Genera el código fuente COMPLETO y FUNCIONAL para {formatoCodigo} {rutaInfo} que cumpla con la TAREA ESPECÍFICA.
2.  El código debe ser COMPILABLE y estar listo para usarse en un proyecto .NET 8 Blazor Server.
3.  Incluye TODOS los 'using' necesarios al principio del archivo.
4.  Añade comentarios XML (<summary>...</summary>) para todas las clases y métodos públicos (si es C#). Para código Razor, añade comentarios C# (// o /* */) para explicar lógica compleja en el bloque @code.
5.  Sigue las mejores prácticas de codificación de .NET 8 y Blazor.
6.  Implementa manejo básico de errores (try-catch) donde sea crítico (ej. llamadas a base de datos, servicios externos).
{instruccionesAdicionales}
RESTRICCIÓN ABSOLUTA: Devuelve ÚNICAMENTE el código fuente completo y correcto para el archivo solicitado. NO incluyas NINGUNA explicación, introducción, resumen, notas, advertencias, ni texto adicional antes o después del bloque de código. NO uses bloques de markdown (como ```csharp, ```html o ```razor) alrededor del código final. Solo el contenido puro del archivo.";
        }

        // --- Métodos Helper (sin cambios funcionales) ---
        #region Helper Methods

        /// <summary>
        /// Extracts a relative file path from the task description using refined Regex.
        /// Returns null if no valid path is confidently extracted.
        /// </summary>
        private string? ExtractPathFromTask(string task)
        {
            Match match = PathExtractionRegex.Match(task);
            if (match.Success && match.Groups["path"].Success)
            {
                string extractedPath = match.Groups["path"].Value.Trim();
                // Basic sanity check on extracted path
                if (!string.IsNullOrWhiteSpace(extractedPath) &&
                    (extractedPath.EndsWith(".cs") || extractedPath.EndsWith(".razor") || extractedPath.EndsWith(".csproj")) &&
                     extractedPath.Length > 3 && !extractedPath.Contains(" ")) // Avoid paths with spaces for now
                {
                    _logger.LogTrace("PathExtractionRegex encontró: '{ExtractedPath}'", extractedPath);
                    return extractedPath;
                }
            }
            _logger.LogDebug("PathExtractionRegex no encontró ruta válida en: '{Task}'. Intentando FileNameExtractionRegex.", task);
            // Fallback to FileNameExtractionRegex if the primary fails
            match = FileNameExtractionRegex.Match(task);
            if (match.Success && match.Groups["filename"].Success)
            {
                string filename = match.Groups["filename"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(filename) && filename.Length > 3)
                {
                    _logger.LogDebug("FileNameExtractionRegex encontró: '{Filename}'. Se usará para inferencia.", filename);
                    // Return null to indicate folder inference is needed
                    return null;
                }
            }

            _logger.LogDebug("Ningún Regex pudo extraer una ruta/nombre de archivo válido de la tarea: {Task}", task);
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
                  // Check if the file is directly in the root (allow specific base files)
                  if ((Path.GetDirectoryName(normalizedFullPath) ?? "").Equals(Path.GetFullPath(projectRootPath), StringComparison.OrdinalIgnoreCase))
                  {
                       string fileNameLower = Path.GetFileName(normalizedFullPath).ToLowerInvariant();
                       if(fileNameLower == "program.cs" || fileNameLower == "app.razor" || fileNameLower == "_imports.razor" || fileNameLower.EndsWith(".csproj") || fileNameLower == "routes.razor") // Added Routes.razor
                       {
                            return true;
                       }
                       _logger.LogWarning("Archivo '{FileName}' detectado en la raíz del proyecto, pero no es un archivo base esperado.", Path.GetFileName(normalizedFullPath));
                       // return false; // Decide if non-base files in root are allowed
                  }
                  // Check if the path starts with the project root directory
                  return normalizedFullPath.StartsWith(normalizedProjectRootPath, StringComparison.OrdinalIgnoreCase);
             }
             catch (Exception ex)
             {
                  _logger.LogWarning(ex, "Error al validar ruta '{FullPath}' dentro de '{ProjectRootPath}'", fullPath, projectRootPath);
                  return false; // Safer default
             }
        }

        private string LimpiarCodigoGemini(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return "";

            var lines = codigo.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();

            // 1. Remove markdown code fences
            // Ensure there are lines to check before accessing Last()
            if (lines.Any() && lines[0].Trim().StartsWith("```")) { lines.RemoveAt(0); }
            if (lines.Any() && lines.Last().Trim() == "```") { lines.RemoveAt(lines.Count - 1); }


            // 2. Remove common introductory/concluding phrases (conservative approach)
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

            // Check and remove from the beginning
            for (int i = 0; i < 3 && lines.Any(); i++) // Check first 3 lines
            {
                var trimmedLowerLine = lines[0].Trim().ToLowerInvariant();
                bool removed = false;
                foreach (var phrase in commonPhrases)
                {
                    if (trimmedLowerLine.StartsWith(phrase) || trimmedLowerLine.EndsWith(phrase)) // Also check if the line *is* the phrase
                    {
                        _logger.LogTrace("LimpiarCodigoGemini: Removiendo línea introductoria: '{Line}'", lines[0]);
                        lines.RemoveAt(0);
                        removed = true;
                        break;
                    }
                }
                if (!removed) break; // Stop if a line is not a common phrase
            }

            // Check and remove from the end
            for (int i = 0; i < 3 && lines.Any(); i++) // Check last 3 lines
            {
                var trimmedLowerLine = lines.Last().Trim().ToLowerInvariant();
                 bool removed = false;
                foreach (var phrase in commonPhrases)
                {
                    // For concluding remarks, 'contains' might be too broad, stick to exact or starts/ends with
                    if (trimmedLowerLine.StartsWith(phrase) || trimmedLowerLine.EndsWith(phrase) || trimmedLowerLine == phrase)
                    {
                        _logger.LogTrace("LimpiarCodigoGemini: Removiendo línea conclusiva: '{Line}'", lines.Last());
                        lines.RemoveAt(lines.Count - 1);
                        removed = true;
                        break;
                    }
                }
                 if (!removed) break; // Stop if a line is not a common phrase
            }

            var processedLines = lines.Select(l => l.TrimEnd()).ToList();
            // Remove empty lines from start
            while (processedLines.Any() && string.IsNullOrWhiteSpace(processedLines[0]))
            {
                processedLines.RemoveAt(0);
            }
            // Remove empty lines from end
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
                !l.TrimStart().StartsWith("//") && // C#, C++ style comments
                !l.TrimStart().StartsWith("/*") && // Start of C-style multi-line comment
                !(l.TrimStart().StartsWith("*") && !l.TrimStart().StartsWith("*/") && lines.Any(prevL => prevL.TrimStart().StartsWith("/*") && !prevL.TrimEnd().EndsWith("*/"))) && // Middle of C-style multi-line comment
                !l.TrimStart().StartsWith("@*") && // Razor comment start
                !(l.TrimStart().StartsWith("*") && !l.TrimStart().StartsWith("*@") && lines.Any(prevL => prevL.TrimStart().StartsWith("@*") && !prevL.TrimEnd().EndsWith("*@"))) // Middle of Razor comment
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
             // Prioritize Razor if UI-related keywords are present
             if (tLower.Contains(".razor") || tLower.Contains("página") || tLower.Contains("componente") || tLower.Contains(" vista ") || tLower.Contains(" ui ") || (tLower.Contains("interfaz") && !tLower.Contains("servicio")))
             {
                  return "Razor";
             }
             // Default to C#
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
             bool lastWasInvalid = true; // Treat start as invalid char context
             foreach (var c in input.Trim())
             {
                  if (char.IsLetterOrDigit(c))
                  {
                       sb.Append(c);
                       lastWasInvalid = false;
                  }
                  else if (c == '_' || c == '-') // Allow underscore and hyphen
                  {
                      if (!lastWasInvalid) // Avoid consecutive invalid chars
                      {
                         sb.Append(c); // Use hyphen as separator generally
                         lastWasInvalid = true;
                      }
                  }
                  else if (char.IsWhiteSpace(c))
                  {
                       if (!lastWasInvalid)
                       {
                            sb.Append('-'); // Replace whitespace with hyphen
                            lastWasInvalid = true;
                       }
                  }
                  // Ignore other invalid characters
             }
             // Remove trailing invalid char if any
             if (sb.Length > 0 && (sb[^1] == '-' || sb[^1] == '_'))
             {
                  sb.Length--;
             }

             var result = sb.ToString();
             // Optional: Convert to lowercase
             // result = result.ToLowerInvariant();

             const int maxLength = 50; // Max filename length constraint
             if (result.Length > maxLength)
             {
                  result = result.Substring(0, maxLength).TrimEnd('-', '_');
             }
             return string.IsNullOrWhiteSpace(result) ? "proyecto-generado" : result;
        }


        private string ExtractCSharpFilename(string code, string tareaFallback)
        {
            // Try to find class/interface/record/enum/struct definition
            var match = Regex.Match(code, @"\b(?:public\s+|internal\s+)?(?:sealed\s+|abstract\s+)?(?:partial\s+)?(class|interface|record|enum|struct)\s+([A-Za-z_][\w]*)");
            if (match.Success)
            {
                return match.Groups[2].Value + ".cs";
            }

            _logger.LogWarning("No se pudo extraer nombre C# del código. Fallback de tarea: {Tarea}", tareaFallback);

            // Try extracting from task description using FileNameExtractionRegex first
            Match nameMatch = FileNameExtractionRegex.Match(tareaFallback);
            if (nameMatch.Success && nameMatch.Groups["filename"].Success && nameMatch.Groups["filename"].Value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                 // Sanitize the extracted filename
                 return SanitizeFileName(Path.GetFileNameWithoutExtension(nameMatch.Groups["filename"].Value)) + ".cs";
            }

            // Try extracting common patterns like "crear clase X"
            string nombreDeTarea = Regex.Match(tareaFallback, @"\b(?:crear|implementar|generar)\s+(?:la\s+)?(?:clase|interfaz|servicio|modelo|contexto|enum|componente|record|struct)?\s*'?([A-Za-z_]\w+)'?").Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(nombreDeTarea))
            {
                return SanitizeFileName(nombreDeTarea) + ".cs";
            }

            _logger.LogWarning("Fallback GUID nombre archivo C#.");
            return "Clase_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".cs"; // Use N format for guid
        }

        private string ExtractRazorFilename(string code, string tareaFallback)
        {
            // Try extracting from @page directive
            var pageMatch = Regex.Match(code, @"@page\s+""\/?([\w\/-]+)(?:/{.*?})?/??""");
            if (pageMatch.Success)
            {
                var parts = pageMatch.Groups[1].Value.Split('/');
                var lastPart = parts.LastOrDefault(p => !string.IsNullOrWhiteSpace(p) && !p.Contains('{')); // Find last non-parameter part
                if (!string.IsNullOrWhiteSpace(lastPart))
                {
                    return UppercaseFirst(SanitizeFileName(lastPart)) + ".razor";
                }
            }

            // Try extracting from @code block class definition
            var codeClassMatch = Regex.Match(code, @"@code\s*\{?\s*(?:public\s+)?(?:partial\s+)?class\s+([A-Z][A-Za-z_]\w*)\b", RegexOptions.Singleline);
            if (codeClassMatch.Success)
            {
                return codeClassMatch.Groups[1].Value + ".razor";
            }

            // Try extracting from component tag usage (less reliable)
            // var compMatch = Regex.Match(code, @"<([A-Z][A-Za-z_]\w*)\b");
            // if (compMatch.Success && !pageMatch.Success) // Avoid matching HTML tags if @page exists
            // {
            //     return compMatch.Groups[1].Value + ".razor";
            // }

            _logger.LogWarning("No se pudo extraer nombre Razor del código. Fallback de tarea: {Tarea}", tareaFallback);

            // Try extracting from task description using FileNameExtractionRegex first
            Match nameMatch = FileNameExtractionRegex.Match(tareaFallback);
             if (nameMatch.Success && nameMatch.Groups["filename"].Success && nameMatch.Groups["filename"].Value.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            {
                 // Sanitize and ensure PascalCase
                 return UppercaseFirst(SanitizeFileName(Path.GetFileNameWithoutExtension(nameMatch.Groups["filename"].Value))) + ".razor";
            }

            // Try extracting common patterns like "crear pagina X"
            string nombreDeTarea = Regex.Match(tareaFallback, @"\b(?:crear|implementar|generar)\s+(?:la\s+)?(?:página|componente|vista)?\s*'?([A-Za-z_]\w+)'?").Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(nombreDeTarea))
            {
                return UppercaseFirst(SanitizeFileName(nombreDeTarea)) + ".razor";
            }

            _logger.LogWarning("Fallback GUID nombre archivo Razor.");
            return "Componente_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".razor"; // Use N format for guid
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
            var c = codigo.ToLowerInvariant(); // Lowercase code content for matching

            // Check specific keywords and code patterns
            if (t.Contains("controlador") || t.Contains("controller") || t.Contains(" api ") || c.Contains("[apicontroller]") || c.Contains("controllerbase")) return "Controllers";
            if (t.Contains("contexto") || t.Contains("dbcontext") || t.Contains("base de datos") || t.Contains("database") || t.Contains("repositorio") || t.Contains("repository") || t.Contains("unit of work") || c.Contains(" entityframeworkcore") || c.Contains(": dbcontext")) return "Data";
            if (t.Contains("servicio") || t.Contains("service") || t.Contains("email") || t.Contains("exportar") || t.Contains("login") || t.Contains("fachada") || t.Contains("manager") || t.Contains("helper") || t.Contains("client") || t.Contains("logic") || t.Contains("business")) return "Services"; // Added common service/logic terms
            if (c.Contains("@page ") || (t.Contains("página") && t.Contains(".razor"))) return "Pages"; // Routable components typically go in Pages
            if (t.Contains("layout") || (t.Contains(".razor") && (t.Contains("layout/") || t.Contains("layout\\")))) return "Layout"; // .NET 8 layout convention
            if (t.Contains("navmenu") || (t.Contains(".razor") && (t.Contains("shared/") || t.Contains("shared\\")))) return "Shared"; // Keep Shared for NavMenu or explicitly mentioned Shared components
            if (t.Contains(".razor") || t.Contains("componente") || (t.Contains(" ui ") && !t.Contains("servicio")) || (t.Contains("interfaz") && !c.Contains(" public interface ") && !t.Contains("servicio"))) return "Components"; // Non-routable/shared UI components
            if (t.Contains("interfaz") || t.Contains("interface") || c.Contains(" public interface ")) return "Interfaces"; // Or potentially Services/Interfaces or Models/Interfaces
            if (t.Contains("modelo") || t.Contains("model") || t.Contains("entidad") || t.Contains("entity") || t.Contains("dto") || t.Contains("viewmodel") || c.Contains(" public class ") || c.Contains(" public record ") || c.Contains(" public struct ")) return "Models";
            if (t.Contains("configuración") || t.Contains("appsettings") || t.Contains("startup") || t.Contains("program.cs")) return ""; // Root files

            _logger.LogDebug("No se pudo inferir subcarpeta para tarea '{Tarea}'. Usando raíz.", tarea);
            return ""; // Default to root if no specific folder inferred
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
             // Replace invalid chars with underscore, ensure starts with letter or underscore
             var sanitized = Regex.Replace(projectName, @"[^\w\.]", "_"); // Allow letters, numbers, underscore, period
             if (string.IsNullOrWhiteSpace(sanitized) || (!char.IsLetter(sanitized[0]) && sanitized[0] != '_'))
             {
                  sanitized = "_" + sanitized;
             }
             // Avoid consecutive periods or periods at start/end
             sanitized = Regex.Replace(sanitized, @"\.{2,}", ".");
             sanitized = sanitized.Trim('.');
             // Ensure PascalCase for parts between periods
             sanitized = string.Join(".", sanitized.Split('.').Select(p => ToPascalCase(p)));

             return string.IsNullOrWhiteSpace(sanitized) ? "_GeneratedProject" : sanitized;
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return "_"; // Return underscore for empty/null

            // Split by common separators and filter out empty entries
            var parts = input.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // If splitting results in nothing (e.g., input was just separators),
            // try to use the original input after filtering non-alphanumeric chars.
            if (!parts.Any())
            {
                var fallback = new string(input.Where(char.IsLetterOrDigit).ToArray());
                if (string.IsNullOrEmpty(fallback)) return "_"; // Final fallback
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
                        sb.Append(part.Substring(1).ToLowerInvariant()); // Optionally lowercase the rest
                    }
                }
            }

            // Handle case where the result might still be empty or invalid
            var result = sb.ToString();
            if (string.IsNullOrEmpty(result)) return "_";
             // Ensure starts with letter or underscore if it somehow starts with a digit after processing
             if (char.IsDigit(result[0])) result = "_" + result;

            return result;
        }
        #endregion
    }
}
