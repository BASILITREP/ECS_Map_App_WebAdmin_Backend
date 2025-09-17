using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcsFeMappingApi.Data;
using EcsFeMappingApi.Models;

namespace EcsFeMappingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SeedController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SeedController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("sample-data")]
        public async Task<IActionResult> SeedSampleData()
        {
            try
            {
                // Add sample branches
                if (!_context.Branches.Any())
                {
                    var branches = new List<Branch>
                    {
                        new Branch { Name = "BDO Dasmarinas", Address = "Dasmarinas, Cavite", Latitude = 14.3294, Longitude = 120.9367 },
                        new Branch { Name = "BDO Imus", Address = "Imus, Cavite", Latitude = 14.4297, Longitude = 120.9367 },
                        new Branch { Name = "BDO MOA", Address = "Mall of Asia, Pasay", Latitude = 14.5378, Longitude = 120.9726 }
                    };
                    _context.Branches.AddRange(branches);
                }

                // Add sample engineers
                if (!_context.FieldEngineers.Any())
                {
                    var engineers = new List<FieldEngineer>
                    {
                        new FieldEngineer 
                        { 
                            Name = "John Doe", 
                            Email = "john@company.com", 
                            Phone = "123-456-7890", 
                            Status = "Active", 
                            IsAvailable = true,
                            CurrentLatitude = 14.5995,
                            CurrentLongitude = 120.9842,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        },
                        new FieldEngineer 
                        { 
                            Name = "Jane Smith", 
                            Email = "jane@company.com", 
                            Phone = "098-765-4321", 
                            Status = "On Assignment", 
                            IsAvailable = false,
                            CurrentLatitude = 14.6091,
                            CurrentLongitude = 121.0223,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        }
                    };
                    _context.FieldEngineers.AddRange(engineers);
                }

                await _context.SaveChangesAsync();

                return Ok(new { 
                    message = "Sample data seeded successfully!",
                    branches = await _context.Branches.CountAsync(),
                    engineers = await _context.FieldEngineers.CountAsync()
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}