using System.Reflection;
using Buteco.Api.Knowledge.Indexing;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// D7 da change indexacao-sem-job-orfao: toda escrita passa pelo pedido de indexação,
/// e o único tipo do <c>apps/api</c> que fala com o publisher é o despacho. Os testes
/// de comportamento cobrem os quatro caminhos de hoje; este é o que reprova o quinto —
/// uma escrita nova que voltasse ao "publica depois e esquece".
/// </summary>
public class KnowledgeIndexingPublisherDependencyTests
{
    [Fact]
    public void OnlyTheDispatcherDependsOnTheIndexingPublisher()
    {
        var dependents = typeof(Program).Assembly.GetTypes()
            .Where(type => !type.IsInterface && type != typeof(RabbitMqKnowledgeIndexingJobPublisher))
            .Where(type => type
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(IKnowledgeIndexingJobPublisher))))
            .Select(type => type.FullName)
            .Order()
            .ToList();

        Assert.Equal(new[] { typeof(KnowledgeIndexingRequestDispatcher).FullName }, dependents);
    }
}
