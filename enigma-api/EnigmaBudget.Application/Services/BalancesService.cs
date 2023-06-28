using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;

namespace EnigmaBudget.Application.Services
{
    public class BalancesService : IBalancesService
    {

        private readonly IDepositAccountRepository _depositAccountRepository;

        public BalancesService(IDepositAccountRepository depositAccountsService)
        {
            _depositAccountRepository = depositAccountsService;
        }

        public async Task<AppResult<List<Balance>>> ObtenerBalances()
        {
            var appResult = new AppResult<List<Balance>>();

            appResult.Data = await _depositAccountRepository.GetBalances().ToListAsync();

            return appResult;
        }
    }
}
