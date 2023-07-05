using AutoMapper;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnigmaBudget.Persistence.Repositories.EFCore
{
    public class TransactionsRepository : BaseEnigmaEFRepository, ITransactionsRepository
    {
        private readonly IContextRepository _contextRepository;
        public TransactionsRepository(
            EnigmaContext dbContext,
            IMapper mapper,
            IContextRepository contextRepository
            ) : base(dbContext, mapper)
        {
            _contextRepository = contextRepository;
        }
        public async Task<DepositAccountTransaction> Create(DepositAccountTransaction model)
        {
            DepositAccountTransactionEntity newTransaction;
            using(var trx = _context.Database.BeginTransaction())
            {
                var addTrx = _context.Transactions.Add(new DepositAccountTransactionEntity()
                {
                    DatUsuId = _contextRepository.GetLoggedUserID()!.Value,
                    DatFechaAlta = DateTime.Now,
                    DatFechaModif = DateTime.Now,
                    DatFechaBaja = null,
                    DatDescription = model.Details,
                    DatCurrencyCode = "",
                    //DatName = "",
                    DatAmmount = model.Ammount,
                    DatDeaId=1
                });
                await _context.SaveChangesAsync();
                newTransaction = addTrx.Entity;
                await trx.CommitAsync();
            }

            return _mapper.Map<DepositAccountTransactionEntity, DepositAccountTransaction>(newTransaction);
        }

        public Task<bool> Delete(string id)
        {
            throw new NotImplementedException();
        }

        public Task<DepositAccountTransaction> GetById(string id)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<DepositAccountTransaction> ListAll()
        {
            throw new NotImplementedException();
        }

        public Task<bool> Update(DepositAccountTransaction model)
        {
            throw new NotImplementedException();
        }
    }
}
