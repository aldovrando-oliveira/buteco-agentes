using System.Net;
using Buteco.Inbox.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Buteco.Inbox.Tests;

/// <summary>
/// As fixtures padrão anexam o token do emissor de teste (design.md da change
/// fixtures-token-emissor, D2), e o primeiro pedido só passa depois da primeira obtenção das
/// chaves. Com as chaves seguras pelo <see cref="TestIssuer"/> do início ao fim, a
/// inicialização de uma fixture que espera termina sempre em <see cref="TimeoutException"/>;
/// a de uma fixture sem a espera terminaria bem, e o primeiro pedido dela responderia
/// <c>503</c>, sempre. A espera é encurtada só aqui, para o caso não custar 15 s por fixture.
/// </summary>
public class FixtureIssuerKeysWaitTests
{
    public static TheoryData<string> Fixtures => new()
    {
        nameof(InboxFactoryFixture),
        nameof(OrchestrationFactoryFixture),
        nameof(ReconciliationFactoryFixture),
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Initialize_WithTheKeysHeld_DoesNotFinishWithoutThem(string name)
    {
        var (fixture, issuer, setWait) = Create(name);
        var lifetime = (IAsyncLifetime)fixture;
        try
        {
            issuer.HoldKeys();
            setWait(TimeSpan.FromSeconds(2));

            var failure = await Record.ExceptionAsync(() => lifetime.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(60)));

            if (failure is null)
            {
                var response = await fixture.CreateClient().GetAsync("/channels");
                Assert.Fail($"A fixture terminou a inicialização com as chaves do emissor seguras; o primeiro pedido respondeu {(int)response.StatusCode}.");
            }

            var timeout = Assert.IsType<TimeoutException>(failure);
            Assert.Equal("as chaves do emissor de teste não foram obtidas", timeout.Message);
            Assert.True(issuer.RequestedWhileHeld.IsCompleted, "a fixture não pediu as chaves ao emissor de teste");
        }
        finally
        {
            issuer.ReleaseKeys();
            await lifetime.DisposeAsync();
            await fixture.DisposeAsync();
        }
    }

    private static (WebApplicationFactory<Program> Fixture, TestIssuer Issuer, Action<TimeSpan> SetWait) Create(string name)
    {
        switch (name)
        {
            case nameof(InboxFactoryFixture):
                var inbox = new InboxFactoryFixture();
                return (inbox, inbox.Issuer, wait => inbox.IssuerKeysWait = wait);
            case nameof(OrchestrationFactoryFixture):
                var orchestration = new OrchestrationFactoryFixture();
                return (orchestration, orchestration.Issuer, wait => orchestration.IssuerKeysWait = wait);
            case nameof(ReconciliationFactoryFixture):
                var reconciliation = new ReconciliationFactoryFixture();
                return (reconciliation, reconciliation.Issuer, wait => reconciliation.IssuerKeysWait = wait);
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }
}
