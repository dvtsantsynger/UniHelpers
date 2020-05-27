using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UniHelpers.Concurent
{
    public sealed class Void {
        static Void empty = new Void();

        public static Void Empty => empty;
        private Void() { }
    }

    public abstract class ExecutorService
    {
        public abstract ExecutorService Start(string threadName, int stackSize);

        public abstract void Shutdown();

        public abstract Task Schedule<T>(Func<T> action);

        public abstract Task Schedule<T>(Func<T> action, TimeSpan delay);

        public abstract Task Schedule<T>(Func<T> action, TimeSpan delay, TimeSpan period);

        public Task Schedule(Action action)
        {
            return Schedule<Void>(() =>
            {
                action?.Invoke();
                return Void.Empty;
            });
        }

        public Task Schedule(Action action, TimeSpan delay) {
            return Schedule<Void>(() =>
            {
                action?.Invoke();
                return Void.Empty;
            }, delay);
        }

        public Task Schedule(Action action, TimeSpan delay, TimeSpan period) {
            return Schedule<Void>(() =>
            {
                action?.Invoke();
                return Void.Empty;
            }, delay, period);
        }

        public static ExecutorService CreateNew() { return new EventLoopThread(); }

        private class TaskImpl : Task
        {
            public bool InterruptIfRunning { get; private set; }
            private ManualResetEvent manualEvent = new ManualResetEvent(false);
            private object Result;

            public void Set(object value)
            {
                Result = value;
                manualEvent.Set();
            }

            public void Reset()
            {
                Result = null;
                manualEvent.Reset();
            }

            public override void Cancel(bool interruptIfRunning)
            {
                InterruptIfRunning = interruptIfRunning;
                manualEvent.Dispose();
                manualEvent = null;
            }

            public override bool IsCancelled()
            {
                return manualEvent == null;
            }

            public override bool IsDone()
            {
                return manualEvent.WaitOne(0);
            }

            public override T Wait<T>()
            {
                manualEvent.WaitOne();
                return (T)Result;
            }

            public override T Wait<T>(TimeSpan timeout)
            {
                manualEvent.WaitOne(timeout.Milliseconds);
                return (T)Result;
            }

            public override void Wait()
            {
                manualEvent.WaitOne();
            }

            public override void Wait(TimeSpan timeout)
            {
                manualEvent.WaitOne(timeout.Milliseconds);
            }
        }


        private struct EventWithState
        {
            public Func<object> action;
            public long time;
            public TaskImpl task;
        }

        private class EventLoopThread : ExecutorService
        {
            [ThreadStatic]
            private static ExecutorService mCurrent;

            private EventWithState[] mEvents = new EventWithState[16];

            protected int head = 0;
            protected int tail = 0;
            protected int size = 0;

            private object mForeignLock = new object();

            private Thread mThread;

            private volatile bool mActive = false;

            private LinkedList<EventWithState> mScheduled = new LinkedList<EventWithState>();

            public bool IsEmpty { get { return size == 0; } }
            public int Count { get { return size; } }
            public int Capacity { get { return mEvents.Length; } }

            protected void addToBuffer(Func<object> action, TaskImpl task)
            {
                size++;
                mEvents[tail].action = action;
                mEvents[tail].task = task;
                mEvents[tail].time = -1;
                tail = (tail + 1) % Capacity;
            }

            private void PushScheduled(Func<object> action, long time, TaskImpl task)
            {
                if (action == null) return;
                lock (mForeignLock)
                {
                    EventWithState e = new EventWithState();
                    e.action = action;
                    e.time = time;
                    e.task = task;

                    if (mScheduled.Count == 0 || mScheduled.First.Value.time > time)
                    {
                        mScheduled.AddFirst(e);
                        return;
                    }
                    else if (mScheduled.Last.Value.time < time)
                    {
                        mScheduled.AddLast(e);
                        return;
                    }
                    else
                    {
                        var currentNode = mScheduled.First;
                        while (currentNode != null)
                        {
                            if (currentNode.Value.time > time) break;
                            currentNode = currentNode.Next;
                        }
                        mScheduled.AddBefore(currentNode, e);
                    }
                }
            }

            private void Push(Func<object> action, TaskImpl task)
            {
                if (action == null) return;
                lock (mForeignLock)
                {
                    // If tail & head are equal and the buffer is not empty, assume
                    // that it would overflow and expand the capacity before adding the
                    // item.
                    if (tail == head && size != 0)
                    {
                        EventWithState[] _newArray = new EventWithState[mEvents.Length << 1];
                        for (int i = 0; i < Capacity; i++)
                        {
                            _newArray[i] = mEvents[i];
                        }
                        mEvents = _newArray;
                        tail = (head + size) % Capacity;
                        addToBuffer(action, task);
                    }
                    // If the buffer would not overflow, just add the item.
                    else
                    {
                        addToBuffer(action, task);
                    }
                }
            }

            private bool Pop(ref EventWithState e)
            {
                bool result = false;
                lock (mForeignLock)
                {
                    if (size > 0)
                    {
                        e = mEvents[head];
                        head = (head + 1) % Capacity;
                        size--;
                        result = true;
                    }
                }
                return result;
            }

            public override ExecutorService Start(string threadName, int stackSize)
            {
                mThread = new Thread(() => {
                    mCurrent = this;
                    mActive = true;
                    DispatchLoop();
                    mActive = false;
                    mCurrent = null;
                }, stackSize);
                mThread.Name = threadName;
                mThread.Start();
                return this;
            }

            public override void Shutdown()
            {
                mActive = false;
                mThread.Join();
            }

            private void DispatchLoop()
            {
                EventWithState eventWithState = new EventWithState();
                while (mActive)
                {
                    while (mScheduled.Count > 0 && mScheduled.First.Value.time <= GetCurrentUnixTimestampMillis())
                    {
                        try
                        {
                            TaskImpl task = mScheduled.First.Value.task;
                            if (!task.IsCancelled()) {
                                task.Reset();
                                task.Set(mScheduled.First.Value.action?.Invoke());
                            }
                            mScheduled.RemoveFirst();
                        }
                        catch (Exception ex) { 
                            Debug.LogError(ex); 
                        }
                    }

                    if (Pop(ref eventWithState))
                    {
                        try
                        {
                            if (!eventWithState.task.IsCancelled()) {
                                eventWithState.task.Set(eventWithState.action?.Invoke());
                            }
                        }
                        catch (Exception ex) { Debug.LogError(ex); }
                    }


                    if (Count == 0 && (mScheduled.Count == 0 || mScheduled.First.Value.time - GetCurrentUnixTimestampMillis() > 1))
                    {
                        Thread.Sleep(1);
                    }
                }
            }

            public override Task Schedule<T>(Func<T> action)
            {
                TaskImpl task = new TaskImpl();
                Push(() => action.Invoke(), task);
                return task;
            }

            public override Task Schedule<T>(Func<T> action, TimeSpan delay)
            {
                TaskImpl task = new TaskImpl();
                PushScheduled(() => action.Invoke(), GetCurrentUnixTimestampMillis() + (long)delay.TotalMilliseconds, task);
                return task;
            }

            public override Task Schedule<T>(Func<T> action, TimeSpan delay, TimeSpan period)
            {
                TaskImpl task = new TaskImpl();
                Func<object> act = null;
                act = () => {
                    object ret = action.Invoke();
                    PushScheduled(act, GetCurrentUnixTimestampMillis() + (long)period.TotalMilliseconds, task);
                    return ret;
                };
                PushScheduled(act, GetCurrentUnixTimestampMillis() + (long)delay.TotalMilliseconds, task);
                return task;
            }

            private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public static long GetCurrentUnixTimestampMillis()
            {
                DateTime localDateTime, univDateTime;
                localDateTime = DateTime.Now;
                univDateTime = localDateTime.ToUniversalTime();
                return (long)(univDateTime - UnixEpoch).TotalMilliseconds;
            }
        }
    }
}
