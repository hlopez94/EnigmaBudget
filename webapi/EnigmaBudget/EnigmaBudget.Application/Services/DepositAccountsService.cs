using EnigmaBudget.Application.Model;
using EnigmaBudget.Infrastructure.Auth;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;

namespace EnigmaBudget.Application.Services
{
    public class DepositAccountsService : IDepositAccountsService
    {
        private readonly IDepositAccountRepository _depositAccountRepository;
        private readonly IAuthService _authService;
        public DepositAccountsService(IDepositAccountRepository depositAccountRepository, IAuthService authService)
        {
            _depositAccountRepository = depositAccountRepository;
            _authService = authService;
        }
        public async Task<AppResult<DepositAccount>> CreateDepositAccount(CreateDepositAccountRequest request)
        {
            var result = new AppResult<DepositAccount>();


            DepositAccount newAccount = new DepositAccount()
            {
                Currency = request.Currency,
                Country = request.Country,
                OwnerId = _authService.GetProfile().Data.Id,
                Description = request.Description,
                Funds = request.InitialFunds,
                Type = request.Type
            };

            _depositAccountRepository.Create(newAccount);
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
