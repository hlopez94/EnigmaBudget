using AutoMapper;
using EnigmaBudget.Infrastructure.Auth.Entities;
using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;
using EnigmaBudget.Model.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EnigmaBudget.Infrastructure.Auth
{
    public class AuthServiceOptions
    {
        public string Issuer { get; set; }
        public string Audience { get; set; }
        public string Subject { get; set; }
        public string Key { get; set; }

        public AuthServiceOptions(string issuer, string audience, string subject, string key)
        {
            Issuer = issuer;
            Audience = audience;
            Subject = subject;
            Key = key;
        }
    }

    public class AuthService : IAuthService
    {
        MySqlConnection _connection;
        AuthServiceOptions _configuration;
        IHttpContextAccessor _httpContextAccessor;
        IMapper _mapper;

        public AuthService(IHttpContextAccessor httpContextAccessor,
            IMapper mapper,
                            MySqlConnection connection,
                            AuthServiceOptions options)
        {
            _connection = connection;
            _configuration = options;
            _httpContextAccessor = httpContextAccessor;
            _mapper = mapper;
        }

        public AppServiceResponse<LoginInfo> Login(LoginRequest request)
        {
            AppServiceResponse<LoginInfo> result;

            if (request == null || request.UserName == null)
            {
                return new AppServiceResponse<LoginInfo>("Solicitud Inválida", null);
            }

            string sql = "SELECT * FROM usuarios WHERE usu_usuario = @usuario AND usu_fecha_baja IS NULL";

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {

                cmd.Parameters.Add(new MySqlParameter("usuario", request.UserName));

                _connection.Open();

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        usuarios entity = _mapper.Map<DbDataReader, usuarios>(reader);

                        if (AreEqual(request.Password, entity.usu_password, entity.usu_seed))
                        {
                            result = new AppServiceResponse<LoginInfo>(
                                new LoginInfo()
                                {
                                    Email = entity.usu_correo,
                                    UserName = entity.usu_usuario,
                                    JWT = GenerateJWT(entity)
                                });
                        }
                        else
                        {
                            result = new AppServiceResponse<LoginInfo>("Credenciales inválidas", null);
                        }
                    }
                    else
                    {
                        result = new AppServiceResponse<LoginInfo>("No se encontró usuario con las credenciales brindadas", null);

                    }
                }
                _connection.Close();
            }

            return result;
        }

        public AppServiceResponse<SignUpInfo> SignUp(SignUpRequest signup)
        {
            var seed = CreateSalt();
            var hash = GenerateHash(signup.Password, seed);

            if (!AreEqual(signup.Password, hash, seed))
            {
                throw new CryptographicException("Error al generar hash de password, no coinciden.");
            }

            var sql = @"INSERT INTO usuarios (usu_usuario, usu_correo, usu_password, usu_seed) 
                        VALUES (@usuario, @correo, @password, @seed)";

            AppServiceResponse<SignUpInfo> result;

            _connection.Open();

            using (MySqlTransaction trx = _connection.BeginTransaction())
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, trx))
            {
                cmd.Parameters.AddWithValue("usuario", signup.UserName);
                cmd.Parameters.AddWithValue("correo", signup.Email);
                cmd.Parameters.AddWithValue("password", hash);
                cmd.Parameters.AddWithValue("seed", seed);

                try
                {
                    cmd.ExecuteNonQuery();
                    trx.Commit();
                    result = new AppServiceResponse<SignUpInfo>(new SignUpInfo() { Email = signup.Email, UserName = signup.UserName });
                }
                catch (MySqlException e)
                {


                    if (e.Message.Contains("UNI_usuarios_usu_correo"))
                    {
                        result = new AppServiceResponse<SignUpInfo>("Ya existe una cuenta con el correo indicado.", null);
                    }
                    else if (e.Message.Contains("UNI_usuarios_usu_usuario"))
                    {
                        result = new AppServiceResponse<SignUpInfo>("Ya existe una cuenta con nombre de usuario indicado.", null);
                    }
                    else
                    {
                        throw;
                    }
                    trx.Rollback();
                }
                finally
                {
                    _connection.Close();
                }
            }

            return result;

        }


        private string GenerateJWT(usuarios entity)
        {
            var claims = new[] {
                        new Claim(JwtRegisteredClaimNames.Sub, _configuration.Subject),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                        new Claim(JwtRegisteredClaimNames.Iat, DateTime.UtcNow.ToString()),
                        new Claim("user", entity.usu_usuario),
                        new Claim("email", entity.usu_correo),
                        new Claim("id", new Guid(entity.usu_id).ToString())
                    };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.Key));
            var signIn = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                _configuration.Issuer,
                _configuration.Audience,
                claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: signIn);

            return new JwtSecurityTokenHandler().WriteToken(token);

        }

        private string CreateSalt()
        {
            //Generate a cryptographic random number.
            RandomNumberGenerator rng = RandomNumberGenerator.Create();
            byte[] buff = new byte[16];
            rng.GetBytes(buff);
            return Convert.ToBase64String(buff);
        }

        private string GenerateHash(string input, string salt)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input + salt);

            SHA256.Create();
            SHA256 sHA256ManagedString = SHA256.Create();
            byte[] hash = sHA256ManagedString.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private bool AreEqual(string plainTextInput, string hashedInput, string salt)
        {
            string newHashedPin = GenerateHash(plainTextInput, salt);
            return newHashedPin.Equals(hashedInput);
        }

        public AppServiceResponse<Perfil> GetProfile()
        {

            var usu = GetUserById(GetAuthenticatedId());
            Perfil res = new Perfil();
            return new AppServiceResponse<Perfil>(res);
        }

        public AppServiceResponse<bool> UpdateProfile(Perfil perfil)
        {
            Guid loggedUser = GetAuthenticatedId();
            if(loggedUser == Guid.Empty)
            {
                throw new UnauthorizedAccessException();
            }

            throw new NotImplementedException();
        }

        private Guid GetAuthenticatedId()
        {
            var user = _httpContextAccessor.HttpContext.User;
            ClaimsIdentity identity = (ClaimsIdentity)user.Identity!;
            return Guid.Parse(identity!.Claims.First(c => c.Type == "id").Value);
        }

        private usuarios GetUserById(Guid id)
        {
            var sql = "SELECT * FROM usuarios WHERE usu_id = @id AND usu_fecha_baja IS NULL";

            usuarios usuario = null;
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();

                cmd.Parameters.Add(new MySqlParameter("id", (id.ToByteArray())));

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        usuario = _mapper.Map<DbDataReader, usuarios>(reader);
                    }
                }

                _connection.Close();
            }

            return usuario;
        }

        public AppServiceResponse<bool> ChangePassword(ChangePasswordRequest request)
        {
            Guid loggedInGuid = GetAuthenticatedId();
            usuarios loggedInInfo = GetUserById(loggedInGuid);

            if (request.OldPassword.Equals(request.NewPassword))
            {
                return new AppServiceResponse<bool>("La nueva contraseña no puede ser igual a la anterior", null);

            }

            if (!AreEqual(request.OldPassword, loggedInInfo.usu_password, loggedInInfo.usu_seed))
            {
                return new AppServiceResponse<bool>("La contraseña a cambiar no coincide", null);
            }

            var sql = "UPDATE usuarios SET usu_password = @pass, usu_seed = @seed WHERE usu_id = @id";

            string salt = CreateSalt();
            string hashedPass = GenerateHash(request.NewPassword, salt);


            _connection.Open();
            var transaction = _connection.BeginTransaction();
            AppServiceResponse<bool> response;
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, transaction))
            {
                cmd.Parameters.Add(new MySqlParameter("pass", hashedPass));
                cmd.Parameters.Add(new MySqlParameter("seed", salt));
                cmd.Parameters.Add(new MySqlParameter("id", loggedInGuid.ToByteArray()));

                try
                {
                    int rows = cmd.ExecuteNonQuery();
                    if (rows == 1)
                    {
                        transaction.Commit();
                        response = new AppServiceResponse<bool>(true);
                    }
                    else
                    {
                        transaction.Rollback();
                        response = new AppServiceResponse<bool>("Error al cambiar la contraseña.", null);
                    }

                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
                finally
                {
                    _connection.Close();
                }
                return response;

            }
        }
    }
}
