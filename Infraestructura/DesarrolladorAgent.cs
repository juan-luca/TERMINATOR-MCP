// --- START OF FILE DesarrolladorAgent.cs --- ADDED LOGGING

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Shared;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Infraestructura
{
    public class DesarrolladorAgent : IDesarrolladorAgent
    {
        private readonly GeminiClient _gemini;
        private readonly ILogger<DesarrolladorAgent> _logger;

        public DesarrolladorAgent(
            GeminiClient gemini,
            ILogger<DesarrolladorAgent> logger)
        {
            _gemini = gemini;
            _logger = logger;
        }

        public async Task GenerarCodigoParaTarea(Prompt prompt, string tarea)
        {
            var nombreProyecto = SanitizeFileName(prompt.Titulo);
            var rutaProyecto = Path.Combine("output", nombreProyecto);

            _logger.LogInformation("➡️ Procesando Tarea: '{Tarea}' para Proyecto: '{Proyecto}' en '{Ruta}'",
                tarea, nombreProyecto, rutaProyecto);

            string codigoGenerado = ""; // Initialize empty
            string rawCodigoGenerado = ""; // To store raw response

            try
            {
                Directory.CreateDirectory(rutaProyecto);
                await GenerarCsprojAsync(nombreProyecto, rutaProyecto);
                await GenerarArchivosBaseAsync(nombreProyecto, rutaProyecto, prompt.Titulo);

                string tipoCodigo = InferirTipoCodigo(tarea);
                string promptParaGemini = CrearPromptParaTarea(prompt, tarea, tipoCodigo, rutaProyecto);

                _logger.LogDebug("🔄 Llamando a Gemini para generar código para la tarea...");
                try
                {
                    rawCodigoGenerado = await _gemini.GenerarAsync(promptParaGemini);
                    // *** DETAILED LOG 1: Raw response ***
                    _logger.LogTrace("Respuesta RAW de Gemini (Tarea: {Tarea}, Longitud: {Length}):\n{RawCode}",
                        tarea, rawCodigoGenerado?.Length ?? 0, rawCodigoGenerado); // Log raw response on Trace level

                    codigoGenerado = LimpiarCodigoGemini(rawCodigoGenerado);

                    // *** DETAILED LOG 2: Cleaned response ***
                    _logger.LogDebug("Código LIMPIO de Gemini (Tarea: {Tarea}, Longitud: {Length}):\n{CleanedCode}",
                        tarea, codigoGenerado?.Length ?? 0,
                        (codigoGenerado?.Length ?? 0) > 500 ? codigoGenerado?.Substring(0, 500) + "..." : codigoGenerado); // Log cleaned (truncated if long)
                }
                catch (Exception ex)
                {
                    // Log exception from GeminiClient or cleaning
                    _logger.LogError(ex, "❌ Error durante la generación/limpieza de Gemini para la tarea '{Tarea}'.", tarea);
                    return; // Stop processing this task
                }

                // --- Check for empty/whitespace AFTER cleaning ---
                if (string.IsNullOrWhiteSpace(codigoGenerado))
                {
                    // *** ENHANCED LOG ***
                    _logger.LogWarning("⚠️ El código generado y limpio para la tarea '{Tarea}' está VACÍO o es solo espacio en blanco. Se omite la creación del archivo.", tarea);
                    // Log the raw response again if the cleaned one is empty, might give clues
                    if (string.IsNullOrWhiteSpace(codigoGenerado) && !string.IsNullOrWhiteSpace(rawCodigoGenerado))
                    {
                        _logger.LogWarning("   (La respuesta raw de Gemini NO estaba vacía, tenía longitud {Length}. Revisar lógica de LimpiarCodigoGemini o respuesta raw en Trace logs)", rawCodigoGenerado.Length);
                    }
                    return; // IMPORTANT: Skip file writing
                }
                // --- End of check ---

                _logger.LogDebug("Código limpio no está vacío. Procediendo a determinar nombre y ruta...");

                string nombreArchivo;
                if (tipoCodigo == "Razor")
                    nombreArchivo = ExtractRazorFilename(codigoGenerado, tarea);
                else // C#
                    nombreArchivo = ExtractCSharpFilename(codigoGenerado, tarea);

                string subcarpeta = InferirSubcarpeta(tarea, codigoGenerado);
                string rutaCompletaArchivo = Path.Combine(rutaProyecto, subcarpeta, nombreArchivo);

                string? directoryPath = Path.GetDirectoryName(rutaCompletaArchivo);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                // *** DETAILED LOG 3: Before writing file ***
                _logger.LogInformation("💾 Escribiendo archivo (Longitud: {Length}): {FilePath}", codigoGenerado.Length, rutaCompletaArchivo);
                await File.WriteAllTextAsync(rutaCompletaArchivo, codigoGenerado);
                _logger.LogInformation("✅ Archivo generado/actualizado para la tarea '{Tarea}': '{RutaArchivo}'", tarea, rutaCompletaArchivo);

            }
            catch (Exception ex)
            {
                // Log critical errors during file path determination or writing
                _logger.LogError(ex, "❌ Error crítico procesando la tarea '{Tarea}' para el proyecto '{Proyecto}'. Código generado (si existe, limpio, longitud {Length}):\n{Codigo}",
                    tarea, nombreProyecto, codigoGenerado?.Length ?? 0,
                    (codigoGenerado?.Length ?? 0) > 500 ? codigoGenerado?.Substring(0, 500) + "..." : codigoGenerado);
            }
        }

        // ... (Resto de los métodos: LimpiarCodigoGemini, InferirTipoCodigo, CrearPromptParaTarea, EsTareaCrud, SanitizeFileName, Extract..., UppercaseFirst, InferirSubcarpeta, GenerarCsprojAsync, GenerarArchivosBaseAsync, SanitizeNamespace, ToPascalCase)
        // --- PASTE THE REST OF THE METHODS FROM THE PREVIOUS CORRECTED VERSION HERE ---
        // --- (Ensure the method signatures and bodies are correct) ---

        #region Helper Methods (Paste from previous corrected version)

        private string LimpiarCodigoGemini(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return "";
            var lines = codigo.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                            .Select(l => l.TrimEnd())
                            .ToList();

            if (lines.Count > 0 && lines[0].Trim().StartsWith("```")) { lines.RemoveAt(0); }
            if (lines.Count > 0 && lines[^1].Trim() == "```") { lines.RemoveAt(lines.Count - 1); }
            return string.Join(Environment.NewLine, lines).Trim();
        }

        private string InferirTipoCodigo(string tarea)
        {
            var tLower = tarea.ToLowerInvariant();
            if (tLower.Contains(".razor") || tLower.Contains("página") || tLower.Contains("componente") || tLower.Contains(" ui ") || tLower.Contains("interfaz")) { return "Razor"; }
            return "C#";
        }

        private string CrearPromptParaTarea(Prompt promptOriginal, string tareaEspecifica, string tipoCodigo, string rutaProyecto)
        {
            string formatoCodigo = tipoCodigo == "Razor" ? "un componente Blazor (.razor)" : "una clase C# (.cs)";
            string instruccionesAdicionales = "";
            if (EsTareaCrud(tareaEspecifica))
            {
                instruccionesAdicionales = @$"
                Asegúrate de implementar las operaciones CRUD estándar (Crear, Leer/Listar, Leer por ID, Actualizar, Eliminar) para la entidad mencionada.
                Utiliza buenas prácticas como DTOs si es aplicable, e inyecta dependencias necesarias (como DbContext o servicios).
                Considera validaciones básicas en los modelos o DTOs.
                Si es un componente Razor, incluye la lógica @code necesaria para interactuar con los servicios o el backend.
                Si es una clase C#, incluye los métodos correspondientes a cada operación CRUD.";
            }
            return @$"Contexto General del Proyecto (Prompt Original):
{promptOriginal.Descripcion}

Tarea Específica a Implementar:
{tareaEspecifica}

Instrucciones:
1. Genera el código completo para {formatoCodigo} que cumpla con la TAREA ESPECÍFICA.
2. El código debe ser funcional, completo y seguir las convenciones de {tipoCodigo} y .NET 8 / Blazor.
3. Incluye los `using` necesarios.
4. Incluye comentarios XML básicos (`<summary>`) para clases y métodos públicos.
5. Implementa manejo básico de errores donde sea crítico (ej. try-catch en llamadas a BD o APIs externas).
6. {instruccionesAdicionales}
7. Devuelve ÚNICAMENTE el código fuente completo del archivo solicitado ({formatoCodigo}). No incluyas explicaciones, introducciones ni texto adicional antes o después del código. No uses bloques de markdown como ```csharp o ```razor.";
        }

        private bool EsTareaCrud(string tarea)
        {
            var tLower = tarea.ToLowerInvariant();
            return tLower.Contains("crud") || tLower.Contains("abm") || (tLower.Contains("crear") && tLower.Contains("leer") && tLower.Contains("actualizar") && tLower.Contains("eliminar")) || tLower.Contains("gestionar") || tLower.Contains("administrar");
        }

        public string SanitizeFileName(string input)
        {
            var sb = new StringBuilder();
            bool lastWasHyphen = true;
            foreach (var c in input.Trim()) { if (char.IsLetterOrDigit(c) || c == '_') { sb.Append(c); lastWasHyphen = false; } else if (c == '-' || char.IsWhiteSpace(c)) { if (!lastWasHyphen) { sb.Append('-'); lastWasHyphen = true; } } }
            if (sb.Length > 0 && sb[^1] == '-') { sb.Length--; }
            var result = sb.ToString().ToLowerInvariant();
            const int maxLength = 50;
            if (result.Length > maxLength) { result = result.Substring(0, maxLength).TrimEnd('-'); }
            return string.IsNullOrWhiteSpace(result) ? "proyecto-generado" : result;
        }

        private string ExtractCSharpFilename(string code, string tareaFallback)
        {
            var match = Regex.Match(code, @"\b(?:class|interface|record|enum|struct)\s+([A-Za-z_][\w]*)"); if (match.Success) { return match.Groups[1].Value + ".cs"; }
            _logger.LogWarning("No se pudo extraer nombre de clase C# del código. Usando fallback de la tarea: {Tarea}", tareaFallback); string nombreDeTarea = Regex.Match(tareaFallback, @"\b(?:crear|implementar|generar)\s+(?:la\s+)?(?:clase|interfaz|servicio|modelo|contexto|enum|componente)?\s*'?([A-Za-z_]\w+)'?").Groups[1].Value; if (!string.IsNullOrWhiteSpace(nombreDeTarea)) { return SanitizeFileName(nombreDeTarea).Replace("-", "") + ".cs"; }
            _logger.LogWarning("Fallback de tarea falló. Usando GUID como nombre de archivo."); return "Clase_" + Guid.NewGuid().ToString().Substring(0, 8) + ".cs";
        }

        private string ExtractRazorFilename(string code, string tareaFallback)
        {
            var pageMatch = Regex.Match(code, @"@page\s+""\/?([\w\/-]+)(?:\{.*)?\/??"""); if (pageMatch.Success) { var parts = pageMatch.Groups[1].Value.Split('/'); var lastPart = parts.LastOrDefault(p => !string.IsNullOrWhiteSpace(p) && !p.Contains('{')); if (!string.IsNullOrWhiteSpace(lastPart)) { return UppercaseFirst(SanitizeFileName(lastPart).Replace("-", "")) + ".razor"; } }
            var codeClassMatch = Regex.Match(code, @"@code\s*\{.*?\b(?:partial\s+)?class\s+([A-Z][A-Za-z_]\w*)\b", RegexOptions.Singleline); if (codeClassMatch.Success) { return codeClassMatch.Groups[1].Value + ".razor"; }
            var compMatch = Regex.Match(code, @"<([A-Z][A-Za-z_]\w*)\b"); if (compMatch.Success && !pageMatch.Success) { return compMatch.Groups[1].Value + ".razor"; }
            _logger.LogWarning("No se pudo extraer nombre de componente Razor del código. Usando fallback de la tarea: {Tarea}", tareaFallback); string nombreDeTarea = Regex.Match(tareaFallback, @"\b(?:crear|implementar|generar)\s+(?:la\s+)?(?:página|componente|vista)?\s*'?([A-Za-z_]\w+)'?").Groups[1].Value; if (!string.IsNullOrWhiteSpace(nombreDeTarea)) { return UppercaseFirst(SanitizeFileName(nombreDeTarea).Replace("-", "")) + ".razor"; }
            _logger.LogWarning("Fallback de tarea falló. Usando GUID como nombre de archivo."); return "Componente_" + Guid.NewGuid().ToString().Substring(0, 8) + ".razor";
        }

        private string UppercaseFirst(string s) { if (string.IsNullOrEmpty(s)) return s; if (s.Length == 1) return s.ToUpperInvariant(); return char.ToUpperInvariant(s[0]) + s.Substring(1); }

        private string InferirSubcarpeta(string tarea, string codigo)
        {
            var t = tarea.ToLowerInvariant(); var c = codigo.ToLowerInvariant();
            if (t.Contains("controlador") || t.Contains("controller") || t.Contains(" api ") || c.Contains("[apicontroller]") || c.Contains("controllerbase")) return "Controllers";
            if (t.Contains("contexto") || t.Contains("dbcontext") || t.Contains("base de datos") || t.Contains("database") || t.Contains("repositorio") || t.Contains("repository") || t.Contains("unit of work") || c.Contains("dbcontext")) return "Data";
            if (t.Contains("servicio") || t.Contains("service") || t.Contains("email") || t.Contains("exportar") || t.Contains("login") || t.Contains("fachada") || t.Contains("manager") || t.Contains("helper") || t.Contains("client")) return "Services";
            if (c.Contains("@page ") || (t.Contains("página") && t.Contains(".razor"))) return "Pages";
            if (t.Contains(".razor") || t.Contains("componente") || (t.Contains(" ui ") && !t.Contains("servicio")) || (t.Contains("interfaz") && !c.Contains(" public interface "))) return "Components";
            if (t.Contains("interfaz") || t.Contains("interface") || c.Contains(" public interface ")) return "Interfaces";
            if (t.Contains("modelo") || t.Contains("model") || t.Contains("entidad") || t.Contains("entity") || t.Contains("dto") || t.Contains("viewmodel") || c.Contains(" public class ") || c.Contains(" public record ") || c.Contains(" public struct ")) return "Models";
            if (t.Contains("configuración") || t.Contains("appsettings") || t.Contains("startup") || t.Contains("program.cs")) return "";
            _logger.LogDebug("No se pudo inferir subcarpeta específica para tarea '{Tarea}'. Usando raíz del proyecto.", tarea); return "";
        }

        private async Task GenerarCsprojAsync(string nombreProyecto, string rutaProyecto)
        {
            var csprojPath = Path.Combine(rutaProyecto, $"{nombreProyecto}.csproj"); if (File.Exists(csprojPath)) return;
            _logger.LogDebug("Generando archivo .csproj: {Path}", csprojPath);
            var csprojContent = @$"<Project Sdk=""Microsoft.NET.Sdk.Web"">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><RootNamespace>{SanitizeNamespace(nombreProyecto)}</RootNamespace></PropertyGroup>
<ItemGroup><PackageReference Include=""Microsoft.AspNetCore.Components.Web"" Version=""8.0.0"" /><PackageReference Include=""Microsoft.EntityFrameworkCore.InMemory"" Version=""8.0.0"" /></ItemGroup>
<ItemGroup><Content Include=""wwwroot\**"" CopyToPublishDirectory=""PreserveNewest"" /></ItemGroup>
</Project>";
            await File.WriteAllTextAsync(csprojPath, csprojContent);
        }

        private async Task GenerarArchivosBaseAsync(string nombreProyecto, string rutaProyecto, string promptTitulo)
        {
            string projectNamespace = SanitizeNamespace(nombreProyecto);
            var programPath = Path.Combine(rutaProyecto, "Program.cs"); if (!File.Exists(programPath)) { _logger.LogDebug("Generando archivo Program.cs"); var programContent = @$"using Microsoft.AspNetCore.Builder;using Microsoft.AspNetCore.Components;using Microsoft.AspNetCore.Components.Web;using Microsoft.Extensions.DependencyInjection;using Microsoft.Extensions.Hosting;var builder = WebApplication.CreateBuilder(args);builder.Services.AddRazorPages();builder.Services.AddServerSideBlazor();var app = builder.Build();if (!app.Environment.IsDevelopment()){{app.UseExceptionHandler(""/Error"");}}app.UseStaticFiles();app.UseRouting();app.MapBlazorHub();app.MapFallbackToPage(""/_Host"");app.Run();"; await File.WriteAllTextAsync(programPath, programContent); }
            var appRazorPath = Path.Combine(rutaProyecto, "App.razor"); if (!File.Exists(appRazorPath)) { _logger.LogDebug("Generando archivo App.razor"); var appContent = @$"@using {projectNamespace}.Shared <Router AppAssembly=""@typeof(Program).Assembly""><Found Context=""routeData""><RouteView RouteData=""@routeData"" DefaultLayout=""@typeof(MainLayout)"" /><FocusOnNavigate RouteData=""@routeData"" Selector=""h1"" /></Found><NotFound><LayoutView Layout=""@typeof(MainLayout)""><p role=""alert"">Sorry, there's nothing at this address.</p></LayoutView></NotFound></Router>"; await File.WriteAllTextAsync(appRazorPath, appContent); }
            var importsPath = Path.Combine(rutaProyecto, "_Imports.razor"); if (!File.Exists(importsPath)) { _logger.LogDebug("Generando archivo _Imports.razor"); var importsContent = @$"@using System.Net.Http @using Microsoft.AspNetCore.Authorization @using Microsoft.AspNetCore.Components.Authorization @using Microsoft.AspNetCore.Components.Forms @using Microsoft.AspNetCore.Components.Routing @using Microsoft.AspNetCore.Components.Web @using Microsoft.AspNetCore.Components.Web.Virtualization @using Microsoft.JSInterop @using {projectNamespace} @using {projectNamespace}.Shared @using {projectNamespace}.Components"; await File.WriteAllTextAsync(importsPath, importsContent); }
            var sharedFolder = Path.Combine(rutaProyecto, "Shared"); Directory.CreateDirectory(sharedFolder);
            var layoutPath = Path.Combine(sharedFolder, "MainLayout.razor"); if (!File.Exists(layoutPath)) { _logger.LogDebug("Generando archivo Shared/MainLayout.razor"); var layoutContent = @$"@inherits LayoutComponentBase <div class=""page""><div class=""sidebar""><NavMenu /></div><main><div class=""top-row px-4""><a href=""https://learn.microsoft.com/aspnet/core/"" target=""_blank"">About</a></div><article class=""content px-4"">@Body</article></main></div>"; await File.WriteAllTextAsync(layoutPath, layoutContent); }
            var navMenuPath = Path.Combine(sharedFolder, "NavMenu.razor"); if (!File.Exists(navMenuPath)) { _logger.LogDebug("Generando archivo Shared/NavMenu.razor"); var navMenuContent = @"<div class=""top-row ps-3 navbar navbar-dark""><div class=""container-fluid""><a class=""navbar-brand"" href="""">" + projectNamespace + @"</a><button title=""Navigation menu"" class=""navbar-toggler"" @onclick=""ToggleNavMenu""><span class=""navbar-toggler-icon""></span></button></div></div><div class=""@NavMenuCssClass"" @onclick=""ToggleNavMenu""><nav class=""flex-column""><div class=""nav-item px-3""><NavLink class=""nav-link"" href="""" Match=""NavLinkMatch.All""><span class=""oi oi-home"" aria-hidden=""true""></span> Home</NavLink></div></nav></div>@code { private bool collapseNavMenu = true; private string? NavMenuCssClass => collapseNavMenu ? ""collapse"" : null; private void ToggleNavMenu() { collapseNavMenu = !collapseNavMenu; } }"; await File.WriteAllTextAsync(navMenuPath, navMenuContent); var navMenuCssPath = Path.ChangeExtension(navMenuPath, ".razor.css"); if (!File.Exists(navMenuCssPath)) { var cssContent = @".navbar-toggler { background-color: rgba(255, 255, 255, 0.1); } .top-row { height: 3.5rem; background-color: rgba(0,0,0,0.4); }"; await File.WriteAllTextAsync(navMenuCssPath, cssContent); } }
            var pagesFolder = Path.Combine(rutaProyecto, "Pages"); Directory.CreateDirectory(pagesFolder);
            var hostPath = Path.Combine(pagesFolder, "_Host.cshtml"); if (!File.Exists(hostPath)) { _logger.LogDebug("Generando archivo Pages/_Host.cshtml"); var hostContent = @$"@page ""/"" @namespace {projectNamespace}.Pages @using {projectNamespace} @using {projectNamespace}.Shared @addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers <!DOCTYPE html><html lang=""en""><head><meta charset=""utf-8"" /><meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" /><title>{promptTitulo}</title><base href=""~/"" /><link rel=""stylesheet"" href=""css/bootstrap/bootstrap.min.css"" /><link href=""css/site.css"" rel=""stylesheet"" /><link href=""{nombreProyecto}.styles.css"" rel=""stylesheet"" /><component type=""typeof(HeadOutlet)"" render-mode=""ServerPrerendered"" /></head><body><component type=""typeof(App)"" render-mode=""ServerPrerendered"" /><div id=""blazor-error-ui""><environment include=""Staging,Production"">An error has occurred.</environment><environment include=""Development"">An unhandled exception has occurred.</environment><a href="""" class=""reload"">Reload</a><a class=""dismiss"">🗙</a></div><script src=""_framework/blazor.server.js""></script></body></html>"; await File.WriteAllTextAsync(hostPath, hostContent); }
            var errorPath = Path.Combine(pagesFolder, "Error.cshtml"); var errorModelPath = Path.Combine(pagesFolder, "Error.cshtml.cs"); if (!File.Exists(errorPath)) { _logger.LogDebug("Generando archivos Pages/Error.cshtml y .cs"); var errorPageContent = @"@page @model ErrorModel @{ ViewData[""Title""] = ""Error""; } <h1 class=""text-danger"">Error.</h1> <h2 class=""text-danger"">An error occurred while processing your request.</h2> @if (Model.ShowRequestId) { <p><strong>Request ID:</strong> <code>@Model.RequestId</code></p> }"; var errorModelContent = @$"using Microsoft.AspNetCore.Mvc; using Microsoft.AspNetCore.Mvc.RazorPages; using Microsoft.Extensions.Logging; using System.Diagnostics; namespace {projectNamespace}.Pages {{ [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] [IgnoreAntiforgeryToken] public class ErrorModel : PageModel {{ public string? RequestId {{ get; set; }} public bool ShowRequestId => !string.IsNullOrEmpty(RequestId); private readonly ILogger<ErrorModel> _logger; public ErrorModel(ILogger<ErrorModel> logger) {{ _logger = logger; }} public void OnGet() {{ RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier; }} }} }}"; await File.WriteAllTextAsync(errorPath, errorPageContent); await File.WriteAllTextAsync(errorModelPath, errorModelContent); }
            var indexPagePath = Path.Combine(pagesFolder, "Index.razor"); if (!File.Exists(indexPagePath)) { _logger.LogDebug("Generando archivo Pages/Index.razor"); var indexPageContent = @$"@page ""/"" <PageTitle>Index - {promptTitulo}</PageTitle> <h1>Hello, world!</h1> Welcome to your new app '{promptTitulo}'."; await File.WriteAllTextAsync(indexPagePath, indexPageContent); }
            var wwwrootFolder = Path.Combine(rutaProyecto, "wwwroot"); Directory.CreateDirectory(wwwrootFolder); var cssFolder = Path.Combine(wwwrootFolder, "css"); Directory.CreateDirectory(cssFolder); var bootstrapFolder = Path.Combine(cssFolder, "bootstrap"); Directory.CreateDirectory(bootstrapFolder); var bootstrapCssPath = Path.Combine(bootstrapFolder, "bootstrap.min.css"); if (!File.Exists(bootstrapCssPath)) { _logger.LogDebug("Generando placeholder para wwwroot/css/bootstrap/bootstrap.min.css"); await File.WriteAllTextAsync(bootstrapCssPath, @"/* Minified Bootstrap CSS */"); }
            var siteCssPath = Path.Combine(cssFolder, "site.css"); if (!File.Exists(siteCssPath)) { _logger.LogDebug("Generando archivo wwwroot/css/site.css"); var siteCssContent = @"html, body { font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif; } h1:focus { outline: none; } a, .btn-link { color: #0071c1; } .btn-primary { color: #fff; background-color: #1b6ec2; border-color: #1861ac; } .content { padding-top: 1.1rem; } #blazor-error-ui { background: lightyellow; bottom: 0; box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2); display: none; left: 0; padding: 0.6rem 1.25rem 0.7rem 1.25rem; position: fixed; width: 100%; z-index: 1000; } #blazor-error-ui .dismiss { cursor: pointer; position: absolute; right: 0.75rem; top: 0.5rem; }"; await File.WriteAllTextAsync(siteCssPath, siteCssContent); }
            var faviconPath = Path.Combine(wwwrootFolder, "favicon.ico"); if (!File.Exists(faviconPath)) { await File.WriteAllBytesAsync(faviconPath, Array.Empty<byte>()); }
        }

        private string SanitizeNamespace(string projectName)
        {
            var sanitized = Regex.Replace(projectName, @"[^A-Za-z0-9_.]", "_"); if (string.IsNullOrEmpty(sanitized)) { return "_GeneratedProject"; }
            if (char.IsDigit(sanitized[0])) { sanitized = "_" + sanitized; }
            return ToPascalCase(sanitized);
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return "_"; var parts = input.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries); if (!parts.Any()) { var fallback = new string(input.Where(char.IsLetterOrDigit).ToArray()); if (string.IsNullOrEmpty(fallback)) return "_"; parts = new[] { fallback }; }
            var sb = new StringBuilder(); foreach (var part in parts) { if (part.Length > 0) { sb.Append(char.ToUpperInvariant(part[0])); if (part.Length > 1) { sb.Append(part.Substring(1).ToLowerInvariant()); } } }
            if (sb.Length == 0) { return "_"; }
            if (!char.IsLetter(sb[0]) && sb[0] != '_') { sb.Insert(0, '_'); }
            return sb.ToString();
        }

        #endregion

    }
}
// --- END OF FILE DesarrolladorAgent.cs --- ADDED LOGGING