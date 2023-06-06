using EnigmaBudget.Application;
using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    public class DepositAccountsController : BaseController
    {
        private readonly IDepositAccountsService _depositAccountsService;
        public DepositAccountsController(IDepositAccountsService depositAccountsService)
        {
            _depositAccountsService = depositAccountsService;
        }

        [HttpGet()]
        public async Task<AppResult<List<DepositAccount>>> ListAccounts()
        {
            return await _depositAccountsService.ListUserDepositAccounts();
        }

        [HttpGet("{accountId}")]
        public async Task<AppResult<DepositAccountDetails>> GetAccountDetails([FromRoute] string accountId)
        {
            return await _depositAccountsService.GetDepositAccountDetails(accountId);
        }

        [HttpPost("{accountId}/withdraw")]
        public async Task<AppResult<AccountMovement>> MakeWithdrawalOnAccount([FromRoute] string accountId, [FromBody] WithdrawRequest withdrawRequest)
        {
            withdrawRequest.AccountId = accountId;
            return await _depositAccountsService.MakeWithdrawOnAccount(withdrawRequest);
        }

        [HttpPost("{accountId}/deposit")]
        public async Task<AppResult<AccountMovement>> MakeDepositOnAccount([FromRoute] string accountId, [FromBody] DepositOnAccountRequest depositRequest)
        {
            depositRequest.AccountId = accountId;
            return await _depositAccountsService.MakeDepositOnAccount(depositRequest);
        }

        [HttpPost()]
        public async Task<AppResult<DepositAccount>> PostDepositAccount(CreateDepositAccountRequest body)
        {
            return await _depositAccountsService.CreateDepositAccount(body);
        }

        [HttpPut("{accountId}")]
        public async Task<AppResult<DepositAccount>> PutDepositAccount(string accountId, [FromBody] EditDepositAccountRequest body)
        {
            return await _depositAccountsService.EditDepositAccountDetails(accountId, body);
        }

    }
}
