using System.Text;
using Kalypsis.Cli;

// Windows console input must use ReadConsoleW. ReadFile with code page 65001
// can return NUL for Greek characters in a real console/ConPTY session.
// Redirected input and non-Windows terminals remain UTF-8.
Console.InputEncoding = OperatingSystem.IsWindows() && !Console.IsInputRedirected
    ? Encoding.Unicode : new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
return Application.Run(args, Console.In, Console.Out, Console.Error, !Console.IsInputRedirected && !Console.IsOutputRedirected);
