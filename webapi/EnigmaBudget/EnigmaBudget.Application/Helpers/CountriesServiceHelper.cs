using EnigmaBudget.Domain.Model;
using ISO._3166;

namespace EnigmaBudget.Application.Helpers
{
    internal static class CountriesServiceHelper
    {
        internal static Country? MapToCountry(this ISO._3166.Models.CountryCode code)
        {
            if (code is null)
                return null;

            return new Country()
            {
                Alpha2 = code.Alpha2.ToUpperInvariant(),
                Alpha3 = code.Alpha3.ToUpperInvariant(),
                Name = code.Name,
                NumericCode = code.NumericCode
            };
        }
        internal static Currency MapToCurrency(this ISO._4217.Models.Currency currency)
        {
            return new Currency()
            {
                Code = currency.Code,
                Country = CountryCodesResolver.GetList().FirstOrDefault(c=>currency.Country.ToUpperInvariant() ==c.Name.ToUpperInvariant()).MapToCountry(),
                Exponent = currency.Exponent,
                Name = currency.Name,
                Num = currency.Num
            };
        }
    }
}
