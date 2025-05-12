using System.ComponentModel.DataAnnotations;

namespace AppBasicaCRUD.Models
{
    /// <summary>
    /// Representa un producto en el sistema.
    /// </summary>
    public class Producto
    {
        /// <summary>
        /// Identificador único del producto.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Nombre del producto (obligatorio).
        /// </summary>
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        public string? Nombre { get; set; }

        /// <summary>
        /// Descripción del producto.
        /// </summary>
        public string? Descripcion { get; set; }

        /// <summary>
        /// Precio del producto (obligatorio y mayor que cero).
        /// </summary>
        [Required(ErrorMessage = "El precio es obligatorio.")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor que cero.")]
        public decimal Precio { get; set; }

        /// <summary>
        /// Fecha de alta del producto.
        /// </summary>
        public DateTime FechaAlta { get; set; } = DateTime.Now;
    }
}