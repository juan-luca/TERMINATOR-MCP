using System;
using System.ComponentModel.DataAnnotations; // Required for validation attributes like [Key], [Required], [Range]
using System.ComponentModel.DataAnnotations.Schema; // Potentially useful for attributes like [Column] if needed later

namespace AppBasicaCRUD.Models
{
    /// <summary>
    /// Representa un producto en el sistema.
    /// Contiene información básica y de precio del producto.
    /// </summary>
    public class Producto
    {
        /// <summary>
        /// Identificador único del producto (Clave Primaria).
        /// Es asignado automáticamente por la base de datos.
        /// </summary>
        [Key] // Marca la propiedad Id como la clave primaria de la entidad.
        public int Id { get; set; }

        /// <summary>
        /// Nombre del producto. Este campo es obligatorio.
        /// </summary>
        [Required(ErrorMessage = "El nombre del producto es obligatorio.")]
        [StringLength(100, ErrorMessage = "El nombre no puede exceder los 100 caracteres.")] // Buena práctica limitar longitud
        public string Nombre { get; set; } = string.Empty; // Inicializar para evitar nulls si NRTs están habilitados

        /// <summary>
        /// Descripción detallada u opcional del producto.
        /// Puede ser nulo o vacío si no se proporciona descripción.
        /// </summary>
        [StringLength(500, ErrorMessage = "La descripción no puede exceder los 500 caracteres.")]
        public string? Descripcion { get; set; } // Permite valores nulos para la descripción

        /// <summary>
        /// Precio de venta del producto.
        /// Este campo es obligatorio y debe ser un valor positivo mayor que cero.
        /// </summary>
        [Required(ErrorMessage = "El precio es obligatorio.")]
        // Se usa (double)decimal.MaxValue para compatibilidad con RangeAttribute que a veces prefiere double, aunque decimal es más preciso para moneda.
        // O se puede usar un tipo específico si se valida de otra forma. El requerimiento pide double.MaxValue.
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser un valor positivo mayor que 0.")]
        [Column(TypeName = "decimal(18, 2)")] // Especifica el tipo de dato en la base de datos para precisión monetaria.
        public decimal Precio { get; set; }

        /// <summary>
        /// Fecha y hora en que el producto fue registrado por primera vez en el sistema.
        /// Generalmente se establece automáticamente al crear un nuevo producto.
        /// </summary>
        [DataType(DataType.DateTime)] // Ayuda a los helpers de UI a renderizar el campo adecuadamente.
        public DateTime FechaAlta { get; set; } = DateTime.UtcNow; // Establece un valor predeterminado a la fecha/hora actual UTC.
    }
}