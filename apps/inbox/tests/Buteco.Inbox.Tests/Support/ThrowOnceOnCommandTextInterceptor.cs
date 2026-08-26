using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Buteco.Inbox.Tests.Support;

// Interceptor de comando do EF Core (extensibilidade padrão, sem
// abstração nova em produção) usado só em teste para forçar, sob
// demanda, uma falha de infraestrutura na consulta real de candidatos de
// DebounceSweepService.ProcessDueDispatchesAsync — o mesmo caminho onde o
// 57P01 real foi reproduzido (inbox-sweep-service-resiliencia, design.md).
// Arma-se com um fragmento do texto do comando SQL; a primeira execução
// de comando cujo CommandText contenha esse fragmento lança e o
// interceptor se desarma sozinho, não afetando nenhuma consulta seguinte
// (do mesmo teste ou de outro teste da classe).
public sealed class ThrowOnceOnCommandTextInterceptor : DbCommandInterceptor
{
    private string? _armedFragment;

    public void ArmNextMatchingCommand(string commandTextFragment) => _armedFragment = commandTextFragment;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var armedFragment = _armedFragment;
        if (armedFragment is not null && command.CommandText.Contains(armedFragment, StringComparison.Ordinal))
        {
            _armedFragment = null;
            throw new InvalidOperationException(
                "Falha de infraestrutura forçada pelo teste (ThrowOnceOnCommandTextInterceptor).");
        }

        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
