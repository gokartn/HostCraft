namespace HostCraft.Api.Models.Shared;

public record PagedResult<T>(IEnumerable<T> Items, int TotalCount, int Page, int PageSize);
