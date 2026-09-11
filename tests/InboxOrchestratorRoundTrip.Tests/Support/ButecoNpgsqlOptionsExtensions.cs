using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace InboxOrchestratorRoundTrip.Tests.Support;

/// <summary>
/// Configura o provider Npgsql para o banco <c>buteco_agents</c> — o de
/// <c>apps/api</c> e <c>apps/workers</c>.
///
/// <para>
/// <b>O nome cita o banco de propósito.</b> Este ajudante registra
/// <c>UseVector()</c>, que é o mapeamento do tipo <c>vector</c> do pgvector, e
/// só o banco <c>buteco_agents</c> tem a extensão. <c>apps/inbox</c> tem banco
/// próprio (<c>buteco_inbox</c>), sem a entidade de fragmento e sem a extensão —
/// <b>não use este ajudante lá</b>: registraria um mapeamento que aquele
/// contexto não precisa e acoplaria apps deliberadamente isolados.
/// </para>
///
/// <para>
/// <b>Por que ele existe:</b> o modo de falha é traiçoeiro. Sem
/// <c>UseVector()</c>, o EF valida o modelo inteiro na primeira materialização e
/// recusa a propriedade <c>Vector</c> com uma mensagem que fala em "database
/// provider does not support mapping" — e parece defeito do modelo, não
/// configuração faltando. Repetir a chamada em cada sítio não resolveria o modo
/// de falha: instanciaria ele uma vez por sítio, e o próximo teste escrito
/// esqueceria de novo.
/// </para>
/// </summary>
public static class ButecoNpgsqlOptionsExtensions
{
    public static DbContextOptionsBuilder UseButecoAgentsNpgsql(
        this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql => npgsql.UseVector());

    public static DbContextOptionsBuilder<TContext> UseButecoAgentsNpgsql<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString)
        where TContext : DbContext =>
        builder.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
}
