using AutoMapper;
using EnigmaBudget.Infrastructure.Auth.Entities;
using EnigmaBudget.Infrastructure.Auth.Helpers;
using EnigmaBudget.Infrastructure.Auth.Model;
using EnigmaBudget.Infrastructure.Auth.Requests;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Infrastructure.SendInBlue.Model;
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

        public AuthService(
            IHttpContextAccessor httpContextAccessor,
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

        public AuthResult<LoginInfo> Login(LoginRequest request)
        {
            AuthResult<LoginInfo> result = new AuthResult<LoginInfo>();

            if (request == null || request.UserName == null)
            {
                result.AddInputDataError("Solicitud Inválida");
                return result;
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

                        if (entity.usu_password.HashedPasswordIsValid(request.Password, entity.usu_seed))
                        {
                            result = new AuthResult<LoginInfo>(
                                new LoginInfo()
                                {
                                    Email = entity.usu_correo,
                                    UserName = entity.usu_usuario,
                                    JWT = GenerateJWT(entity)
                                });
                        }
                        else
                        {
                            result.AddBusinessError("Credenciales Inválidas");
                            return result;
                        }
                    }
                    else
                    {
                        result.AddNotFoundError("No se encontró usuario con las credenciales brindadas");
                        return result;
                    }
                }
                _connection.Close();
            }

            return result;
        }

        public AuthResult ResendValidationEmail()
        {

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
            return new AuthResult();


        }

        public AuthResult ValidateEmail(string token)
        {
            var result = new AuthResult();
            var sql = "SELECT * FROM enigma.usuarios_validacion_email WHERE @uve_salt = @token";

            _connection.Open();

            usuarios_validacion_email validacion = null;
            using (var cmdSel = new MySqlCommand(sql, _connection))
            {
                cmdSel.Parameters.AddWithValue("token", token);
                using (var reader = cmdSel.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        validacion = _mapper.Map<DbDataReader, usuarios_validacion_email>(reader);
                    }
                }
            }

            if (validacion is null)
            {
                result.AddBusinessError("Token inválido");
            }

            if (!validacion.valida)
            {
                if (validacion.uve_fecha_baja < DateTime.Now)
                    result.AddBusinessError("Token inválido");
            }


            return result;
        }



        private void CargarYEnviarValidacionEmail(usuarios usuario, string nuevoCorreo)
        {

            var token = HashHelper.CreateSalt();

            var sql = @"INSERT INTO enigma.usuarios_validacion_email (uve_usu_id, uve_fecha_alta, uve_fecha_baja, uve_salt, uve_nuevo_correo, uve_validado) 
                        VALUES (@id, @alta, @baja, @salt, @correo, @validado)";


            _connection.Open();

            using (MySqlTransaction trx = _connection.BeginTransaction())
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, trx))
            {
                cmd.Parameters.AddWithValue("id", usuario.usu_id);
                cmd.Parameters.AddWithValue("alta", DateTime.Now);
                cmd.Parameters.AddWithValue("baja", DateTime.Now.AddDays(1));
                cmd.Parameters.AddWithValue("salt", token);
                cmd.Parameters.AddWithValue("correo", nuevoCorreo);
                cmd.Parameters.AddWithValue("validado", true);

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

        public AuthResult<SignUpInfo> SignUp(SignUpRequest signup)
        {
            var seed = HashHelper.CreateSalt();
            var hash = signup.Password.HashPassword(seed);

            if (!hash.HashedPasswordIsValid(signup.Password, seed))
            {
                throw new CryptographicException("Error al generar hash de password, no coinciden.");
            }

            var sql = @"INSERT INTO usuarios (usu_usuario, usu_correo, usu_password, usu_seed, usu_fecha_alta, usu_fecha_modif, usu_correo_validado) 
                        VALUES (@usuario, @correo, @password, @seed, @fechaAlta, @fechaModif, @correoValidado);";

            AuthResult<SignUpInfo> result = new AuthResult<SignUpInfo>();

            _connection.Open();

            using (MySqlTransaction trx = _connection.BeginTransaction())
            using (MySqlCommand cmd = new MySqlCommand(sql, _connection, trx))
            {
                cmd.Parameters.AddWithValue("usuario", signup.UserName);
                cmd.Parameters.AddWithValue("correo", signup.Email);
                cmd.Parameters.AddWithValue("password", hash);
                cmd.Parameters.AddWithValue("seed", seed);
                cmd.Parameters.AddWithValue("fechaAlta", DateOnly.FromDateTime(DateTime.Now));
                cmd.Parameters.AddWithValue("fechaModif", DateOnly.FromDateTime(DateTime.Now));
                cmd.Parameters.AddWithValue("correoValidado", true);

                try
                {
                    var prev_id = cmd.LastInsertedId;

                    cmd.ExecuteNonQuery();
                    var id = cmd.LastInsertedId;
                    trx.Commit();
                    usuarios new_usu = new usuarios()
                    {
                        usu_id = id,
                        usu_correo = signup.Email,
                        usu_usuario = signup.UserName
                    };
                    _connection.Close();
                    CargarYEnviarValidacionEmail(new_usu, signup.Email);

                    result = new AuthResult<SignUpInfo>(new SignUpInfo() { Email = signup.Email, UserName = signup.UserName });
                }
                catch (MySqlException e)
                {
                    if (e.Message.Contains("UNI_usuarios_usu_correo"))
                    {
                        result.AddInputDataError("Ya existe una cuenta con el correo indicado.");
                    }
                    else if (e.Message.Contains("UNI_usuarios_usu_usuario"))
                    {
                        result.AddInputDataError("Ya existe una cuenta con nombre de usuario indicado.");
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

        public AuthResult<UserProfile> GetProfile()
        {

            var usu = GetUserById(GetAuthenticatedId());

            var sql = @"SELECT * 
                        FROM usuario_perfil 
                        WHERE usp_usu_id = @id;";

            UserProfile perfil = new UserProfile();

            using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
            {
                _connection.Open();

                cmd.Parameters.Add(new MySqlParameter("id", (usu.usu_id)));

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        usuario_perfil entity = _mapper.Map<DbDataReader, usuario_perfil>(reader);
                        perfil = _mapper.Map<usuario_perfil, UserProfile>(entity);
                    }
                    perfil.Usuario = usu.usu_usuario;
                    perfil.Correo = usu.usu_correo;
                }

                _connection.Close();
            }

            return new AuthResult<UserProfile>(perfil);
        }

        public AuthResult UpdateProfile(UserProfile perfil)
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

            return new AuthResult();
        }

        private long GetAuthenticatedId()
        {
            var user = _httpContextAccessor.HttpContext.User;
            ClaimsIdentity identity = (ClaimsIdentity)user.Identity!;
            return EncodeDecodeHelper.DecryptLong(identity!.Claims.First(c => c.Type == "id").Value);
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

        public AuthResult<IEnumerable<Pais>> GetCountries()
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


            return new AuthResult<IEnumerable<Pais>>(result);


        }

        public AuthResult ChangePassword(ChangePasswordRequest request)
        {
            long loggedID = GetAuthenticatedId();
            usuarios loggedInInfo = GetUserById(loggedID);
            var result = new AuthResult();
            if (request.OldPassword.Equals(request.NewPassword))
            {
                result.AddInputDataError("La nueva contraseña no puede ser igual a la anterior");
                return result;
            }

            if (!loggedInInfo.usu_password.HashedPasswordIsValid(request.OldPassword, loggedInInfo.usu_seed))
            {
                result.AddInputDataError("La contraseña a cambiar no coincide");
                return result;
            }

            var sql = "UPDATE usuarios SET usu_password = @pass, usu_seed = @seed WHERE usu_id = @id";

            string salt = HashHelper.CreateSalt();
            string hashedPass = request.NewPassword.HashPassword(salt);


            _connection.Open();
            var transaction = _connection.BeginTransaction();
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
                    }
                    else
                    {
                        transaction.Rollback();
                        result.AddInternalError("Error al cambiar la contraseña.");
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
                return result;

            }
        }

        private string GenerateJWT(usuarios entity, List<string>? roles = null)
        {
            var claims = new List<Claim>() {
                        new Claim(JwtRegisteredClaimNames.Sub, _configuration.Subject),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                        new Claim(JwtRegisteredClaimNames.Iat, DateTime.UtcNow.ToString()),
                        new Claim(JwtRegisteredClaimNames.NameId, DateTime.UtcNow.ToString()),
                        new Claim("user", entity.usu_usuario),
                        new Claim("email", entity.usu_correo),
                        new Claim("id", EncodeDecodeHelper.Encrypt(entity.usu_id.ToString())),
                        new Claim("verified-account", entity.usu_correo_verificado.ToString().ToLower())
                    };

            if (roles is not null && roles.Any())
            {
                claims.AddRange(roles.Select(r => { return new Claim(ClaimTypes.Role, r); }));
            }
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
    }
}
