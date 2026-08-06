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
    public class IndexModel : PageModel
    {
        private readonly Gameregistry.Data.VideogamedbContext _context;

        public IndexModel(Gameregistry.Data.VideogamedbContext context)
        {
            _context = context;
        }

        public IList<Gameregistry.Models.Videogames> Videogames { get;set; } = default!;

        public async Task OnGetAsync()
        {
            Videogames = await _context.VideogamesList.ToListAsync();
        }
    }
}
