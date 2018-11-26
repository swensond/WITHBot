using System.Threading;

namespace WITHBot
{
    public class ThreadDefinition
    {
        public string name;
        public object arguments;
        public ParameterizedThreadStart method;
    }
}
