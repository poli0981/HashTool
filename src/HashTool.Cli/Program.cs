using System;
using System.Threading.Tasks;

namespace HashTool.Cli;

internal static class Program
{
    private static Task<int> Main(string[] args) => HashToolCommand.RunAsync(args, Console.Out, Console.Error);
}
