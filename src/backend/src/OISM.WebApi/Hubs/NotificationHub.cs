using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;

namespace OISM.WebApi.Hubs;

/// <summary>
/// SignalR Hub phục vụ thông báo thời gian thực (/hubs/notifications).
/// Hỗ trợ Web Admin Portal và Web POS PWA.
/// 
/// Nguyên tắc bảo mật & phân quyền:
/// 1. Yêu cầu xác thực JWT Bearer (hỗ trợ query parameter 'access_token' khi kết nối WebSocket/SSE).
/// 2. Hỗ trợ vai trò: Owner, Staff, Cashier.
/// 3. Phân nhóm nhận (Group Segregation):
///    - tenant_{tenantId}: Tự động gia nhập khi kết nối thành công, nhận sự kiện toàn cửa hàng (OrderCreated, LowStockWarning).
///    - tenant_{tenantId}_branch_{branchId}: Gia nhập qua JoinBranchGroup sau khi server xác thực chi nhánh thuộc Tenant.
/// 4. Client tuyệt đối không được phép tự chỉ định chuỗi tên Group bất kỳ.
/// </summary>
[Authorize(Roles = "Owner,Staff,Cashier")]
public class NotificationHub : Hub
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        IAppDbContext dbContext,
        ICurrentTenantService currentTenantService,
        ILogger<NotificationHub> logger)
    {
        _dbContext = dbContext;
        _currentTenantService = currentTenantService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var tenantId = GetCurrentTenantId();
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogWarning("Từ chối kết nối SignalR {ConnectionId}: Không xác định được TenantId hợp lệ từ Claims.", Context.ConnectionId);
            Context.Abort();
            return;
        }

        _currentTenantService.SetTenant(tenantId.Value);

        var tenantGroupName = $"tenant_{tenantId.Value}";
        await Groups.AddToGroupAsync(Context.ConnectionId, tenantGroupName);
        _logger.LogInformation("Client {ConnectionId} (Tenant: {TenantId}) đã kết nối và tham gia nhóm {Group}.",
            Context.ConnectionId, tenantId.Value, tenantGroupName);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tenantId = GetCurrentTenantId();
        if (tenantId.HasValue)
        {
            _logger.LogInformation("Client {ConnectionId} (Tenant: {TenantId}) đã ngắt kết nối.", Context.ConnectionId, tenantId.Value);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Cho phép client (POS, nhân viên chi nhánh) đăng ký nhận biến động tồn kho của một chi nhánh cụ thể.
    /// Server kiểm tra chi nhánh phải thuộc về Tenant của người dùng.
    /// </summary>
    public async Task JoinBranchGroup(Guid branchId)
    {
        var tenantId = GetCurrentTenantId();
        if (!tenantId.HasValue || branchId == Guid.Empty)
        {
            throw new HubException("Yêu cầu không hợp lệ: Thiếu TenantId hoặc BranchId.");
        }

        _currentTenantService.SetTenant(tenantId.Value);

        var branchExists = await _dbContext.Branches
            .IgnoreQueryFilters()
            .AnyAsync(b => b.TenantId == tenantId.Value && b.Id == branchId && b.IsActive);

        if (!branchExists)
        {
            _logger.LogWarning("Client {ConnectionId} thuộc Tenant {TenantId} cố gắng tham gia chi nhánh {BranchId} không hợp lệ hoặc không thuộc quyền.",
                Context.ConnectionId, tenantId.Value, branchId);
            throw new HubException($"Chi nhánh '{branchId}' không tồn tại, đã ngừng hoạt động hoặc không thuộc Tenant của bạn.");
        }

        var branchGroupName = $"tenant_{tenantId.Value}_branch_{branchId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, branchGroupName);
        _logger.LogInformation("Client {ConnectionId} đã tham gia nhóm chi nhánh {Group}.", Context.ConnectionId, branchGroupName);
    }

    /// <summary>
    /// Cho phép client hủy đăng ký nhận biến động tồn kho chi nhánh.
    /// </summary>
    public async Task LeaveBranchGroup(Guid branchId)
    {
        var tenantId = GetCurrentTenantId();
        if (!tenantId.HasValue || branchId == Guid.Empty)
        {
            return;
        }

        var branchGroupName = $"tenant_{tenantId.Value}_branch_{branchId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, branchGroupName);
        _logger.LogInformation("Client {ConnectionId} đã rời nhóm chi nhánh {Group}.", Context.ConnectionId, branchGroupName);
    }

    private Guid? GetCurrentTenantId()
    {
        var user = Context.User;
        if (user == null) return null;

        var claim = user.FindFirst("tenantId")?.Value
            ?? user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst(ClaimTypes.GroupSid)?.Value;

        return Guid.TryParse(claim, out var tenantId) ? tenantId : null;
    }
}
