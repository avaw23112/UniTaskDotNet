using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;

namespace Cysharp.Threading.Tasks
{
    public enum DelayType
    {
        DeltaTime,
        UnscaledDeltaTime,
        Realtime
    }

    public partial struct UniTask
    {
        public static YieldAwaitable Yield()
        {
            return new YieldAwaitable(PlayerLoopTiming.Update);
        }

        public static YieldAwaitable Yield(PlayerLoopTiming timing)
        {
            return new YieldAwaitable(timing);
        }

        public static UniTask Yield(CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return new UniTask(YieldPromise.Create(PlayerLoopTiming.Update, cancellationToken, cancelImmediately, out var token), token);
        }

        public static UniTask Yield(PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return new UniTask(YieldPromise.Create(timing, cancellationToken, cancelImmediately, out var token), token);
        }

        public static UniTask Delay(int millisecondsDelay, bool ignoreTimeScale = false, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            _ = ignoreTimeScale;
            return Delay(TimeSpan.FromMilliseconds(millisecondsDelay), delayTiming: delayTiming, cancellationToken: cancellationToken, cancelImmediately: cancelImmediately);
        }

        public static UniTask Delay(TimeSpan delayTimeSpan, bool ignoreTimeScale = false, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            _ = ignoreTimeScale;
            return Delay(delayTimeSpan, DelayType.DeltaTime, delayTiming, cancellationToken, cancelImmediately);
        }

        public static UniTask Delay(int millisecondsDelay, DelayType delayType = DelayType.DeltaTime, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return Delay(TimeSpan.FromMilliseconds(millisecondsDelay), delayType, delayTiming, cancellationToken, cancelImmediately);
        }

        public static UniTask Delay(TimeSpan delayTimeSpan, DelayType delayType = DelayType.DeltaTime, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            _ = delayType;
            return new UniTask(DelayPromise.Create(delayTimeSpan, delayTiming, cancellationToken, cancelImmediately, out var token), token);
        }

        public static UniTask WaitForSeconds(float duration, bool ignoreTimeScale = false, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale, delayTiming, cancellationToken, cancelImmediately);
        }

        public static UniTask WaitForSeconds(int duration, bool ignoreTimeScale = false, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return Delay(TimeSpan.FromSeconds(duration), ignoreTimeScale, delayTiming, cancellationToken, cancelImmediately);
        }

        public static UniTask NextFrame(PlayerLoopTiming timing = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return new UniTask(DelayFramePromise.Create(1, timing, cancellationToken, cancelImmediately, out var token), token);
        }

        public static UniTask DelayFrame(int delayFrameCount, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update, CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            if (delayFrameCount < 0) throw new ArgumentOutOfRangeException(nameof(delayFrameCount));
            return new UniTask(DelayFramePromise.Create(delayFrameCount, delayTiming, cancellationToken, cancelImmediately, out var token), token);
        }

        public static YieldAwaitable WaitForEndOfFrame()
        {
            return Yield(PlayerLoopTiming.LastPostLateUpdate);
        }

        public static UniTask WaitForEndOfFrame(CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return Yield(PlayerLoopTiming.LastPostLateUpdate, cancellationToken, cancelImmediately);
        }

        public static YieldAwaitable WaitForFixedUpdate()
        {
            return Yield(PlayerLoopTiming.LastFixedUpdate);
        }

        public static UniTask WaitForFixedUpdate(CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return Yield(PlayerLoopTiming.LastFixedUpdate, cancellationToken, cancelImmediately);
        }

        public readonly struct YieldAwaitable
        {
            private readonly PlayerLoopTiming timing;

            public YieldAwaitable(PlayerLoopTiming timing)
            {
                this.timing = timing;
            }

            public Awaiter GetAwaiter()
            {
                return new Awaiter(timing);
            }

            public readonly struct Awaiter : ICriticalNotifyCompletion
            {
                private readonly PlayerLoopTiming timing;

                public Awaiter(PlayerLoopTiming timing)
                {
                    this.timing = timing;
                }

                public bool IsCompleted => false;

                public void GetResult()
                {
                }

                public void OnCompleted(Action continuation)
                {
                    UnsafeOnCompleted(continuation);
                }

                public void UnsafeOnCompleted(Action continuation)
                {
                    if (continuation == null) throw new ArgumentNullException(nameof(continuation));
                    PlayerLoopHelper.AddContinuation(timing, continuation);
                }
            }
        }

