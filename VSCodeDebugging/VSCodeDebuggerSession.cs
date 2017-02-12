using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation;
using VSCodeDebugging.VSCodeProtocol;
using Breakpoint = Mono.Debugging.Client.Breakpoint;
using VSBreakpoint = VSCodeDebugging.VSCodeProtocol.Breakpoint;

namespace VSCodeDebugging
{
	public class VSCodeDebuggerSession : DebuggerSession
	{
		private readonly VSCodeDebuggerAgentParameters debuggerAgentParameters;

		public VSCodeDebuggerSession(VSCodeDebuggerAgentParameters debuggerAgentParameters)
		{
			this.debuggerAgentParameters = debuggerAgentParameters;
		}

		long currentThreadId;

		protected override void OnAttachToProcess(long processId)
		{
			StartDebugAgent();

			ProtocolClient.SendRequestSync(new AttachRequest(new AttachRequestArguments {
				Name = ".NET Core Attach",
				Type = "coreclr",
				Request = "attach",
				ProcessId = processId
			}));

			OnStarted();
		}

		protected override void OnContinue()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new ContinueRequest(new ContinueRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnDetach()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new DisconnectRequest(new DisconnectRequestArguments()
			{
				Terminate = false
			}));
		}

		protected override void OnUpdateBreakEvent(BreakEventInfo eventInfo)
		{
		}

		protected override void OnEnableBreakEvent(BreakEventInfo eventInfo, bool enable)
		{
			lock (breakpointsSync) {
				var breakEvent = eventInfo.BreakEvent;
				if (breakEvent == null)
					return;
				if (breakEvent is Breakpoint)
				{
					var breakpoint = (Breakpoint)breakEvent;
					UpdatePositionalBreakpoints(breakpoint.FileName);
				}
				else if (breakEvent is Catchpoint)
				{
					UpdateExceptions();
				}
			}
		}

