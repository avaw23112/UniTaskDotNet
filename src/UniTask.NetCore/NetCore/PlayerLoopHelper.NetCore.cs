using System;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;

namespace Cysharp.Threading.Tasks
{
    public static class PlayerLoopHelper
    {
        private static readonly object initLock = new object();
        private static bool initialized;
        private static SynchronizationContext unitySynchronizationContext;
        private static int mainThreadId;
        private static string applicationDataPath = AppContext.BaseDirectory;
        private static ContinuationQueue[] yielders;
        private static PlayerLoopRunner[] runners;
        private static IPlayerLoopRunnerScheduler scheduler;

        static PlayerLoopHelper()
        {
            Initialize();
        }

        public static SynchronizationContext UnitySynchronizationContext => unitySynchronizationContext;

        public static int MainThreadId => mainThreadId;

        internal static string ApplicationDataPath => applicationDataPath;

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == mainThreadId;

        public static void Initialize(IPlayerLoopRunnerScheduler customScheduler = null, SynchronizationContext synchronizationContext = null, int? mainThreadOverride = null)
        {
            lock (initLock)
            {
                scheduler = customScheduler ?? new ThreadPoolPlayerLoopScheduler();
                unitySynchronizationContext = synchronizationContext ?? scheduler.SynchronizationContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
                mainThreadId = mainThreadOverride ?? scheduler.ThreadId ?? Thread.CurrentThread.ManagedThreadId;

                var timingCount = Enum.GetValues(typeof(PlayerLoopTiming)).Length;
                yielders = new ContinuationQueue[timingCount];
                runners = new PlayerLoopRunner[timingCount];

                for (var i = 0; i < timingCount; i++)
                {
                    var timing = (PlayerLoopTiming)i;
                    yielders[i] = new ContinuationQueue(timing, scheduler);
                    runners[i] = new PlayerLoopRunner(timing, scheduler);
                }

                initialized = true;
            }
        }

        public static void AddAction(PlayerLoopTiming timing, IPlayerLoopItem action)
        {
            EnsureInitialized();
            var runner = runners[(int)timing];
            if (runner == null)
            {
                throw new InvalidOperationException($"PlayerLoopRunner is not available for timing {timing}.");
            }

            runner.AddAction(action);
        }

        public static void AddContinuation(PlayerLoopTiming timing, Action continuation)
        {
            EnsureInitialized();
            var queue = yielders[(int)timing];
            if (queue == null)
            {
                throw new InvalidOperationException($"Continuation queue is not available for timing {timing}.");
            }

            queue.Enqueue(continuation);
        }

        private static void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }
    }

    public enum PlayerLoopTiming
    {
        Initialization = 0,
        LastInitialization = 1,
        EarlyUpdate = 2,
        LastEarlyUpdate = 3,
        FixedUpdate = 4,
        LastFixedUpdate = 5,
        PreUpdate = 6,
        LastPreUpdate = 7,
        Update = 8,
        LastUpdate = 9,
        PreLateUpdate = 10,
        LastPreLateUpdate = 11,
        PostLateUpdate = 12,
        LastPostLateUpdate = 13,
        TimeUpdate = 14,
        LastTimeUpdate = 15
    }

    public interface IPlayerLoopItem
    {
        bool MoveNext();
    }
}
