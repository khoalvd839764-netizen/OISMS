using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Branches.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Hubs;

/// <summary>
/// SignalR Hub phục vụ thông báo thời gian thực (/hubs/notifications).
/// Hỗ trợ Admin Dashboard và ứng dụng POS (pos-app).
/// 
/// Nguyên tắc bảo mật & phân quyền:
/// 1. Yêu cầu xác thực JWT Bearer (hỗ trợ query parameter 'access_token' khi kết nối WebSocket/SSE).
/// 2. Hỗ trợ vai trò: Owner, Staff, Cashier.
/// 3. Phân nhóm nhận (Group Segregation):
///    - tenant_{tenantId}: Owner/Staff tự động gia nhập khi kết nối, nhận sự kiện toàn cửa hàng (OrderCreated, LowStockWarning).
///      Cashier không vào nhóm này.
///    - tenant_{tenantId}_branch_{branchId}: Gia nhập qua JoinBranchGroup sau khi server xác thực chi nhánh thuộc Tenant
///      và, với Cashier, thuộc chi nhánh được phân công (CS-02). Nhận StockLevelChanged, BranchOrderCreated.
/// 4. Client tuyệt đối không được phép tự chỉ định chuỗi tên Group bất kỳ.
/// </summary>
[Authorize(Roles = "Owner,Staff,Cashier")]
public class NotificationHub : Hub
{
    private readonly IBranchService _branchService;
    private readonly IBranchAccessService _branchAccessService;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        IBranchService branchService,
        IBranchAccessService branchAccessService,
        ICurrentTenantService currentTenantService,
        ILogger<NotificationHub> logger)
    {
        _branchService = branchService;
        _branchAccessService = branchAccessService;
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

        // Cashier không vào nhóm toàn tenant: OrderCreated chứa tổng tiền, tên khách của mọi chi nhánh (CS-02).
        // POS chỉ nhận sự kiện của chi nhánh qua JoinBranchGroup.
        if (Context.User?.IsInRole("Cashier") == true)
        {
            _logger.LogInformation("Client {ConnectionId} (Tenant: {TenantId}, Cashier) đã kết nối, chỉ nhận sự kiện theo chi nhánh.",
                Context.ConnectionId, tenantId.Value);
        }
        else
        {
            var tenantGroupName = $"tenant_{tenantId.Value}";
            await Groups.AddToGroupAsync(Context.ConnectionId, tenantGroupName);
            _logger.LogInformation("Client {ConnectionId} (Tenant: {TenantId}) đã kết nối và tham gia nhóm {Group}.",
                Context.ConnectionId, tenantId.Value, tenantGroupName);
        }

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

        var branchExists = await _branchService.ExistsActiveAsync(tenantId.Value, branchId, Context.ConnectionAborted);

        if (!branchExists)
        {
            _logger.LogWarning("Client {ConnectionId} thuộc Tenant {TenantId} cố gắng tham gia chi nhánh {BranchId} không hợp lệ hoặc không thuộc quyền.",
                Context.ConnectionId, tenantId.Value, branchId);
            throw new HubException($"Chi nhánh '{branchId}' không tồn tại, đã ngừng hoạt động hoặc không thuộc Tenant của bạn.");
        }

        if (Context.User?.IsInRole("Cashier") == true)
        {
            var userId = Context.User.GetUserId();
            if (!userId.HasValue || userId.Value == Guid.Empty)
            {
                throw new HubException("Không xác định được danh tính người dùng Cashier.");
            }

            try
            {
                await _branchAccessService.EnsureCashierBranchAllowedAsync(
                    userId.Value,
                    isCashier: true,
                    branchId,
                    Context.ConnectionAborted);
            }
            catch (OismException ex)
            {
                _logger.LogWarning("Cashier {UserId} bị từ chối tham gia nhóm chi nhánh {BranchId}: {Reason}",
                    userId.Value, branchId, ex.Message);
                throw new HubException(ex.Message);
            }
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
