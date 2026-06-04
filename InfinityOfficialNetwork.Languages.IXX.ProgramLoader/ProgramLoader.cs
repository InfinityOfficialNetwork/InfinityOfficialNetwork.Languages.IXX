using LLVMSharp.Interop;
using System.Runtime.InteropServices;

public static class ProgramLoader
{
	private delegate int MainDelegate();

	// 1. Define the delegate with Cdecl calling convention
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void WriteConsoleDelegate(byte value);

	// 2. Keep a static reference so the GC doesn't collect the function pointer
	private static readonly WriteConsoleDelegate WriteConsoleInstance = write_console;

	public static void write_console(byte value)
	{
		Console.Write((char)value);
	}

	public static unsafe int RunLLVMProgram(byte[] programBytes)
	{
		// 1. Initialize ALL native components
		LLVM.InitializeNativeTarget();
		LLVM.InitializeNativeAsmParser();
		LLVM.InitializeNativeAsmPrinter();

		// 2. Load the IR from memory
		LLVMMemoryBufferRef memoryBuffer;
		fixed (byte* pBytes = programBytes)
		{
			sbyte* bufferName = (sbyte*)Marshal.StringToHGlobalAnsi("in_memory_buffer");
			memoryBuffer = LLVM.CreateMemoryBufferWithMemoryRangeCopy((sbyte*)pBytes, (UIntPtr)programBytes.Length, bufferName);
			Marshal.FreeHGlobal((IntPtr)bufferName);
		}

		LLVMContextRef context = LLVM.ContextCreate();
		LLVMOpaqueModule* module;
		sbyte* errorMsg;

		if (LLVM.ParseIRInContext(context, memoryBuffer, &module, &errorMsg) != 0)
		{
			throw new InvalidOperationException(Marshal.PtrToStringAnsi((IntPtr)errorMsg));
		}
		//LLVM.DisposeMemoryBuffer(memoryBuffer);

		// ============================================================
		// START OPTIMIZATION BLOCK
		// ============================================================

		// 3. Setup Target Machine for Native Architecture
		sbyte* triple = LLVM.GetDefaultTargetTriple();
		LLVMTarget* target;
		if (LLVM.GetTargetFromTriple(triple, &target, &errorMsg) != 0)
			throw new Exception(Marshal.PtrToStringAnsi((IntPtr)errorMsg));

		sbyte* cpu = LLVM.GetHostCPUName();
		sbyte* features = LLVM.GetHostCPUFeatures();

		// Create Target Machine with Aggressive (O3) code generation
		var targetMachine = LLVM.CreateTargetMachine(
			target, triple, cpu, features,
			LLVMCodeGenOptLevel.LLVMCodeGenLevelAggressive,
			LLVMRelocMode.LLVMRelocDefault,
			LLVMCodeModel.LLVMCodeModelDefault
		);

		// 4. CRITICAL: Set Module Data Layout
		var dataLayout = LLVM.CreateTargetDataLayout(targetMachine);
		LLVM.SetModuleDataLayout(module, dataLayout);

		// --- NEW: INJECT NATIVE ATTRIBUTES INTO FUNCTIONS ---
		// This forces the optimizer to actually use the host CPU's specialized instruction sets (AVX, etc.)
		var cpuStr = Marshal.PtrToStringAnsi((IntPtr)cpu);
		var featuresStr = Marshal.PtrToStringAnsi((IntPtr)features);

		nint target_cpu = Marshal.StringToHGlobalAnsi("target-cpu");
		var attrCpu = LLVM.CreateStringAttribute(context, (sbyte*)target_cpu, 10, cpu, (uint)cpuStr.Length);
		Marshal.FreeHGlobal(target_cpu);

		nint target_features = Marshal.StringToHGlobalAnsi("target-features");
		var attrFeatures = LLVM.CreateStringAttribute(context, (sbyte*)target_features, 15, features, (uint)featuresStr.Length);
		Marshal.FreeHGlobal(target_features);

		nint nounwind = Marshal.StringToHGlobalAnsi("nounwind");
		var func = LLVM.GetFirstFunction(module);
		while (func != null)
		{
			LLVM.AddAttributeAtIndex(func, LLVMAttributeIndex.LLVMAttributeFunctionIndex, attrCpu);
			LLVM.AddAttributeAtIndex(func, LLVMAttributeIndex.LLVMAttributeFunctionIndex, attrFeatures);
			// Also add "NoUnwind" to help the inliner if the code doesn't use exceptions
			LLVM.AddAttributeAtIndex(func, LLVMAttributeIndex.LLVMAttributeFunctionIndex,
				LLVM.CreateEnumAttribute(context, LLVM.GetEnumAttributeKindForName((sbyte*)nounwind, 8), 0));

			func = LLVM.GetNextFunction(func);
		}
		Marshal.FreeHGlobal(nounwind);

		// 5. Run the O3 Optimization Pipeline
		var passOptions = LLVM.CreatePassBuilderOptions();
		LLVM.PassBuilderOptionsSetLoopVectorization(passOptions, 1);
		LLVM.PassBuilderOptionsSetSLPVectorization(passOptions, 1);
		// NEW: Identical Function Merging (reduces code size and can improve cache hits)
		LLVM.PassBuilderOptionsSetMergeFunctions(passOptions, 1);

		sbyte* passes = (sbyte*)Marshal.StringToHGlobalAnsi("default<O3>");
		var error = LLVM.RunPasses(module, passes, targetMachine, passOptions);

		if (error != null)
			throw new Exception("Optimization pass failed.");

		// Clean up optimization handles
		Marshal.FreeHGlobal((IntPtr)passes);
		LLVM.DisposePassBuilderOptions(passOptions);
		LLVM.DisposeTargetData(dataLayout);
		// ============================================================
		// END OPTIMIZATION BLOCK
		// ============================================================

		// 6. Create JIT Engine
		LLVMOpaqueExecutionEngine* engine;
		if (LLVM.CreateExecutionEngineForModule(&engine, module, &errorMsg) != 0)
		{
			throw new InvalidOperationException(Marshal.PtrToStringAnsi((IntPtr)errorMsg));
		}

		// ============================================================
		// LINKING EXTERNAL FUNCTIONS
		// ============================================================

		// A. Find the function declaration in the IR
		nint write_name = Marshal.StringToHGlobalAnsi("write_console");
		LLVMValueRef writeFunc = LLVM.GetNamedFunction(module, (sbyte*)write_name);

		if (writeFunc.Handle != IntPtr.Zero)
		{
			// B. Get the actual memory address of our C# method
			IntPtr writePtr = Marshal.GetFunctionPointerForDelegate(WriteConsoleInstance);

			// C. Map the IR value to the C# memory address
			// We cast engine to LLVMExecutionEngineRef as required by the wrapper
			LLVM.AddGlobalMapping(engine, writeFunc, (void*)writePtr);
		}
		Marshal.FreeHGlobal(write_name);

		// 7. Execute
		LLVMValueRef function = LLVM.GetNamedFunction(module, (sbyte*)Marshal.StringToHGlobalAnsi("main"));
		IntPtr functionAddress = (IntPtr)LLVM.GetPointerToGlobal(engine, function);
		var main = Marshal.GetDelegateForFunctionPointer<MainDelegate>(functionAddress);

		int result = main();

		// Cleanup
		LLVM.DisposeExecutionEngine(engine); // This also disposes targetMachine and module
		LLVM.ContextDispose(context);
		LLVM.DisposeMessage(triple);
		LLVM.DisposeMessage(cpu);
		LLVM.DisposeMessage(features);

		return result;
	}
}