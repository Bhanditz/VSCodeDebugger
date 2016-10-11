using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using VSCodeDebugging.VSCodeProtocol;
using StackFrame = VSCodeDebugging.VSCodeProtocol.StackFrame;

namespace VSCodeDebugging
{
	public partial class VSCodeDebuggerBacktrace : IBacktrace
	{
		private static readonly HashSet<string> PredefinedGroupNames = new HashSet<string> { "Static members", "Non-Public members" };

		long threadId;
		readonly VSCodeDebuggerSession vsCodeDebuggerSession;
		readonly StackFrame[] frames;

		public VSCodeDebuggerBacktrace(VSCodeDebuggerSession vsCodeDebuggerSession, long threadId)
		{
			this.vsCodeDebuggerSession = vsCodeDebuggerSession;
			this.threadId = threadId;
			var body = vsCodeDebuggerSession.ProtocolClient.SendRequestSync(new StackTraceRequest(new StackTraceArguments {
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
			var scopeBody = vsCodeDebuggerSession.ProtocolClient.SendRequestSync(new ScopesRequest(new ScopesArguments {
				frameId = frames[frameIndex].id
			}));
			foreach (var variablesGroup in scopeBody.scopes) {
				var variables = vsCodeDebuggerSession.ProtocolClient.SendRequestSync(new VariablesRequest(new VariablesRequestArguments {
					variablesReference = variablesGroup.variablesReference
				}));
				foreach (var variable in variables.variables) {
					results.Add(VsCodeVariableToObjectValue(vsCodeDebuggerSession, variable.name, variable.value, variable.type, variable.variablesReference));
				}
			}
			return results.ToArray();
		}

		public ExceptionInfo GetException(int frameIndex, EvaluationOptions options)
		{
			return new ExceptionInfo(GetAllLocals(frameIndex, options).FirstOrDefault(o => o.Name == "$exception"));
		}

		public CompletionData GetExpressionCompletionData(int frameIndex, string exp)
		{
			return new CompletionData();
		}

		public ObjectValue[] GetExpressionValues(int frameIndex, string[] expressions, EvaluationOptions options)
		{
			var results = new List<ObjectValue>();
			foreach (var expr in expressions) {
				var responseBody = vsCodeDebuggerSession.ProtocolClient.SendRequestSync(new EvaluateRequest(new EvaluateRequestArguments {
					expression = expr,
					frameId = frames[frameIndex].id
				}));
				results.Add(VsCodeVariableToObjectValue(vsCodeDebuggerSession, expr, responseBody.result, responseBody.type, responseBody.variablesReference));
			}
			return results.ToArray();
		}

		static ObjectValue VsCodeVariableToObjectValue(VSCodeDebuggerSession vsCodeDebuggerSession, string name, string value, string type, int variablesReference)
		{
			name = NormalizeName(name);
			var resultingTypeName = type ?? "unknown";

			var isGroup = IsGroup(name, type);
			if (variablesReference == 0)  //This is some kind of primitive... 
				return ObjectValue.CreatePrimitive(null, new ObjectPath(name), resultingTypeName, new EvaluationResult(value), isGroup ? ObjectValueFlags.Group : ObjectValueFlags.None);
			else
				return ObjectValue.CreateObject(new VSCodeObjectSource(vsCodeDebuggerSession, variablesReference), new ObjectPath(name), resultingTypeName, 
					new EvaluationResult(value), isGroup ? ObjectValueFlags.Group : ObjectValueFlags.None, null);
		}

		static bool IsGroup(string name, string type)
		{
			if (type != null)
				return false;
			return PredefinedGroupNames.Contains(name);
		}

		static string NormalizeName(string name)
		{
			var firstBracket = name.IndexOf("[", StringComparison.InvariantCulture);
			var lastBracket = name.LastIndexOf("]", StringComparison.InvariantCulture);
			// don't cut if the name starts with '['
			if (firstBracket > 0 && lastBracket != -1 && firstBracket < lastBracket) {
				name = name.Remove(firstBracket, lastBracket - firstBracket + 1);
			}
			return name.Trim();
		}

		public ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
		{
			throw new NotImplementedException();
		}

		public ObjectValue[] GetParameters(int frameIndex, EvaluationOptions options)
		{
			throw new NotImplementedException();
/*			List<ObjectValue> results = new List<ObjectValue>();
			var scopeBody = vsCodeDebuggerSession.ProtocolClient.SendRequestSync(new ScopesRequest(new ScopesArguments {
				frameId = frames[frameIndex].id
			}));
			foreach (var variablesGroup in scopeBody.scopes) {
				var variables = vsCodeDebuggerSession.ProtocolClient.SendRequestSync(new VariablesRequest(new VariablesRequestArguments {
					variablesReference = variablesGroup.variablesReference
				}));
				foreach (var variable in variables.variables) {
					results.Add(ObjectValue.CreatePrimitive(null, new ObjectPath(variable.name), "unknown", new EvaluationResult(variable.value), ObjectValueFlags.None));
				}
			}
			return results.ToArray();*/
		}

		public Mono.Debugging.Client.StackFrame[] GetStackFrames(int firstIndex, int lastIndex)
		{
			var maxIndex = Math.Min(lastIndex, frames.Length);
			var stackFrames = new Mono.Debugging.Client.StackFrame[maxIndex - firstIndex];
			for (int i = firstIndex; i < maxIndex; i++)
			{
				var vsFrame = frames[i];
				var vsFrameSource = vsFrame.source;
				var vsFrameName = vsFrame.name;
				var parts = vsFrameName.Split('.').ToList();
				var fullTypeName = string.Empty;
				if (parts.Count > 1) {
					fullTypeName = string.Join(".", parts.Take(parts.Count - 1));
				}
				var hasDebugInfo = vsFrameSource != null;
				stackFrames[i - firstIndex] = new Mono.Debugging.Client.StackFrame(vsFrame.id, string.Empty, 
					new SourceLocation(vsFrameName, vsFrameSource?.path, vsFrame.line, vsFrame.column, -1, -1), "C#", !hasDebugInfo, hasDebugInfo, string.Empty, fullTypeName);
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
}
