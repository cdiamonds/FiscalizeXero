using System.Threading.Channels;

namespace FiscalizeXero.Api.Services.Webhooks
{
    /// <summary>Hands stored WebhookEvent ids from the receiving controller to the background processor.</summary>
    public interface IWebhookQueue
    {
        void Enqueue(long webhookEventId);
        IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct);
    }

    public class WebhookQueue : IWebhookQueue
    {
        private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        public void Enqueue(long webhookEventId) => _channel.Writer.TryWrite(webhookEventId);

        public IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    }
}
