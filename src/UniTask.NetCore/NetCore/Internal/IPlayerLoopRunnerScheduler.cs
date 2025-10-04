using System;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    public interface IPlayerLoopRunnerScheduler
    {
        SynchronizationContext SynchronizationContext { get; }
        int? ThreadId { get; }
        void Schedule(Action<object> action, object state);
    }
}
