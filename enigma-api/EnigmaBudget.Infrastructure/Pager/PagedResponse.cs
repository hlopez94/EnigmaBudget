namespace EnigmaBudget.Infrastructure.Pager
{
    public class PagedResponse<T>
    {
        public int PageIndex { get; private set; } = 0;
        public int TotalCount { get; set; }
        public int PageSize { get; private set; } = 10;
        public int TotalPages => (int)Math.Ceiling((decimal)TotalCount / PageSize);
        public IEnumerable<T> Items { get; set; }

        public PagedResponse(PagedRequest request)
        {
            PageIndex = request.PageIndex;
            PageSize = request.PageSize;
        }
    }
}
