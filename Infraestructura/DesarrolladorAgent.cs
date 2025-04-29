using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Shared;
using Microsoft.Extensions.Logging;


namespace Infraestructura
{

    public class DesarrolladorAgent : IDesarrolladorAgent
{
    
        private readonly GeminiClient _gemini;
        private readonly IErrorFixer _errorFixer;
        private readonly ILogger<DesarrolladorAgent> _logger;

        public DesarrolladorAgent(
            GeminiClient gemini,
            IErrorFixer errorFixer,
            ILogger<DesarrolladorAgent> logger)
        {
            _gemini = gemini;
            _errorFixer = errorFixer;
            _logger = logger;
        }

        public async Task GenerarCodigoParaTarea(Prompt prompt, string tarea)
        {
            var nombreProyecto = SanitizeFileName(prompt.Titulo);
            var rutaProyecto = Path.Combine("output", nombreProyecto);

            _logger.LogInformation("➡️ Generando código para: {Tarea}", tarea);
            Directory.CreateDirectory(rutaProyecto);

            await GenerarCsprojAsync(nombreProyecto, rutaProyecto);
            await GenerarArchivosBaseAsync(nombreProyecto, rutaProyecto);

            if (await CompilarProyectoAsync(rutaProyecto))
            {
                _logger.LogInformation("✅ Proyecto {Proyecto} compiló sin errores.", nombreProyecto);
                return;
            }

            _logger.LogWarning("⚠️ Proyecto {Proyecto} compiló con errores. Iniciando corrección automática...", nombreProyecto);

            List<string> archivosCorregidos = new();
            try
            {
                archivosCorregidos = await _errorFixer.CorregirErroresDeCompilacionAsync(rutaProyecto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error al corregir errores de compilación del proyecto {Proyecto}", nombreProyecto);
            }

            if (archivosCorregidos.Count > 0)
            {
                _logger.LogInformation("🔄 Se corrigieron {Count} archivos. Reintentando build...", archivosCorregidos.Count);
                if (await CompilarProyectoAsync(rutaProyecto))
                    _logger.LogInformation("✅ Proyecto {Proyecto} compiló correctamente tras corrección.", nombreProyecto);
                else
                    _logger.LogError("❌ Sigue fallando tras corrección. Revisar build_errors_after_fix.log");
            }
            else
            {
                _logger.LogError("❌ No se aplicaron correcciones automáticas. Revisar build_errors.log");
            }
        }

        public string SanitizeFileName(string input)
    {
        var sb = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
        }
        var result = sb.ToString().ToLowerInvariant();
        return result.Length > 40 ? result.Substring(0, 40) : result;
    }
        /// <summary>
        /// Busca 'class NombreClase' y devuelve 'NombreClase.cs'. Si no lo encuentra, recorta tarea.
        /// </summary>
        private string ExtractCSharpFilename(string code)
        {
            var match = Regex.Match(code, @"\bclass\s+([A-Za-z_]\w*)");
            if (match.Success)
                return match.Groups[1].Value + ".cs";

            // fallback
            return SanitizeFileName(Guid.NewGuid().ToString()) + ".cs";
        }

        /// <summary>
        /// Busca '@page "/Ruta"' o '<ComponentName>' y devuelve 'ComponentName.razor'.
        /// </summary>
        private string ExtractRazorFilename(string code)
        {
            // Primero intentar con un tag de componente <MyComponent
            var compMatch = Regex.Match(code, @"<([A-Za-z_]\w*)\b");
            if (compMatch.Success)
                return compMatch.Groups[1].Value + ".razor";

            // Luego intentar con @page
            var pageMatch = Regex.Match(code, @"@page\s+""/(\w+)""");
            if (pageMatch.Success)
                return UppercaseFirst(pageMatch.Groups[1].Value) + ".razor";

            // fallback
            return SanitizeFileName(Guid.NewGuid().ToString()) + ".razor";
        }

        private string UppercaseFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];


        private string InferirSubcarpeta(string tarea)
    {
        var t = tarea.ToLowerInvariant();

        if (t.Contains("página") || t.Contains(".razor") || t.Contains("razor"))
            return "Pages";
        if (t.Contains("servicio") || t.Contains("email") || t.Contains("exportar") || t.Contains("login"))
            return "Services";
        if (t.Contains("contexto") || t.Contains("dbcontext") || t.Contains("base de datos"))
            return "Data";
        if (t.Contains("configuración") || t.Contains("appsettings"))
            return "";
        return "Components";
    }

        private async Task GenerarCsprojAsync(string nombreProyecto, string rutaProyecto)
        {
            // 1) Asegurar que la carpeta exista (por si llaman directo a este método)
            Directory.CreateDirectory(rutaProyecto);

            // 2) Ruta completa del .csproj
            var csprojPath = Path.Combine(rutaProyecto, $"{nombreProyecto}.csproj");

            // 3) Si ya existe, salir sin hacer nada
            if (File.Exists(csprojPath))
                return;

            // 4) Contenido mínimo del .csproj
            var csprojContent = $"""
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="8.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="8.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.0" />
  </ItemGroup>

</Project>
""";

            // 5) Escribir el archivo
            await File.WriteAllTextAsync(csprojPath, csprojContent);
        }


