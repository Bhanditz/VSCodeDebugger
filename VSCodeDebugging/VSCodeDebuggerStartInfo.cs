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
	}
}