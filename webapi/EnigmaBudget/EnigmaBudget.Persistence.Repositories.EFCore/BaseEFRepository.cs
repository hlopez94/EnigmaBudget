using AutoMapper;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;

namespace EnigmaBudget.Persistence.Repositories.EFCore
{
    public class BaseEFRepository
    {
        protected readonly EnigmaContext _context;
        protected readonly IMapper _mapper;
        public BaseEFRepository(EnigmaContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

    }
}