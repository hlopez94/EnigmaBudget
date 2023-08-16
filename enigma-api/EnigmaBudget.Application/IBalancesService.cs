using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;

namespace EnigmaBudget.Application
{
    public interface IBalancesService
    {
        Task<AppResult<List<Balance>>> ObtenerBalances();
    }
}
