using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Gameregistry.Data;
using Gameregistry.Models;

namespace Gameregistry.Pages.Videogames
{
    public class EditModel : PageModel
    {
        private readonly Gameregistry.Data.VideogamedbContext _context;

        public EditModel(Gameregistry.Data.VideogamedbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public Gameregistry.Models.Videogames Videogames { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var videogames =  await _context.VideogamesList.FirstOrDefaultAsync(m => m.Id == id);
            if (videogames == null)
            {
                return NotFound();
            }
            Videogames = videogames;
            return Page();
        }

        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more information, see https://aka.ms/RazorPagesCRUD.
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            _context.Attach(Videogames).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!VideogamesExists(Videogames.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return RedirectToPage("./Index");
        }

        private bool VideogamesExists(int id)
        {
            return _context.VideogamesList.Any(e => e.Id == id);
        }
    }
}
