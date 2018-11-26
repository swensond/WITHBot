using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;

namespace WITHBot
{
    public class ThreadManager
    {
        private readonly OrderedDictionary threads = new OrderedDictionary();
        private readonly List<ThreadDefinition> usedDefinitions = new List<ThreadDefinition>();

        public int Count { get { return threads.Count; } }

        public void Spawn(ThreadDefinition definition)
        {
            // Setup a new lock for the thread
            ManualResetEvent newLock = new ManualResetEvent(false);
            // Create the thread
            Thread thread = new Thread(definition.method);
            // Pass the lock into the thread and start
            thread.Start(newLock);
            // preserve the thread, lock, and definition
            usedDefinitions.Add(definition);
            threads.Add(definition.name, new Tuple<Thread, ManualResetEvent>(thread, newLock));
        }

        public void ReSpawn()
        {
            // Setup a clone variable
            DictionaryEntry[] clone = new DictionaryEntry[threads.Count];
            // Clone threads into the clone
            threads.CopyTo(clone, 0);
            // Cycle through the clone
            foreach (DictionaryEntry entry in clone)
            {
                // Break the lock for the thread
                (entry.Value as Tuple<Thread, ManualResetEvent>).Item2.Set();
                // Grab the definition for the thread
                ThreadDefinition usedDefinition = usedDefinitions.Find((ThreadDefinition definition) => definition.name.Equals(entry.Key));
                // Remove the definition, thread, and lock
                usedDefinitions.Remove(usedDefinition);
                threads.Remove(entry.Key);
                // Respawn the thread
                Spawn(usedDefinition);
            }
        }

        public void DeSpawn()
        {
            // Grab the last thread
            Tuple<Thread, ManualResetEvent> thread = (Tuple<Thread, ManualResetEvent>)threads[threads.Count - 1];
            // Break the lock for the thread
            thread.Item2.Set();
            // Remove the definition, thread, and lock
            threads.RemoveAt(threads.Count - 1);
            usedDefinitions.RemoveAt(usedDefinitions.Count - 1);
        }

        public void SanityCheck()
        {
            // Setup a clone variable
            DictionaryEntry[] clone = new DictionaryEntry[threads.Count];
            // Clone threads into the clone
            threads.CopyTo(clone, 0);
            // Cycle through the clone
            foreach (DictionaryEntry entry in clone)
            {
                // Check if the Thread is alive
                if ((entry.Value as Tuple<Thread, ManualResetEvent>).Item1.IsAlive)
                    continue;

                // If the thread is dead, we want to grab the definition used for Spawn
                ThreadDefinition usedDefinition = usedDefinitions.Find((ThreadDefinition definition) => definition.name.Equals(entry.Key));
                // Remove the definition, thread, and lock
                usedDefinitions.Remove(usedDefinition);
                threads.Remove(entry.Key);
                // Spawn the thread using the same definition
                Spawn(usedDefinition);
            }
        }
    }
}
