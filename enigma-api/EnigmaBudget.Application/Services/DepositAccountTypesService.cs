using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;

namespace EnigmaBudget.Application.Services
{
    public class DepositAccountTypesService : IDepositAccounTypesService
    {
        private readonly IDepositAccountTypeRepository _depositAccountTypesRepository;

        public DepositAccountTypesService(IDepositAccountTypeRepository depositAccountTypesRepository)
        {
            _depositAccountTypesRepository = depositAccountTypesRepository;
        }

        public Task<AppResult<DepositAccountType>> GetDepositAccountType(string uid)
        {
            throw new NotImplementedException();
        }

        public Task<AppResult<DepositAccountType>> GetDepositAccountTypeByEnumName(string enumName)
        {
            throw new NotImplementedException();
        }

        public async Task<AppResult<List<DepositAccountType>>> ListDepositAccounTypes()
        {
            var depositAccountTypes = await _depositAccountTypesRepository.ListAll().ToListAsync();

            return new AppResult<List<DepositAccountType>>(depositAccountTypes);
        }
    }
}
