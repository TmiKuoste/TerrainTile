using Azure.Messaging.ServiceBus;

namespace BuilderServices
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Receiving message from Azure Service Bus...");

            string? connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
            if (string.IsNullOrEmpty(connectionString))
            {
                Console.WriteLine("Connection string is not set.");
                return;
            }

            string? queueName = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_SB_QUEUE_NAME");
            if (string.IsNullOrEmpty(queueName))
            {
                Console.WriteLine("Queue name is not set.");
                return;
            }

            await using ServiceBusClient client = new(connectionString);
            await using ServiceBusReceiver receiver = client.CreateReceiver(queueName);

            try
            {
                // Try to receive a single message
                var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(10));
                if (message != null)
                {
                    string body = message.Body.ToString();
                    Console.WriteLine($"Received: {body}");

                    // Complete the message (remove from queue)
                    await receiver.CompleteMessageAsync(message);
                }
                else
                {
                    Console.WriteLine("No message received.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex}");
            }
        }
    }
}
