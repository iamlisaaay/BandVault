namespace BandVault.Web.Services
{
    // B1: спільні правила пагінації API (skip/limit або page/pageSize)
    public readonly record struct PageRequest(int Skip, int Limit)
    {
        public const int DefaultLimit = 10;
        public const int MaxLimit = 100;

        public int CurrentPage => Skip / Limit + 1;

        // null = некоректні параметри (від'ємний skip / page)
        public static PageRequest? From(int? skip, int? limit, int page, int pageSize)
        {
            int actualLimit = limit ?? (pageSize > 0 ? pageSize : DefaultLimit);
            if (actualLimit <= 0) actualLimit = DefaultLimit;
            actualLimit = Math.Min(actualLimit, MaxLimit);

            if (skip is < 0 || page < 1 && skip == null) return null;
            return new PageRequest(skip ?? (page - 1) * actualLimit, actualLimit);
        }

        public int TotalPages(int totalItems) => (int)Math.Ceiling(totalItems / (double)Limit);

        public bool HasNext(int totalItems) => Skip + Limit < totalItems;

        public bool HasPrevious => Skip > 0;

        public int PreviousSkip => Math.Max(0, Skip - Limit);
    }
}
