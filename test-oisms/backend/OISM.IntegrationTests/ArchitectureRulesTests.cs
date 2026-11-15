using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Common;
using OISM.Infrastructure.Persistence;
using OISM.WebApi.Controllers;

namespace OISM.IntegrationTests;

/// <summary>
/// Khóa kiến trúc 4 tầng Clean Architecture theo NFR-MAINT-01 (OISMS-59-G1C).
/// Kiểm tra phụ thuộc giữa các assembly, controller không chạm DbContext/Lock/UnitOfWork,
/// và Application không gọi EF raw SQL trực tiếp.
/// </summary>
public sealed class ArchitectureRulesTests
{
    private static readonly Regex ForbiddenRawSqlRegex = new(
        @"\b(ExecuteSqlRaw(?:Async)?|ExecuteSqlInterpolated(?:Async)?|FromSqlRaw|FromSqlInterpolated|SqlQueryRaw)\s*(?:<[^>]+>)?\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void AC01_Domain_ShouldOnlyReferenceBclAssemblies()
    {
        var domainAssembly = typeof(BaseEntity).Assembly;
        var references = domainAssembly.GetReferencedAssemblies();
        references.Should().NotBeEmpty("OISM.Domain phải có danh sách assembly tham chiếu để kiểm tra");

        var violations = references
            .Select(r => r.Name)
            .Where(name => !IsBclAssembly(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            $"[AC-01 / NFR-MAINT-01] OISM.Domain chỉ được tham chiếu BCL (System.*, Microsoft.CSharp, netstandard); " +
            $"phát hiện tham chiếu vi phạm tại {domainAssembly.GetName().Name}: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AC02_Application_ShouldNotReferenceInfrastructureWebApiOrProviderPackages()
    {
        var appAssembly = typeof(IAppDbContext).Assembly;
        var references = appAssembly.GetReferencedAssemblies();
        references.Should().NotBeEmpty("OISM.Application phải có danh sách assembly tham chiếu để kiểm tra");

        string[] forbiddenPrefixes =
        [
            "OISM.Infrastructure",
            "OISM.WebApi",
            "Npgsql",
            "Hangfire",
            "Microsoft.AspNetCore",
            "BCrypt"
        ];

        var violations = references
            .Select(r => r.Name)
            .Where(name => name is not null && (
                forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                (name.StartsWith("OISM.", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(name, "OISM.Domain", StringComparison.OrdinalIgnoreCase))))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            $"[AC-02 / NFR-MAINT-01] OISM.Application chỉ được tham chiếu OISM.Domain và các gói hợp lệ " +
            $"(bao gồm ngoại lệ có chủ đích Microsoft.EntityFrameworkCore qua IAppDbContext), " +
            $"cấm OISM.Infrastructure/OISM.WebApi/Npgsql*/Hangfire*/Microsoft.AspNetCore*/BCrypt*; " +
            $"phát hiện tại {appAssembly.GetName().Name}: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AC03_Infrastructure_ShouldNotReferenceWebApi()
    {
        var infraAssembly = typeof(AppDbContext).Assembly;
        var references = infraAssembly.GetReferencedAssemblies();
        references.Should().NotBeEmpty("OISM.Infrastructure phải có danh sách assembly tham chiếu để kiểm tra");

        var violations = references
            .Select(r => r.Name)
            .Where(name => name is not null &&
                           (string.Equals(name, "OISM.WebApi", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("OISM.WebApi.", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            $"[AC-03 / NFR-MAINT-01] OISM.Infrastructure không được tham chiếu OISM.WebApi; " +
            $"phát hiện tại {infraAssembly.GetName().Name}: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AC04_WebApiControllers_ShouldNotDependOnDbContextUnitOfWorkOrSqlLockService()
    {
        var webApiAssembly = typeof(BranchController).Assembly;
        var controllers = webApiAssembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        controllers.Should().NotBeEmpty("OISM.WebApi phải có ít nhất một controller kế thừa ControllerBase");

        var violations = new List<string>();
        foreach (var controller in controllers)
        {
            foreach (var ctor in controller.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var param in ctor.GetParameters())
                {
                    if (IsForbiddenControllerDependency(param.ParameterType))
                    {
                        violations.Add($"{controller.Name} (ctor parameter '{param.Name}': {param.ParameterType.Name})");
                    }
                }
            }

            var current = controller;
            while (current is not null && current != typeof(ControllerBase) && current != typeof(object))
            {
                foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (IsForbiddenControllerDependency(field.FieldType))
                    {
                        violations.Add($"{controller.Name} (field '{field.Name}' declared on {current.Name}: {field.FieldType.Name})");
                    }
                }

                current = current.BaseType;
            }
        }

        violations.Should().BeEmpty(
            $"[AC-04 / NFR-MAINT-01] Controller trong OISM.WebApi không được có constructor parameter hoặc field " +
            $"kiểu DbContext, AppDbContext, IAppDbContext, IUnitOfWork, ISqlLockService; " +
            $"các controller vi phạm: {string.Join("; ", violations)}");
    }

    [Fact]
    public void AC05_ApplicationSource_ShouldNotInvokeEfRawSqlMethods()
    {
        var repoRoot = FindRepoRoot();
        var applicationRoot = Path.Combine(repoRoot, "src", "backend", "src", "OISM.Application");
        Directory.Exists(applicationRoot).Should().BeTrue(
            $"[AC-05 / NFR-MAINT-01] Phải tìm thấy thư mục nguồn OISM.Application tại '{applicationRoot}'");

        var csFiles = Directory
            .EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactPath(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        csFiles.Should().NotBeEmpty("OISM.Application phải có các file .cs để kiểm tra quy tắc raw SQL");

        var violations = new List<string>();
        foreach (var filePath in csFiles)
        {
            var relativePath = Path.GetRelativePath(repoRoot, filePath);
            var lines = File.ReadAllLines(filePath);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (ForbiddenRawSqlRegex.IsMatch(line))
                {
                    violations.Add($"{relativePath}:{i + 1}: {line.Trim()}");
                }
            }
        }

        violations.Should().BeEmpty(
            $"[AC-05 / NFR-MAINT-01] OISM.Application không được gọi trực tiếp các phương thức raw SQL của EF Core " +
            $"(ExecuteSqlRaw/ExecuteSqlInterpolated/FromSqlRaw/FromSqlInterpolated/SqlQueryRaw); " +
            $"phát hiện tại: {string.Join(" | ", violations)}");
    }

    private static bool IsBclAssembly(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.StartsWith("System.", StringComparison.Ordinal) ||
         name is "System" or "Microsoft.CSharp" or "netstandard");

    private static bool IsForbiddenControllerDependency(Type type) =>
        typeof(DbContext).IsAssignableFrom(type) ||
        typeof(IAppDbContext).IsAssignableFrom(type) ||
        typeof(IUnitOfWork).IsAssignableFrom(type) ||
        typeof(ISqlLockService).IsAssignableFrom(type);

    private static bool IsBuildArtifactPath(string filePath)
    {
        var segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "backend", "OISM.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy thư mục gốc repo chứa src/backend/OISM.sln.");
    }
}
