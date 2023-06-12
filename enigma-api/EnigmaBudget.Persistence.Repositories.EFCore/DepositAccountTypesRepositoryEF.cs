using AutoMapper;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Persistence.Repositories.EFCore
{
    public class DepositAccountTypesRepositoryEF : BaseEnigmaEFRepository, IDepositAccountTypeRepository
    {
        private readonly IContextRepository _contextRepository;
        public DepositAccountTypesRepositoryEF(
            EnigmaContext dbContext,
            IMapper mapper,
            IContextRepository contextRepository
            ) : base(dbContext, mapper)
        {
            _contextRepository = contextRepository;
        }

        public Task<DepositAccountType> Create(DepositAccountType model)
        {
            throw new NotImplementedException();
        }

        public Task<bool> Delete(string id)
        {
            throw new NotImplementedException();
        }

        public Task<DepositAccountType> GetById(string id)
        {
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<DepositAccountType> ListAll()
        {
            var query = _context.TypesDepositAccounts.Where(da => da.TdaFechaAlta <= DateOnly.FromDateTime(DateTime.Now) && da.TdaFechaBaja > DateOnly.FromDateTime(DateTime.Now)).ToListAsync();

            foreach (var entity in await query)
            {
                yield return _mapper.Map<TypesDepositAccountEntity, DepositAccountType>(entity);
            }
        }

        public Task<bool> Update(DepositAccountType model)
        {
            throw new NotImplementedException();
        }
    }
}