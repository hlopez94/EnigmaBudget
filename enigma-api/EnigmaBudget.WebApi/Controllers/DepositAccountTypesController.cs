using EnigmaBudget.Application;
using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DepositAccountTypesController : BaseController
    {

        private readonly IDepositAccounTypesService _depositAccountsService;
        public DepositAccountTypesController(IDepositAccounTypesService depositAccountsService)
        {
            _depositAccountsService = depositAccountsService;
        }

        [HttpGet()]
        public async Task<AppResult<List<DepositAccountType>>> ListTypes()
        {
            return await _depositAccountsService.ListDepositAccounTypes();
        }

        [HttpGet("{typeId}")]
        public async Task<AppResult<DepositAccountType>> GetType([FromRoute] string typeId)
        {
            return await _depositAccountsService.GetDepositAccountType(typeId);
        }
        [HttpGet("/enum/{typeEnumName}")]
        public async Task<AppResult<DepositAccountType>> GetTypeByEnumName([FromRoute] string typeEnumName)
        {
            return await _depositAccountsService.GetDepositAccountTypeByEnumName(typeEnumName);
        }
    }
}
