// Shared/FileTask.cs
namespace Shared
{
    public class FileTask
    {
        /// <summary>Ruta relativa del archivo a generar, p.ej. "Models/Producto.cs"</summary>
        public string Path { get; set; } = "";

        /// <summary>Descripción precisa de qué debe contener el archivo</summary>
        public string Description { get; set; } = "";
    }
}
