using System.ComponentModel.DataAnnotations;

namespace AppBasicaCRUD.Models;

/// <summary>
/// Represents a product in the application.
/// </summary>
public class Producto
{
    /// <summary>
    /// Gets or sets the unique identifier for the product.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the product.  Required.
    /// </summary>
    [Required(ErrorMessage = "El nombre es requerido.")]
    public string? Nombre { get; set; }

    /// <summary>
    /// Gets or sets the description of the product.
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Gets or sets the price of the product. Required and must be greater than 0.
    /// </summary>
    [Required(ErrorMessage = "El precio es requerido.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor que 0.")]
    public decimal Precio { get; set; }

    /// <summary>
    /// Gets or sets the date when the product was added.
    /// </summary>
    public DateTime FechaAlta { get; set; } = DateTime.Now;
}