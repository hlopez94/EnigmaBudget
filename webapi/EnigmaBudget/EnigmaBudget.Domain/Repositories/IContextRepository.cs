namespace EnigmaBudget.Domain.Repositories
{
    public interface IContextRepository
    {
        public long? GetLoggedUserID();
        public string GetLoggedUserUUID();
        public bool UserHasRole(string roleName);
        
    }
}
