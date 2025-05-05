using AppBasicaCRUD.Data;
using AppBasicaCRUD.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AppBasicaCRUD.Pages.Productos
{
    /// <summary>
    /// Code-behind para la página de listado de productos.
    /// Maneja la carga, paginación y búsqueda de productos.
    /// </summary>
    public partial class Index
    {
        [Inject]
        private ApplicationDbContext _context { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        /// <summary>
        /// Lista de productos a mostrar en la página actual.
        /// </summary>
        private IList<Producto> ProductosList = new List<Producto>();

        /// <summary>
        /// Término de búsqueda ingresado por el usuario.
        /// </summary>
        private string SearchTerm { get; set; } = string.Empty;

        // Paginación
        private int CurrentPage { get; set; } = 1;
        private int PageSize { get; set; } = 5; // Número de productos por página
        private int TotalPages { get; set; }
        private int TotalCount { get; set; }

        /// <summary>
        /// Mensaje de error a mostrar al usuario.
        /// </summary>
        private string? ErrorMessage { get; set; }

        /// <summary>
        /// Indica si los datos están cargando.
        /// </summary>
        private bool IsLoading { get; set; } = true;


        /// <summary>
        /// Se ejecuta al inicializar el componente. Carga la lista inicial de productos.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            await LoadProductosAsync();
        }

        /// <summary>
        /// Carga los productos desde la base de datos aplicando filtros y paginación.
        /// </summary>
        private async Task LoadProductosAsync()
        {
            IsLoading = true;
            ErrorMessage = null;
            try
            {
                var query = _context.Productos.AsQueryable();

                // Aplicar filtro de búsqueda si existe
                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    query = query.Where(p => p.Nombre.Contains(SearchTerm));
                }

                // Obtener el conteo total para la paginación (después de filtrar)
                TotalCount = await query.CountAsync();

                // Calcular el número total de páginas
                TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);

                // Asegurarse que la página actual es válida
                CurrentPage = Math.Max(1, Math.Min(CurrentPage, TotalPages == 0 ? 1 : TotalPages));

                // Aplicar paginación
                query = query.OrderBy(p => p.Nombre) // Opcional: ordenar antes de paginar
                             .Skip((CurrentPage - 1) * PageSize)
                             .Take(PageSize);

                ProductosList = await query.ToListAsync();
            }
            catch (Exception ex)
            {
                // Manejo básico de errores
                ErrorMessage = $"Error al cargar los productos: {ex.Message}";
                ProductosList = new List<Producto>(); // Limpiar lista en caso de error
                TotalCount = 0;
                TotalPages = 0;
            }
            finally
            {
                IsLoading = false;
                // Forzar actualización de la UI si es necesario (aunque StateHasChanged suele ser implícito)
                // StateHasChanged();
            }
        }

        /// <summary>
        /// Se ejecuta cuando el término de búsqueda cambia. Reinicia la paginación y recarga los productos.
        /// </summary>
        private async Task OnSearchChanged()
        {
            CurrentPage = 1; // Volver a la primera página al buscar
            await LoadProductosAsync();
        }

        /// <summary>
        /// Navega a una página específica de la lista de productos.
        /// </summary>
        /// <param name="pageNumber">Número de página al que navegar.</param>
        private async Task GoToPageAsync(int pageNumber)
        {
            if (pageNumber >= 1 && pageNumber <= TotalPages && pageNumber != CurrentPage)
            {
                CurrentPage = pageNumber;
                await LoadProductosAsync();
            }
        }

        /// <summary>
        /// Navega a la página de edición para un producto específico.
        /// </summary>
        /// <param name="id">ID del producto a editar.</param>
        private void EditProduct(int id)
        {
            NavigationManager.NavigateTo($"/productos/edit/{id}");
        }

        /// <summary>
        /// Navega a la página de confirmación de eliminación para un producto específico.
        /// </summary>
        /// <param name="id">ID del producto a eliminar.</param>
        private void DeleteProduct(int id)
        {
            NavigationManager.NavigateTo($"/productos/delete/{id}");
        }
    }
}