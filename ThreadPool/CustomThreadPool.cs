using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CustomThreadPool
{
    public sealed class ThreadPool
    {
        #region Private readonly fields
        private readonly int minThreads = 1;
        private readonly int maxThreads = 250;
        private readonly int minTimeOut = 500;
        private readonly int maxTimeOut = 600000;
        #endregion

        #region Private fields
        private int threadTimeOut;
        private volatile int concurrencyLevel;
        private bool isExitRequested;
        private bool isFixedConcurencyLevel;
        private volatile int threadsWaiting;
        private List<Thread> threads;
        private Queue<WorkItem> queue = new Queue<WorkItem>();
        private bool isDisposed = false;
        private Timer timer;
        #endregion

        #region Public properties
        public int CurrentNumberOfThreads
        {
            get { return concurrencyLevel; }
        }

        public int MaxThreadsNumber
        {
            get { return maxThreads; }
        }

        public int MinThreadsNumber
        {
            get { return minThreads; }
        }

        public int CurrentThreadsTimeOut
        {
            get { return threadTimeOut; }
        }

        public int MinThreadsTimeOut
        {
            get { return minTimeOut; }
        }
        public int MaxThreadsTimeOut
        {
            get { return maxTimeOut; }
        }

        #endregion

        #region Constructors
        public ThreadPool()
            : this(Environment.ProcessorCount, 10000, false) { }

        public ThreadPool(int concurrencyLevel)
            : this(concurrencyLevel, 10000, false) { }

        public ThreadPool(int concurrencyLevel, int threadTimeOut)
            : this(concurrencyLevel, threadTimeOut, false) { }

        public ThreadPool(int concurrencyLevel, bool isFixedConcurrencyLevel)
            : this(concurrencyLevel, 10000, isFixedConcurrencyLevel) { }

        public ThreadPool(int concurrencyLevel, int threadTimeOut, bool isFixedConcurrencyLevel)
        {
            if (concurrencyLevel < minThreads)
                throw new ArgumentOutOfRangeException(String.Format("{0} is lower than minimum number of threads, that threadpool can create. Minimum number of threads is {1}.", concurrencyLevel, minThreads), (Exception)null);

            if (concurrencyLevel > maxThreads)
                throw new ArgumentOutOfRangeException(String.Format("{0} is greater than maximum number of threads, that threadpool can create. Maximum number of threads is {1}.", concurrencyLevel, maxThreads), (Exception)null);

            if (threadTimeOut > maxTimeOut)
                throw new ArgumentOutOfRangeException(String.Format("{0} is greater than maximum thread timeout. Maximum thread timeout is {1} milliseconds.", threadTimeOut, maxTimeOut), (Exception)null);

            if (threadTimeOut < minTimeOut)
                throw new ArgumentOutOfRangeException(String.Format("{0} is lower than minimum thread timeout. Minimum thread timeout is {1} milliseconds.", threadTimeOut, minTimeOut), (Exception)null);

            this.concurrencyLevel = concurrencyLevel;
            this.threadTimeOut = threadTimeOut;
            this.isFixedConcurencyLevel = isFixedConcurrencyLevel;
        }
        #endregion

        struct WorkItem
        {
            internal WaitCallback callback;
            internal object state;
            internal ExecutionContext executionContext;

            internal WorkItem(WaitCallback waitCallback, object obj)
            {
                callback = waitCallback;
                state = obj;
                executionContext = null;
            }

            internal void Invoke()
            {
                if (executionContext == null)
                    callback(state);
                else
                    ExecutionContext.Run(executionContext, ContextInvoke, null);
            }

            private void ContextInvoke(object obj)
            {
                callback(state);
            }
        }

        #region QueueUserWorkItem method overloads

        //It allocates a new WorkItem, optionally captures and stores an 
        //ExecutionContext, ensures the pool has started, and then enqueues 
        //the WorkItem into the pool, possibly pulsing a single thread 
        //(if any are waiting), creates a new thread, if it needed.  
        //There’s also a convenient overload that doesn’t take an obj 
        //for situations where it isn’t needed.        
        //

        public void QueueUserWorkItem(WaitCallback work)
        {
            QueueUserWorkItem(work, null);
        }

        public void QueueUserWorkItem(WaitCallback work, object parameter)
        {
            if (!isDisposed)
            {
                WorkItem wi = new WorkItem(work, parameter);

                EnsureStarted();

                Monitor.Enter(queue); // Enter in critical section

                try
                {
                    //
                    // If we are able to create the workitem, we need to get it in the queue without being interrupted
                    // by a ThreadAbortException.
                    //                
                    try { }
                    finally
                    {
                        queue.Enqueue(wi);
                        if (threadsWaiting > 0)
                            Monitor.Pulse(queue);
                        else
                        {
                            Monitor.Exit(queue); // Exit from critical section to create new thread
                            timer = new Timer(AddThread, timer, 1000, Timeout.Infinite);
                        }
                    }
                }
                finally
                {
                    if (Monitor.IsEntered(queue))
                        Monitor.Exit(queue);  // Exit from critical section if new thread wasn't needed
                }
            }
            else throw new ObjectDisposedException("ThreadPool");
        }

        #endregion
         
        //
        // A simple helper method that will lazily initialize and start 
        // the set of threads in a particular pool. The lazy aspect ensures 
        // that a pool that doesn’t ever get used won’t allocate threads.
        // 
        private void EnsureStarted()
        {
            if (threads == null)
            {
                lock (queue)
                {
                    if (threads == null)
                    {
                        threads = new List<Thread>();
                        for (int i = 0; i < concurrencyLevel; i++)
                        {
                            Thread th = new Thread(ThreadTask);
                            threads.Add(th);
                            th.IsBackground = true;
                            th.Start();
                        }
                    }
                }
            }
        }

        //
        // Creating a new thread and adding it in the threadpool.
        //
        private void AddThread(object state)
        {
            if (queue.Count > 0 && threadsWaiting == 0)
            {
                Thread th = new Thread(ThreadTask);
                Console.WriteLine("Created " + th.ManagedThreadId);
                threads.Add(th);
                th.IsBackground = true;
                th.Start();
                concurrencyLevel++;
            }
        }

        //
        // This is the main method run by each pool thread. All it does is sit in a loop dequeuing 
        //and (if the queue is empty) waiting for new work to arrive. When shutdown is initiated, 
        //the method voluntarily quits. If any pool work items throw an exception, this top-level 
        //method lets them go unhandled, resulting in a crash of the thread.
        //
        private void ThreadTask()
        {
            while (true)
            {
                WorkItem wi = default(WorkItem);

                Monitor.Enter(queue);

                try
                {
                    if (isExitRequested) return;

                    while (queue.Count == 0)
                    {
                        threadsWaiting++;
                        try
                        {
                            bool isBeforeTimeOut = Monitor.Wait(queue, threadTimeOut);
                            if (queue.Count == 0 && concurrencyLevel > minThreads && isBeforeTimeOut == false && !isDisposed)
                            {
                                Monitor.Exit(queue);
                                threads.Remove(Thread.CurrentThread);
                                concurrencyLevel--;
                                Thread.CurrentThread.Abort();
                                return;
                            }
                        }
                        finally
                        {
                            threadsWaiting--;
                        }

                        if (isExitRequested) return;
                    }

                    wi = queue.Dequeue();
                }
                finally
                {
                    if (Monitor.IsEntered(queue))
                        Monitor.Exit(queue);
                }

                wi.Invoke();
            }
        }

        #region Finalizator and Dispose methods
        ~ThreadPool()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;
                GC.SuppressFinalize(this);
                isExitRequested = true;
                lock (queue)
                {
                    Monitor.PulseAll(queue);
                }

                for (int i = 0; i < threads.Count; i++)
                {
                    if (threads[i].Join(1000) == false)
                        threads[i].Abort();
                }
                queue = null;
                threads = null;
                
                //timer.Dispose();
            }
        }
        #endregion
    }
}
