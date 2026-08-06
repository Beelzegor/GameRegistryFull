using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Gameregistry.Data;
using Gameregistry.Models;

namespace Gameregistry.Pages.Videogames
{
    public class DetailsModel : PageModel
    {
        private readonly Gameregistry.Data.VideogamedbContext _context;

        public DetailsModel(Gameregistry.Data.VideogamedbContext context)
        {
            _context = context;
        }

        public Gameregistry.Models.Videogames Videogames { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var videogames = await _context.VideogamesList.FirstOrDefaultAsync(m => m.Id == id);

            if (videogames is not null)
            {
                Videogames = videogames;

                return Page();
            }

            return NotFound();
        }
    }
}
