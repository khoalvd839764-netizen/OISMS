using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Users;
using OISM.Application.Features.Users.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class UserServiceTests
{
    private readonly Mock<IPasswordHasher> _passwordHasherMock;

    public UserServiceTests()
    {
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<string>()))
            .Returns<string>(p => $"hashed_{p}");
        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((p, h) => h == $"hashed_{p}");
    }

    [Fact]
    public async Task GetUsersAsync_ReturnsOnlyCurrentTenantUsers()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var otherTenantId = Guid.NewGuid();

        using (context.OpenPrivilegedScope())
        {
            context.Users.AddRange(
                new User
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    DisplayName = "Nhân viên 1",
                    Email = "staff1@example.com",
                    Role = "Staff",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    TenantId = otherTenantId,
                    DisplayName = "Nhân viên Tenant khác",
                    Email = "other@example.com",
                    Role = "Staff",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            );
            await context.SaveChangesAsync();
        }

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);

        // Act
        var result = await service.GetUsersAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("staff1@example.com", result[0].Email);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersBySearchAndRoleAndIsActive()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();

        context.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DisplayName = "Nguyễn Văn Thu Ngân",
                Email = "cashier@example.com",
                Role = "Cashier",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DisplayName = "Trần Thị Kho",
                Email = "staff@example.com",
                Role = "Staff",
                IsActive = false,
                CreatedAt = DateTimeOffset.UtcNow
            }
        );
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);

        // Act
        var cashierOnly = await service.GetUsersAsync(role: "Cashier");
        var activeOnly = await service.GetUsersAsync(isActive: true);
        var searchResult = await service.GetUsersAsync(search: "Thu Ngân");

        // Assert
        Assert.Single(cashierOnly);
        Assert.Equal("Cashier", cashierOnly[0].Role);

        Assert.Single(activeOnly);
        Assert.Equal("cashier@example.com", activeOnly[0].Email);

        Assert.Single(searchResult);
        Assert.Equal("Nguyễn Văn Thu Ngân", searchResult[0].DisplayName);
    }

    [Fact]
    public async Task CreateUserAsync_Success_HashesPasswordAndAssignsTenant()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);

        var request = new CreateUserRequest
        {
            DisplayName = "Lê Nhân Viên",
            Email = "lenhanvien@example.com",
            Phone = "+84912345678",
            Password = "SecurePassword123",
            Role = "Staff"
        };

        // Act
        var result = await service.CreateUserAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("lenhanvien@example.com", result.Email);
        Assert.Equal("Staff", result.Role);
        Assert.True(result.IsActive);
        Assert.Equal(tenantId, result.TenantId);

        var savedUser = await context.Users.FindAsync(tenantId, result.Id);
        Assert.NotNull(savedUser);
        Assert.Equal("hashed_SecurePassword123", savedUser.PasswordHash);
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateEmail_ThrowsConflict409()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "User Đã Có",
            Email = "duplicate@example.com",
            PasswordHash = "hashed",
            Role = "Staff"
        });
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);

        var request = new CreateUserRequest
        {
            DisplayName = "User Mới",
            Email = "DUPLICATE@EXAMPLE.COM", // hoa thường phải normalize
            Password = "SecretPassword123",
            Role = "Staff"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OismException>(() => service.CreateUserAsync(request));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("EMAIL_ALREADY_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateUserAsync_SelfDeactivation_ThrowsBadRequest400()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var ownerId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = ownerId,
            TenantId = tenantId,
            DisplayName = "Chủ Shop",
            Email = "owner@example.com",
            Role = "Owner",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);

        var updateReq = new UpdateUserRequest
        {
            DisplayName = "Chủ Shop Đổi Tên",
            Role = "Owner",
            IsActive = false // Tự khóa tài khoản của mình
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OismException>(() => service.UpdateUserAsync(ownerId, updateReq, ownerId));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("CANNOT_DEACTIVATE_SELF", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateUserAsync_SelfDemotionOfOwner_ThrowsBadRequest400()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var ownerId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = ownerId,
            TenantId = tenantId,
            DisplayName = "Chủ Shop",
            Email = "owner@example.com",
            Role = "Owner",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);

        var updateReq = new UpdateUserRequest
        {
            DisplayName = "Chủ Shop",
            Role = "Staff", // Tự hạ quyền
            IsActive = true
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OismException>(() => service.UpdateUserAsync(ownerId, updateReq, ownerId));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("CANNOT_DEMOTE_SELF", ex.ErrorCode);
    }

    [Fact]
    public async Task UpdateUserStatusAsync_Success()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var ownerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = staffId,
            TenantId = tenantId,
            DisplayName = "Nhân viên thử việc",
            Email = "intern@example.com",
            Role = "Staff",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);

        // Act - Khóa tài khoản
        var result = await service.UpdateUserStatusAsync(staffId, false, ownerId);

        // Assert
        Assert.False(result.IsActive);
        var userInDb = await context.Users.FindAsync(tenantId, staffId);
        Assert.False(userInDb!.IsActive);
    }

    [Fact]
    public async Task UpdateUser_OwnerEditingOwnProfile_PassesValidatorAndSaves()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var ownerId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = ownerId,
            TenantId = tenantId,
            DisplayName = "Chủ Shop",
            Email = "owner@example.com",
            Role = "Owner",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);
        var request = new UpdateUserRequest { DisplayName = "Chủ Shop Mới", Role = "Owner", IsActive = true };

        // Act
        var validation = await new UpdateUserValidator().ValidateAsync(request);
        var result = await service.UpdateUserAsync(ownerId, request, ownerId);

        // Assert
        Assert.True(validation.IsValid);
        Assert.Equal("Chủ Shop Mới", result.DisplayName);
        Assert.Equal("Owner", result.Role);
    }

    [Fact]
    public async Task UpdateUserAsync_PromoteStaffToOwner_ThrowsBadRequest400()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var staffId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = staffId,
            TenantId = tenantId,
            DisplayName = "Nhân viên",
            Email = "staff@example.com",
            Role = "Staff",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);
        var request = new UpdateUserRequest { DisplayName = "Nhân viên", Role = "Owner", IsActive = true };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OismException>(() => service.UpdateUserAsync(staffId, request, Guid.NewGuid()));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("CANNOT_ASSIGN_OWNER", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUserAsync_SamePhoneInLocalFormat_ThrowsConflict409()
    {
        // Arrange: số đã lưu dạng E.164, request gửi dạng 0xxx
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "Chủ Shop",
            Email = "owner@example.com",
            Phone = "+84901234567",
            Role = "Owner",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);
        var request = new CreateUserRequest
        {
            DisplayName = "Nhân viên",
            Email = "staff@example.com",
            Phone = "0901234567",
            Password = "SecurePassword123",
            Role = "Staff"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OismException>(() => service.CreateUserAsync(request));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("PHONE_ALREADY_EXISTS", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateUserAsync_InvalidPhone_ThrowsBadRequest400()
    {
        // Arrange
        var (context, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext();
        var service = new UserService(context, tenantMock.Object, _passwordHasherMock.Object);
        var request = new CreateUserRequest
        {
            DisplayName = "Nhân viên",
            Email = "staff@example.com",
            Phone = "abc",
            Password = "SecurePassword123",
            Role = "Staff"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OismException>(() => service.CreateUserAsync(request));
        Assert.Equal("INVALID_PHONE", ex.ErrorCode);
        Assert.False((await new CreateUserValidator().ValidateAsync(request)).IsValid);
    }
}