		protected override void OnExit()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new DisconnectRequest(new DisconnectRequestArguments()
			{
				Terminate = true
			}));
		}

		protected override void OnFinish()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new StepOutRequest(new StepOutRequestArguments {
				threadId = currentThreadId
			}));
		}

		readonly ProcessInfo[] processInfo = { new ProcessInfo(1, "debugee") };

		protected override ProcessInfo[] OnGetProcesses()
		{
			return processInfo;
		}

		protected override Backtrace OnGetThreadBacktrace(long processId, long threadId)
		{
			return GetThreadBacktrace(threadId);
		}

		protected override ThreadInfo[] OnGetThreads(long processId)
		{
			var threadsResponse = ProtocolClient.SendRequestSync(new ThreadsRequest());
			var threads = new ThreadInfo[threadsResponse.threads.Length];
			for (int i = 0; i < threads.Length; i++) {
				threads[i] = new ThreadInfo(processId,
										  threadsResponse.threads[i].id,
										  threadsResponse.threads[i].name,
										  "not implemented");
			}
			return threads;
		}

		class PathComparer : EqualityComparer<string>
		{
			public override bool Equals(string x, string y)
			{
				return BreakpointStore.FileNameEquals(x, y);
			}

			public override int GetHashCode(string obj)
			{
				if (obj != null)
					return obj.GetHashCode();
				throw new ArgumentNullException("obj");
			}
		}

		static readonly EqualityComparer<string> pathComparer = new PathComparer();


		readonly object breakpointsSync = new object();
		readonly Dictionary<BreakEvent, BreakEventInfo> breakEventToInfo = new Dictionary<BreakEvent, BreakEventInfo>();
		// we have to track bp ids per document because we have no event on breakpoint removing from debugger. so we just replace all of the breakpoints for specified document
		readonly Dictionary<int, Breakpoint> idToDocumentBreakEvent = new Dictionary<int, Breakpoint>();
		readonly Dictionary<Breakpoint, int> breakEventToId = new Dictionary<Breakpoint, int>();


		protected override BreakEventInfo OnInsertBreakEvent(BreakEvent breakEvent)
		{
			lock (breakpointsSync) {
				var newBreakEventInfo = new BreakEventInfo();
				breakEventToInfo[breakEvent] = newBreakEventInfo;
				if (breakEvent is Breakpoint) {
					var breakpoint = (Breakpoint)breakEvent;
					UpdatePositionalBreakpoints(breakpoint.FileName);
					return newBreakEventInfo;
				}
				if (breakEvent is Catchpoint) {
					var catchpoint = (Catchpoint)breakEvent;
					UpdateExceptions();
					return newBreakEventInfo;
				}
				throw new NotImplementedException(breakEvent.GetType().FullName);
			}
		}

		void UpdateExceptions()
		{
			var hasCustomExceptions = Breakpoints.GetCatchpoints().Any(catchpoint => catchpoint.Enabled);
			ProtocolClient.SendRequestAsync(new SetExceptionBreakpointsRequest(new SetExceptionBreakpointsArguments {
				filters = Capabilities.exceptionBreakpointFilters.Where(f => hasCustomExceptions || f.@default).Select(f => f.filter).ToArray()
			}));
		}

		protected override void OnNextInstruction()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new NextRequest(new NextRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnNextLine()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new NextRequest(new NextRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnRemoveBreakEvent(BreakEventInfo eventInfo)
		{
			lock (breakpointsSync)
			{
				var originalBreakEvent = eventInfo.BreakEvent;
				if (originalBreakEvent == null)
					return;
				breakEventToInfo.Remove(originalBreakEvent);
				if (originalBreakEvent is Breakpoint)
				{
					var breakpoint = (Breakpoint)originalBreakEvent;
					UpdatePositionalBreakpoints(breakpoint.FileName);
				}
				else if (originalBreakEvent is Catchpoint)
				{
					UpdateExceptions();
				}
			}
		}

		Process debugAgentProcess;
		public ProtocolClient ProtocolClient { get; private set; }

		Backtrace GetThreadBacktrace(long threadId)
		{
			return new Backtrace(new VSCodeDebuggerBacktrace(this, threadId));
		}

		private ObjectValue EvaluateExpression(int threadId, string expressionToEvaluate)
		{
			var threadInfo = GetThread(OnGetProcesses()[0], threadId);
			var vsCodeBacktrace = new VSCodeDebuggerBacktrace(this, threadInfo.Id);
			var frames = vsCodeBacktrace.GetStackFrames(0, 1);

			if (frames.Length == 0)
				throw new EvaluatorException("Frame list is empty when try to compute value of " + expressionToEvaluate);
			var objectValues = vsCodeBacktrace.GetExpressionValues(frames[0].Index,
				new[] {expressionToEvaluate}, EvaluationOptions);

			if (objectValues.Length != 1)
				throw new EvaluatorException(string.Format("Failed to evaluate {0}, "+
				                                           "backtrace returned nothing", expressionToEvaluate));
			return objectValues.First();
		}

		void HandleEvent(EventBody eventBody)
		{
			if (eventBody is OutputEventBody) {
				var outputEventBody = (OutputEventBody) eventBody;
				switch (outputEventBody.category) {
					case "telemetry":
						OnDebuggerOutput(false, string.Format("{0}: {1} {2}", outputEventBody.category, outputEventBody.output, outputEventBody.data).Trim());
						break;
					case "stdout":
						if (outputEventBody.output != null)
							OnTargetOutput(false, outputEventBody.output);
						break;
					case "stderr":
						if (outputEventBody.output != null)
							OnTargetOutput(true, outputEventBody.output);
						break;
					case "console":
					default:
						if (outputEventBody.output != null)
							OnTargetDebug(-1, "", outputEventBody.output);
						break;
				}				
			}
			if (eventBody is InitializedEventBody)
			{
				//OnStarted();
			}
			else if (eventBody is BreakpointEventBody)
			{
				var breakpointEventBody = (BreakpointEventBody)eventBody;
				var responseBreakpoint = breakpointEventBody.breakpoint;
				var id = responseBreakpoint.id ?? -1;
				if (id == -1) {
					//OnDebuggerOutput(true, "Breakpoint has no id");
					return;
				}
				Breakpoint breakpoint;
				BreakEventInfo info;

				lock (breakpointsSync)
				{
					if (!idToDocumentBreakEvent.TryGetValue(id, out breakpoint)) {
						//OnDebuggerOutput(true, string.Format("No breakpoint with id {0} in document {1}", id, source.path));
						return;
					}

					if (!breakEventToInfo.TryGetValue(breakpoint, out info)) {
						OnDebuggerOutput(true, string.Format("No break event for breakpoint {0}", breakpoint));
						return;
					}
				}
				UpdateBreakEventInfoFromProtocolBreakpoint(info, breakpoint, responseBreakpoint);
			}
			else if (eventBody is StoppedEventBody)
			{
				var stoppedEvent = (StoppedEventBody)eventBody;
				TargetEventArgs args;
				switch (stoppedEvent.reason) {
					case "breakpoint":
						args = new TargetEventArgs(TargetEventType.TargetHitBreakpoint);
						if (stoppedEvent.source == null)
						{
							OnDebuggerOutput(true, "No source file returned from debugger");
						}
						else
						{
							var sourcePath = stoppedEvent.source.path;
							if (stoppedEvent.line == null)
							{
								OnDebuggerOutput(true, "No line specified by debugger");
							}
							else
							{
								var breakpointAtLine = Breakpoints.GetBreakpointsAtFileLine(sourcePath, stoppedEvent.line.Value).ToList();
								breakpointAtLine.AddRange(GetRunToCursorBreakpointsAtFile(sourcePath, stoppedEvent.line.Value));
								var suitableBreakpoint = breakpointAtLine.FirstOrDefault();
								if (stoppedEvent.column != null)
								{
									suitableBreakpoint = breakpointAtLine.FirstOrDefault(b => b.Column == stoppedEvent.column.Value) ?? suitableBreakpoint;
								}

								if (suitableBreakpoint != null)
								{
									if (!string.IsNullOrEmpty (suitableBreakpoint.ConditionExpression)) {
										try {
											var objectValue = EvaluateExpression(stoppedEvent.threadId, suitableBreakpoint.ConditionExpression);
											var conditionResult = objectValue.Value;
											if (suitableBreakpoint.BreakIfConditionChanges) {
												if (conditionResult == suitableBreakpoint.LastConditionValue) {
													Continue();
													return;
												}
												suitableBreakpoint.LastConditionValue =
													suitableBreakpoint.LastConditionValue = conditionResult;
											}
											else {
												if (conditionResult != null && conditionResult.ToLower() != "true") {
													Continue();
													return;
												}
											}
										}
										catch (Exception ex)
										{
											OnDebuggerOutput(false, ex.Message);
											Continue();
											return;
										}
									}

									if ((suitableBreakpoint.HitAction & HitAction.PrintTrace) != HitAction.None)
									{
										OnTargetDebug(0, "", "Breakpoint reached: " + suitableBreakpoint.FileName + ":" + suitableBreakpoint.Line + Environment.NewLine);
									}

									if ((suitableBreakpoint.HitAction & HitAction.PrintExpression) != HitAction.None)
									{
										var traceExpression = EvaluateTrace(GetThread(OnGetProcesses()[0], stoppedEvent.threadId), suitableBreakpoint.TraceExpression);
										OnTargetDebug(0, "", traceExpression + Environment.NewLine);
									}

									if ((suitableBreakpoint.HitAction & HitAction.Break) == HitAction.None)
									{
										Continue();
										return;
									}

									lock (breakpointsSync)
									{
										BreakEventInfo info;
										if (!breakEventToInfo.TryGetValue(suitableBreakpoint, out info)) {
											OnDebuggerOutput(false,
												string.Format("No break event for breakpoint {0}", suitableBreakpoint));
											return;
										}
										info.IncrementHitCount();
										if (!info.HitCountReached)
										{
											Continue();
											return;
										}
									}
								}

								args.BreakEvent = suitableBreakpoint;
							}
						}
						break;
					case "step":
					case "pause":
						args = new TargetEventArgs(TargetEventType.TargetStopped);
						break;
					case "exception":
						args = new TargetEventArgs(TargetEventType.UnhandledException);
						break;
					default:
						throw new NotImplementedException(stoppedEvent.reason);
				}
				currentThreadId = stoppedEvent.threadId;
				args.Process = OnGetProcesses()[0];
				args.Thread = GetThread(args.Process, stoppedEvent.threadId);
				args.Backtrace = GetThreadBacktrace(stoppedEvent.threadId);
				args.IsStopEvent = true;

				OnTargetEvent(args);
			}
			else if (eventBody is ExitedEventBody)
			{
				var exitedEvent = (ExitedEventBody)eventBody;
				var targetEventArgs = new TargetEventArgs(TargetEventType.TargetExited)
				{
					ExitCode = exitedEvent.exitCode
				};
				OnTargetEvent(targetEventArgs);
			}
			else if (eventBody is TerminatedEventBody)
			{
				debugAgentProcess.Kill();
			}
		}

		string EvaluateTrace(ThreadInfo threadInfo, string exp)
		{
			StringBuilder sb = new StringBuilder();
			int last = 0;
			int i = exp.IndexOf('{');
			while (i != -1) {
				if (i < exp.Length - 1 && exp [i+1] == '{') {
					sb.Append(exp.Substring(last, i - last + 1));
					last = i + 2;
					i = exp.IndexOf('{', i + 2);
					continue;
				}
				int j = exp.IndexOf('}', i + 1);
				if (j == -1)
					break;
				string expressionToEvaluate = exp.Substring(i + 1, j - i - 1);
				var vsCodeBacktrace = new VSCodeDebuggerBacktrace(this, threadInfo.Id);
				var frames = vsCodeBacktrace.GetStackFrames(0, 1);

				if (frames.Length == 0)
					return "";
				var objectValues = vsCodeBacktrace.GetExpressionValues(frames[0].Index,
					new[] {expressionToEvaluate}, EvaluationOptions);
				sb.Append(exp.Substring(last, i - last));
				sb.Append(objectValues[0].Value);
				last = j + 1;
				i = exp.IndexOf('{', last);
			}
			sb.Append(exp.Substring(last, exp.Length - last));
			return sb.ToString();
		}

		ThreadInfo GetThread(ProcessInfo process, long threadId)
		{
			foreach (var threadInfo in OnGetThreads(process.Id)) {
				if (threadInfo.Id == threadId)
					return threadInfo;
			}
			return null;
		}

		public ReadOnlyCollection<Breakpoint> GetRunToCursorBreakpointsAtFile(string filename, int line = -1)
		{
			if (filename == null)
				throw new ArgumentNullException ("filename");

			var list = new List<Breakpoint> ();
			if (string.IsNullOrEmpty (filename))
				return list.AsReadOnly ();

			try {
				filename = Path.GetFullPath (filename);
			} catch {
				return list.AsReadOnly ();
			}

			foreach (var bp in Breakpoints.OfType<RunToCursorBreakpoint> ()) {
				if (BreakpointStore.FileNameEquals(bp.FileName, filename)) {
					if (line != -1 && !(bp.OriginalLine == line || bp.Line == line))
						continue;
					list.Add(bp);
				}
			}

			return list.AsReadOnly ();
		}

		void UpdatePositionalBreakpoints(string filename)
		{
			var allFileBreakpoints = Breakpoints.GetBreakpointsAtFile(filename).ToList();

			allFileBreakpoints.AddRange(GetRunToCursorBreakpointsAtFile(filename));

			var activeFileBreakpoints = allFileBreakpoints.Where(bp => bp.Enabled).ToList();
			ProtocolClient.SendRequestAsync(new SetBreakpointsRequest(new SetBreakpointsRequestArguments
			{
				Source = new Source(filename),
				Breakpoints = activeFileBreakpoints.Select(bp => new SourceBreakpoint
				{
					Line = bp.Line,
					Column = bp.Column,
					Condition = bp.ConditionExpression
				}).ToList()
			})).ContinueWith(t =>
			{
				if (t.IsFaulted)
					return;
				var response = t.Result;
				if (response.breakpoints.Length != activeFileBreakpoints.Count)
				{
					OnDebuggerOutput(true, string.Format("Debugger returned {0} breakpoints but was requested to set {1}",
						response.breakpoints.Length, activeFileBreakpoints.Count));
					return;
				}
				lock (breakpointsSync) {
					// remove old breakpoints

					foreach (var oldBreakevent in allFileBreakpoints) {
						int oldId;
						if (breakEventToId.TryGetValue(oldBreakevent, out oldId)) {
							idToDocumentBreakEvent.Remove(oldId);
							breakEventToId.Remove(oldBreakevent);
						}
					}
					for (int i = 0; i < response.breakpoints.Length; i++) {
						var breakpoint = activeFileBreakpoints[i];
						BreakEventInfo info;
						if (!breakEventToInfo.TryGetValue(breakpoint, out info)) {
							OnDebuggerOutput(false, string.Format("Can't update status for breakpoint {0} {1} {2} because it isn't actual",
								breakpoint.FileName, breakpoint.Line, breakpoint.Column));
							continue;
						}
						var responseBreakpoint = response.breakpoints[i];
						UpdateBreakEventInfoFromProtocolBreakpoint(info, breakpoint, responseBreakpoint);
						var id = responseBreakpoint.id ?? -1;
						if (id != -1) {
							idToDocumentBreakEvent[id] = breakpoint;
							breakEventToId[breakpoint] = id;
						}
						else {
							OnDebuggerOutput(true, string.Format("Debugger returned breakpoint without id. File {0}, line {1}, column {2}",
								breakpoint.FileName, breakpoint.Line, breakpoint.Column));
						}
					}
				}
			});
		}

		void UpdateBreakEventInfoFromProtocolBreakpoint(BreakEventInfo breakEventInfo, Breakpoint breakpoint,
			VSBreakpoint responseBreakpoint)
		{
			breakEventInfo.SetStatus(responseBreakpoint.verified ? BreakEventStatus.Bound : BreakEventStatus.NotBound, responseBreakpoint.message ?? "");
			breakEventInfo.AdjustBreakpointLocation(responseBreakpoint.column ?? breakpoint.Line, responseBreakpoint.column ?? breakpoint.Column);
			breakEventInfo.Handle = responseBreakpoint;
		}

		void StartDebugAgent()
		{
			if (debuggerAgentParameters.CoreClrDebugAdapterLocation == null)
				throw new ArgumentException("vsCodeStartInfo.CoreClrDebugAdapterLocation must not be null");
			var adapterFullpath = Path.Combine(debuggerAgentParameters.CoreClrDebugAdapterLocation, VSCodeDebuggerAgentParameters.AdapterFilename);
			if (!File.Exists(adapterFullpath))
				throw new FileNotFoundException("Debugger adapter not found", adapterFullpath);
			var startInfo = new ProcessStartInfo(adapterFullpath)
			{
				RedirectStandardOutput = true,
				RedirectStandardInput = true,
				StandardOutputEncoding = Encoding.UTF8,
				UseShellExecute = false
			};
			if (!string.IsNullOrEmpty(debuggerAgentParameters.DebuggerEngineLogFilePath)) {
				startInfo.Arguments = $"--trace=response --engineLogging='{debuggerAgentParameters.DebuggerEngineLogFilePath}'";
			}
			if (!string.IsNullOrEmpty(debuggerAgentParameters.DotNetCliLocation)) {
				startInfo.EnvironmentVariables["PATH"] = debuggerAgentParameters.DotNetCliLocation + Path.PathSeparator +
														startInfo.EnvironmentVariables["PATH"];
			}
			debugAgentProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true};
			var cancellationTokenSource = new CancellationTokenSource();
			debugAgentProcess.Exited += (sender, args) =>
			{
				try {
					cancellationTokenSource.Cancel();
					OnTargetEvent(new TargetEventArgs(TargetEventType.TargetExited));
				}
				catch (Exception e) {
					HandleException(e);
				}
			};
			debugAgentProcess.Start();
			ProtocolClient = new ProtocolClient(debugAgentProcess.StandardOutput.BaseStream, debugAgentProcess.StandardInput.BaseStream, cancellationTokenSource.Token);
			ProtocolClient.OnEvent += HandleEvent;
			ProtocolClient.DispatchException += ProtocolClientOnException;
			ProtocolClient.ReceiveException += ProtocolClientOnException;
			ProtocolClient.SendException += ProtocolClientOnException;
			ProtocolClient.Start();
			var initRequest = new InitializeRequest(new InitializeRequestArguments() {
				adapterID = "coreclr",
				linesStartAt1 = true,
				columnsStartAt1 = true,
				pathFormat = "path",
				supportsVariableType = true
			});
			Capabilities = ProtocolClient.SendRequestSync(initRequest);
		}

		void ProtocolClientOnException(object sender, ThreadExceptionEventArgs threadExceptionEventArgs)
		{
			HandleException(threadExceptionEventArgs.Exception);
		}

		Capabilities Capabilities;

		protected override void OnRun(DebuggerStartInfo startInfo)
		{
			var vsCodeDebuggerStartInfo = startInfo as VSCodeDebuggerStartInfo;
			if (vsCodeDebuggerStartInfo == null)
				throw new ArgumentException("startInfo must be VSCodeDebuggerStartInfo");

			StartDebugAgent();
			var cwd = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? Path.GetDirectoryName(startInfo.Command) : startInfo.WorkingDirectory;
			//var dotnetCommand = vsCodeDebuggerStartInfo.DotNetCliPath ?? (PlatformUtil.IsWindows ? "dotnet.exe" : "dotnet");
			var launchRequestArguments = new LaunchRequestArguments {
				Name = ".NET Core Launch (console)",
				Type = "coreclr",
				Request = "launch",
				Program = startInfo.Command,
				Args = ParametersUtil.ReadArgs(startInfo.Arguments).Cast<object>().ToArray(),
				Cwd = cwd,
				NoDebug = false,
				StopAtEntry = vsCodeDebuggerStartInfo.StopAtEntry,
				ExternalConsole = vsCodeDebuggerStartInfo.UseExternalConsole,
				Env = vsCodeDebuggerStartInfo.EnvironmentVariables
			};
			if (vsCodeDebuggerStartInfo.BuildBeforeRun) {
				launchRequestArguments.PreLaunchTask = "build";
			}
			var launchRequest = new LaunchRequest(launchRequestArguments);
			var lal = ProtocolClient.SendRequestSync(launchRequest);
			OnStarted();
		}

		protected override void OnStarted(ThreadInfo t)
		{
			base.OnStarted(t);
			// was sync
			ProtocolClient.SendRequestAsync(new ConfigurationDoneRequest());
		}

		protected override void OnSetActiveThread(long processId, long threadId)
		{
			currentThreadId = threadId;
		}

		protected override void OnStepInstruction()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new StepInRequest(new StepInRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnStepLine()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new StepInRequest(new StepInRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnStop()
		{
			// was sync
			ProtocolClient.SendRequestAsync(new PauseRequest(new PauseRequestArguments {
				threadId = currentThreadId
			}));
		}
	}
}

