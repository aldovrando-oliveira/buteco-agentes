namespace Buteco.Workers.Messaging;

public interface ITaskJobPublisher
{
    Task PublishAsync(TaskJobMessage message, CancellationToken cancellationToken = default);
}
