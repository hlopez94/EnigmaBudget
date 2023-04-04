using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
