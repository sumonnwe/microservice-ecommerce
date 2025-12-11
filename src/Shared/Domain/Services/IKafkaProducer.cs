using System.Threading.Tasks;

namespace Shared.Domain.Services
{
    public interface IKafkaProducer
    {
        Task ProduceAsync(string topic, string payload);
    }
}
