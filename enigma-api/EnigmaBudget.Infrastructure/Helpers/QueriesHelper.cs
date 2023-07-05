using AutoMapper;
using EnigmaBudget.Infrastructure.Pager;

namespace EnigmaBudget.Domain.Repositories
{
    public static class QueriesHelper<TModel,TEntity>
    {
        public static PagedResponse<TModel> PageQuery( IQueryable<TEntity> query, PagedRequest pagingRequest, IMapper mapper) 
        {
            var res = new PagedResponse<TModel>(pagingRequest);
            var pagedQuery = query.ToPagedSearch(pagingRequest.PageIndex + 1, pagingRequest.PageSize, pagingRequest.SortBy);

            res.TotalCount = query.Count();
            res.Items = pagedQuery.Select(entity => mapper.Map<TEntity, TModel>(entity));

            return res;
        }

    }
}
