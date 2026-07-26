namespace Buteco.Api.Messaging;

public interface ITaskJobPublisher
{
    Task PublishAsync(TaskJobMessage message, CancellationToken cancellationToken = default);
}
