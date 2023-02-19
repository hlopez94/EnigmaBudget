using EnigmaBudget.Application;
using EnigmaBudget.Application.Model;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    public class AccountsController : BaseController
    {
        private readonly IDepositAccountsService _depositAccountsService;
        public AccountsController(IDepositAccountsService depositAccountsService) 
        {
            _depositAccountsService= depositAccountsService;
        }

        [HttpGet()]
        public AppServiceResponse<List<DepositAccount>> ListAccounts()
        {
            return _depositAccountsService.ListDepositAccounts();
        }

    }
}
