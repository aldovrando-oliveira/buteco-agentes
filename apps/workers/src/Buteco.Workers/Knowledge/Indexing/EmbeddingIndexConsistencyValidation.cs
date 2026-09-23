using Buteco.Workers.Infrastructure;
using Buteco.Workers.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Checagem de integridade entre o modelo de embedding <b>declarado</b> e o que
/// está <b>gravado</b> no índice. Quinto caso da convenção 8 — e o primeiro
/// desta base numa forma nova: <b>é a única checagem de boot que faz I/O</b>.
///
/// <para>
/// As três anteriores varrem <c>IServiceCollection</c>
/// (<c>ValidateKnowledgeExtractorRegistrations</c>,
/// <c>ValidateChannelAdapterRegistrations</c>) ou leem configuração
/// (<c>ValidateTimeZoneConfiguration</c>). Esta consulta o banco, e o custo é
/// aceitável por um motivo verificado, não por conveniência: no compose
/// <c>apps/workers</c> declara <c>depends_on: migrator:
/// service_completed_successfully</c>, e o <c>migrator</c> declara
/// <c>postgres: service_healthy</c> — transitivamente, em produção o banco está
/// saudável e migrado antes deste processo subir.
/// </para>
///
/// <para>
/// <b>Mudança de comportamento local, declarada:</b> quem roda
/// <c>dotnet run</c> fora do compose passa a não subir com o Postgres parado,
/// onde antes subia e falhava por mensagem. <c>apps/workers</c> não faz nada sem
/// banco e sem fila, então a diferença prática é o momento e a clareza da falha
/// — mas é mudança que se nota antes de entender, e por isso está escrita.
/// </para>
///
/// <para>
/// <b>Por que falhar o boot e não tolerar:</b> drift de modelo corrompe o índice
/// em silêncio. Vetores de modelos diferentes são incomparáveis, e a busca
/// continuaria devolvendo resultados — errados, sem erro nenhum. Não há modo de
/// tolerância nem bypass por configuração.
/// </para>
/// </summary>
public static class EmbeddingIndexConsistencyValidation
{
    public static void ValidateEmbeddingIndexConsistency(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var declared = scope.ServiceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

        // LINHAS DE SUCESSO — change compactacao-historico, D6. Até esta change a
        // única manifestação desta checagem no log era a consulta que o EF Core
        // imprimia, e o MESMO escopo silencia essa consulta em produção
        // (Microsoft.EntityFrameworkCore.Database.Command em Warning). Sem linha
        // própria, a checagem passaria a rodar invisível: sucesso indistinguível
        // de ausência, que é o defeito que checagem de boot existe para não ter.
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(EmbeddingIndexConsistencyValidation));

        // SELECT DISTINCT sobre as três colunas de proveniência. Uma combinação
        // por modelo já usado no índice.
        var found = dbContext.KnowledgeFragments
            .Select(fragment => new
            {
                fragment.EmbeddingProvider,
                fragment.EmbeddingModel,
                fragment.EmbeddingDimensions,
            })
            .Distinct()
            .ToList();

        // Índice vazio sobe: é o primeiro deploy, e não há nada contra o que
        // divergir.
        //
        // DUAS LINHAS DISTINTAS PARA OS DOIS CAMINHOS DE SUCESSO, de propósito:
        // "índice vazio" e "conferido contra o índice" são fatos diferentes, e
        // uma linha só para os dois devolveria a ambiguidade que a linha veio
        // remover — quem lê o boot precisa saber se houve conferência de verdade.
        if (found.Count == 0)
        {
            logger.LogInformation(
                "Índice de conhecimento vazio: nada a conferir contra o modelo declarado "
              + "'{Provider}'/'{Model}'/{Dimensions}.",
                declared.Provider,
                declared.Model,
                declared.Dimensions);

            return;
        }

        // Mais de uma combinação é corrupção por troca anterior NÃO detectada —
        // falha mesmo que uma delas seja a declarada. Um índice com dois modelos
        // não tem resposta certa: metade dos vetores é incomparável com a outra.
        if (found.Count > 1)
        {
            var combinations = string.Join("; ", found
                .Select(f => $"'{f.EmbeddingProvider}'/'{f.EmbeddingModel}'/{f.EmbeddingDimensions}")
                .Order(StringComparer.Ordinal));

            throw new InvalidOperationException(
                $"Índice de conhecimento corrompido: os fragmentos gravados usam {found.Count} combinações "
              + $"distintas de provedor/modelo/dimensão ({combinations}), quando só pode haver uma. "
              + "Isso indica troca de modelo sem reindexação. Reindexe todos os documentos antes de subir.");
        }

        var single = found[0];
        if (string.Equals(single.EmbeddingProvider, declared.Provider, StringComparison.Ordinal)
            && string.Equals(single.EmbeddingModel, declared.Model, StringComparison.Ordinal)
            && single.EmbeddingDimensions == declared.Dimensions)
        {
            logger.LogInformation(
                "Índice de conhecimento conferido: fragmentos e configuração usam "
              + "'{Provider}'/'{Model}'/{Dimensions}.",
                declared.Provider,
                declared.Model,
                declared.Dimensions);

            return;
        }

        // Nomeia o declarado E o encontrado, nos dois sentidos — idioma de
        // ValidateKnowledgeExtractorRegistrations, que é o molde da convenção 8
        // nesta base.
        throw new InvalidOperationException(
            "Divergência entre o modelo de embedding declarado e o gravado no índice: "
          + $"a configuração declara '{declared.Provider}'/'{declared.Model}'/{declared.Dimensions} "
          + $"e os fragmentos gravados usam '{single.EmbeddingProvider}'/'{single.EmbeddingModel}'/{single.EmbeddingDimensions}. "
          + "Vetores de modelos diferentes são incomparáveis: ou a configuração volta ao modelo do índice, "
          + "ou todos os documentos são reindexados.");
    }
}
