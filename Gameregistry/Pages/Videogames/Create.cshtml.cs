using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Gameregistry.Data;
using Gameregistry.Models;

namespace Gameregistry.Pages.Videogames
{
    public class CreateModel : PageModel
    {
        private readonly Gameregistry.Data.VideogamedbContext _context;

        public CreateModel(Gameregistry.Data.VideogamedbContext context)
        {
            _context = context;
        }

        public IActionResult OnGet()
        {
            return Page();
        }

        [BindProperty]
        public Gameregistry.Models.Videogames Videogames { get; set; } = default!;

        // For more information, see https://aka.ms/RazorPagesCRUD.
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            _context.VideogamesList.Add(Videogames);
            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
