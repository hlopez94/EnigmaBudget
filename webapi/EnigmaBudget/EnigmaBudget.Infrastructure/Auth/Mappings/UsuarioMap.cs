using AutoMapper;
using EnigmaBudget.Infrastructure.Auth.Entities;
using System.Data.Common;

namespace EnigmaBudget.Infrastructure.Auth.Mappings
{
    public  class UsuarioMap : Profile
    {
        public UsuarioMap()
        {
            CreateMap<DbDataReader, usuarios>()
                .ForMember(p => p.usu_id, opt => opt.MapFrom(q => q["usu_id"]))
                .ForMember(p => p.usu_usuario, opt => opt.MapFrom(q => q["usu_usuario"]))
                .ForMember(p => p.usu_correo, opt => opt.MapFrom(q => q["usu_correo"]))
                .ForMember(p => p.usu_password, opt => opt.MapFrom(q => q["usu_password"]))
                .ForMember(p => p.usu_seed, opt => opt.MapFrom(q => q["usu_seed"]))
                .ForMember(p => p.usu_fecha_alta, opt => opt.MapFrom(q => q["usu_fecha_alta"]))
                .ForMember(p => p.usu_fecha_modif, opt => opt.MapFrom(q => q["usu_fecha_modif"]))
                .ForMember(p => p.usu_correo_verificado, opt => opt.MapFrom(q => q["usu_correo_validado"]))
                .ForMember(p => p.usu_fecha_baja, opt =>
                {
                    opt.MapFrom(q => q.IsDBNull(q.GetOrdinal("usu_fecha_baja")) ? default(DateTime?) : q["usu_fecha_baja"]);
                })
                ;
        }


    }
}
