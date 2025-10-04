using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks.Internal;

namespace Cysharp.Threading.Tasks
{
    public abstract class PlayerLoopTimer : IDisposable, IPlayerLoopItem
    {
        private readonly CancellationToken cancellationToken;
        private readonly Action<object> timerCallback;
        private readonly object state;
        private readonly PlayerLoopTiming playerLoopTiming;
        private readonly bool periodic;

        private bool tryStop;
        private bool isDisposed;

        protected PlayerLoopTimer(bool periodic, PlayerLoopTiming playerLoopTiming, CancellationToken cancellationToken, Action<object> timerCallback, object state)
        {
            this.periodic = periodic;
            this.playerLoopTiming = playerLoopTiming;
            this.cancellationToken = cancellationToken;
            this.timerCallback = timerCallback;
            this.state = state;
        }

        public static PlayerLoopTimer Create(TimeSpan interval, bool periodic, DelayType delayType, PlayerLoopTiming playerLoopTiming, CancellationToken cancellationToken, Action<object> timerCallback, object state)
        {
            _ = delayType;
            return new StopwatchPlayerLoopTimer(interval, periodic, playerLoopTiming, cancellationToken, timerCallback, state);
        }

        public static PlayerLoopTimer StartNew(TimeSpan interval, bool periodic, DelayType delayType, PlayerLoopTiming playerLoopTiming, CancellationToken cancellationToken, Action<object> timerCallback, object state)
        {
            var timer = Create(interval, periodic, delayType, playerLoopTiming, cancellationToken, timerCallback, state);
            timer.Restart();
            return timer;
        }

        public void Restart()
        {
            if (isDisposed) throw new ObjectDisposedException(null);
            ResetCore(null);
            tryStop = false;
            PlayerLoopHelper.AddAction(playerLoopTiming, this);
        }

        public void Restart(TimeSpan interval)
        {
            if (isDisposed) throw new ObjectDisposedException(null);
            ResetCore(interval);
            tryStop = false;
            PlayerLoopHelper.AddAction(playerLoopTiming, this);
        }

        public void Stop()
        {
            tryStop = true;
        }

        protected abstract void ResetCore(TimeSpan? newInterval);

        public void Dispose()
        {
            isDisposed = true;
        }

        bool IPlayerLoopItem.MoveNext()
        {
            if (isDisposed)
            {
                return false;
            }

            if (tryStop)
            {
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!MoveNextCore())
            {
                timerCallback(state);

                if (periodic)
                {
                    ResetCore(null);
                    PlayerLoopHelper.AddAction(playerLoopTiming, this);
                    return false;
                }

                return false;
            }

            PlayerLoopHelper.AddAction(playerLoopTiming, this);
            return false;
        }

        protected abstract bool MoveNextCore();
    }

    internal sealed class StopwatchPlayerLoopTimer : PlayerLoopTimer
    {
        private TimeSpan interval;
        private readonly Stopwatch stopwatch = new Stopwatch();

        public StopwatchPlayerLoopTimer(TimeSpan interval, bool periodic, PlayerLoopTiming playerLoopTiming, CancellationToken cancellationToken, Action<object> timerCallback, object state)
            : base(periodic, playerLoopTiming, cancellationToken, timerCallback, state)
        {
            ResetCore(interval);
        }

        protected override bool MoveNextCore()
        {
            return stopwatch.Elapsed < interval;
        }

        protected override void ResetCore(TimeSpan? newInterval)
        {
            if (newInterval.HasValue)
            {
                interval = newInterval.Value;
            }

            stopwatch.Restart();
        }
    }
}
