using Mono.Debugging.Client;

namespace VSCodeDebugging
{
	public class VSCodeDebuggerStartInfo : DebuggerStartInfo
	{
		public bool BuildBeforeRun { get; set; }

		public bool StopAtEntry { get; set; }
		public bool ExternalConsole { get; set; }

		/// <summary>
		/// Path to 'dotnet' executable. If null then command 'dotnet' without path will be passed (so dotnet location should be in PATH)
		/// </summary>
		public string DotNetCliPath { get; set; }

	}
}