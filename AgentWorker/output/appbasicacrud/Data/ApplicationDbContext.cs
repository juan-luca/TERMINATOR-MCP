using Microsoft.EntityFrameworkCore;
using AppBasicaCRUD.Models; // Asegúrate que el namespace del modelo Producto sea correcto

namespace AppBasicaCRUD.Data
{
    /// <summary>
    /// Contexto de la base de datos para la aplicación.
    /// Gestiona las entidades de la aplicación, como Producto.
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        /// <summary>
        /// Inicializa una nueva instancia de la clase <see cref="ApplicationDbContext"/>.
        /// </summary>
        /// <param name="options">Las opciones a usar por este DbContext.</param>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) // Tarea específica: Llamada al constructor base con las opciones
        {
        }

        /// <summary>
        /// Obtiene o establece el DbSet para la entidad Producto.
        /// Representa la colección de todos los productos en la base de datos.
        /// </summary>
        public DbSet<Producto> Productos { get; set; }

        // Puedes añadir aquí configuraciones adicionales del modelo si son necesarias,
        // usando OnModelCreating. Para este caso simple, no es requerido.
        /*
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Ejemplo: Configuración adicional para la entidad Producto
            // modelBuilder.Entity<Producto>().ToTable("CatalogoProductos");
        }
        */
    }
}