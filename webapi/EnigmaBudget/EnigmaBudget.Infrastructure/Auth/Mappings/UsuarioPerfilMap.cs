using AutoMapper;
using EnigmaBudget.Infrastructure.Auth.Entities;
using EnigmaBudget.Infrastructure.Helpers;
using EnigmaBudget.Model.Model;
using System.Data.Common;

namespace EnigmaBudget.Infrastructure.Auth.Mappings
{
    public class UsuarioPerfilMap : Profile
    {
        public UsuarioPerfilMap()
        {
            CreateMap<DbDataReader, usuario_perfil>()
                .ForMember(p => p.usp_usu_id, opt => opt.MapFrom(q => q["usp_usu_id"]))
                .ForMember(p => p.usp_nombre, opt => opt.MapFrom(q => q["usp_nombre"]))
                .ForMember(p => p.usp_tel_cod_pais, opt => opt.MapFrom(q => q.IsDBNull(q.GetOrdinal("usp_tel_cod_pais")) ? default(short?) : q["usp_tel_cod_pais"]))
                .ForMember(p => p.usp_tel_cod_area, opt => opt.MapFrom(q => q.IsDBNull(q.GetOrdinal("usp_tel_cod_area")) ? default(short?) : q["usp_tel_cod_area"]))
                .ForMember(p => p.usp_tel_nro, opt => opt.MapFrom(q => q.IsDBNull(q.GetOrdinal("usp_tel_nro")) ? default(short?) : q["usp_tel_nro"]))
                .ForMember(p => p.usp_fecha_nacimiento, opt => opt.MapFrom(q => q.IsDBNull(q.GetOrdinal("usp_fecha_nacimiento")) ? default(short?) : q["usp_fecha_nacimiento"]))
                .ForMember(p => p.usp_fecha_alta, opt => opt.MapFrom(q => q["usp_fecha_alta"]))
                .ForMember(p => p.usp_fecha_modif, opt => opt.MapFrom(q => q["usp_fecha_modif"]))
                .ForMember(p => p.usp_fecha_baja, opt => opt.MapFrom(q => q.IsDBNull(q.GetOrdinal("usp_fecha_baja")) ? default(DateTime?) : q["usp_fecha_baja"]))
                ;

            CreateMap<usuario_perfil, Perfil>()
                .ForMember(p => p.TelefonoNumero, opt => opt.MapFrom(q => q.usp_tel_nro))
                .ForMember(p => p.IdUnicoUsuario, opt => opt.MapFrom(q => q.usp_usu_id))
                .ForMember(p => p.TelefonoCodigoPais, opt => opt.MapFrom(q => q.usp_tel_cod_pais))
                .ForMember(p => p.TelefonoCodigoArea, opt => opt.MapFrom(q => q.usp_tel_cod_area))
                .ForMember(p => p.FechaNacimiento, opt => opt.MapFrom(q => q.usp_fecha_nacimiento))
                .ForMember(p => p.Nombre, opt => opt.MapFrom(q => q.usp_nombre))
                .ForMember(p => p.IdUnicoUsuario, opt => opt.MapFrom(q => EncodeDecodeHelper.Encrypt(q.usp_usu_id.ToString())))
                ;
        }


    }
}
