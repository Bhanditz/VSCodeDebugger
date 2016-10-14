/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
// ReSharper disable InconsistentNaming

namespace VSCodeDebugging.VSCodeProtocol
{
	public class ProtocolMessage
	{
		public int seq;
		public string type { get; }

		public ProtocolMessage(string typ)
		{
			type = typ;
		}

		public ProtocolMessage(string typ, int sq)
		{
			type = typ;
			seq = sq;
		}
	}

	public abstract class Request : ProtocolMessage
	{
		public Request()
			: base("request")
		{

		}
		[JsonIgnore]
		ResponseBody objectBody;

		[JsonIgnore]
		public ResponseBody ObjectBody {
			get {
				return objectBody;
			}

			set {
				objectBody = value;
				ValueSet();
			}
		}

		protected abstract void ValueSet();
		[JsonIgnore]
		string errorMessage;

		[JsonIgnore]
		public string ErrorMessage {
			get {
				return errorMessage;
			}

			set {
				errorMessage = value;
				ValueSet();
			}
		}

		public abstract Type GetResponseBodyType();
	}

	public class Request<TRequestArguments, TResponseBody> : Request where TResponseBody : ResponseBody, new()
	{
		[JsonProperty("arguments", NullValueHandling = NullValueHandling.Ignore)]
		public TRequestArguments Arguments { get; set; }
		[JsonIgnore]
		public TResponseBody Response {
			get {
				return (TResponseBody)ObjectBody;
			}
		}

		public override Type GetResponseBodyType()
		{
			return typeof(TResponseBody);
		}

		protected override void ValueSet()
		{
			if (ErrorMessage != null)
				WaitingResponse.SetException(new Exception(ErrorMessage));
			else
				WaitingResponse.SetResult(Response);
		}

		[JsonIgnore]
		public TaskCompletionSource<TResponseBody> WaitingResponse { get; } = new TaskCompletionSource<TResponseBody>();

		public string command;

		public Request(string cmd, TRequestArguments args)
		{
			command = cmd;
			Arguments = args;
		}
	}

	/** Arguments for "initialize" request. */
	public class InitializeRequestArguments
	{
		/** The ID of the debugger adapter. Used to select or verify debugger adapter. */
		public string adapterID { get; set; }
		/** If true all line numbers are 1-based (default). */
		public bool linesStartAt1 { get; set; }
		/** If true all column numbers are 1-based (default). */
		public bool columnsStartAt1 { get; set; }
		/** Determines in what format paths are specified. Possible values are 'path' or 'uri'. The default is 'path', which is the native format. */
		public string pathFormat { get; set; }
		/** Client supports the optional type attribute for variables. */
		public bool supportsVariableType { get; set; }
		/** Client supports the paging of variables. */
		public bool supportsVariablePaging { get; set; }
		/** Client supports the runInTerminal request. */
		public bool supportsRunInTerminalRequest { get; set; }
	}

	public class LaunchRequest : Request<LaunchRequestArguments, ResponseBody>
	{
		public LaunchRequest(LaunchRequestArguments args)
		: base("launch", args)
		{

		}
	}

	public class LaunchRequestArguments
	{

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("type")]
		public string Type { get; set; }

		[JsonProperty("request")]
		public string Request { get; set; }

		[JsonProperty("preLaunchTask")]
		public string PreLaunchTask { get; set; }

		[JsonProperty("program")]
		public string Program { get; set; }

		[JsonProperty("args")]
		public IList<object> Args { get; set; }

		[JsonProperty("cwd")]
		public string Cwd { get; set; }

		[JsonProperty("stopAtEntry")]
		public bool StopAtEntry { get; set; }

		[JsonProperty("noDebug")]
		public bool NoDebug { get; set; }

		[JsonProperty("externalConsole")]
		public bool ExternalConsole { get; set; }

