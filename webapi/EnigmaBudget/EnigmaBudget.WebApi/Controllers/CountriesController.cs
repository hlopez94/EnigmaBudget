using EnigmaBudget.Application;
using EnigmaBudget.Application.Model;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Domain.Model;
using Microsoft.AspNetCore.Mvc;

namespace EnigmaBudget.WebApi.Controllers
{
    [Route("api")]
    public class CountriesController : ControllerBase
    {
        private readonly ICountriesService _countriesService;
        public CountriesController(ICountriesService countriesService)
        {
            _countriesService= countriesService;
        }

        [HttpGet("countries")]
        public AppResult<List<Country>> GetCountries()
        {
            return _countriesService.ListCountries();
        }

        [HttpGet("countries/{alpha3}")]
        public AppResult<Country> GetCountryById(string alpha3)
        {
            return _countriesService.GetCountryById(alpha3);
        }

        [HttpGet("countries/{alpha3}/currency")]
        public AppResult<Currency> GetCountryCurrency(string alpha3)
        {
            return _countriesService.CountryCurrency(alpha3);

        }
        [HttpGet("currencies")]
        public AppResult<List<Currency>> Getcurrencies()
        {
            return _countriesService.ListCurrencies();
        }
    }
}
