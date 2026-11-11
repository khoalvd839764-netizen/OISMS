using FluentAssertions;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// Kiểm thử chính sách khởi tạo Seed Demo theo nguyên tắc an toàn tuyệt đối của DemoSeedPolicy:
/// 1. Chỉ Development và Test được phép seed demo.
/// 2. Production, Staging và các môi trường lạ TUYỆT ĐỐI không seed demo ngay cả khi cấu hình Database:SeedDemoData = true.
/// 3. Mặc định (khi không cấu hình Database:SeedDemoData hoặc = false) không được tự động seed.
/// 4. Khi bật cấu hình nhưng thiếu mật khẩu ('Database:DemoSeedPassword'), báo lỗi cấu hình và từ chối seed; tuyệt đối không fallback hardcode.
/// 5. OismsDatabaseSeeder.SeedAsync ném InvalidOperationException nếu mật khẩu rỗng hoặc null.
/// </summary>
public class DemoSeedConfigurationTests
{
    private const string ValidDemoPassword = "SecureDemoPassword_2026!#$";

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    public void Production_MustNeverSeedDemoData_EvenIfConfigIsTrue(string env)
    {
        var result = DemoSeedPolicy.Evaluate(env, seedConfigEnabled: true, configuredPassword: ValidDemoPassword);

        result.ShouldSeed.Should().BeFalse("Production tuyệt đối không được tạo tài khoản và dữ liệu demo");
        result.Reason.Should().Contain("không được phép chạy seed demo");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("staging")]
    [InlineData("STAGING")]
    public void Staging_MustNeverSeedDemoData_EvenIfConfigIsTrue(string env)
    {
        var result = DemoSeedPolicy.Evaluate(env, seedConfigEnabled: true, configuredPassword: ValidDemoPassword);

        result.ShouldSeed.Should().BeFalse("Staging tuyệt đối không được tạo tài khoản và dữ liệu demo");
        result.Reason.Should().Contain("không được phép chạy seed demo");
    }

    [Theory]
    [InlineData("UAT")]
    [InlineData("PreProd")]
    [InlineData("CustomUnknownEnv")]
    [InlineData("")]
    [InlineData(null)]
    public void ForeignOrUnknownEnvironments_MustNeverSeedDemoData(string? env)
    {
        var result = DemoSeedPolicy.Evaluate(env, seedConfigEnabled: true, configuredPassword: ValidDemoPassword);

        result.ShouldSeed.Should().BeFalse("Các môi trường không nằm trong danh sách cho phép tuyệt đối không được seed demo");
        result.Reason.Should().Contain("không được phép chạy seed demo");
    }

    [Fact]
    public void Development_DefaultConfiguration_MustNotSeedDemoData()
    {
        var result = DemoSeedPolicy.Evaluate("Development", seedConfigEnabled: false, configuredPassword: ValidDemoPassword);

        result.ShouldSeed.Should().BeFalse("Mặc định khi không bật cấu hình thì không được tự ý seed dữ liệu");
        result.Reason.Should().Contain("chưa được bật");
    }

    [Fact]
    public void Development_ExplicitlyEnabled_WithValidPassword_ShouldSeedDemoData()
    {
        var result = DemoSeedPolicy.Evaluate("Development", seedConfigEnabled: true, configuredPassword: ValidDemoPassword);

        result.ShouldSeed.Should().BeTrue("Khi được bật chủ động kèm mật khẩu hợp lệ trong Development thì cho phép seed");
        result.IsConfigurationError.Should().BeFalse();
    }

    [Fact]
    public void TestEnvironment_ExplicitlyEnabled_WithValidPassword_ShouldSeedDemoData()
    {
        var result = DemoSeedPolicy.Evaluate("Test", seedConfigEnabled: true, configuredPassword: ValidDemoPassword);

        result.ShouldSeed.Should().BeTrue("Môi trường Test được phép seed khi bật cấu hình và có mật khẩu");
        result.IsConfigurationError.Should().BeFalse();
    }

    [Fact]
    public void TestingEnvironment_ExplicitlyEnabled_WithValidPassword_ShouldSeedDemoData()
    {
        var result = DemoSeedPolicy.Evaluate("Testing", seedConfigEnabled: true, configuredPassword: ValidDemoPassword);

        result.ShouldSeed.Should().BeTrue("Môi trường Testing được phép seed khi bật cấu hình và có mật khẩu");
        result.IsConfigurationError.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Development_Enabled_WithMissingPassword_MustReportConfigurationError_AndNotSeed(string? missingPassword)
    {
        var result = DemoSeedPolicy.Evaluate("Development", seedConfigEnabled: true, configuredPassword: missingPassword);

        result.ShouldSeed.Should().BeFalse("Thiếu mật khẩu cấu hình thì không được phép seed");
        result.IsConfigurationError.Should().BeTrue("Phải báo lỗi cấu hình khi thiếu mật khẩu");
        result.Reason.Should().Contain("thiếu cấu hình 'Database:DemoSeedPassword'");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OismsDatabaseSeeder_SeedAsync_WhenPasswordIsNullOrEmpty_ThrowsInvalidOperationException(string? invalidPassword)
    {
        Func<Task> act = async () => await OismsDatabaseSeeder.SeedAsync(null!, invalidPassword!);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Demo seed requires an explicit, non-empty password*Hardcoded fallback is prohibited*");
    }
}
