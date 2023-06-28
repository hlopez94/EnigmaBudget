using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Infrastructure.Auth;

namespace EnigmaBudget.Application.Services
{
    public class DepositAccountsService : IDepositAccountsService
    {
        private readonly IDepositAccountRepository _depositAccountRepository;
        private readonly IAuthService _authService;
        private readonly ICountriesService _countriesService;
        public DepositAccountsService(IDepositAccountRepository depositAccountRepository, IAuthService authService, ICountriesService countriesService)
        {
            _depositAccountRepository = depositAccountRepository;
            _authService = authService;
            _countriesService = countriesService;
        }
        public async Task<AppResult<DepositAccount>> CreateDepositAccount(CreateDepositAccountRequest request)
        {
            var result = new AppResult<DepositAccount>();


            DepositAccount newAccount = new DepositAccount()
            {
                Currency = request.Currency,
                Country = request.Country,
                Description = request.Description,
                Funds = request.InitialFunds,
                Type = request.Type,
                Name = request.AccountAlias
            };

            await _depositAccountRepository.Create(newAccount);
            result.Data = newAccount;
            return result;
        }

        public async Task<AppResult<DepositAccount>> EditDepositAccountDetails(string guid, EditDepositAccountRequest request)
        {
            var result = new AppResult<DepositAccount>();
            var account = await _depositAccountRepository.GetById(request.DepositAccountUUID);

            if (account is null || account.OwnerId != _authService.GetProfile().Data.Id)
            {
                result.AddNotFoundError("Deposit account not found");
                return result;
            }

            //TODO: Make changes on account's properties

            await _depositAccountRepository.Update(account);

            result.Data = account;
            return result;

        }

        public Task<AppResult<DepositAccountDetails>> GetDepositAccountDetails(string guid)
        {
            throw new NotImplementedException();
        }


        public async Task<AppResult<List<DepositAccount>>> ListUserDepositAccounts()
        {
            var appResult = new AppResult<List<DepositAccount>>();
            appResult.Data = await _depositAccountRepository.ListUserDepositAccounts().ToListAsync();

            foreach(var account in appResult.Data)
            {
                account.Currency = _countriesService.GetCurrencyById(account.Currency.Num).Data;
                account.Country = _countriesService.GetCountryById(account.Country.Alpha3).Data;
            }
            return appResult;
        }

        public Task<AppResult<AccountMovement>> MakeDepositOnAccount(DepositOnAccountRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<AppResult<AccountMovement>> MakeWithdrawOnAccount(WithdrawRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
