using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace GunGame.Stats
{
    public class DatabaseOperationQueue
    {
        private ConcurrentQueue<Func<Task>> _operationsQueue = new ConcurrentQueue<Func<Task>>();
        private SemaphoreSlim _signal = new SemaphoreSlim(0);
        private Task _worker;
        private bool _running = true;
        private GunGame Plugin;
        public DatabaseOperationQueue(GunGame plugin)
        {
            Plugin = plugin;
            // Start the worker task
            _worker = Task.Run(ProcessQueueAsync);
        }

        public void EnqueueOperation(Func<Task> operation)
        {
            _operationsQueue.Enqueue(operation);
            _signal.Release();
        }

        private async Task ProcessQueueAsync()
        {
            while (_running)
            {
                await _signal.WaitAsync();

                if (_operationsQueue.TryDequeue(out Func<Task>? operation) && operation != null)
                {
                    try
                    {
                        Server.NextFrame(async () => {
                            await operation();
                        });
                        
                    }
                    catch (Exception ex)
                    {
                        // Handle exception (e.g., log error)
                        Console.WriteLine($"******************* Database operation failed: {ex.Message}");
                        Server.NextFrame( () => 
                        {
                            Plugin.Logger.LogError($"ProcessQueueAsync: Database operation failed: {ex.Message}");
                        });
                    }
                }
            }
        }
        public void Stop()
        {
            _running = false;
            _signal.Release(); // Ensure the worker can exit if it's waiting
        }
    }
}
