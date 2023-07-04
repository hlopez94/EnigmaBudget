namespace EnigmaBudget.Infrastructure.Pager
{
    public class PagedRequest
    {
        public int PageIndex { get; set; } = 0;
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// Comma separated string containing a list of
        /// properties which to sort by.
        /// <para>
        /// Append a '-' symbol
        /// to the property name to indicate that it shoud be sorted on descending order
        /// </para>
        /// <para>
        /// Ex. 'propA,-propB,propC' will sort by propA ascending, propB descending and propC ascending
        /// </para>
        /// </summary>
        public string SortBy { get; set; }
    }
}
