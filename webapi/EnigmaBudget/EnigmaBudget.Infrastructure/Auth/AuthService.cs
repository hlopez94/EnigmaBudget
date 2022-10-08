using AutoMapper;
using EnigmaBudget.Infrastructure.Auth.Entities;
using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Infrastructure.SendInBlue.Model;
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

    public class AuthService : IAuthService
    {
        private readonly MySqlConnection _connection;
        private readonly AuthServiceOptions _configuration;

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMapper _mapper;
        private readonly IEmailApiService _mailSvc;

        public AuthService(IHttpContextAccessor httpContextAccessor,
            IMapper mapper,
            IEmailApiService mailApiSvc,
                            MySqlConnection connection,
                            AuthServiceOptions options)
        {
            _connection = connection;
            _configuration = options;
            _httpContextAccessor = httpContextAccessor;
            _mapper = mapper;
            _mailSvc = mailApiSvc;
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

        public AppServiceResponse<bool> ResendValidationEmail()
        {
            var res = false;

            //usuarios usuario = GetUserById(GetAuthenticatedId());

            //if (usuario != null)
            //{
            //    throw new UnauthorizedAccessException();
            //}

            //var sql = "SELECT * FROM enigma.usuarios_validacion_email WHERE uve_usu_id = @id ";
            //usuarios_validacion_email validacion = null;

            //_connection.Open();
            //using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            //{
            //    cmd.Parameters.AddWithValue("id", usuario.usu_id);

            //    using (var reader = cmd.ExecuteReader())
            //    {
            //        if (reader.Read())
            //        {
            //            validacion = _mapper.Map<DbDataReader, usuarios_validacion_email>(reader);
            //        } 
            //    }
            //}

            //if(validacion is null)
            //    throw new InvalidOperationException("No existe ")

            //EmailValidacionInfo infoValidacion = new EmailValidacionInfo()
            //{
            //    Correo = nuevoCorreo,
            //    Token = token,
            //    UrlApp = _configuration.UiUrl,
            //    UsuarioNombre = usuario.usu_usuario
            //};


            //_mailSvc.EnviarCorreoValidacionCuenta(infoValidacion);
            return new AppServiceResponse<bool>(res);


        }

        public AppServiceResponse<bool> ValidateEmail(string token)
        {
            var sql = "SELECT * FROM enigma.usuarios_validacion_email WHERE @uve_salt = @token";

            _connection.Open();

            usuarios_validacion_email validacion = null;
            using(var cmdSel = new MySqlCommand(sql, _connection))
            {
                cmdSel.Parameters.AddWithValue("token", token);
                using(var reader= cmdSel.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        validacion = _mapper.Map<DbDataReader, usuarios_validacion_email>(reader);
                    }
                }
            }

            if(validacion is null)
            {
                return new AppServiceResponse<bool>("Token inválido",null);
            }

            if (!validacion.valida)
            {
                if(validacion.uve_fecha_baja < DateTime.Now)
                    return new AppServiceResponse<bool>("Token inválido",null);
            }


            return new AppServiceResponse<bool>(true);
        }



        private void CargarYEnviarValidacionEmail(usuarios usuario, string nuevoCorreo)
        {

            var token = CreateSalt();

            var sql = "INSERT INTO enigma.usuarios_validacion_email (uve_usu_id, uve_fecha_alta, uve_fecha_baja, uve_salt, uve_nuevo_correo) VALUES (@id, @alta, @baja, @salt, @correo)";


            _connection.Open();

            using (MySqlTransaction trx = _connection.BeginTransaction())
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, trx))
            {
                cmd.Parameters.AddWithValue("id", usuario.usu_id);
                cmd.Parameters.AddWithValue("alta", DateTime.Now);
                cmd.Parameters.AddWithValue("baja", DateTime.Now.AddDays(1));
                cmd.Parameters.AddWithValue("salt", token);
                cmd.Parameters.AddWithValue("correo", nuevoCorreo);

                try
                {
                    cmd.ExecuteNonQuery();
                    trx.Commit();
                }
                catch (MySqlException e)
                {
                    trx.Rollback();
                    throw;
                }
                finally
                {
                    _connection.Close();
                }
            }

            EmailValidacionInfo infoValidacion = new EmailValidacionInfo()
            {
                Correo = nuevoCorreo,
                Token = token,
                UrlApp = _configuration.UiUrl,
                UsuarioNombre = usuario.usu_usuario
            };


            _mailSvc.EnviarCorreoValidacionCuenta(infoValidacion);
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
                    var prev_id = cmd.LastInsertedId;

                    cmd.ExecuteScalar();
                    trx.Commit();

                    var id = cmd.LastInsertedId;
                    usuarios new_usu = new usuarios()
                    {
                        usu_id = id,
                        usu_correo = signup.Email,
                        usu_usuario = signup.UserName
                    };
                    _connection.Close();
                    CargarYEnviarValidacionEmail(new_usu, signup.Email);

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
                        new Claim(JwtRegisteredClaimNames.NameId, DateTime.UtcNow.ToString()),
                        new Claim("user", entity.usu_usuario),
                        new Claim("email", entity.usu_correo),
                        new Claim("id", EncodeDecodeHelper.Encrypt(entity.usu_id.ToString())),
                        new Claim("verified-account", entity.usu_correo_verificado.ToString().ToLower())
                    };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.Key));
            var signIn = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                _configuration.Issuer,
                _configuration.Audience,
                claims,
                expires: DateTime.UtcNow.AddMinutes(360),
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

            var sql = @"SELECT * 
                        FROM usuario_perfil 
                        WHERE usp_usu_id = @id;";

            Perfil perfil = new Perfil();

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();

                cmd.Parameters.Add(new MySqlParameter("id", (usu.usu_id)));

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        usuario_perfil entity = _mapper.Map<DbDataReader, usuario_perfil>(reader);
                        perfil = _mapper.Map<usuario_perfil, Perfil>(entity);
                    }
                    perfil.Usuario = usu.usu_usuario;
                    perfil.Correo = usu.usu_correo;
                }

                _connection.Close();
            }

            return new AppServiceResponse<Perfil>(perfil);
        }

        public AppServiceResponse<bool> UpdateProfile(Perfil perfil)
        {
            var loggedUser = GetUserById(GetAuthenticatedId());

            if (loggedUser is null)
            {
                throw new UnauthorizedAccessException();
            }

            var sql = @"INSERT 
                            INTO enigma.usuario_perfil
                                  ( usp_usu_id, usp_nombre, usp_fecha_nacimiento, usp_tel_cod_pais, usp_tel_cod_area, usp_tel_nro) 
                            VALUES
                                  ( @id,        @nombre,    @nacimiento,          @telPais,         @telArea,         @telNumero) 
                            ON DUPLICATE KEY UPDATE
                                usp_nombre = @nombre,
                                usp_fecha_nacimiento = @nacimiento,
                                usp_tel_cod_pais = @telPais,
                                usp_tel_cod_area = @telArea,
                                usp_tel_nro = @telNumero;";

            _connection.Open();

            using (MySqlTransaction trx = _connection.BeginTransaction())
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, trx))
            {
                try
                {
                    cmd.Parameters.AddWithValue("id", loggedUser.usu_id);
                    cmd.Parameters.AddWithValue("idKey", loggedUser.usu_id);
                    cmd.Parameters.AddWithValue("nombre", perfil.Nombre);
                    cmd.Parameters.AddWithValue("nacimiento", perfil.FechaNacimiento);
                    cmd.Parameters.AddWithValue("telPais", perfil.TelefonoCodigoPais);
                    cmd.Parameters.AddWithValue("telArea", perfil.TelefonoCodigoArea);
                    cmd.Parameters.AddWithValue("telNumero", perfil.TelefonoNumero);

                    var aalgo = cmd.ExecuteNonQuery();
                    trx.Commit();
                }
                catch (Exception)
                {
                    trx.Rollback();
                    throw;
                }
                finally
                {
                    _connection.Close();
                }

            }

            return new AppServiceResponse<bool>(true);
        }

        private long GetAuthenticatedId()
        {
            var user = _httpContextAccessor.HttpContext.User;
            ClaimsIdentity identity = (ClaimsIdentity)user.Identity!;
            return long.Parse(EncodeDecodeHelper.Decrypt(identity!.Claims.First(c => c.Type == "id").Value));
        }

        private usuarios GetUserById(long id)
        {
            var sql = "SELECT * FROM usuarios WHERE usu_id = @id AND usu_fecha_baja IS NULL";

            usuarios usuario = null;

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();

                cmd.Parameters.Add(new MySqlParameter("id", (id)));

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

        public AppServiceResponse<IEnumerable<Pais>> GetCountries()
        {
            List<Pais> result = new List<Pais>();

            string sql = @"SELECT * FROM paises;";


            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        paises entity = _mapper.Map<DbDataReader, paises>(reader);
                        result.Add(_mapper.Map<paises, Pais>(entity));
                    }
                }

                _connection.Close();
            }


            return new AppServiceResponse<IEnumerable<Pais>>(result);


        }

        public AppServiceResponse<bool> ChangePassword(ChangePasswordRequest request)
        {
            long loggedID = GetAuthenticatedId();
            usuarios loggedInInfo = GetUserById(loggedID);

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
                cmd.Parameters.Add(new MySqlParameter("id", loggedID));

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
