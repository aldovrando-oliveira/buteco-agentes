using System.Collections.Concurrent;
using Buteco.Api.Messaging;

namespace Buteco.Api.Tests.Support;

public sealed class FakeTaskJobPublisher : ITaskJobPublisher
{
    private readonly ConcurrentQueue<TaskJobMessage> _publishedMessages = new();

    public IReadOnlyCollection<TaskJobMessage> PublishedMessages => _publishedMessages.ToArray();

    public Task PublishAsync(TaskJobMessage message, CancellationToken cancellationToken = default)
    {
        _publishedMessages.Enqueue(message);
        return Task.CompletedTask;
    }
}
