using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;

namespace EnigmaBudget.Application
{
    public interface ICountriesService
    {
        AppResult<Currency> CountryCurrency(string alpha3);
        AppResult<Currency> GetCurrencyById(string num);
        AppResult<Country> GetCountryById(string alpha3);
        AppResult<List<Country>> ListCountries();
        AppResult<List<Currency>> ListCurrencies();
    }
}