        private async Task GenerarArchivosBaseAsync(string nombreProyecto, string rutaProyecto)
        {
            // 1) Program.cs
            var programContent = """
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
""";
            var programPath = Path.Combine(rutaProyecto, "Program.cs");
            if (!File.Exists(programPath))
                await File.WriteAllTextAsync(programPath, programContent);

            // 2) App.razor
            var appContent = """
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(Program).Assembly">
        <Found Context="routeData">
            <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        </Found>
        <NotFound>
            <LayoutView Layout="@typeof(MainLayout)">
                <p>Lo sentimos, no se encontró la página.</p>
            </LayoutView>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
""";
            var appPath = Path.Combine(rutaProyecto, "App.razor");
            if (!File.Exists(appPath))
                await File.WriteAllTextAsync(appPath, appContent);

            // 3) _Imports.razor
            var importsContent = $"""
@using {nombreProyecto}
@using {nombreProyecto}.Shared
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
""";
            var importsPath = Path.Combine(rutaProyecto, "_Imports.razor");
            if (!File.Exists(importsPath))
                await File.WriteAllTextAsync(importsPath, importsContent);

            // 4) Shared folder con MainLayout y NavMenu
            var sharedFolder = Path.Combine(rutaProyecto, "Shared");
            Directory.CreateDirectory(sharedFolder);

            // 4a) MainLayout.razor
            var layoutContent = """
@inherits LayoutComponentBase

<div class="page">
    <div class="sidebar">
        <NavMenu />
    </div>
    <div class="main">
        <div class="content px-4">
            @Body
        </div>
    </div>
</div>
""";
            var layoutPath = Path.Combine(sharedFolder, "MainLayout.razor");
            if (!File.Exists(layoutPath))
                await File.WriteAllTextAsync(layoutPath, layoutContent);

            // 4b) NavMenu.razor
            var navMenuContent = """
<nav class="sidebar-nav">
    <ul class="nav flex-column">
        <li class="nav-item px-3">
            <NavLink class="nav-link" href="/" Match="NavLinkMatch.All">Home</NavLink>
        </li>
        <!-- podés agregar más enlaces aquí -->
    </ul>
</nav>
""";
            var navMenuPath = Path.Combine(sharedFolder, "NavMenu.razor");
            if (!File.Exists(navMenuPath))
                await File.WriteAllTextAsync(navMenuPath, navMenuContent);

            // 5) Pages/_Host.cshtml (entry point Server-Side Blazor)
            var pagesFolder = Path.Combine(rutaProyecto, "Pages");
            Directory.CreateDirectory(pagesFolder);

            var hostContent = $"""
@page "/"
@namespace {nombreProyecto}.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
<!DOCTYPE html>
<html lang="es">
<head>
    <meta charset="utf-8" />
    <title>{nombreProyecto}</title>
    <base href="~/" />
    <link href="css/bootstrap/bootstrap.min.css" rel="stylesheet" />
    <link href="css/site.css" rel="stylesheet" />
</head>
<body>
    <app>
        <component type="typeof(App)" render-mode="ServerPrerendered" />
    </app>
    <script src="_framework/blazor.server.js"></script>
</body>
</html>
""";
            var hostPath = Path.Combine(pagesFolder, "_Host.cshtml");
            if (!File.Exists(hostPath))
                await File.WriteAllTextAsync(hostPath, hostContent);

            // 6) Pages/Index.razor (página de inicio funcional)
            var indexPageContent = $"""
@page "/"
@using {nombreProyecto}.Shared

<h1>¡Bienvenido a {nombreProyecto}!</h1>
<p>Esta es la página de inicio generada automáticamente.</p>
""";
            var indexPagePath = Path.Combine(pagesFolder, "Index.razor");
            if (!File.Exists(indexPagePath))
                await File.WriteAllTextAsync(indexPagePath, indexPageContent);

            // 7) wwwroot/index.html (solo para hosting estático si lo necesitás)
            var wwwrootFolder = Path.Combine(rutaProyecto, "wwwroot");
            Directory.CreateDirectory(wwwrootFolder);

            var indexHtmlContent = $"""
<!DOCTYPE html>
<html lang="es">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{nombreProyecto}</title>
    <link href="css/bootstrap/bootstrap.min.css" rel="stylesheet" />
    <link href="css/site.css" rel="stylesheet" />
</head>
<body>
    <h1>¡Bienvenido a {nombreProyecto}!</h1>
</body>
</html>
""";
            var indexHtmlPath = Path.Combine(wwwrootFolder, "index.html");
            if (!File.Exists(indexHtmlPath))
                await File.WriteAllTextAsync(indexHtmlPath, indexHtmlContent);
        }


        private async Task<bool> CompilarProyectoAsync(string rutaProyecto)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build",
            WorkingDirectory = rutaProyecto,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = processInfo;
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var errorPath = Path.Combine(rutaProyecto, "build_errors.log");
            await File.WriteAllTextAsync(errorPath, output + Environment.NewLine + error);
            return false;
        }

        return true;
    }
    }
}
