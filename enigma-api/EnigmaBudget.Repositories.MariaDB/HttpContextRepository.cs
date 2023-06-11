using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Infrastructure.Helpers;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace EnigmaBudget.Persistence.Repositories.MariaDB
{
    public class HttpContextRepository : IContextRepository
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public HttpContextRepository(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public long? GetLoggedUserID()
        {

            var user = _httpContextAccessor.HttpContext.User;
            ClaimsIdentity identity = (ClaimsIdentity)user.Identity!;
            return long.Parse(EncodeDecodeHelper.Decrypt(identity!.Claims.First(c => c.Type == "id").Value));
        }

        public string GetLoggedUserUUID()
        {
            var user = _httpContextAccessor.HttpContext.User;
            ClaimsIdentity identity = (ClaimsIdentity)user.Identity!;
            return identity!.Claims.First(c => c.Type == "id").Value;
        }

        public bool UserHasRole(string roleName)
        {
            return _httpContextAccessor.HttpContext.User.IsInRole(roleName);
        }
    }
}
