namespace EnigmaBudget.Infrastructure.Pager
{
    public class PagedResponse<T>
    {
        public int PageIndex { get; set; } = 0;
        public int TotalCount { get; set; }
        public int PageSize { get; set; } = 10;
        public int TotalPages { get; set; } 
        public int PageCount { get; set; }  
        public IEnumerable<T> Items { get; set; }
            
    }
}
