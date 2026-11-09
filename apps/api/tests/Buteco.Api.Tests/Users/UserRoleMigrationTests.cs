using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// A migração <c>AddUserRole</c> e o papel gravado (design.md da change papeis-no-token, D4;
/// spec <c>local-users</c>, "Papel do usuário, gravado sem padrão no código"): todo usuário que
/// já existe vira <c>admin</c>; o <c>DEFAULT 'reader'</c> fica só no banco, para a imagem
/// anterior numa volta atrás; o código nunca depende dele.
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class UserRoleMigrationTests(MigrationPostgresFixture postgres)
{
    private const string PreviousMigrationId = "20261102053419_AddUserAccessLinks";

    [Fact]
    public async Task ExistingUsers_ActiveDeactivatedAndInvited_BecomeAdmin()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var options = Options(connectionString);
        await MigrateAsync(options, PreviousMigrationId);

        var active = Guid.NewGuid();
        var deactivated = Guid.NewGuid();
        var invited = Guid.NewGuid();
        await using (var dbContext = new AppDbContext(options))
        {
            await InsertWithoutRoleAsync(dbContext, active, "active");
            await InsertWithoutRoleAsync(dbContext, deactivated, "deactivated");
            await InsertWithoutRoleAsync(dbContext, invited, "invited");
        }

        await MigrateAsync(options, null);

        await using (var dbContext = new AppDbContext(options))
        {
            var roles = await dbContext.Users.AsNoTracking().ToDictionaryAsync(user => user.Id, user => user.Role);
            Assert.Equal(UserRole.Admin, roles[active]);
            Assert.Equal(UserRole.Admin, roles[deactivated]);
            Assert.Equal(UserRole.Admin, roles[invited]);
        }
    }

    /// <summary>
    /// A volta atrás: a imagem anterior insere sem a coluna, e a linha sai com o menor papel. E o
    /// modelo do EF não declara padrão nenhum: com um, o EF omitiria do <c>INSERT</c> o papel igual
    /// ao padrão do CLR e deixaria o banco preencher.
    /// </summary>
    [Fact]
    public async Task InsertWithoutTheColumn_GetsReader_AndTheModelDeclaresNoDefault()
    {
        var options = Options(await postgres.CreateDatabaseAsync());
        await MigrateAsync(options, null);

        var id = Guid.NewGuid();
        await using var dbContext = new AppDbContext(options);
        await InsertWithoutRoleAsync(dbContext, id, "active");

        Assert.Equal(UserRole.Reader, (await dbContext.Users.AsNoTracking().SingleAsync(user => user.Id == id)).Role);

        var property = dbContext.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.Role))!;
        // A anotação, e não GetDefaultValue(): este devolve o padrão do tipo (0) sem configuração nenhuma.
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
    }

    /// <summary>
    /// Todo <c>INSERT INTO users</c> do código lista a coluna <c>role</c>, e o papel lido é o
    /// pedido: o do usuário criado, o do convite e o do bootstrap. Nenhum sai com o <c>reader</c>
    /// do default.
    /// </summary>
    [Fact]
    public async Task EveryInsertFromTheCode_CarriesTheRole()
    {
        var capture = new EmittedSqlCapture();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(await postgres.CreateDatabaseAsync())
            .UseLoggerFactory(LoggerFactory.Create(builder => builder.AddProvider(capture)))
            .Options;
        await MigrateAsync(options, null);

        var now = DateTimeOffset.UtcNow;
        var admin = User.Create($"admin-{Guid.NewGuid():N}@exemplo.test", "Admin", passwordHash: null, now, UserRole.Admin);
        var bootstrap = User.CreateBootstrap($"boot-{Guid.NewGuid():N}@exemplo.test", "100000.c2Fs.aGFzaA==", now);
        var invitation = User.CreateInvitation($"conv-{Guid.NewGuid():N}@exemplo.test", "Convidada", admin.Id, now, UserRole.Operator);

        var commands = await capture.CaptureAsync(async () =>
        {
            await using var dbContext = new AppDbContext(options);
            dbContext.Users.AddRange(admin, bootstrap);
            await dbContext.SaveChangesAsync();
            dbContext.Users.Add(invitation);
            await dbContext.SaveChangesAsync();
        });

        var inserts = commands.Where(command => command.Contains("INSERT INTO users", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(inserts);
        Assert.All(inserts, insert => Assert.Contains("role", insert[..insert.IndexOf("VALUES", StringComparison.Ordinal)], StringComparison.Ordinal));

        await using var reader = new AppDbContext(options);
        var roles = await reader.Users.AsNoTracking().ToDictionaryAsync(user => user.Id, user => user.Role);
        Assert.Equal(UserRole.Admin, roles[admin.Id]);
        Assert.Equal(UserRole.Admin, roles[bootstrap.Id]);
        Assert.Equal(UserRole.Operator, roles[invitation.Id]);
    }

    [Fact]
    public async Task TheCheck_RefusesARoleOutsideTheThree()
    {
        var options = Options(await postgres.CreateDatabaseAsync());
        await MigrateAsync(options, null);

        await using var dbContext = new AppDbContext(options);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertWithRoleAsync(dbContext, Guid.NewGuid(), "superuser"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_users_role", exception.ConstraintName);
    }

    [Fact]
    public async Task AnUnknownRole_FailsOnRead_CitingTheValue()
    {
        var options = Options(await postgres.CreateDatabaseAsync());
        await MigrateAsync(options, null);

        var id = Guid.NewGuid();
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE users DROP CONSTRAINT \"CK_users_role\";");
        await InsertWithRoleAsync(dbContext, id, "superuser");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dbContext.Users.AsNoTracking().SingleAsync(user => user.Id == id));
        Assert.Contains("'superuser'", exception.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<AppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options;

    private static async Task MigrateAsync(DbContextOptions<AppDbContext> options, string? target)
    {
        await using var dbContext = new AppDbContext(options);
        await dbContext.GetService<IMigrator>().MigrateAsync(target!, CancellationToken.None);
    }

    // As colunas obrigatórias de users em AddUserAccessLinks; sem role, como a imagem anterior grava.
    private static Task InsertWithoutRoleAsync(AppDbContext dbContext, Guid id, string status) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, normalized_email, name, status, password_hash_is_bootstrap_legacy, security_stamp, created_at)
            VALUES ({id}, {id + "@exemplo.test"}, {id + "@exemplo.test"}, 'Antes', {status}, false, 'STAMP', now())
            """);

    private static Task InsertWithRoleAsync(AppDbContext dbContext, Guid id, string role) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO users (id, email, normalized_email, name, status, password_hash_is_bootstrap_legacy, security_stamp, created_at, role)
            VALUES ({id}, {id + "@exemplo.test"}, {id + "@exemplo.test"}, 'Antes', 'active', false, 'STAMP', now(), {role})
            """);
}
