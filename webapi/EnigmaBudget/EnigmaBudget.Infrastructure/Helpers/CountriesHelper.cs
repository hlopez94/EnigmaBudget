using ISO._3166;
using EnigmaBudget.Model.Model;

namespace EnigmaBudget.Infrastructure.Helpers
{
    public static class CountriesHelper
    {
        public static Country GetCountryByCode(string alpha3Code)
        {
            return CountryCodesResolver.GetByAlpha3Code(alpha3Code).MapToModel();
        }
        public static IEnumerable<Country> ListCountries()
        {

            foreach (var country in CountryCodesResolver.GetList())
            {
                yield return country.MapToModel();
            }
        }

        private static Country MapToModel(this ISO._3166.Models.CountryCode code)
        {
            return new Country()
            {
                Alpha2 = code.Alpha2,
                Alpha3 = code.Alpha3,
                Name = code.Name,
                NumericCode = code.NumericCode
            };
        }

    }
}
