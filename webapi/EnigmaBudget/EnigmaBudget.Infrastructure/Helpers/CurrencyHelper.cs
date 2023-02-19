using EnigmaBudget.Model.Model;
using ISO._4217;

namespace EnigmaBudget.Infrastructure.Helpers
{
    public static class CurrencyHelper
    {
        public static Currency GetCurrencyByCode(string num)
        {
            return CurrencyCodesResolver.Codes.Where(cur => cur.Num == num).First().MapISOCurrency();
        }
        public static Currency GetCurrencyByCountry(Country country)
        {
            return CurrencyCodesResolver.Codes.Where(cur => cur.Num == country.NumericCode).First().MapISOCurrency();
        }
        public static IEnumerable<Currency> ListCurrencies()
        {

            foreach (var currency in CurrencyCodesResolver.Codes)
            {
                yield return currency.MapISOCurrency();
            }
        }

        private static Currency MapISOCurrency(this ISO._4217.Models.Currency currency)
        {
            return new Currency()
            {
                Code = currency.Code,
                Country = currency.Country,
                Exponent = currency.Exponent,
                Name = currency.Name,
                Num = currency.Num
            };
        }
    }
}
