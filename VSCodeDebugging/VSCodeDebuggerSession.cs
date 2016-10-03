using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using VSCodeDebugging.VSCodeProtocol;
using Breakpoint = Mono.Debugging.Client.Breakpoint;
using VSBreakpoint = VSCodeDebugging.VSCodeProtocol.Breakpoint;
using VSStackFrame = VSCodeDebugging.VSCodeProtocol.StackFrame;
using StackFrame = Mono.Debugging.Client.StackFrame;

namespace VSCodeDebugging
{
	public class VSCodeDebuggerSession : DebuggerSession
	{
		long currentThreadId;
		protected override void OnAttachToProcess(long processId)
		{
			throw new NotImplementedException();
		}

		protected override void OnContinue()
		{
			// was sync
			protocolClient.SendRequestAsync(new ContinueRequest(new ContinueRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnDetach()
		{
			// was sync
			protocolClient.SendRequestAsync(new DisconnectRequest());
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
			protocolClient.SendRequestAsync(new DisconnectRequest());
		}

		protected override void OnFinish()
		{
			// was sync
			protocolClient.SendRequestAsync(new StepOutRequest(new StepOutRequestArguments {
				threadId = currentThreadId
			}));
		}

		ProcessInfo[] processInfo = { new ProcessInfo(1, "debugee") };

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
			var threadsResponse = protocolClient.SendRequestSync(new ThreadsRequest());
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
		readonly Dictionary<string, Dictionary<int, Breakpoint>> idToDocumentBreakEvent = new Dictionary<string, Dictionary<int, Breakpoint>>(pathComparer);


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
			// TODO
//			var catchpoints = Breakpoints.GetCatchpoints().Where(catchpoint => catchpoint.Enabled);
//			protocolClient.SendRequestAsync(new SetExceptionBreakpointsRequest(new SetExceptionBreakpointsArguments {
//				filters = Capabilities.exceptionBreakpointFilters.Where(f => hasCustomExceptions || (f.Default ?? false)).Select(f => f.Filter).ToArray()
//			}));
		}

		protected override void OnNextInstruction()
		{
			// was sync
			protocolClient.SendRequestAsync(new NextRequest(new NextRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnNextLine()
		{
			// was sync
			protocolClient.SendRequestAsync(new NextRequest(new NextRequestArguments {
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
		ProtocolClient protocolClient;

		class VSCodeDebuggerBacktrace : IBacktrace
		{
			long threadId;
			VSCodeDebuggerSession vsCodeDebuggerSession;
			VSStackFrame[] frames;

			public VSCodeDebuggerBacktrace(VSCodeDebuggerSession vsCodeDebuggerSession, long threadId)
			{
				this.vsCodeDebuggerSession = vsCodeDebuggerSession;
				this.threadId = threadId;
				var body = vsCodeDebuggerSession.protocolClient.SendRequestSync(new StackTraceRequest(new StackTraceArguments {
					threadId = threadId,
					startFrame = 0,
					levels = 20
				}));
				frames = body.stackFrames;
			}

			public int FrameCount {
				get {
					return frames.Length;
				}
			}

			public AssemblyLine[] Disassemble(int frameIndex, int firstLine, int count)
			{
				throw new NotImplementedException();
			}

			public ObjectValue[] GetAllLocals(int frameIndex, EvaluationOptions options)
			{
				List<ObjectValue> results = new List<ObjectValue>();
				var scopeBody = vsCodeDebuggerSession.protocolClient.SendRequestSync(new ScopesRequest(new ScopesArguments {
					frameId = frames[frameIndex].id
				}));
				foreach (var variablesGroup in scopeBody.scopes) {
					var varibles = vsCodeDebuggerSession.protocolClient.SendRequestSync(new VariablesRequest(new VariablesRequestArguments {
						variablesReference = variablesGroup.variablesReference
					}));
					foreach (var variable in varibles.variables) {
						results.Add(VsCodeVariableToObjectValue(vsCodeDebuggerSession, variable.name, variable.value, variable.variablesReference));
					}
				}
				return results.ToArray();
			}

			public ExceptionInfo GetException(int frameIndex, EvaluationOptions options)
			{
				return new ExceptionInfo(GetAllLocals(frameIndex, options).Where(o => o.Name == "$exception").FirstOrDefault());
			}

			public CompletionData GetExpressionCompletionData(int frameIndex, string exp)
			{
				return new CompletionData();
			}

			class VSCodeObjectSource : IObjectValueSource
			{
				int variablesReference;
				VSCodeDebuggerSession vsCodeDebuggerSession;

				public VSCodeObjectSource(VSCodeDebuggerSession vsCodeDebuggerSession, int variablesReference)
				{
					this.vsCodeDebuggerSession = vsCodeDebuggerSession;
					this.variablesReference = variablesReference;
				}

				public ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
				{
					var children = vsCodeDebuggerSession.protocolClient.SendRequestSync(new VariablesRequest(new VariablesRequestArguments {
						variablesReference = variablesReference
					})).variables;
					return children.Select(c => VsCodeVariableToObjectValue(vsCodeDebuggerSession, c.name, c.value, c.variablesReference)).ToArray();
				}

				public object GetRawValue(ObjectPath path, EvaluationOptions options)
				{
					throw new NotImplementedException();
				}

				public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
				{
					throw new NotImplementedException();
				}

				public void SetRawValue(ObjectPath path, object value, EvaluationOptions options)
				{
					throw new NotImplementedException();
				}

				public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
				{
					throw new NotImplementedException();
				}
			}

			public ObjectValue[] GetExpressionValues(int frameIndex, string[] expressions, EvaluationOptions options)
			{
				var results = new List<ObjectValue>();
				foreach (var expr in expressions) {
					var responseBody = vsCodeDebuggerSession.protocolClient.SendRequestSync(new EvaluateRequest(new EvaluateRequestArguments {
						expression = expr,
						frameId = frames[frameIndex].id
					}));
					results.Add(VsCodeVariableToObjectValue(vsCodeDebuggerSession, expr, responseBody.result, responseBody.variablesReference));
				}
				return results.ToArray();
			}

			static ObjectValue VsCodeVariableToObjectValue(VSCodeDebuggerSession vsCodeDebuggerSession, string name, string value, int variablesReference)
			{
				if (variablesReference == 0)//This is some kind of primitive...
					return ObjectValue.CreatePrimitive(null, new ObjectPath(name), "unknown", new EvaluationResult(value), ObjectValueFlags.ReadOnly);
				else
					return ObjectValue.CreateObject(new VSCodeObjectSource(vsCodeDebuggerSession, variablesReference), new ObjectPath(name), "unknown", new EvaluationResult(value), ObjectValueFlags.ReadOnly, null);
			}

			public ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
			{
				throw new NotImplementedException();
			}

			public ObjectValue[] GetParameters(int frameIndex, EvaluationOptions options)
			{
				List<ObjectValue> results = new List<ObjectValue>();
				var scopeBody = vsCodeDebuggerSession.protocolClient.SendRequestSync(new ScopesRequest(new ScopesArguments {
					frameId = frames[frameIndex].id
				}));
				foreach (var variablesGroup in scopeBody.scopes) {
					var varibles = vsCodeDebuggerSession.protocolClient.SendRequestSync(new VariablesRequest(new VariablesRequestArguments {
						variablesReference = variablesGroup.variablesReference
					}));
					foreach (var variable in varibles.variables) {
						results.Add(ObjectValue.CreatePrimitive(null, new ObjectPath(variable.name), "unknown", new EvaluationResult(variable.value), ObjectValueFlags.None));
					}
				}
				return results.ToArray();
			}

			public StackFrame[] GetStackFrames(int firstIndex, int lastIndex)
			{
				var maxIndex = Math.Min(lastIndex, frames.Length);
				var stackFrames = new StackFrame[maxIndex - firstIndex];
				for (int i = firstIndex; i < maxIndex; i++)
				{
					var vsFrame = frames[i];
					stackFrames[i - firstIndex] = new StackFrame(vsFrame.id, new SourceLocation(vsFrame.name, vsFrame.source?.path, vsFrame.line, vsFrame.column, -1, -1), "C#");
				}
				return stackFrames;
			}

			public ObjectValue GetThisReference(int frameIndex, EvaluationOptions options)
			{
				return GetAllLocals(frameIndex, options).FirstOrDefault(l => l.Name == "this");
			}

			public ValidationResult ValidateExpression(int frameIndex, string expression, EvaluationOptions options)
			{
				return new ValidationResult(true, null);
			}
		}

		Backtrace GetThreadBacktrace(long threadId)
		{
			return new Backtrace(new VSCodeDebuggerBacktrace(this, threadId));
		}

		void HandleEvent(EventBody eventBody)
		{
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
				var source = responseBreakpoint.source;
				if (source == null) {
					//OnDebuggerOutput(true, "No source specified for breakpoint");
					return;
				}
				if (source.path == null) {
					//OnDebuggerOutput(true, "No source path specified for breakpoint");
					return;
				}
				Breakpoint breakpoint;
				BreakEventInfo info;

				lock (breakpointsSync)
				{
					Dictionary<int, Breakpoint> idToBreakpoint;
					if (!idToDocumentBreakEvent.TryGetValue(source.path, out idToBreakpoint)) {
						OnDebuggerOutput(true, string.Format("No breakpoints for document: {0}", source.path));
						return;
					}
					if (!idToBreakpoint.TryGetValue(id, out breakpoint)) {
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
								var breakpointAtLine = Breakpoints.GetBreakpointsAtFileLine(sourcePath, stoppedEvent.line.Value);
								var suitableBreakpoint = breakpointAtLine.FirstOrDefault();
								if (stoppedEvent.column != null)
								{
									suitableBreakpoint = breakpointAtLine.FirstOrDefault(b => b.Column == stoppedEvent.column.Value) ?? suitableBreakpoint;
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
						args = new TargetEventArgs(TargetEventType.ExceptionThrown);
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
				//var terminatedEvent = (TerminatedEventBody)eventBody;
				OnTargetEvent(new TargetEventArgs(TargetEventType.TargetExited));
			}
		}

		ThreadInfo GetThread(ProcessInfo process, long threadId)
		{
			foreach (var threadInfo in OnGetThreads(process.Id)) {
				if (threadInfo.Id == threadId)
					return threadInfo;
			}
			return null;
		}

		void UpdatePositionalBreakpoints(string filename)
		{
			var breakpointsForFile = Breakpoints.GetBreakpointsAtFile(filename).Where(bp => bp.Enabled).ToList();
			protocolClient.SendRequestAsync(new SetBreakpointsRequest(new SetBreakpointsRequestArguments
			{
				Source = new Source(filename),
				Breakpoints = breakpointsForFile.Select(bp => new SourceBreakpoint
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
				if (response.breakpoints.Length != breakpointsForFile.Count)
				{
					OnDebuggerOutput(true, string.Format("Debugger returned {0} breakpoints but was requested to set {1}",
						response.breakpoints.Length, breakpointsForFile.Count));
					return;
				}
				lock (breakpointsSync) {
					if (!idToDocumentBreakEvent.ContainsKey(filename)) {
						idToDocumentBreakEvent[filename] = new Dictionary<int, Breakpoint>();
					}
					var idToBreakEventForDocument = idToDocumentBreakEvent[filename];
					idToBreakEventForDocument.Clear();
					for (int i = 0; i < response.breakpoints.Length; i++) {
						var breakpoint = breakpointsForFile[i];
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
							idToBreakEventForDocument[id] = breakpoint;
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
			var startInfo = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(typeof(VSCodeDebuggerSession).Assembly.Location), "CoreClrAdaptor", "OpenDebugAD7.exe"));
			startInfo.RedirectStandardOutput = true;
			startInfo.RedirectStandardInput = true;
			startInfo.StandardOutputEncoding = Encoding.UTF8;
			startInfo.StandardOutputEncoding = Encoding.UTF8;
			startInfo.UseShellExecute = false;
			startInfo.Arguments = "--trace=response --engineLogging=VSDebugLog.log";
			startInfo.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH") + "C:\\Program Files\\dotnet;";
			debugAgentProcess = new Process { StartInfo = startInfo };
			protocolClient = new ProtocolClient();
			protocolClient.OnEvent += HandleEvent;
			protocolClient.DispatchException += ProtocolClientOnException;
			protocolClient.ReceiveException += ProtocolClientOnException;
			protocolClient.SendException += ProtocolClientOnException;
			debugAgentProcess.Start();
			protocolClient.Start(debugAgentProcess.StandardOutput.BaseStream, debugAgentProcess.StandardInput.BaseStream);
			var initRequest = new InitializeRequest(new InitializeRequestArguments() {
				adapterID = "coreclr",
				linesStartAt1 = true,
				columnsStartAt1 = true,
				pathFormat = "path"
			});
			Capabilities = protocolClient.SendRequestSync(initRequest);
		}

		void ProtocolClientOnException(object sender, ThreadExceptionEventArgs threadExceptionEventArgs)
		{
			HandleException(threadExceptionEventArgs.Exception);
		}

		Capabilities Capabilities;

		protected override void OnRun(DebuggerStartInfo startInfo)
		{
			StartDebugAgent();
			var cwd = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? Path.GetDirectoryName(startInfo.Command) : startInfo.WorkingDirectory;
			var launchRequest = new LaunchRequest(new LaunchRequestArguments {
				Name = ".NET Core Launch (console)",
				Type = "coreclr",
				Request = "launch",
				PreLaunchTask = "build",
				Program = startInfo.Command,
				// for test purposes
				Args = new[] {"C:\\Users\\Artem.Bukhonov\\RiderProjects\\DotNetCoreConsoleApplication\\DotNetCoreConsoleApplication\\bin\\Debug\\netcoreapp1.0\\DotNetCoreConsoleApplication.dll"},//startInfo.Arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
				Cwd = cwd,
				NoDebug = false,
				StopAtEntry = false
			});
			var lal = protocolClient.SendRequestSync(launchRequest);
			OnStarted();
		}

		protected override void OnStarted(ThreadInfo t)
		{
			base.OnStarted(t);
			// was sync
			protocolClient.SendRequestAsync(new ConfigurationDoneRequest());
		}

		protected override void OnSetActiveThread(long processId, long threadId)
		{
			currentThreadId = threadId;
		}

		protected override void OnStepInstruction()
		{
			// was sync
			protocolClient.SendRequestAsync(new StepInRequest(new StepInRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnStepLine()
		{
			// was sync
			protocolClient.SendRequestAsync(new StepInRequest(new StepInRequestArguments {
				threadId = currentThreadId
			}));
		}

		protected override void OnStop()
		{
			// was sync
			protocolClient.SendRequestAsync(new PauseRequest(new PauseRequestArguments {
				threadId = currentThreadId
			}));
		}
	}
}

