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
        private readonly List<ParameterizedThreadDefinition> usedParameterizedDefinitions = new List<ParameterizedThreadDefinition>();

        public int Count { get { return threads.Count; } }

        public void Spawn(ThreadDefinition definition)
        {
            Thread thread = new Thread(definition.callback);
            thread.Start();
            usedDefinitions.Add(definition);
            threads.Add(definition.name, new Tuple<Thread, ManualResetEvent>(thread, null));
        }

        public void ManagedSpawn(ParameterizedThreadDefinition definition)
        {
            ManualResetEvent newLock = new ManualResetEvent(false);
            Thread thread = new Thread(definition.callback);
            thread.Start(newLock);
            usedParameterizedDefinitions.Add(definition);
            threads.Add(definition.name, new Tuple<Thread, ManualResetEvent>(thread, newLock));
        }

        public void ReSpawn()
        {
            DictionaryEntry[] clone = new DictionaryEntry[threads.Count];
            threads.CopyTo(clone, 0);
            foreach (DictionaryEntry entry in clone)
            {
                (entry.Value as Tuple<Thread, ManualResetEvent>).Item2?.Set();

                ThreadDefinition threadDefinition = usedDefinitions.Find((ThreadDefinition obj) => obj.name.Equals(entry.Key));
                if (threadDefinition != null)
                {
                    usedDefinitions.Remove(threadDefinition);
                    threads.Remove(entry.Key);
                    Spawn(threadDefinition);
                }

                ParameterizedThreadDefinition parameterizedThreadDefinition = usedParameterizedDefinitions.Find((ParameterizedThreadDefinition obj) => obj.name.Equals(entry.Key));
                if (parameterizedThreadDefinition != null)
                {
                    usedParameterizedDefinitions.Remove(parameterizedThreadDefinition);
                    threads.Remove(entry.Key);
                    ManagedSpawn(parameterizedThreadDefinition);
                }
            }
        }

        public void DeSpawn()
        {
            Tuple<Thread, ManualResetEvent> thread = (Tuple<Thread, ManualResetEvent>)threads[threads.Count - 1];
            thread.Item2?.Set();

            ThreadDefinition threadDefinition = usedDefinitions.Find((ThreadDefinition obj) => obj.name.Equals(thread.Item1.Name));
            if (threadDefinition != null)
            {
                usedDefinitions.Remove(threadDefinition);
                threads.Remove(thread.Item1.Name);
            }

            ParameterizedThreadDefinition parameterizedThreadDefinition = usedParameterizedDefinitions.Find((ParameterizedThreadDefinition obj) => obj.name.Equals(thread.Item1.Name));
            if (parameterizedThreadDefinition != null)
            {
                usedParameterizedDefinitions.Remove(parameterizedThreadDefinition);
                threads.Remove(thread.Item1.Name);
            }
        }

        public void SanityCheck()
        {
            DictionaryEntry[] clone = new DictionaryEntry[threads.Count];
            threads.CopyTo(clone, 0);
            foreach (DictionaryEntry entry in clone)
            {
                if ((entry.Value as Tuple<Thread, ManualResetEvent>).Item1.IsAlive)
                    continue;

                ThreadDefinition usedDefinition = usedDefinitions.Find((ThreadDefinition definition) => definition.name.Equals(entry.Key));
                usedDefinitions.Remove(usedDefinition);
                threads.Remove(entry.Key);
                Spawn(usedDefinition);
            }
        }
    }
}
