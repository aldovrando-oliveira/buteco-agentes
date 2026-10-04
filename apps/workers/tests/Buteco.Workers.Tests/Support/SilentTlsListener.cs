using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Buteco.Workers.Tests.Support;

/// <summary>
/// Servidor em loopback que ACEITA a conexão TCP e nunca escreve nada: um cliente
/// <c>https://</c> fica parado no handshake TLS, esperando um ServerHello que não
/// vem. É a forma de exercitar o timeout de conexão sem depender da rede da
/// máquina (change timeout-de-conexao-saida-workers, D5).
///
/// <para>
/// <b>Por que serve de substituto para o SYN sem resposta do defeito real:</b> o
/// <c>ConnectTimeout</c> do <c>SocketsHttpHandler</c> cobre TCP E TLS. Medido em
/// 03/10/2026 com o mesmo handler: TLS mudo cortado em 3,07 s e SYN pendurado em
/// 3,01 s, ambos com a mesma <c>TimeoutException</c>. Um destino de SYN sem resposta
/// (<c>10.255.255.1</c>) depende da rede de quem roda a suíte: numa VPN ou rede
/// corporativa a faixa privada pode existir, e o guarda passaria por acaso.
/// </para>
/// </summary>
public sealed class SilentTlsListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<TcpClient> _accepted = new();
    private readonly CancellationTokenSource _stop = new();

    public SilentTlsListener()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string Url => $"https://127.0.0.1:{Port}";

    /// <summary>Conexões TCP aceitas até agora, uma por tentativa do cliente.</summary>
    public int AcceptedConnections => _accepted.Count;

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                // Guardado para o socket continuar aberto (e mudo) até o Dispose.
                _accepted.Enqueue(await _listener.AcceptTcpClientAsync(_stop.Token));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        while (_accepted.TryDequeue(out var client))
        {
            client.Dispose();
        }

        _stop.Dispose();
    }
}
