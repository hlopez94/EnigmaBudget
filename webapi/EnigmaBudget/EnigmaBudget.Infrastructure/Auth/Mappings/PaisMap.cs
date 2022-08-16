using AutoMapper;
using EnigmaBudget.Infrastructure.Auth.Entities;
using EnigmaBudget.Infrastructure.Auth.Model;
using System.Data.Common;

namespace EnigmaBudget.Infrastructure.Auth.Mappings
{
    public class PaisMap : Profile
    {
        public PaisMap()
        {
            CreateMap<DbDataReader, paises>()
                .ForMember(p => p.pai_iso2, opt => opt.MapFrom(q => q["pai_iso2"]))
                .ForMember(p => p.pai_iso3, opt => opt.MapFrom(q => q["pai_iso3"]))
                .ForMember(p => p.pai_phone_code, opt => opt.MapFrom(q => q["pai_phone_code"]))
                .ForMember(p => p.pai_nombre, opt => opt.MapFrom(q => q["pai_nombre"]))
                .ForMember(p => p.pai_nombre_int, opt => opt.MapFrom(q => q["pai_nombre_int"]));

            CreateMap<paises, Pais>()
                .ForMember(p => p.CodigoTelefonico, opt => opt.MapFrom(q => q.pai_phone_code))
                .ForMember(p => p.NombreInternacional, opt => opt.MapFrom(q => q.pai_nombre_int))
                .ForMember(p => p.Nombre, opt => opt.MapFrom(q => q.pai_nombre))
                .ForMember(p => p.ISO_2, opt => opt.MapFrom(q => q.pai_iso2))
                .ForMember(p => p.ISO_3, opt => opt.MapFrom(q => q.pai_iso3));

        }
    }
}
