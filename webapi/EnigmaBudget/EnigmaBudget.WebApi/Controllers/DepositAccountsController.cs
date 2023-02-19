using EnigmaBudget.Application;
using EnigmaBudget.Application.Model;
using EnigmaBudget.Model.Model;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    public class DepositAccountsController : BaseController
    {
        private readonly IDepositAccountsService _depositAccountsService;
        public DepositAccountsController(IDepositAccountsService depositAccountsService) 
        {
            _depositAccountsService= depositAccountsService;
        }

        [HttpGet()]
        public AppServiceResponse<List<DepositAccount>> ListAccounts()
        {
            return _depositAccountsService.ListDepositAccounts();
        }

        [HttpGet("{accountId}")]
        public AppServiceResponse<DepositAccountDetails> GetAccountDetails([FromRoute]Guid accountId)
        {
            return _depositAccountsService.GetDepositAccountDetails(accountId);
        }

        [HttpGet("{accountId}/withdraw")]
        public AppServiceResponse<AccountMovement> MakeWithdrawalOnAccount([FromRoute] Guid accountId, [FromBody] WithdrawRequest withdrawRequest)
        {
            withdrawRequest.AccountId=accountId;
            return _depositAccountsService.MakeWithdrawOnAccount(withdrawRequest);
        }

        [HttpGet("{accountId}/deposit")]
        public AppServiceResponse<AccountMovement> MakeDepositOnAccount([FromRoute] Guid accountId, [FromBody] DepositOnAccountRequest depositRequest)
        {
            depositRequest.AccountId=accountId;
            return _depositAccountsService.MakeDepositOnAccount(depositRequest);
        }

        [HttpPost()]
        public AppServiceResponse<DepositAccount> PostDepositAccount(CreateDepositAccountRequest body)
        {
            return _depositAccountsService.CreateDepositAccount(body);
        }

        [HttpPut()]
        public AppServiceResponse<DepositAccount> PutDepositAccount(Guid accountId, [FromBody] EditDepositAccountRequest body)
        {
            return _depositAccountsService.EditDepositAccountDetails(accountId, body);
        }

    }
}
