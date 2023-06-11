using AutoMapper;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnigmaBudget.Persistence.Repositories.EFCore
{
    public class DepositAccountsRepositoryEF : BaseEnigmaEFRepository, IDepositAccountRepository
    {
        private readonly IContextRepository _contextRepository;
        public DepositAccountsRepositoryEF(
            EnigmaContext dbContext,
            IMapper mapper,
            IContextRepository contextRepository
            ) : base(dbContext, mapper)
        {
            _contextRepository = contextRepository;
        }

        public async Task<DepositAccount> Create(DepositAccount model)
        {
            DepositAccountEntity newDepositAccount;
            using (var trx = _context.Database.BeginTransaction())
            {
                var addTrx = _context.DepositAccounts.Add(new DepositAccountEntity()
                {
                    DeaUsuId = EncodeDecodeHelper.DecryptLong(model.OwnerId),
                    DeaTdaId = long.Parse(model.Type.Id),
                    DeaFechaAlta = DateTime.Now,
                    DeaFechaModif = DateTime.Now,
                    DeaFechaBaja = null,
                    DeaDescription = model.Description,
                    DeaFunds = model.Funds,
                    DeaCountryCode = model.Country.Alpha3,
                    DeaCurrencyCode = model.Currency.Num,
                });
                _context.SaveChanges();
                newDepositAccount = addTrx.Entity;
                trx.Commit();
            }

            return _mapper.Map<DepositAccountEntity, DepositAccount>(newDepositAccount);
        }

        public async Task<bool> Delete(string id)
        {
            using (var trx = _context.Database.BeginTransaction())
            {
                var daEntity = await _context.DepositAccounts.SingleOrDefaultAsync(da => da.DeaId == EncodeDecodeHelper.DecryptLong(id) && !da.DeaFechaBaja.HasValue);

                if (daEntity == null)
                    return false;

                daEntity.DeaFechaBaja = DateTime.Now;
                _context.SaveChanges();
                trx.Commit();
            }

            return true;
        }

        public async Task<DepositAccount> GetById(string id)
        {
            var query = await _context.DepositAccounts
                                .SingleOrDefaultAsync(da => da.DeaId == EncodeDecodeHelper.DecryptLong(id));

            return _mapper.Map<DepositAccountEntity, DepositAccount>(query);

        }

        public async IAsyncEnumerable<DepositAccount> ListAll()
        {
            var query = await _context.DepositAccounts
                                .Where(da => da.DeaUsuId == _contextRepository.GetLoggedUserID())
                                .AsNoTracking()
                                .ToListAsync()
                                ;
            foreach (var item in query)
            {
                yield return _mapper.Map<DepositAccountEntity, DepositAccount>(item);
            }
        }

        public async IAsyncEnumerable<DepositAccount> ListUserDepositAccounts()
        {
            var query = await _context.DepositAccounts
                                .Where(da => da.DeaUsuId == _contextRepository.GetLoggedUserID())
                                .AsNoTracking()
                                .ToListAsync()
                                ;
            foreach (var item in query)
            {
                yield return _mapper.Map<DepositAccountEntity, DepositAccount>(item);
            }
        }

        public async Task<bool> Update(DepositAccount model)
        {
            using (var trx = await _context.Database.BeginTransactionAsync())
            {
                DepositAccountEntity newDepositAccount = await _context.DepositAccounts.SingleAsync(da => da.DeaId == EncodeDecodeHelper.DecryptLong(model.Id));

                newDepositAccount.DeaTdaId = long.Parse(model.Type.Id);
                newDepositAccount.DeaFechaModif = DateTime.Now;
                newDepositAccount.DeaDescription = model.Description;
                newDepositAccount.DeaCountryCode = model.Country.Alpha3;
                newDepositAccount.DeaCurrencyCode = model.Currency.Num;
                await _context.SaveChangesAsync();
                await trx.CommitAsync();
            }

            return true;
        }
    }
}