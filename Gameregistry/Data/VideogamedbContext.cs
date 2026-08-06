namespace Gameregistry.Data;
using Microsoft.EntityFrameworkCore;
using Gameregistry.Models;

    public class VideogamedbContext : DbContext
    {
        
        public VideogamedbContext(DbContextOptions<VideogamedbContext> options) : base(options)
        {
        }
        public DbSet<Gameregistry.Models.Videogames> VideogamesList { get; set; }
        

}
