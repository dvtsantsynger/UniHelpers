using System;

namespace UniHelpers.Concurent { 
    public abstract class Task
    {
        public abstract void Wait();

        public abstract void Wait(TimeSpan timeout);

        public abstract T Wait<T>();

        public abstract T Wait<T>(TimeSpan timeout);

        public abstract void Cancel(bool interruptIfRunning);

        public abstract bool IsCancelled();

        public abstract bool IsDone();

        public abstract Task ContinueWith<T>(Func<T> action);

        public abstract Task ContinueWith(Action action);
    }
}