using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;

namespace EnigmaBudget.Application
{
    public interface IDepositAccounTypesService
    {
        Task<AppResult<List<DepositAccountType>>> ListDepositAccounTypes();
        Task<AppResult<DepositAccountType>> GetDepositAccountType(string uid);
        Task<AppResult<DepositAccountType>> GetDepositAccountTypeByEnumName(string enumName);
    }
}
