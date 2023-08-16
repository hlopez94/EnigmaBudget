using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Infrastructure.Pager;
using EnigmaBudget.WebApi.Controllers;

namespace EnigmaBudget.Application.Model
{
    public class TransactionsService : ITransactionsService
    {
        private readonly ITransactionsRepository _transactionsRepository;
        private readonly ITransactionsRepository _depositAccountRepository;
        public TransactionsService(ITransactionsRepository transactionsRepository, ITransactionsRepository depositAccountRepository)
        {
            _transactionsRepository = transactionsRepository;
            _depositAccountRepository = depositAccountRepository;
        }
        public async Task<AppResult<DepositAccountTransaction>> DepositOnAccount(DepositOnAccountRequest request)
        {
            var result = new AppResult<DepositAccountTransaction>();
            var acc = _depositAccountRepository.GetById(request.AccountId);

            if(acc is null)
            {
                result.AddBusinessError("Cuenta depósito inexistente.");
                return result;
            }

            //TransactionType type = await _transactionTypesRepository.GetByEnumName(TransactionTypeEnum.DEPOSIT);
            //
            //if(type is null)
            //{
            //    result.AddBusinessError("No existe el tipo de transacción indicado.");
            //    return result;
            //}

            var newTransaction = await _transactionsRepository.Create(new DepositAccountTransaction()
            {
                Ammount = request.Ammount,
                Date = request.Date,
                Details = request.Description,
                //    Type = type
            }
            );

            result.Data = newTransaction;
            return result;

        }

        public async Task<AppResult<PagedResponse<DepositAccountTransaction>>> GetAccountTransactions(AccountTransactionsRequest request)
        {
            var result = new AppResult<PagedResponse<DepositAccountTransaction>>();
            var acc = _depositAccountRepository.GetById(request.AccountId);

            if(acc is null)
            {
                result.AddBusinessError("Cuenta depósito inexistente.");
                return result;
            }

            //  var newTransaction = await _transactionsRepository.GetPaginadoPorCuenta(request.AccountId, request);
            //  result.Data = newTransaction;
            return result;
        }

        public Task<AppResult<PagedResponse<DepositAccountTransaction>>> GetUserTransactions(PagedRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<AppResult<DepositAccountTransaction>> WithdrawOnAccount(WithdrawRequest request)
        {
            throw new NotImplementedException();
        }
    }
}
