using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Search.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Tìm kiếm toàn hệ thống đa thực thể (CS-04).
/// Hỗ trợ tìm kiếm nhanh SKU, Đơn hàng và Phiếu nhập kho theo quyền và Tenant.
/// </summary>
[ApiController]
[Route("api/search")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// Thực hiện tìm kiếm toàn hệ thống đa thực thể theo từ khóa.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GlobalSearch(
        [FromQuery] string? q,
        [FromQuery] int limit = 5,
        CancellationToken ct = default)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Staff";
        var results = await _searchService.SearchAsync(q ?? string.Empty, role, limit, ct);
        return Ok(results);
    }
}
