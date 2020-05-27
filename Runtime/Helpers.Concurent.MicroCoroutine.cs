using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace UniHelpers.Concurent
{
    public abstract class MicroCoroutine
    {
        private class Runtime : Singleton<Runtime>
        {

            public static Runtime Instance => Api;

            const int MaxArrayLength = 0X7FEFFFFF;
            const int InitialSize = 16;

            //static object gate = new object();
            bool dequing = false;

            int actionListCount = 0;
            Action<object>[] actionList = new Action<object>[InitialSize];
            object[] actionStates = new object[InitialSize];

            int waitingListCount = 0;
            Action<object>[] waitingList = new Action<object>[InitialSize];
            object[] waitingStates = new object[InitialSize];

            private void Enqueue(Action<object> action, object state)
            {
                //lock (gate) {
                if (dequing)
                {
                    // Ensure Capacity
                    if (waitingList.Length == waitingListCount)
                    {
                        var newLength = waitingListCount * 2;
                        if ((uint)newLength > MaxArrayLength) newLength = MaxArrayLength;

                        var newArray = new Action<object>[newLength];
                        var newArrayState = new object[newLength];
                        Array.Copy(waitingList, newArray, waitingListCount);
                        Array.Copy(waitingStates, newArrayState, waitingListCount);
                        waitingList = newArray;
                        waitingStates = newArrayState;
                    }
                    waitingList[waitingListCount] = action;
                    waitingStates[waitingListCount] = state;
                    waitingListCount++;
                }
                else
                {
                    // Ensure Capacity
                    if (actionList.Length == actionListCount)
                    {
                        var newLength = actionListCount * 2;
                        if ((uint)newLength > MaxArrayLength) newLength = MaxArrayLength;

                        var newArray = new Action<object>[newLength];
                        var newArrayState = new object[newLength];
                        Array.Copy(actionList, newArray, actionListCount);
                        Array.Copy(actionStates, newArrayState, actionListCount);
                        actionList = newArray;
                        actionStates = newArrayState;
                    }
                    actionList[actionListCount] = action;
                    actionStates[actionListCount] = state;
                    actionListCount++;
                }
                //}
            }

            private void ExecuteAll(Action<Exception> unhandledExceptionCallback)
            {
                //lock (gate) {
                if (actionListCount == 0) return;
                dequing = true;
                //}

                for (int i = 0; i < actionListCount; i++)
                {
                    var action = actionList[i];
                    var state = actionStates[i];
                    try
                    {
                        action(state);
                    }
                    catch (Exception ex)
                    {
                        unhandledExceptionCallback(ex);
                    }
                    finally
                    {
                        // Clear
                        actionList[i] = null;
                        actionStates[i] = null;
                    }
                }

                //lock (gate) {
                dequing = false;

                var swapTempActionList = actionList;
                var swapTempActionStates = actionStates;

                actionListCount = waitingListCount;
                actionList = waitingList;
                actionStates = waitingStates;

                waitingListCount = 0;
                waitingList = swapTempActionList;
                waitingStates = swapTempActionStates;
                //}
            }

            private void ConsumeEnumerator(IEnumerator routine)
            {
                if (routine.MoveNext())
                {
                    var current = routine.Current;
                    if (current == null)
                    {
                        goto ENQUEUE;
                    }

                    var type = current.GetType();
#if UNITY_2018_3_OR_NEWER
#pragma warning disable CS0618
#endif
                    if (type == typeof(WWW))
                    {
                        var www = (WWW)current;
                        Enqueue(_ => ConsumeEnumerator(UnwrapWaitWWW(www, routine)), null);
                        return;
                    }
#if UNITY_2018_3_OR_NEWER
#pragma warning restore CS0618
#endif
                    else if (type == typeof(AsyncOperation))
                    {
                        var asyncOperation = (AsyncOperation)current;
                        Enqueue(_ => ConsumeEnumerator(UnwrapWaitAsyncOperation(asyncOperation, routine)), null);
                        return;
                    }
                    else if (type == typeof(WaitForSeconds))
                    {
                        var waitForSeconds = (WaitForSeconds)current;
                        var accessor = typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.Instance | BindingFlags.GetField | BindingFlags.NonPublic);
                        var second = (float)accessor.GetValue(waitForSeconds);
                        Enqueue(_ => ConsumeEnumerator(UnwrapWaitForSeconds(second, routine)), null);
                        return;
                    }
                    else if (type == typeof(Coroutine))
                    {
                        Debug.Log("Can't wait coroutine on UnityEditor");
                        goto ENQUEUE;
                    }
                    //#if SupportCustomYieldInstruction
                    else if (current is IEnumerator)
                    {
                        var enumerator = (IEnumerator)current;
                        Enqueue(_ => ConsumeEnumerator(UnwrapEnumerator(enumerator, routine)), null);
                        return;
                    }
                    //#endif

                    ENQUEUE:
                    Enqueue(_ => ConsumeEnumerator(routine), null); // next update
                }
            }

#if UNITY_2018_3_OR_NEWER
#pragma warning disable CS0618
#endif
            private IEnumerator UnwrapWaitWWW(WWW www, IEnumerator continuation)
            {
                while (!www.isDone)
                {
                    yield return null;
                }
                ConsumeEnumerator(continuation);
            }
#if UNITY_2018_3_OR_NEWER
#pragma warning restore CS0618
#endif

            private IEnumerator UnwrapWaitAsyncOperation(AsyncOperation asyncOperation, IEnumerator continuation)
            {
                while (!asyncOperation.isDone)
                {
                    yield return null;
                }
                ConsumeEnumerator(continuation);
            }

            private IEnumerator UnwrapWaitForSeconds(float second, IEnumerator continuation)
            {
                var startTime = DateTimeOffset.UtcNow;
                while (true)
                {
                    yield return null;

                    var elapsed = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
                    if (elapsed >= second)
                    {
                        break;
                    }
                };
                ConsumeEnumerator(continuation);
            }

            private IEnumerator UnwrapEnumerator(IEnumerator enumerator, IEnumerator continuation)
            {
                while (enumerator.MoveNext())
                {
                    yield return null;
                }
                ConsumeEnumerator(continuation);
            }

            protected override void OnUpdate()
            {
                ExecuteAll(err => Debug.LogError(err));
            }

            public void StartCoroutine(IEnumerator routine)
            {
                Enqueue(_ => ConsumeEnumerator(routine), null);
            }
        }

        public static void Start(IEnumerator routine)
        {
            Runtime.Instance.StartCoroutine(routine);
        }

        public static void StopAllCoroutines()
        {
            Runtime.Shutdown();
        }
    }
}