        sealed class YieldPromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<YieldPromise>
        {
            static TaskPool<YieldPromise> pool;
            YieldPromise nextNode;
            public ref YieldPromise NextNode => ref nextNode;

            CancellationToken cancellationToken;
            bool cancelImmediately;
            CancellationTokenRegistration cancellationTokenRegistration;
            UniTaskCompletionSourceCore<AsyncUnit> core;
            PlayerLoopTiming timing;

            static YieldPromise()
            {
                TaskPool.RegisterSizeGetter(typeof(YieldPromise), () => pool.Size);
            }

            public static IUniTaskSource Create(PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancelImmediately && cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!pool.TryPop(out var promise))
                {
                    promise = new YieldPromise();
                }

                promise.timing = timing;
                promise.cancellationToken = cancellationToken;
                promise.cancelImmediately = cancelImmediately;

                if (cancellationToken.CanBeCanceled)
                {
                    promise.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(static state =>
                    {
                        ((YieldPromise)state).OnCanceled();
                    }, promise);
                }

                TaskTracker.TrackActiveTask(promise, 3);
                PlayerLoopHelper.AddAction(timing, promise);

                token = promise.core.Version;
                return promise;
            }

            void OnCanceled()
            {
                if (cancelImmediately)
                {
                    PlayerLoopHelper.AddContinuation(timing, CancelCore);
                }
            }

