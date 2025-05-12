using Microsoft.EntityFrameworkCore;
using AppBasicaCRUD.Models;

namespace AppBasicaCRUD.Data
{
    /// <summary>
    /// Clase que representa el contexto de la base de datos de la aplicación.
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        /// <summary>
        /// Constructor de la clase ApplicationDbContext.
        /// </summary>
        /// <param name="options">Opciones de configuración del contexto.</param>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        /// Representa la tabla de Productos en la base de datos.
        /// </summary>
        public DbSet<Producto> Productos { get; set; }
    }
}