		[JsonProperty("env")]
		public Dictionary<string, string> Env { get; set; }
	}

	/** Next request; value of command field is "next".
		The request starts the debuggee to run again for one step.
		penDebug will respond with a StoppedEvent (event type 'step') after running the step.
	*/
	public class NextRequest : Request<NextRequestArguments, ResponseBody>
	{
		public NextRequest(NextRequestArguments args)
		: base("next", args)
		{

		}
	}
	/** Arguments for "next" request. */
	public class NextRequestArguments
	{
		/** Continue execution for this thread. */
		public long threadId { get; set; }
	}

	/** StepIn request; value of command field is "stepIn".
		The request starts the debuggee to run again for one step.
		The debug adapter will respond with a StoppedEvent (event type 'step') after running the step.
	*/
	public class StepInRequest : Request<StepInRequestArguments, ResponseBody>
	{
		public StepInRequest(StepInRequestArguments args)
		: base("stepIn", args)
		{

		}
	}
	/** Arguments for "stepIn" request. */
	public class StepInRequestArguments
	{
		/** Continue execution for this thread. */
		public long threadId { get; set; }
	}
	/** StepOut request; value of command field is "stepOut".
		The request starts the debuggee to run again for one step.
		penDebug will respond with a StoppedEvent (event type 'step') after running the step.
	*/
	public class StepOutRequest : Request<StepOutRequestArguments, ResponseBody>
	{
		public StepOutRequest(StepOutRequestArguments args)
		: base("stepOut", args)
		{

		}
	}
	/** Arguments for "stepIn" request. */
	public class StepOutRequestArguments
	{
		/** Continue execution for this thread. */
		public long threadId { get; set; }
	}
	/** Pause request; value of command field is "pause".
		The request suspenses the debuggee.
		penDebug will respond with a StoppedEvent (event type 'pause') after a successful 'pause' command.
	*/
	public class PauseRequest : Request<PauseRequestArguments, ResponseBody>
	{
		public PauseRequest(PauseRequestArguments args)
		: base("pause", args)
		{

		}
	}
	/** Arguments for "pause" request. */
	public class PauseRequestArguments
	{
		/** Pause execution for this thread. */
		public long threadId { get; set; }
	}

	/** Continue request; value of command field is "continue".
		The request starts the debuggee to run again.
	*/
	public class ContinueRequest : Request<ContinueRequestArguments, ContinueResponse>
	{
		public ContinueRequest(ContinueRequestArguments args) :
		base("continue", args)
		{

		}
	}
	/** Arguments for "continue" request. */
	public class ContinueRequestArguments
	{
		/** Continue execution for the specified thread (if possible). If the backend cannot continue on a single thread but will continue on all threads, it should set the allThreadsContinued attribute in the response to true. */
		public long threadId { get; set; }
	}
	/** Response to "continue" request. */
	public class ContinueResponse : ResponseBody
	{
		public bool? allThreadsContinued { get; set; }
	}

	/// <summary>
	/// Initialize request; value of command field is "initialize".
	/// </summary>
	public class InitializeRequest : Request<InitializeRequestArguments, Capabilities>
	{
		public InitializeRequest(InitializeRequestArguments args)
			: base("initialize", args)
		{

		}
	}

	public class ThreadsRequest : Request<object, ThreadsResponseBody>
	{
		public ThreadsRequest()
			: base("threads", null)
		{
		}
	}

	/** Evaluate request; value of command field is "evaluate".
	Evaluates the given expression in the context of the top most stack frame.
	The expression has access to any variables and arguments that are in scope.
*/
	public class EvaluateRequest : Request<EvaluateRequestArguments, EvaluateResponseBody>
	{
		public EvaluateRequest(EvaluateRequestArguments args)
		: base("evaluate", args)
		{
		}
	}
	/** Arguments for "evaluate" request. */
	public class EvaluateRequestArguments
	{
		/** The expression to evaluate. */
		public string expression { get; set; }
		/** Evaluate the expression in the scope of this stack frame. If not specified, the expression is evaluated in the global scope. */
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public long? frameId { get; set; }
		/** The context in which the evaluate request is run. Possible values are 'watch' if evaluate is run in a watch, 'repl' if run from the REPL console, or 'hover' if run from a data hover. */
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public string context { get; set; }
	}

	public class SetBreakpointsRequest : Request<SetBreakpointsRequestArguments, SetBreakpointsResponseBody>
	{
		public SetBreakpointsRequest(SetBreakpointsRequestArguments args)
		: base("setBreakpoints", args)
		{

		}
	}

	/** SetExceptionBreakpoints request; value of command field is "setExceptionBreakpoints".
		Enable that the debuggee stops on exceptions with a StoppedEvent (event type 'exception').
	*/
	public class SetExceptionBreakpointsRequest : Request<SetExceptionBreakpointsArguments, ResponseBody>
	{
		public SetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments args)
			: base("setExceptionBreakpoints", args)
		{
		}
	}

	/** Arguments for "setExceptionBreakpoints" request. */
	public class SetExceptionBreakpointsArguments
	{
		/** Names of enabled exception breakpoints. */
		public string[] filters { get; set; }
	}

	/** Properties of a breakpoint passed to the setBreakpoints request.
	*/
	public class SourceBreakpoint
	{
		/** The source line of the breakpoint. */
		[JsonProperty("line")]
		public int Line { get; set; }
		/** An optional source column of the breakpoint. */
		[JsonProperty("column")]
		public int? Column { get; set; }
		/** An optional expression for conditional breakpoints. */
		[JsonProperty("condition", NullValueHandling = NullValueHandling.Ignore)]
		public string Condition { get; set; }
	}

	public class SetBreakpointsRequestArguments
	{
		[JsonProperty("source")]
		public Source Source { get; set; }

		[JsonProperty("breakpoints")]
		public IList<SourceBreakpoint> Breakpoints { get; set; }
	}

	/** ConfigurationDone request; value of command field is "configurationDone".
	The client of the debug protocol must send this request at the end of the sequence of configuration requests (which was started by the InitializedEvent)
*/
	public class ConfigurationDoneRequest : Request<object, ResponseBody>
	{
		public ConfigurationDoneRequest()
			: base("configurationDone", null)
		{
		}
	}

	/** Disconnect request; value of command field is "disconnect".
	*/
	public class DisconnectRequest : Request<object, ResponseBody>
	{
		public DisconnectRequest()
			: base("disconnect", null)
		{
		}
	}

	/** Scopes request; value of command field is "scopes".
	The request returns the variable scopes for a given stackframe ID.
*/
	public class ScopesRequest : Request<ScopesArguments, ScopesResponseBody>
	{
		public ScopesRequest(ScopesArguments args)
		: base("scopes", args)
		{

		}
	}

	/** Arguments for "scopes" request. */
	public class ScopesArguments
	{
		/** Retrieve the scopes for this stackframe. */
		public int frameId { get; set; }
	}

	/** Variables request; value of command field is "variables".
		Retrieves all children for the given variable reference.
	*/
	public class VariablesRequest : Request<VariablesRequestArguments, VariablesResponseBody>
	{
		public VariablesRequest(VariablesRequestArguments args)
			: base("variables", args)
		{

		}
	}
	/** Arguments for "variables" request. */
	public class VariablesRequestArguments
	{
		/** The Variable reference. */
		public int variablesReference { get; set; }
		/** Optional filter to limit the child variables to either named or indexed. If ommited, both types are fetched. */
		public string filter { get; set; } //'indexed' | 'named';
		/** The index of the first variable to return; if omitted children start at 0. */
		public int? start { get; set; }
		/** The number of variables to return. If count is missing or 0, all variables are returned. */
		public int? count { get; set; }
	}

	/** StackTrace request; value of command field is "stackTrace".
		The request returns a stacktrace from the current execution state.
	*/
	public class StackTraceRequest : Request<StackTraceArguments, StackTraceResponseBody>
	{
		public StackTraceRequest(StackTraceArguments args)
			: base("stackTrace", args)
		{
		}
	}

	/** Arguments for "stackTrace" request. */
	public class StackTraceArguments
	{
		/** Retrieve the stacktrace for this thread. */
		public long threadId;
		/** The index of the first frame to return; if omitted frames start at 0. */
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public int? startFrame;
		/** The maximum number of frames to return. If levels is not specified or 0, all frames are returned. */
		[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
		public int? levels;
	}

	/// <summary>
	/// Subclasses of ResponseBody are serialized as the body of a response.
	/// Don't change their instance variables since that will break the debug protocol.
	/// </summary>
	public class ResponseBody
	{
		public static readonly IDictionary<string, Type> CommandToType = new Dictionary<string, Type>
		{
			{"initialize", typeof(Capabilities)},
			{"continue", typeof(ContinueResponse)},
			{"evaluate", typeof(EvaluateResponseBody)},
			{"scopes", typeof(ScopesResponseBody)},
			{"setBreakpoints", typeof(SetBreakpointsResponseBody)},
			{"stackTrace", typeof(StackTraceResponseBody)},
			{"threads", typeof(ThreadsResponseBody)},
			{"variables", typeof(VariablesResponseBody)},
		};

		// empty
	}

	public class Response : ProtocolMessage
	{
		public bool success { get; set; }
		public string message { get; set; }
		public int request_seq { get; set; }
		public string command { get; set; }
		[JsonProperty(ItemConverterType = typeof(ResponseBodyConverter))]
		public ResponseBody body { get; set; }

		public Response()
			: base("response")
		{
		}

		public void SetBody(ResponseBody bdy)
		{
			success = true;
			body = bdy;
		}

		public void SetErrorBody(string msg, ResponseBody bdy = null)
		{
			success = false;
			message = msg;
			body = bdy;
		}
	}

	public class Event : ProtocolMessage
	{
		[JsonProperty(PropertyName = "event")]
		public string eventType { get; set; }

		[JsonProperty(ItemConverterType = typeof(EventBodyConverter))]
		public EventBody body { get; set; }

		public Event() : base("event") {}

		public Event(string type, dynamic bdy = null) : base("event")
		{
			eventType = type;
			body = bdy;
		}
	}

	// ---- Types -------------------------------------------------------------------------

	public class Message
	{
		public int id { get; }
		public string format { get; }
		public dynamic variables { get; }
		public dynamic showUser { get; }
		public dynamic sendTelemetry { get; }

		public Message(int id, string format, dynamic variables = null, bool user = true, bool telemetry = false)
		{
			this.id = id;
			this.format = format;
			this.variables = variables;
			this.showUser = user;
			this.sendTelemetry = telemetry;
		}
	}

	public class StackFrame
	{
		public int id { get; set; }
		public Source source { get; set; }
		public int line { get; set; }
		public int column { get; set; }
		public string name { get; set; }

		public StackFrame()
		{

		}

		public StackFrame(int id, string name, Source source, int line, int column)
		{
			this.id = id;
			this.name = name;
			this.source = source;
			this.line = line;
			this.column = column;
		}
	}

	public class Scope
	{
		public string name { get; set; }
		public int variablesReference { get; set; }
		public bool expensive { get; set; }

		public Scope()
		{

		}

		public Scope(string name, int variablesReference, bool expensive = false)
		{
			this.name = name;
			this.variablesReference = variablesReference;
			this.expensive = expensive;
		}
	}

	public class Variable
	{
		/** The variable's name. */
		public string name { get; set; }
		/** The variable's value. For structured objects this can be a multi line text, e.g. for a function the body of a function. */
		public string value { get; set; }
		/** The variable's type. */
		public string type { get; set; }
		/** Properties of a variable that can be used to determine how to render the variable in the UI. Format of the string value: TBD. */
		public string kind { get; set; }
		/** If variablesReference is > 0, the variable is structured and its children can be retrieved by passing variablesReference to the VariablesRequest. */
		public int variablesReference { get; set; }
		/** The number of named child variables in this scope.
			The client can use this optional information to present the children in a paged UI and fetch them in chunks.
		*/
		public int? namedVariables { get; set; }
		/** The number of indexed child variables in this scope.
			The client can use this optional information to present the children in a paged UI and fetch them in chunks.
		*/
		public int? indexedVariables { get; set; }
	}

	public class Thread
	{
		public int id { get; set; }
		public string name { get; set; }

		public Thread()
		{

		}

		public Thread(int id, string name)
		{
			this.id = id;
			if (name == null || name.Length == 0) {
				this.name = string.Format("Thread #{0}", id);
			} else {
				this.name = name;
			}
		}
	}

	public class Source
	{
		/// <summary>
		/// The short name of the source. Every source returned from the debug adapter has a name. When specifying a source to the debug adapter this name is optional.
		/// </summary>
		public string name { get; set; }
		/// <summary>
		/// The long (absolute) path of the source. It is not guaranteed that the source exists at this location.
		/// </summary>
		public string path { get; set; }
		/// <summary>
		/// If sourceReference > 0 the contents of the source can be retrieved through the SourceRequest. A sourceReference is only valid for a session, so it must not be used to persist a source.
		/// </summary>
		public int sourceReference { get; set; }
		/// <summary>
		/// The (optional) origin of this source: possible values "internal module", "inlined content from source map"
		/// </summary>
		[JsonProperty("origin", NullValueHandling = NullValueHandling.Ignore)]
		public string origin { get; set; }

		public Source()
		{

		}

		public Source(string name, string path, int sourceReference = 0)
		{
			this.name = name;
			this.path = path;
			this.sourceReference = sourceReference;
		}

		public Source(string path, int sourceReference = 0)
		{
			this.name = Path.GetFileName(path);
			this.path = path;
			this.sourceReference = sourceReference;
		}
	}

	// ---- Events Bodies -------------------------------------------------------------------------

	public class EventBody
	{
		public static IDictionary<string, Type> EventTypeMap = new Dictionary<string, Type>
		{
			{"initialized", typeof(InitializedEventBody)},
			{"stopped", typeof(StoppedEventBody)},
			{"continued", typeof(ContinuedEventBody)},
			{"exited", typeof(ExitedEventBody)},
			{"terminated", typeof(TerminatedEventBody)},
			{"thread", typeof(ThreadEventBody)},
			{"output", typeof(OutputEventBody)},
			{"breakpoint", typeof(BreakpointEventBody)},
			{"module", typeof(ModuleEventBody)},
		};
		// base class for event bodies to deserialize events into typed object
	}

	/** Event message for "initialized" event type.
	This event indicates that the debug adapter is ready to accept configuration requests (e.g. SetBreakpointsRequest, SetExceptionBreakpointsRequest).
	A debug adapter is expected to send this event when it is ready to accept configuration requests (but not before the InitializeRequest has finished).
	The sequence of events/requests is as follows:
	- adapters sends InitializedEvent (after the InitializeRequest has returned)
	- frontend sends zero or more SetBreakpointsRequest
	- frontend sends one SetFunctionBreakpointsRequest
	- frontend sends a SetExceptionBreakpointsRequest if one or more exceptionBreakpointFilters have been defined (or if supportsConfigurationDoneRequest is not defined or false)
	- frontend sends other future configuration requests
	- frontend sends one ConfigurationDoneRequest to indicate the end of the configuration
	*/
	public class InitializedEventBody : EventBody
	{
	}

	public class StoppedEventBody : EventBody
	{
		public int threadId { get; set; }
		public string reason { get; set; }
		public string text { get; set; }
		public bool allThreadsStopped { get; set; }
		public Source source { get; set; }
		public int? line { get; set; }
		public int? column { get; set; }
	}

	public class ContinuedEventBody : EventBody
	{
		/** The thread which was continued. */
		public int threadId { get; set; }
		/** If allThreadsContinued is true, a debug adapter can announce that all threads have continued. **/
		public bool allThreadsContinued { get; set; }
	}

	public class ExitedEventBody : EventBody
	{
		public int exitCode { get; set; }
	}

	public class TerminatedEventBody : EventBody
	{
		/** A debug adapter may set 'restart' to true to request that the front end restarts the session. */
		public bool restart { get; set; }
	}

	public class ThreadEventBody : EventBody
	{
		public int threadId { get; set; }
		public string reason { get; set; }
	}

	public class OutputEventBody : EventBody
	{
		public string output { get; set; }
		public string category { get; set; }
		public object data { get; set; }
	}

	public class Breakpoint
	{
		/** An optional unique identifier for the breakpoint. */
		public int? id { get; set; }
		/** If true breakpoint could be set (but not necessarily at the desired location).  */
		public bool verified { get; set; }
		/** An optional message about the state of the breakpoint. This is shown to the user and can be used to explain why a breakpoint could not be verified. */
		public string message { get; set; }
		/** The source where the breakpoint is located. */
		public Source source { get; set; }
		/** The start line of the actual range covered by the breakpoint. */
		public int? line { get; set; }
		/** An optional start column of the actual range covered by the breakpoint. */
		public int? column { get; set; }
		/** An optional end line of the actual range covered by the breakpoint. */
		public int? endLine { get; set; }
		/**  An optional end column of the actual range covered by the breakpoint. If no end line is given, then the end column is assumed to be in the start line. */
		public int? endColumn { get; set; }
	}

	public class Module {
		/** Unique identifier for the module. */
		public string id { get; set; }
		/** A name of the module. */
		public string name { get; set; }
		/** Logical full path to the module. The exact definition is implementation defined, but usually this would be a full path to the on-disk file for the module. */
		public string path { get; set; }
		/** True if the module is optimized. */
		public bool isOptimized { get; set; }
		/** True if the module is considered 'user code' by a debugger that supports 'Just My Code'. */
		public bool isUserCode { get; set; }
		/** Version of Module. */
		public string version { get; set; }
		/** User understandable description of if symbols were found for the module (ex: 'Symbols Loaded', 'Symbols not found', etc */
		public string symbolStatus { get; set; }
		/** Logical full path to the symbol file. The exact definition is implementation defined. */
		public string symbolFilePath { get; set; }
		/** Module created or modified. */
		public string dateTimeStamp { get; set; }
		/** Address range covered by this module. */
		public string addressRange { get; set; }
	}

	/** Event message for "breakpoint" event type.
	The event indicates that some information about a breakpoint has changed.
	*/
	public class BreakpointEventBody : EventBody
	{
		/** The reason for the event (such as: 'changed', 'new'). */
		public string reason { get; set; }
		/** The breakpoint. */
		public Breakpoint breakpoint { get; set; }
	}

	public class ModuleEventBody : EventBody
	{
		/** The reason for the event. */
		public string reason { get; set; } //'new' | 'changed' | 'removed';
		/** The new, changed, or removed module. In case of 'removed' only the module id is used. */
		public Module module { get; set; }
	}

	// ---- Response -------------------------------------------------------------------------

	public class Capabilities : ResponseBody
	{
		/** The debug adapter supports the configurationDoneRequest. */
		public bool supportsConfigurationDoneRequest { get; set; }
		/** The debug adapter supports functionBreakpoints. */
		public bool supportsFunctionBreakpoints { get; set; }
		/** The debug adapter supports conditionalBreakpoints. */
		public bool supportsConditionalBreakpoints  { get; set; }
		/** The debug adapter supports a (side effect free) evaluate request for data hovers. */
		public bool supportsEvaluateForHovers { get; set; }
		/** Available filters for the setExceptionBreakpoints request. */
		public ExceptionBreakpointsFilter[] exceptionBreakpointFilters { get; set; }
		/** The debug adapter supports stepping back. */
		public bool supportsStepBack { get; set; }
		/** The debug adapter supports setting a variable to a value. */
		public bool supportsSetVariable { get; set; }
		/** The debug adapter supports restarting a frame. */
		public bool supportsRestartFrame { get; set; }
		/** The debug adapter supports the gotoTargetsRequest. */
		public bool supportsGotoTargetsRequest { get; set; }
		/** The debug adapter supports the stepInTargetsRequest. */
		public bool supportsStepInTargetsRequest { get; set; }
		/** The debug adapter supports the completionsRequest. */
		public bool supportsCompletionsRequest { get; set; }
	}

	/** An ExceptionBreakpointsFilter is shown in the UI as an option for configuring how exceptions are dealt with. */
	public class ExceptionBreakpointsFilter
	{
		/** The internal ID of the filter. This value is passed to the setExceptionBreakpoints request. */
		public string filter { get; set; }
		/** The name of the filter. This will be shown in the UI. */
		public string label { get; set; }
		/** Initial value of the filter. If not specified a value 'false' is assumed. */
		public bool @default { get; set; }
	}

	public class ErrorResponseBody : ResponseBody
	{

		public Message error { get; }

		public ErrorResponseBody(Message error)
		{
			this.error = error;
		}
	}

	public class StackTraceResponseBody : ResponseBody
	{
		public StackFrame[] stackFrames { get; set; }
		public StackTraceResponseBody()
		{

		}

		public StackTraceResponseBody(List<StackFrame> frames)
		{
			if (frames == null)
				stackFrames = new StackFrame[0];
			else
				stackFrames = frames.ToArray<StackFrame>();
		}
	}

	public class ScopesResponseBody : ResponseBody
	{
		public Scope[] scopes { get; set; }

		public ScopesResponseBody()
		{

		}

		public ScopesResponseBody(List<Scope> scps)
		{
			if (scps == null)
				scopes = new Scope[0];
			else
				scopes = scps.ToArray<Scope>();
		}
	}

	public class VariablesResponseBody : ResponseBody
	{
		public Variable[] variables { get; set; }

		public VariablesResponseBody()
		{

		}

		public VariablesResponseBody(List<Variable> vars)
		{
			if (vars == null)
				variables = new Variable[0];
			else
				variables = vars.ToArray<Variable>();
		}
	}

	public class ThreadsResponseBody : ResponseBody
	{
		public Thread[] threads { get; set; }

		public ThreadsResponseBody()
		{

		}

		public ThreadsResponseBody(List<Thread> vars)
		{
			if (vars == null)
				threads = new Thread[0];
			else
				threads = vars.ToArray<Thread>();
		}
	}

	public class EvaluateResponseBody : ResponseBody
	{
		/** The result of the evaluate request. */
		public string result { get; set; }
		/** The optional type of the evaluate result. Nullable*/
		public string type { get; set; }
		/** If variablesReference is > 0, the evaluate result is structured and its children can be retrieved by passing variablesReference to the VariablesRequest. */
		public int variablesReference { get; set; }
		/** The number of named child variables.
			The client can use this optional information to present the variables in a paged UI and fetch them in chunks.
		*/
		public int? namedVariables { get; set; }
		/** The number of indexed child variables.
			The client can use this optional information to present the variables in a paged UI and fetch them in chunks.
		*/
		public int? indexedVariables { get; set; }
	}

	public class SetBreakpointsResponseBody : ResponseBody
	{
		public Breakpoint[] breakpoints { get; set; }

		public SetBreakpointsResponseBody()
		: this(null)
		{
		}

		public SetBreakpointsResponseBody(List<Breakpoint> bpts)
		{
			if (bpts == null)
				breakpoints = new Breakpoint[0];
			else
				breakpoints = bpts.ToArray<Breakpoint>();
		}
	}


	[Flags]
	public enum TraceLevel
	{
		None = 0,
		Events = 1,
		Responses = 2,
		Requests = 4,
		All = 7
	}

	//--------------------------------------------------------------------------------------
}
