using EnigmaBudget.Application;
using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BalancesController : BaseController
    {
        private readonly IBalancesService _balancesService;
        public BalancesController(IBalancesService balancesService)
        {
            _balancesService = balancesService;
        }

        [HttpGet]
        public async Task<AppResult<List<Balance>>> GetAsync() => await _balancesService.ObtenerBalances();
    }
}
