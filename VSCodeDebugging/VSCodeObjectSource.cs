using System;
using System.Linq;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using VSCodeDebugging.VSCodeProtocol;

namespace VSCodeDebugging
{
	public partial class VSCodeDebuggerBacktrace
	{
		class VSCodeObjectSource : IObjectValueSource
		{
			readonly int variablesReference;
			readonly VSCodeDebuggerSession vsCodeDebuggerSession;

			public VSCodeObjectSource(VSCodeDebuggerSession vsCodeDebuggerSession, int variablesReference)
			{
				this.vsCodeDebuggerSession = vsCodeDebuggerSession;
				this.variablesReference = variablesReference;
			}

			public ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
			{
				var children = vsCodeDebuggerSession.ProtocolClient.SendRequestSync(new VariablesRequest(new VariablesRequestArguments {
					variablesReference = variablesReference
				})).variables;
				return children.Select(c => VsCodeVariableToObjectValue(vsCodeDebuggerSession, c.name, c.value, c.type, c.variablesReference)).ToArray();
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
	}
}
