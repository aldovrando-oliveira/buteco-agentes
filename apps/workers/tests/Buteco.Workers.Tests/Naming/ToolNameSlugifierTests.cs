using Buteco.Workers.Naming;

namespace Buteco.Workers.Tests.Naming;

/// <summary>
/// <see cref="ToolNameSlugifier"/> é função pura, e este arquivo existe porque
/// ela estava sendo testada dentro de uma classe com
/// <c>IClassFixture&lt;WorkerInfrastructureFixture&gt;</c> — pagando um par de
/// containers Postgres + RabbitMQ para exercitar quatro entradas de string.
///
/// <para>
/// A separação foi feita ao mover o tipo para <c>Naming/</c> (change
/// knowledge-tool-resolver, tarefa 1.2) e é divergência declarada do texto da
/// tarefa, que mandava renomear o arquivo inteiro: renomear
/// <c>DelegationToolNameSlugifierTests</c> para <c>ToolNameSlugifierTests</c>
/// deixaria o nome **menos** exato, porque aquela classe cobre sobretudo o
/// contrato de <c>AgentDelegationToolSetResolver</c>, que não é sobre
/// slugificação. O que valia extrair era a teoria de função pura, e ela vem
/// junto com a economia de um fixture (convenção 9).
/// </para>
/// </summary>
public class ToolNameSlugifierTests
{
    [Theory]
    [InlineData("Atendimento", "atendimento")]
    [InlineData("Consulta CEP", "consulta-cep")]
    [InlineData("Suporte    Técnico!!", "suporte-tecnico")]
    [InlineData("---", "agent")]
    public void Slugify_ProducesExpectedSlug(string name, string expectedSlug)
    {
        Assert.Equal(expectedSlug, ToolNameSlugifier.Slugify(name));
    }

    /// <summary>
    /// O segundo consumidor do slugificador é a tool de conhecimento, e o caso
    /// que motivou mover o tipo para um espaço neutro é este: um nome de
    /// cadastro com diacríticos. Sem a remoção de diacríticos daqui, o
    /// sanitizador sozinho produziria <c>Informa__es_Gerais</c> — o padrão real
    /// encontrado no censo de nomes de tool, em que o <c>__</c> não é o
    /// separador.
    /// </summary>
    [Theory]
    [InlineData("Informações Gerais", "informacoes-gerais")]
    [InlineData("Políticas de Cobrança", "politicas-de-cobranca")]
    public void Slugify_RemovesDiacritics_SoTheSanitizerNeverSeesThem(string name, string expectedSlug)
    {
        Assert.Equal(expectedSlug, ToolNameSlugifier.Slugify(name));
    }
}
