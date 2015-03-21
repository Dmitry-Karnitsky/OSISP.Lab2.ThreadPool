using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CustomThreadPool;

using ThreadPool = CustomThreadPool.ThreadPool;

namespace ConsoleUI
{
    class Program
    {
        public static ThreadPool th = new ThreadPool(4, 500, false);

        static void Main(string[] args)
        {                
            for (int i = 0; i < 100; i++)
            {
                th.QueueUserWorkItem(Task, i);
            }           

            for (int i = 0; i < 100; i++)
            {
                th.QueueUserWorkItem(Task, i);                  
            }           

            Console.WriteLine(" " + th.CurrentNumberOfThreads + " " + th.CurrentThreadsTimeOut); 

            Console.ReadLine();
            //Thread.Sleep(11000);
            th.Dispose();
        }

        static void Task(object obj)
        {
            unchecked
            {
                long k = 0;
                for(long i= 0; i < 10000000000000000; i++)
                {
                    k = k + i;
                }
            }
            Console.WriteLine(String.Format("{0}. ThreadID: {1}, CurrentNumberOfThreads: {2}", obj, Thread.CurrentThread.ManagedThreadId, th.CurrentNumberOfThreads));
        }
    }
}
