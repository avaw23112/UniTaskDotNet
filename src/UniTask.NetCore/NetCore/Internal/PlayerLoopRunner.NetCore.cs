using System;
using System.Collections.Generic;
using System.Threading;

namespace Cysharp.Threading.Tasks.Internal
{
    internal sealed class PlayerLoopRunner
    {
        private const int InitialSize = 16;
        private static readonly Action<object> RunDelegate = state => ((PlayerLoopRunner)state).Run();

        private readonly IPlayerLoopRunnerScheduler scheduler;
        private readonly List<IPlayerLoopItem> processingList = new List<IPlayerLoopItem>(InitialSize);
        private MinimumQueue<IPlayerLoopItem> queue = new MinimumQueue<IPlayerLoopItem>(InitialSize);
        private SpinLock gate = new SpinLock(false);
        private bool queued;

        public PlayerLoopRunner(PlayerLoopTiming timing, IPlayerLoopRunnerScheduler scheduler)
        {
            this.scheduler = scheduler;
        }

        public void AddAction(IPlayerLoopItem action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var shouldSchedule = false;
            bool lockTaken = false;
            try
            {
                gate.Enter(ref lockTaken);
                queue.Enqueue(action);
                if (!queued)
                {
                    queued = true;
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

        public int Clear()
        {
            var count = 0;
            bool lockTaken = false;
            try
            {
                gate.Enter(ref lockTaken);
                count = queue.Count;
                queue = new MinimumQueue<IPlayerLoopItem>(InitialSize);
                queued = false;
            }
            finally
            {
                if (lockTaken) gate.Exit(false);
            }

            return count;
        }

        private void Run()
        {
            while (true)
            {
                processingList.Clear();

                bool lockTaken = false;
                try
                {
                    gate.Enter(ref lockTaken);
                    var count = queue.Count;
                    if (count == 0)
                    {
                        queued = false;
                        return;
                    }

                    for (var i = 0; i < count; i++)
                    {
                        processingList.Add(queue.Dequeue());
                    }
                }
                finally
                {
                    if (lockTaken) gate.Exit(false);
                }

                foreach (var item in processingList)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var repeat = false;
                    try
                    {
                        repeat = item.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        UniTaskScheduler.PublishUnobservedTaskException(ex);
                    }

                    if (repeat)
                    {
                        bool requeueLock = false;
                        try
                        {
                            gate.Enter(ref requeueLock);
                            queue.Enqueue(item);
                        }
                        finally
                        {
                            if (requeueLock) gate.Exit(false);
                        }
                    }
                }

                processingList.Clear();

                var hasPending = false;
                lockTaken = false;
                try
                {
                    gate.Enter(ref lockTaken);
                    hasPending = queue.Count > 0;
                    if (!hasPending)
                    {
                        queued = false;
                    }
                }
                finally
                {
                    if (lockTaken) gate.Exit(false);
                }

                if (hasPending)
                {
                    scheduler.Schedule(RunDelegate, this);
                }

                return;
            }
        }
    }
}
