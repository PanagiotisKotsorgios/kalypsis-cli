using System.Text;
using Kalypsis.Cli;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
return Application.Run(args, Console.In, Console.Out, Console.Error, !Console.IsInputRedirected && !Console.IsOutputRedirected);
