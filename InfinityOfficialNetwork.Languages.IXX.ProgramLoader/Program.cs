using LLVMSharp.Interop;
using System.Runtime.InteropServices;
using System.Text;

internal class Program
{

	private static async Task<int> Main(string[] args)
	{
#if DEBUG
		args = [@"S:\source\InfinityOfficialNetwork.Languages.IXX\InfinityOfficialNetwork.Languages.IXX\bin\Debug\net9.0\output.ll"];
#endif
		if (args.Length != 1)
			throw new ArgumentException("LLVM IR file not found in args, usage: loader <program.ll>");
		string programFile = args[0];
		byte[] program = await File.ReadAllBytesAsync(programFile);

		int result = ProgramLoader.RunLLVMProgram(program);

		Console.Error.WriteLine("Program exited with code {0}", result);

		return result;
	}
}