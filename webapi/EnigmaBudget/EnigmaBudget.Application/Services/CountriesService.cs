using EnigmaBudget.Application.Helpers;
using EnigmaBudget.Application.Model;
using EnigmaBudget.Domain.Model;
using ISO._3166;
using ISO._4217;
using Microsoft.IdentityModel.Tokens;

namespace EnigmaBudget.Application.Services
{
    public class CountriesService : ICountriesService
    {
        public AppResult<Currency> CountryCurrency(string alpha3)
        {
            AppResult<Currency> result = new AppResult<Currency>();
            var country = CountryCodesResolver.GetByAlpha3Code(alpha3).MapToCountry();

            if (country is null)
                result.AddNotFoundError("País no encontrado");

            var currency = CurrencyCodesResolver.Codes.FirstOrDefault(cur => cur.Num == country.NumericCode).MapToCurrency();
            if (currency is null)
                result.AddNotFoundError("El país seleccionado no posee moneda internacional definida.");
            else
                result.Data = currency;
            return result;
        }

        public AppResult<Country> GetCountryById(string alpha3)
        {
            AppResult<Country> result = new AppResult<Country>();
            var country = CountryCodesResolver.GetByAlpha3Code(alpha3).MapToCountry();

            if (country is null)
                result.AddNotFoundError("País no encontrado");
            else
                result.Data = country;

            return result;
        }
        public AppResult<Country> GetCurrency(string alpha3)
        {
            AppResult<Country> result = new AppResult<Country>();
            var country = CountryCodesResolver.GetByAlpha3Code(alpha3).MapToCountry();

            if (country is null)
                result.AddNotFoundError("País no encontrado");
            else
                result.Data = country;

            return result;
        }

        public AppResult<Currency> GetCurrencyById(string num)
        {
            AppResult<Currency> result = new AppResult<Currency>();
            var currency = CurrencyCodesResolver.Codes.FirstOrDefault(cur => cur.Num==num).MapToCurrency();

            if (currency is null)
                result.AddNotFoundError("País no encontrado");
            else
                result.Data = currency;

            return result;
        }

        public AppResult<List<Country>> ListCountries()
        {
            AppResult<List<Country>> result = new AppResult<List<Country>>();

            result.Data = CountryCodesResolver.GetList().Select(code => code.MapToCountry()).ToList();

            return result;
        }

        public AppResult<List<Currency>> ListCurrencies()
        {
            AppResult<List<Currency>> result = new AppResult<List<Currency>>();

            result.Data = CurrencyCodesResolver.Codes.DistinctBy(curr => curr.Num).Select(curr=>curr.MapToCurrency()).ToList();

            return result;
        }
    }
}
