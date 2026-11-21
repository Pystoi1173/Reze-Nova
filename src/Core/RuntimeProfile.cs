using System;
using System.Collections.Generic;

namespace RezeNova.Core
{
    internal sealed class RuntimeProfile
    {
        public NovaMode Mode { get; private set; }
        public string EnginePath { get; private set; }
        public string WorkingDirectory { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; }

        public RuntimeProfile(NovaMode mode, string enginePath, string workingDirectory, IList<string> arguments)
        {
            Mode = mode;
            EnginePath = enginePath;
            WorkingDirectory = workingDirectory;
            Arguments = new List<string>(arguments).AsReadOnly();
        }

        public string CommandLineArguments
        {
            get { return String.Join(" ", Arguments); }
        }
    }
}
