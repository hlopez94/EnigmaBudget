using EnigmaBudget.Infrastructure.Auth.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;
using System.Data;
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

        public AuthService(IHttpContextAccessor httpContextAccessor,
                            MySqlConnection connection,
                            AuthServiceOptions options)
        {
            _connection = connection;
            _configuration = options;
            _httpContextAccessor = httpContextAccessor;
        }

        public LoginResponse Login(LoginRequest request)
        {
            LoginResponse result = new LoginResponse();

            if (request == null || (request.Email == null && request.UserName == null))
                return result;

            string sql = String.Empty;
            if (request.Email != null)
            {
                sql = "SELECT * FROM usuarios WHERE usu_correo = @email AND usu_fecha_baja IS NULL";
            }
            else
                sql = "SELECT * FROM usuarios WHERE usu_usuario = @usuario AND usu_fecha_baja IS NULL";


            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                if (request.Email != null)
                    cmd.Parameters.Add(new MySqlParameter("email", request.Email));
                else
                    cmd.Parameters.Add(new MySqlParameter("usuario", request.UserName));

                _connection.Open();

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        UsuariosTable entity = ToEntity(reader);
                        if (AreEqual(request.Password, entity.Password, entity.Salt))
                        {
                            result.LoggedIn = true;
                            result.Email = entity.Correo;
                            result.UserName = entity.UserName;

                            result.JWT = GenerateJWT(entity);

                        }
                    }
                }
                _connection.Close();
            }

            return result;
        }

        public SignUpResponse SignUp(SignUpRequest signup)
        {
            var seed = CreateSalt();
            var hash = GenerateHash(signup.Password, seed);

            if (!AreEqual(signup.Password, hash, seed))
            {
                throw new CryptographicException("Error al generar hash de password, no coinciden.");
            }

            var sql = @"INSERT INTO usuarios (usu_usuario, usu_correo, usu_password, usu_seed) 
                        VALUES (@usuario, @correo, @password, @seed)";

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("usuario", signup.UserName);
                cmd.Parameters.AddWithValue("correo", signup.Email);
                cmd.Parameters.AddWithValue("password", hash);
                cmd.Parameters.AddWithValue("seed", seed);
                _connection.Open();
                cmd.ExecuteNonQuery();
                _connection.Close();
            }

            return new SignUpResponse()
            {
                Email = signup.Email,
                SignedUp = true,
                UserName = signup.UserName
            };

        }


        private string GenerateJWT(UsuariosTable entity)
        {
            var claims = new[] {
                        new Claim(JwtRegisteredClaimNames.Sub, _configuration.Subject),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                        new Claim(JwtRegisteredClaimNames.Iat, DateTime.UtcNow.ToString()),
                        new Claim("user", entity.UserName),
                        new Claim("email", entity.Correo),
                        new Claim("id", entity.Id.ToString())
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

        private UsuariosTable ToEntity(DbDataReader reader)
        {

            return new UsuariosTable()
            {
                Correo = reader.GetString("usu_correo"),
                FechaAlta = reader.GetDateTime("usu_fecha_alta"),
                FechaModificacion = reader.GetDateTime("usu_fecha_modif"),
                FechaBaja = reader.IsDBNull("usu_fecha_baja") ? null : reader.GetDateTime("usu_fecha_baja"),
                Id = reader.GetGuid("usu_id"),
                Password = reader.GetString("usu_password"),
                Salt = reader.GetString("usu_seed"),
                UserName = reader.GetString("usu_usuario")
            };
        }

        public ProfileResponse GetProfile()
        {

            var usu = GetUserById(GetAuthenticatedId());

            return new ProfileResponse()
            {
                Email = usu.Correo,
                FechaAlta = usu.FechaAlta,
                FechaModificacion = usu.FechaModificacion,
                UserName = usu.UserName,
                UUID = usu.Id.ToString()
            };
        }

        private Guid GetAuthenticatedId()
        {
            var user = _httpContextAccessor.HttpContext.User;
            ClaimsIdentity identity = (ClaimsIdentity)user.Identity!;
            return Guid.Parse(identity!.Claims.First(c => c.Type == "id").Value);
        }

        private UsuariosTable GetUserById(Guid id)
        {
            var sql = "SELECT * FROM usuarios WHERE usu_id = @id AND usu_fecha_baja IS NULL";

            UsuariosTable usuario = null;
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();

                cmd.Parameters.Add(new MySqlParameter("id", (id.ToByteArray())));

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        usuario = ToEntity(reader);
                    }
                }

                _connection.Close();
            }

            return usuario;
        }

        public ChangePasswordResponse ChangePassword(ChangePasswordRequest request)
        {
            Guid loggedInGuid = GetAuthenticatedId();
            UsuariosTable loggedInInfo = GetUserById(loggedInGuid);

            if (request.OldPassword.Equals(request.NewPassword))
            {
                return new ChangePasswordResponse()
                {
                    IsPasswordChanged = false,
                    Reason = "La nueva contraseña no puede ser igual a la anterior"
                };

            }

            if (!AreEqual(request.OldPassword, loggedInInfo.Password, loggedInInfo.Salt))
            {
                return new ChangePasswordResponse()
                {
                    IsPasswordChanged = false,
                    Reason = "La contraseña a cambiar no coincide"
                };
            }

            var sql = "UPDATE usuarios SET usu_password = @pass, usu_seed = @seed WHERE usu_id = @id";

            string salt = CreateSalt();
            string hashedPass = GenerateHash(request.NewPassword, salt);

            var response = new ChangePasswordResponse()
            {
                IsPasswordChanged = false
            };

            _connection.Open();
            var transaction = _connection.BeginTransaction();
            using (MySqlCommand cmd = new MySqlCommand(sql,_connection, transaction))
            {
                cmd.Parameters.Add(new MySqlParameter("pass", hashedPass));
                cmd.Parameters.Add(new MySqlParameter("seed", salt));
                cmd.Parameters.Add(new MySqlParameter("id", loggedInGuid.ToByteArray()));

                int rows = cmd.ExecuteNonQuery();

                if (rows == 1)
                {
                    response.IsPasswordChanged = true;
                    transaction.Commit();
                }
                else
                {
                    transaction.Rollback();
                    response.Reason = "Error al cambiar la contraseña.";
                }


                _connection.Close();
            }

            return response;
        }
    }


    internal class UsuariosTable
    {
        public Guid Id { get; set; }
        public string UserName { get; set; }
        public string Correo { get; set; }
        public DateTime FechaAlta { get; set; }
        public DateTime FechaModificacion { get; set; }
        public DateTime? FechaBaja { get; set; }
        public string Password { get; set; }
        public string Salt { get; set; }
    }
}
