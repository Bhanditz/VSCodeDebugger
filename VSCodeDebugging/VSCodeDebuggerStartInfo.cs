using Mono.Debugging.Client;

namespace VSCodeDebugging
{
	public class VSCodeDebuggerStartInfo : DebuggerStartInfo
	{
		/// <summary>
		/// Set to true to build the project before run debug
		/// </summary>
		public bool BuildBeforeRun { get; set; }

		/// <summary>
		/// Set to true to stop on the first instruction of entry point method
		/// </summary>
		public bool StopAtEntry { get; set; }
		
		/// <summary>
		/// Path to 'dotnet' executable. If null then command 'dotnet' without path will be passed (so dotnet location should be in PATH)
		/// </summary>
		public string DotNetCliPath { get; set; }

	}
}