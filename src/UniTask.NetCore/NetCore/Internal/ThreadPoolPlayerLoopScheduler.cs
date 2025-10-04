using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Cysharp.Threading.Tasks.Internal
{
    internal sealed class ThreadPoolPlayerLoopScheduler : IPlayerLoopRunnerScheduler
    {
        private static readonly WaitCallback Callback = state => ((WorkItem)state).Run();

        public SynchronizationContext SynchronizationContext => null;

        public int? ThreadId => null;

        public void Schedule(Action<object> action, object state)
        {
            var item = WorkItem.Create(action, state);
            ThreadPool.UnsafeQueueUserWorkItem(Callback, item);
        }

        private sealed class WorkItem : ITaskPoolNode<WorkItem>
        {
            private static TaskPool<WorkItem> pool;
            private WorkItem nextNode;
            private Action<object> action;
            private object state;

            public ref WorkItem NextNode => ref nextNode;

            static WorkItem()
            {
                TaskPool.RegisterSizeGetter(typeof(WorkItem), () => pool.Size);
            }

            public static WorkItem Create(Action<object> action, object state)
            {
                if (!pool.TryPop(out var workItem))
                {
                    workItem = new WorkItem();
                }

                workItem.action = action;
                workItem.state = state;
                return workItem;
            }

            public void Run()
            {
                var callback = action;
                var callbackState = state;
                action = null;
                state = null;

                try
                {
                    callback(callbackState);
                }
                finally
                {
                    pool.TryPush(this);
                }
            }
        }
    }
}