            public bool MoveNext()
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    PlayerLoopHelper.AddContinuation(timing, CancelCore);
                    return false;
                }

                PlayerLoopHelper.AddContinuation(timing, CompleteCore);
                return false;
            }

            void CompleteCore()
            {
                core.TrySetResult(AsyncUnit.Default);
                TryReturn();
            }

            void CancelCore()
            {
                core.TrySetCanceled(cancellationToken);
                TryReturn();
            }

            public void GetResult(short token)
            {
                try
                {
                    core.GetResult(token);
                }
                finally
                {
                    cancellationTokenRegistration.Dispose();
                }
            }

            public UniTaskStatus GetStatus(short token)
            {
                return core.GetStatus(token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return core.UnsafeGetStatus();
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                core.OnCompleted(continuation, state, token);
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                core.Reset();
                cancellationToken = default;
                cancellationTokenRegistration.Dispose();
                cancelImmediately = false;
                timing = PlayerLoopTiming.Update;
                return pool.TryPush(this);
            }
        }

        sealed class DelayPromise : IUniTaskSource, ITaskPoolNode<DelayPromise>
        {
            static TaskPool<DelayPromise> pool;
            DelayPromise nextNode;
            public ref DelayPromise NextNode => ref nextNode;

            TimeSpan delay;
            PlayerLoopTiming timing;
            CancellationToken cancellationToken;
            bool cancelImmediately;
            bool cancellationRequested;
            CancellationTokenRegistration cancellationTokenRegistration;
            Timer timer;
            UniTaskCompletionSourceCore<AsyncUnit> core;

            static DelayPromise()
            {
                TaskPool.RegisterSizeGetter(typeof(DelayPromise), () => pool.Size);
            }

            public static IUniTaskSource Create(TimeSpan delay, PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancelImmediately && cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (delay <= TimeSpan.Zero)
                {
                    return AutoResetUniTaskCompletionSource.CreateCompleted(out token);
                }

                if (!pool.TryPop(out var promise))
                {
                    promise = new DelayPromise();
                }

                promise.delay = delay;
                promise.timing = timing;
                promise.cancellationToken = cancellationToken;
                promise.cancelImmediately = cancelImmediately;
                promise.cancellationRequested = cancellationToken.IsCancellationRequested;

                if (cancellationToken.CanBeCanceled)
                {
                    promise.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(static state =>
                    {
                        ((DelayPromise)state).OnCanceled();
                    }, promise);
                }

                promise.timer = new Timer(static state => ((DelayPromise)state).OnTimer(), promise, delay, Timeout.InfiniteTimeSpan);

                TaskTracker.TrackActiveTask(promise, 3);
                token = promise.core.Version;
                return promise;
            }

            void OnCanceled()
            {
                cancellationRequested = true;
                if (cancelImmediately)
                {
                    var t = Interlocked.Exchange(ref timer, null);
                    t?.Dispose();
                    PlayerLoopHelper.AddContinuation(timing, CancelCore);
                }
            }

            void OnTimer()
            {
                cancellationTokenRegistration.Dispose();
                var t = Interlocked.Exchange(ref timer, null);
                t?.Dispose();

                if (cancellationRequested)
                {
                    PlayerLoopHelper.AddContinuation(timing, CancelCore);
                }
                else
                {
                    PlayerLoopHelper.AddContinuation(timing, CompleteCore);
                }
            }

            void CompleteCore()
            {
                core.TrySetResult(AsyncUnit.Default);
                TryReturn();
            }

            void CancelCore()
            {
                core.TrySetCanceled(cancellationToken);
                TryReturn();
            }

            public void GetResult(short token)
            {
                try
                {
                    core.GetResult(token);
                }
                finally
                {
                    cancellationTokenRegistration.Dispose();
                    var t = Interlocked.Exchange(ref timer, null);
                    t?.Dispose();
                }
            }

            public UniTaskStatus GetStatus(short token)
            {
                return core.GetStatus(token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return core.UnsafeGetStatus();
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                core.OnCompleted(continuation, state, token);
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                core.Reset();
                cancellationToken = default;
                cancellationTokenRegistration.Dispose();
                cancelImmediately = false;
                cancellationRequested = false;
                delay = TimeSpan.Zero;
                timing = PlayerLoopTiming.Update;
                var t = Interlocked.Exchange(ref timer, null);
                t?.Dispose();
                return pool.TryPush(this);
            }
        }

        sealed class DelayFramePromise : IUniTaskSource, IPlayerLoopItem, ITaskPoolNode<DelayFramePromise>
        {
            static TaskPool<DelayFramePromise> pool;
            DelayFramePromise nextNode;
            public ref DelayFramePromise NextNode => ref nextNode;

            int initialFrame;
            int delayFrameCount;
            PlayerLoopTiming timing;
            CancellationToken cancellationToken;
            bool cancelImmediately;
            CancellationTokenRegistration cancellationTokenRegistration;
            UniTaskCompletionSourceCore<AsyncUnit> core;

            static DelayFramePromise()
            {
                TaskPool.RegisterSizeGetter(typeof(DelayFramePromise), () => pool.Size);
            }

            public static IUniTaskSource Create(int delayFrameCount, PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately, out short token)
            {
                if (cancelImmediately && cancellationToken.IsCancellationRequested)
                {
                    return AutoResetUniTaskCompletionSource.CreateFromCanceled(cancellationToken, out token);
                }

                if (!pool.TryPop(out var promise))
                {
                    promise = new DelayFramePromise();
                }

                promise.delayFrameCount = delayFrameCount;
                promise.timing = timing;
                promise.cancellationToken = cancellationToken;
                promise.cancelImmediately = cancelImmediately;
                promise.initialFrame = 0;

                if (cancellationToken.CanBeCanceled)
                {
                    promise.cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(static state =>
                    {
                        ((DelayFramePromise)state).OnCanceled();
                    }, promise);
                }

                TaskTracker.TrackActiveTask(promise, 3);
                PlayerLoopHelper.AddAction(timing, promise);

                token = promise.core.Version;
                return promise;
            }

            void OnCanceled()
            {
                if (cancelImmediately)
                {
                    PlayerLoopHelper.AddContinuation(timing, CancelCore);
                }
            }

            public bool MoveNext()
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    PlayerLoopHelper.AddContinuation(timing, CancelCore);
                    return false;
                }

                if (initialFrame < delayFrameCount)
                {
                    initialFrame++;
                    return true;
                }

                PlayerLoopHelper.AddContinuation(timing, CompleteCore);
                return false;
            }

            void CompleteCore()
            {
                core.TrySetResult(AsyncUnit.Default);
                TryReturn();
            }

            void CancelCore()
            {
                core.TrySetCanceled(cancellationToken);
                TryReturn();
            }

            public void GetResult(short token)
            {
                try
                {
                    core.GetResult(token);
                }
                finally
                {
                    cancellationTokenRegistration.Dispose();
                }
            }

            public UniTaskStatus GetStatus(short token)
            {
                return core.GetStatus(token);
            }

            public UniTaskStatus UnsafeGetStatus()
            {
                return core.UnsafeGetStatus();
            }

            public void OnCompleted(Action<object> continuation, object state, short token)
            {
                core.OnCompleted(continuation, state, token);
            }

            bool TryReturn()
            {
                TaskTracker.RemoveTracking(this);
                core.Reset();
                cancellationToken = default;
                cancellationTokenRegistration.Dispose();
                cancelImmediately = false;
                initialFrame = 0;
                timing = PlayerLoopTiming.Update;
                delayFrameCount = 0;
                return pool.TryPush(this);
            }
        }
    }
}
