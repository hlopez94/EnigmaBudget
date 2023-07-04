using EnigmaBudget.Application;
using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Infrastructure.Pager;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionsController : ControllerBase
    {
        private readonly ITransactionsService _transactionsService;
        public TransactionsController(ITransactionsService transactionsService) {
            _transactionsService = transactionsService;
        }
        [HttpGet]
        [Route("/")]
        public async Task<AppResult<PagedResponse<DepositAccountTransaction>>> GetLatestTransactions([FromQuery]PagedRequest request)
        {
            return await _transactionsService.GetUserTransactions(request);
        }

        [HttpPost("/accounts/{accountId}/withdraw")]
        public async Task<AppResult<DepositAccountTransaction>> MakeWithdrawalOnAccount([FromRoute] string accountId, [FromBody] WithdrawRequest withdrawRequest)
        {
            withdrawRequest.AccountId = accountId;
            return await _transactionsService.WithdrawOnAccount(withdrawRequest);
        }

        [HttpPost("/accounts/{accountId}/deposit")]
        public async Task<AppResult<DepositAccountTransaction>> MakeDepositOnAccount([FromRoute] string accountId, [FromBody] DepositOnAccountRequest depositRequest)
        {
            depositRequest.AccountId = accountId;
            return await _transactionsService.DepositOnAccount(depositRequest);
        }

    }
}
