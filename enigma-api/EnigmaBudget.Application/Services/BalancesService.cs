using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;

namespace EnigmaBudget.Application.Services
{
    public class BalancesService : IBalancesService
    {

        private readonly IDepositAccountRepository _depositAccountRepository;
        private readonly ICountriesService _countriesService;

        public BalancesService(IDepositAccountRepository depositAccountsService,
            ICountriesService countriesService)
        {
            _depositAccountRepository = depositAccountsService;
            _countriesService = countriesService;
        }

        public async Task<AppResult<List<Balance>>> ObtenerBalances()
        {
            var appResult = new AppResult<List<Balance>>();

            appResult.Data = await _depositAccountRepository.GetBalances().ToListAsync();

            foreach(var balance in appResult.Data)
            {
                balance.Moneda = _countriesService.GetCurrencyById(balance.Moneda.Code).Data;
            }
            return appResult;
        }
    }
}
