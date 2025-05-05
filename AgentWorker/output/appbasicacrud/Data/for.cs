using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using AppBasicaCRUD.Data;
using AppBasicaCRUD.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AppBasicaCRUD.Pages.Productos
{
    /// <summary>
    /// Code-behind class for the Index page, displaying a list of products.
    /// Implements pagination and search functionality.
    /// </summary>
    public partial class Index : ComponentBase
    {
        [Inject]
        private ApplicationDbContext _context { get; set; } = default!;

        [Inject]
        private NavigationManager NavigationManager { get; set; } = default!;

        /// <summary>
        /// Gets or sets the list of products to display on the current page.
        /// </summary>
        protected IList<Producto> Productos { get; set; } = new List<Producto>();

        /// <summary>
        /// Gets or sets the search term entered by the user.
        /// </summary>
        protected string SearchTerm { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current page number for pagination.
        /// </summary>
        protected int CurrentPage { get; set; } = 1;

        /// <summary>
        /// Gets or sets the total number of pages based on the filtered data.
        /// </summary>
        protected int TotalPages { get; set; }

        /// <summary>
        /// Gets or sets the total count of products matching the search criteria.
        /// </summary>
        protected int TotalCount { get; set; }

        /// <summary>
        /// Defines the number of items to display per page.
        /// </summary>
        protected int PageSize { get; set; } = 10; // Or read from config

        /// <summary>
        /// Gets or sets an error message to display, if any.
        /// </summary>
        protected string? ErrorMessage { get; set; }

        /// <summary>
        /// Indicates if data is currently being loaded.
        /// </summary>
        protected bool IsLoading { get; set; } = false;

        /// <summary>
        /// Initializes the component and loads the initial list of products.
        /// </summary>
        protected override async Task OnInitializedAsync()
        {
            await LoadProductosAsync();
        }

        /// <summary>
        /// Loads the products from the database based on the current search term and page number.
        /// </summary>
        protected async Task LoadProductosAsync()
        {
            IsLoading = true;
            ErrorMessage = null;
            try
            {
                var query = _context.Productos.AsQueryable();

                if (!string.IsNullOrWhiteSpace(SearchTerm))
                {
                    query = query.Where(p => p.Nombre.Contains(SearchTerm));
                }

                TotalCount = await query.CountAsync();
                TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);

                // Ensure CurrentPage is within valid bounds
                if (CurrentPage < 1) CurrentPage = 1;
                if (CurrentPage > TotalPages && TotalPages > 0) CurrentPage = TotalPages;

                Productos = await query
                                    .OrderBy(p => p.Nombre) // Example ordering
                                    .Skip((CurrentPage - 1) * PageSize)
                                    .Take(PageSize)
                                    .ToListAsync();
            }
            catch (Exception ex)
            {
                // Log the exception (implement logging as needed)
                Console.WriteLine($"Error loading products: {ex.Message}");
                ErrorMessage = "Error al cargar los productos. Intente de nuevo más tarde.";
                Productos = new List<Producto>(); // Ensure list is empty on error
                TotalCount = 0;
                TotalPages = 0;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Called when the search term changes. Resets to the first page and reloads data.
        /// </summary>
        protected async Task SearchProductos()
        {
            CurrentPage = 1;
            await LoadProductosAsync();
        }

        /// <summary>
        /// Navigates to the specified page number.
        /// </summary>
        /// <param name="pageNumber">The page number to navigate to.</param>
        protected async Task GoToPageAsync(int pageNumber)
        {
            if (pageNumber >= 1 && pageNumber <= TotalPages && pageNumber != CurrentPage)
            {
                CurrentPage = pageNumber;
                await LoadProductosAsync();
            }
        }

        /// <summary>
        /// Navigates to the Create product page.
        /// </summary>
        protected void NavigateToCreate()
        {
            NavigationManager.NavigateTo("/productos/create");
        }

        /// <summary>
        /// Navigates to the Edit page for the specified product ID.
        /// </summary>
        /// <param name="id">The ID of the product to edit.</param>
        protected void NavigateToEdit(int id)
        {
            NavigationManager.NavigateTo($"/productos/edit/{id}");
        }

        /// <summary>
        /// Navigates to the Delete confirmation page for the specified product ID.
        /// </summary>
        /// <param name="id">The ID of the product to delete.</param>
        protected void NavigateToDelete(int id)
        {
            NavigationManager.NavigateTo($"/productos/delete/{id}");
        }
    }
}