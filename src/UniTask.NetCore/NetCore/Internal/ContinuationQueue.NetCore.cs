using System;
using System.Buffers;
using System.Threading;

namespace Cysharp.Threading.Tasks.Internal
{
    internal sealed class ContinuationQueue
    {
        private const int InitialSize = 16;
        private static readonly Action<object> RunDelegate = state => ((ContinuationQueue)state).RunCore();

        private readonly PlayerLoopTiming timing;
        private readonly IPlayerLoopRunnerScheduler scheduler;
        private MinimumQueue<Action> queue = new MinimumQueue<Action>(InitialSize);
        private SpinLock gate = new SpinLock(false);
        private bool scheduled;

        public ContinuationQueue(PlayerLoopTiming timing, IPlayerLoopRunnerScheduler scheduler)
        {
            this.timing = timing;
            this.scheduler = scheduler;
        }

        public void Enqueue(Action continuation)
        {
            if (continuation == null) throw new ArgumentNullException(nameof(continuation));

            var shouldSchedule = false;
            bool lockTaken = false;
            try
            {
                gate.Enter(ref lockTaken);
                queue.Enqueue(continuation);
                if (!scheduled)
                {
                    scheduled = true;
                    shouldSchedule = true;
                }
            }
            finally
            {
                if (lockTaken) gate.Exit(false);
            }

            if (shouldSchedule)
            {
                scheduler.Schedule(RunDelegate, this);
            }
        }

        private void RunCore()
        {
            while (true)
            {
                Action[] items;
                int count;

                bool lockTaken = false;
                try
                {
                    gate.Enter(ref lockTaken);
                    count = queue.Count;
                    if (count == 0)
                    {
                        scheduled = false;
                        return;
                    }

                    items = ArrayPool<Action>.Shared.Rent(count);
                    for (var i = 0; i < count; i++)
                    {
                        items[i] = queue.Dequeue();
                    }
                }
                finally
                {
                    if (lockTaken) gate.Exit(false);
                }

                for (var i = 0; i < count; i++)
                {
                    var action = items[i];
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        UniTaskScheduler.PublishUnobservedTaskException(ex);
                    }
                    items[i] = null;
                }

                ArrayPool<Action>.Shared.Return(items, true);
            }
        }
    }
}
