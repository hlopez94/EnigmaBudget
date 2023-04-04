using AutoMapper;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Domain.Model;
using EnigmaBudget.Domain.Repositories;
using EnigmaBudget.Persistence.MariaDB.Entities;
using MySqlConnector;

namespace EnigmaBudget.Persistence.MariaDB
{
    public class DepositAccountsRepository : BaseRepository, IDepositAccountRepository
    {
        private readonly IContextRepository _contextRepository;

        public DepositAccountsRepository(
            IMapper mapper,
            MySqlConnection connection,
            IContextRepository contextRepository
            ) : base(mapper, connection)
        {
            _contextRepository=contextRepository;
        }
        public async Task<DepositAccount> Create(DepositAccount entity)
        {
            var sql = @"INSERT INTO deposit_accounts (dea_usu_id,dea_tda_id,dea_description,dea_funds,dea_country_code,dea_currency_code,dea_fecha_alta,dea_fecha_modif ) 
                        VALUES (@userId, @depAccountTypeId, @description, @funds, @countryCode, @currencyCode,@fechaAlta,@fechaModif)"
            ;
            var parameters = new List<MySqlParameter>
            {
                new MySqlParameter("userId", _contextRepository.GetLoggedUserID()),
                new MySqlParameter("depAccountTypeId", entity.Type.Id),
                new MySqlParameter("description", entity.Description ),
                new MySqlParameter("funds", entity.Funds),
                new MySqlParameter("countryCode", entity.Country.Alpha3),
                new MySqlParameter("currencyCode", entity.Currency.Num),
                new MySqlParameter("fechaAlta", DateTime.Now),
                new MySqlParameter("fechaModif", DateTime.Now)
            };

            long newId=0;
            var result = await ExecuteNonQuery(sql, parameters.ToArray(), newId);

            if (result > 0)
                entity.Id = EncodeDecodeHelper.Encrypt(newId.ToString());

            return entity;
        }

        public async Task<bool> Delete(string id)
        {
            var sql = @"UPDATE deposit_accounts 
                          SET dea_fecha_baja = SYSDATE 
                        WHERE dea_id = @deaId";

            var parameters = new List<MySqlParameter>
            {
                new MySqlParameter("deaId", EncodeDecodeHelper.Decrypt(id.ToString())),
            };

            var result = await ExecuteNonQuery(sql,parameters.ToArray());

            return result == 1;
        }

        public async Task<DepositAccount> GetById(string id)
        {
            var sql = "SELECT * FROM deposit_accounts where dea_id = @deaId";

            var parameters = new List<MySqlParameter>
            {
                new MySqlParameter("deaId", long.Parse(EncodeDecodeHelper.Decrypt(id)))
            };

            return await ExecuteScalar<deposit_account, DepositAccount>(sql, parameters.ToArray());
            
        }

        public IAsyncEnumerable<DepositAccount> ListAll()
        {
            var sql = "SELECT * FROM deposit_accounts";

            return ExecuteQuery<deposit_account, DepositAccount>(sql);
        }

        public IAsyncEnumerable<DepositAccount> ListUserDepositAccounts()
        {
            var userId = _contextRepository.GetLoggedUserID();
            var sql = "SELECT * FROM deposit_accounts WHERE DEA_USU_ID = @userID";
            var parameters = new List<MySqlParameter>
            {
                new MySqlParameter("userID", _contextRepository.GetLoggedUserID())
            };

            return ExecuteQuery<deposit_account, DepositAccount>(sql, parameters.ToArray());
        }

        public async Task<bool> Update(DepositAccount entity)
        {
            var sql = @"UPDATE deposit_accounts 
                           SET
                               dea_tda_id = @depAccountTypeId,
                               dea_description = @description,
                               dea_funds = @funds,
                         WHERE dea_id = @deaId;";
           ;
            var parameters = new List<MySqlParameter>
            {
                new MySqlParameter("depAccountTypeId", entity.Type.Id),
                new MySqlParameter("description", entity.Description ),
                new MySqlParameter("funds", entity.Funds),
                new MySqlParameter("deaId", long.Parse(EncodeDecodeHelper.Decrypt(entity.Id)))
            };

            var result = await ExecuteNonQuery(sql, parameters.ToArray());

            return result==1;
        }
    }
}