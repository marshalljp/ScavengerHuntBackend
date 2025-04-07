using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using ScavengerHuntBackend.Models;

namespace ScavengerHuntBackend.Controllers
{
    [Route("submissions")]
    [ApiController]
    //[Authorize]
    public class SubmissionController : ControllerBase
    {
        private readonly ScavengerHuntContext _context;

        public SubmissionController(ScavengerHuntContext context)
        {
            _context = context;
        }


    }
}