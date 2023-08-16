using EnigmaBudget.Infrastructure.Pager;

namespace EnigmaBudget.WebApi.Controllers
{
    public class AccountTransactionsRequest : PagedRequest
    {
        public string AccountId { get; set; }
    }
}