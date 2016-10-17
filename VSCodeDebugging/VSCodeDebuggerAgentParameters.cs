using VSCodeDebugging.VSCodeProtocol;

namespace VSCodeDebugging
{
	public class VSCodeDebuggerAgentParameters
	{
		public static readonly string AdapterFilename = PlatformUtil.IsWindows ? "OpenDebugAD7.exe" : "OpenDebugAD7";

		/// <summary>
		/// The directory where OpenDebugAD7 is located (.exe for Windows). Required property.
		/// </summary>
		public string CoreClrDebugAdapterLocation { get; set; }

		/// <summary>
		/// Log file path being passed as --engineLogging to OpenDebugAD7. Ignore to disable logging.
		/// </summary>
		public string DebuggerEngineLogFilePath { get; set; }

		/// <summary>
		/// Directory where 'dotnet' executable is located. If not null this location will added to PATH for the debugger adapter process. 
		/// Else 'dotnet' from user PATH will be used, or debugger will fail if there is no 'dotnet' in PATH
		/// </summary>
		public string DotNetCliLocation { get; set; }
	}
}