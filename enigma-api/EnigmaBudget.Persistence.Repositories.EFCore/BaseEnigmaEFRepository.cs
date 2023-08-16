using AutoMapper;
using EnigmaBudget.Persistence.Contexts.EfCore.Enigma;

namespace EnigmaBudget.Persistence.Repositories.EFCore
{
    public class BaseEnigmaEFRepository
    {
        protected readonly EnigmaContext _context;
        protected readonly IMapper _mapper;
        public BaseEnigmaEFRepository(EnigmaContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }
    }
}