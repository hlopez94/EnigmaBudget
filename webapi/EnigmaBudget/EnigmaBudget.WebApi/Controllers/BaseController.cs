using EnigmaBudget.WebApi.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BaseController : ControllerBase
    {

        private readonly ILogger<BaseController> _logger;
        public BaseController(ILogger<BaseController> logger)
        {
            _logger = logger;
        }

        [HttpGet()]
        public ActionResult<ApiResponse<string>> Get()
        {
            var res = new ApiResponse<string>()
            {
                Ok = true,
                Result = "Hola Giancito"
            };
            return Ok(res);
        }
    }
}
