using AutoMapper;
using Microsoft.VisualBasic;
using MySqlConnector;
using System.Data.Common;
using System.Diagnostics.Metrics;

namespace EnigmaBudget.Persistence.MariaDB
{
    public class BaseRepository
    {
        private readonly IMapper _mapper;
        private readonly MySqlConnection _connection;

        public BaseRepository(IMapper mapper, MySqlConnection connection)
        {
            _mapper = mapper;
            _connection = connection;
        }

        private VModel Map<TEntity, VModel>(TEntity entity)
        {
            return _mapper.Map<TEntity, VModel>(entity);
        }
        private VModel Map<TEntity, VModel>(MySqlDataReader reader)
        {
            TEntity entity = _mapper.Map<DbDataReader, TEntity>(reader);
            return _mapper.Map<TEntity, VModel>(entity);
        }

        protected async Task<long> ExecuteNonQuery(string sql, MySqlParameter[] parameters, long? insertedId)
        {
            _connection.Open();

            using (MySqlTransaction trx = _connection.BeginTransaction())
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, trx))
            {
                cmd.Parameters.AddRange(parameters);
                try
                {

                    var nonQueryResult = await cmd.ExecuteNonQueryAsync();
                    trx.Commit();
                    _connection.Close();

                    if(insertedId.HasValue)
                        insertedId = cmd.LastInsertedId;

                    return nonQueryResult;
                }
                catch (MySqlException)
                {
                    trx.Rollback();
                    throw;
                }
            }
        }
        protected async Task<long> ExecuteNonQuery(string sql, MySqlParameter[] parameters)
        {
            await _connection.OpenAsync();

            using (MySqlTransaction trx = await _connection.BeginTransactionAsync())
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, trx))
            {
                cmd.Parameters.AddRange(parameters);
                try
                {

                    var nonQueryResult = await cmd.ExecuteNonQueryAsync();
                    trx.CommitAsync();

                    await _connection.CloseAsync();

                    return nonQueryResult;
                }
                catch (MySqlException)
                {
                    trx.Rollback();
                    throw;
                }
            }
        }

        protected async IAsyncEnumerable<VModel> ExecuteQuery<TEntity, VModel>(string sql)
        {

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();

                using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        yield return Map<TEntity, VModel>(reader);
                    }
                }

                _connection.Close();
            }
        }

        protected async Task<T> ExecuteScalar<T>(string sql, MySqlParameter[] parameters)
        {

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                cmd.Parameters.AddRange(parameters);

                await _connection.OpenAsync();
                T result = (T)await cmd.ExecuteScalarAsync();
                await _connection.CloseAsync();

                return result;
            }
        }
        protected async Task<VModel> ExecuteScalar<TEntity, VModel>(string sql, MySqlParameter[] parameters)
        {

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                cmd.Parameters.AddRange(parameters);

                await _connection.OpenAsync();
                TEntity result = (TEntity)await cmd.ExecuteScalarAsync();
                await _connection.CloseAsync();

                return Map<TEntity, VModel>(result);
            }
        }
        protected async IAsyncEnumerable<VModel> ExecuteQuery<TEntity, VModel>(string sql, MySqlParameter[] parameters)
        {
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();
                cmd.Parameters.AddRange(parameters);

                using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        yield return Map<TEntity, VModel>(reader);
                    }
                }

                _connection.Close();
            }
        }
    }
}
